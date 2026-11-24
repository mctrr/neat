namespace neat;

/// <summary>A card a page showed NEAT, waiting to find out whether the payment form went through.</summary>
public sealed record PendingCard(string Site, string Number, string Holder, string Expiry, string Cvc, DateTime At);

/// <summary>
/// Remembers, for each tab, the card the person has just submitted in a payment form, until it is known
/// whether the form went through. It is <see cref="Capture"/> for cards, and keeps the same rules:
///   - The page script reports a card only for a real click or key (never one the page made up), and
///     only a number that passes the check digit (<see cref="ItemRules.Luhn"/>).
///   - Reporting is not asking. The window offers to save only when the page moved on (the form went
///     away), and a server that refuses a request of the same site (see <see cref="LoginNet"/>) drops it.
///   - It lives in memory for <see cref="Window"/> and is dropped as soon as it is taken. Nothing is written.
/// </summary>
public sealed class CardCapture
{
    /// <summary>How long a card is held while waiting to find out how it went.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    /// <summary>After offering to save a card for a site, it is not offered again for this long.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long what the frames of a payment form have said (the number in one, the expiry in another, ...)
    /// is kept while waiting for the Pay button of the page above them.
    /// </summary>
    public static readonly TimeSpan PartsWindow = TimeSpan.FromMinutes(10);

    private sealed class Parts
    {
        public string Number = string.Empty;
        public string Holder = string.Empty;
        public string Expiry = string.Empty;
        public string Cvc = string.Empty;
        public object? NumberFrame;
        public DateTime At;
    }

    private readonly Dictionary<object, PendingCard> _held = new();
    private readonly Dictionary<object, Parts> _parts = new();
    private readonly Dictionary<string, DateTime> _offered = new(StringComparer.Ordinal);

    // The clock this (and the self-check) uses.
    internal Func<DateTime> Clock = () => DateTime.UtcNow;

    /// <summary>
    /// Holds a card for a tab, replacing any it held. False (nothing held) when it is not a web origin or
    /// the number is not a card number. A security code or expiry that is not valid is left out, not refused.
    /// </summary>
    public bool Hold(object tab, string site, string number, string holder, string expiry, string cvc)
    {
        var origin = Origin.Of(site);
        var digits = ItemRules.Digits(number);

        if (origin is null || !ItemRules.Luhn(digits) || digits.Length != number.Count(c => char.IsAsciiDigit(c)))
            return false;

        holder = holder.Trim();
        if (holder.Length > 100)
            holder = string.Empty;

        cvc = cvc.Trim();
        if (cvc.Length is < 3 or > 4 || !cvc.All(char.IsAsciiDigit))
            cvc = string.Empty;

        _held[tab] = new PendingCard(origin, digits, holder, ItemRules.Expiry(expiry) ?? string.Empty, cvc, Clock());
        return true;
    }

    /// <summary>
    /// A payment form whose fields are in frames of their own (a card processor puts the number, the expiry and the
    /// security code each in a frame, and the Pay button is in the page above): each frame says what is typed in it,
    /// and what has been said is put together for the tab until <see cref="Submit"/>. Only a number that passes the
    /// check digit is taken; a value that is not valid is left out. A new number starts over (its expiry and code
    /// are not the old card's). Nothing is written, and nothing is held longer than <see cref="PartsWindow"/>.
    /// </summary>
    public void Part(object tab, object frame, string? number, string? holder, string? expiry, string? cvc)
    {
        var now = Clock();

        if (!_parts.TryGetValue(tab, out var p) || now - p.At >= PartsWindow)
            _parts[tab] = p = new Parts();

        p.At = now;

        var digits = ItemRules.Digits(number);
        if (number is not null && ItemRules.Luhn(digits) && digits.Length == number.Count(char.IsAsciiDigit))
        {
            if (p.Number.Length > 0 && p.Number != digits)
            {
                p.Expiry = string.Empty;
                p.Cvc = string.Empty;
            }

            p.Number = digits;
            p.NumberFrame = frame;
        }

        if (holder is not null && holder.Trim().Length is > 0 and <= 100)
            p.Holder = holder.Trim();

        if (ItemRules.Expiry(expiry) is { } e)
            p.Expiry = e;

        if (cvc is not null && cvc.Length is >= 3 and <= 4 && cvc.All(char.IsAsciiDigit))
            p.Cvc = cvc;
    }

    /// <summary>
    /// The page above the frames had its button pressed: what the frames said becomes the held card (as if the form
    /// had been in the page), with <paramref name="holder"/> as the name if no frame gave one. False when no
    /// frame gave a number, or it is too old. <paramref name="numberFrame"/> is the frame the number came from.
    /// </summary>
    public bool Submit(object tab, string site, string holder, out object? numberFrame)
    {
        numberFrame = null;

        if (!_parts.Remove(tab, out var p) || Clock() - p.At >= PartsWindow || p.Number.Length == 0)
            return false;

        if (!Hold(tab, site, p.Number, p.Holder.Length > 0 ? p.Holder : holder, p.Expiry, p.Cvc))
            return false;

        numberFrame = p.NumberFrame;
        return true;
    }

    public bool Holds(object tab)
    {
        return _held.TryGetValue(tab, out var p) && Fresh(p);
    }

    /// <summary>Takes the held card out, for offering it. Null when there is none or it waited too long. Nothing is held afterwards.</summary>
    public PendingCard? Take(object tab)
    {
        return _held.Remove(tab, out var p) && Fresh(p) ? p : null;
    }

    public void Drop(object tab)
    {
        _held.Remove(tab);
    }

    /// <summary>Told about an answer the tab's page received: a refusal of the same site drops the held card. True when it did.</summary>
    public bool Answered(object tab, string? method, string? url, int status)
    {
        if (!_held.TryGetValue(tab, out var p) || !Fresh(p) || !LoginNet.Refuses(p.Site, method, url, status))
            return false;

        Drop(tab);
        return true;
    }

    public void Forget(object tab)
    {
        _held.Remove(tab);
        _parts.Remove(tab);
    }

    public void Clear()
    {
        _held.Clear();
        _parts.Clear();
    }

    /// <summary>Whether an offer for this site may be shown now, and if so notes that it was.</summary>
    public bool MayOffer(string site)
    {
        var now = Clock();

        if (_offered.TryGetValue(site, out var last) && now - last < Cooldown)
            return false;

        _offered[site] = now;

        foreach (var old in _offered.Where(kv => now - kv.Value >= Cooldown).Select(kv => kv.Key).ToList())
            _offered.Remove(old);

        return true;
    }

    private bool Fresh(PendingCard p)
    {
        return Clock() - p.At < Window;
    }
}
