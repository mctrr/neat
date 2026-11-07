using System.Text.Json;

namespace neat;

/// <summary>
/// What an unpacked extension's manifest.json says about it: its name, version, description and,
/// in plain words, what it is allowed to do. WebView2 reports none of this except the name, so it
/// is read from the extension's folder (see <see cref="ExtStore"/>). Nothing here depends on
/// WinUI or WebView2, so tests/ext.check runs it as it is.
///
/// The folder may come from anywhere, so nothing in the manifest is trusted: the file size is
/// capped, a bad file is an error message rather than an exception, and the locale that picks a
/// translation is checked before it becomes part of a path.
/// </summary>
internal sealed class ExtMeta
{
    // A manifest is a few kilobytes; anything past this is not one.
    private const long Max = 1024 * 1024;

    private static readonly JsonDocumentOptions Lax = new()
    {
        // Chrome accepts comments and trailing commas in manifest.json, and extensions use them.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Permissions that Chrome does not warn about, so neither do we.
    private static readonly HashSet<string> Quiet = new(StringComparer.Ordinal)
    {
        "storage", "unlimitedStorage", "alarms", "activeTab", "contextMenus", "scripting", "idle",
        "offscreen", "sidePanel", "declarativeContent", "fontSettings", "power", "tts", "ttsEngine",
        "search", "favicon", "readingList", "webRequestAuthProvider", "background", "action",
    };

    // The rest, as a sentence that finishes "It can: ...". Several permissions may say the same thing.
    private static readonly Dictionary<string, string> Says = new(StringComparer.Ordinal)
    {
        ["tabs"] = "See the addresses and titles of your open tabs",
        ["history"] = "Read and change your browsing history",
        ["webNavigation"] = "Read your browsing history",
        ["topSites"] = "See the sites you visit most",
        ["sessions"] = "See your recently closed tabs",
        ["bookmarks"] = "Read and change your bookmarks",
        ["downloads"] = "Manage your downloads",
        ["cookies"] = "Read and change your cookies",
        ["browsingData"] = "Clear your browsing data",
        ["clipboardRead"] = "Read data you copy and paste",
        ["clipboardWrite"] = "Change data you copy and paste",
        ["notifications"] = "Show notifications",
        ["nativeMessaging"] = "Talk to other programs on your PC",
        ["geolocation"] = "Find out where you are",
        ["management"] = "Manage your other extensions",
        ["privacy"] = "Change your privacy settings",
        ["proxy"] = "Control how the browser connects to the internet",
        ["contentSettings"] = "Change what sites are allowed to do, such as cookies and pop-ups",
        ["webRequest"] = "See the requests that pages make",
        ["webRequestBlocking"] = "Block or change the requests that pages make",
        ["declarativeNetRequest"] = "Block content on any page",
        ["declarativeNetRequestWithHostAccess"] = "Block content on the pages it can access",
        ["declarativeNetRequestFeedback"] = "See which requests it blocked",
        ["debugger"] = "Look inside the pages you open, like the developer tools",
        ["tabCapture"] = "Capture what is shown in your tabs",
        ["desktopCapture"] = "Capture what is shown on your screen",
        ["pageCapture"] = "Save pages as files",
        ["userScripts"] = "Run scripts you add on web pages",
        ["identity"] = "Know which accounts you are signed in with",
    };

    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>The manifest format: 2 or 3 (the older Manifest V2, or the current V3).</summary>
    public int Mv { get; init; }

    /// <summary>What the extension is allowed to do, one short sentence each; empty when it asks for nothing notable.</summary>
    public List<string> Can { get; init; } = new();

    /// <summary>
    /// Reads manifest.json in a folder. Returns the facts, or null and a sentence for the person
    /// when the folder is not an extension.
    /// </summary>
    public static (ExtMeta? Meta, string? Err) Read(string folder)
    {
        var file = Path.Combine(folder, "manifest.json");

        try
        {
            if (!File.Exists(file))
                return (null, "That folder has no manifest.json. Pick the folder that holds it, not one around it.");

            if (new FileInfo(file).Length > Max)
                return (null, "manifest.json is too big to be an extension's.");

            using var doc = JsonDocument.Parse(File.ReadAllText(file), Lax);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return (null, "manifest.json could not be read.");

            var name = Str(root, "name");
            var version = Str(root, "version");
            var mv = root.TryGetProperty("manifest_version", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var n) ? n : 0;

            if (name.Length == 0 || version.Length == 0 || mv == 0)
                return (null, "manifest.json is missing the name, the version or the manifest version, so it is not an extension.");

            return (new ExtMeta
            {
                Name = Say(folder, root, name),
                Version = version,
                Description = Say(folder, root, Str(root, "description")),
                Mv = mv,
                Can = Wants(root),
            }, null);
        }
        catch (JsonException)
        {
            return (null, "manifest.json could not be read.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "The folder could not be read.");
        }
    }

    private static string Str(JsonElement obj, string key)
    {
        return obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : "";
    }

    /// <summary>
    /// Turns "__MSG_key__" into the text from the extension's _locales folder, as Chrome does.
    /// Anything else is returned as it is; so is a placeholder that cannot be found.
    /// </summary>
    private static string Say(string folder, JsonElement root, string text)
    {
        if (text.Length < 8 || !text.StartsWith("__MSG_", StringComparison.Ordinal) || !text.EndsWith("__", StringComparison.Ordinal))
            return text;

        var key = text[6..^2];

        foreach (var locale in Locales(root))
        {
            var file = Path.Combine(folder, "_locales", locale, "messages.json");

            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > Max)
                    continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(file), Lax);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                // Chrome compares the keys without regard to case.
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
                        && p.Value.ValueKind == JsonValueKind.Object
                        && Str(p.Value, "message") is { Length: > 0 } message)
                        return message;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A broken translation file: try the next language.
            }
        }

        return text;
    }

    // The languages to look in, best first. The manifest names the default one; it ends up in a
    // path, so only plain locale names (en, pt_BR, zh-CN) are used and "..\x" is not one.
    private static IEnumerable<string> Locales(JsonElement root)
    {
        var def = Str(root, "default_locale");
        if (def.Length > 0 && def.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            yield return def;

        yield return "en";
        yield return "en_US";
    }

    /// <summary>What the manifest asks for, as sentences: the sites first, then the other permissions.</summary>
    private static List<string> Wants(JsonElement root)
    {
        var apis = new List<string>();
        var hosts = new List<string>();

        Sort(root, "permissions", apis, hosts);
        Sort(root, "host_permissions", apis, hosts);

        // A content script runs on the pages it matches, which is the same as asking for them.
        if (root.TryGetProperty("content_scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in scripts.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.Object)
                    Sort(s, "matches", apis, hosts);
            }
        }

        var can = new List<string>();

        var sites = Sites(hosts);
        if (sites is not null)
            can.Add(sites);

        // Two permissions can say the same thing; it is shown once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var api in apis)
        {
            if (Quiet.Contains(api))
                continue;

            var line = Says.TryGetValue(api, out var said) ? said : "Use the \"" + api + "\" permission";
            if (seen.Add(line))
                can.Add(line);
        }

        return can;
    }

    // A list of strings is split into site patterns (they have a scheme) and API permissions.
    private static void Sort(JsonElement obj, string key, List<string> apis, List<string> hosts)
    {
        if (!obj.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in list.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(text))
                continue;

            if (text == "<all_urls>" || text.Contains("://", StringComparison.Ordinal))
                hosts.Add(text);
            else
                apis.Add(text);
        }
    }

    private static string? Sites(List<string> patterns)
    {
        if (patterns.Count == 0)
            return null;

        var names = new List<string>();

        foreach (var p in patterns)
        {
            // These patterns match every site.
            if (p is "<all_urls>" or "*://*/*" or "http://*/*" or "https://*/*" or "*://*/" or "http://*/" or "https://*/")
                return "Read and change all your data on all websites";

            var at = p.IndexOf("://", StringComparison.Ordinal);
            var rest = p[(at + 3)..];
            var host = rest.Split('/')[0];

            // A pattern for files on this PC has no host.
            var name = p.StartsWith("file:", StringComparison.Ordinal) ? "local files" : host;
            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        if (names.Count == 0)
            return null;

        return names.Count <= 3
            ? "Read and change your data on " + string.Join(", ", names)
            : "Read and change your data on " + names.Count + " websites";
    }
}
