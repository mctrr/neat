using System.Buffers.Binary;
using System.IO.Compression;

namespace neat;

/// <summary>
/// Unpacks an extension that comes as one file, a .zip or a Chrome .crx, into a folder of its own.
/// Nothing here depends on WinUI or WebView2, so tests/ext.check runs it as it is.
///
/// A package is somebody else's file, so every step assumes the worst:
///   - Names are judged before anything is written. A name that is absolute, has a ".." part, or
///     that Windows would treat as something other than a plain file (a device name, a drive, a
///     stream, a trailing dot) refuses the whole package: this is the "zip slip" attack, where an
///     entry called "..\..\Startup\x.exe" is written outside the folder it was meant for.
///   - Each file is created new, so two entries with one name cannot overwrite each other, and the
///     path that results is checked against the folder once more before the file is made.
///   - The size and the number of files are capped, and the size is counted again while copying,
///     because the sizes in a zip's headers are only what the file says about itself.
///   - Nothing in the package is run, and a failure deletes the folder, so nothing is left behind.
///
/// A .crx file is a zip with a header in front. The header carries the publisher's signature, and
/// it is NOT checked here: a signature only proves that the same key made two packages, and NEAT
/// has no list of keys it could trust. The id written in the header is not trusted either; the
/// extension gets whatever id WebView2 gives it. What protects the person is the question that
/// comes before the extension is added (what it can do), and the fact that nothing is run before.
/// </summary>
internal static class ExtPack
{
    private const string Bad = "That package has a file with a name that is not allowed (for instance one that points outside its own folder), so NEAT did not open it.";
    private const string Big = "That package is too big to be an extension.";

    // Names that Windows takes for a device, whatever comes after the dot ("con.js" is the console).
    private static readonly HashSet<string> Devices = new(
        new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }
            .Concat(Enumerable.Range(1, 9).SelectMany(n => new[] { "COM" + n, "LPT" + n })),
        StringComparer.OrdinalIgnoreCase);

    // One entry that will be written: where it came from, its path inside the folder, and whether it is a folder.
    private readonly record struct Item(ZipArchiveEntry Entry, string[] Parts, bool Dir);

    // A refusal with a sentence the person can read. Anything else that goes wrong is reported in general terms.
    private sealed class Refuse : Exception
    {
        public Refuse(string why) : base(why)
        {
        }
    }

    /// <summary>
    /// Unpacks a package into target, which must not exist yet and is made here. Returns null when
    /// it worked, or a sentence for the person; then target is gone again. Never throws for a bad
    /// package. Slow, so call it from a pool thread.
    /// </summary>
    public static string? Open(string package, string target, long maxBytes, int maxFiles)
    {
        var root = Path.GetFullPath(target);

        // The caller makes a fresh name; a folder already there is not ours to fill or to delete.
        if (Directory.Exists(root) || File.Exists(root))
            return "NEAT could not make a folder for the extension.";

        try
        {
            Directory.CreateDirectory(root);
            Unpack(package, root, maxBytes, maxFiles);
            return null;
        }
        catch (Refuse r)
        {
            Wipe(root);
            return r.Message;
        }
        catch (InvalidDataException ex)
        {
            Log.Error("ext", ex, "a package is damaged");
            Wipe(root);
            return "That file is damaged, or it is not a real extension package.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Error("ext", ex, "unpacking a package failed");
            Wipe(root);
            return "NEAT could not unpack that file. Check that the disk has room and that the file can be opened.";
        }
        catch
        {
            // Not an error to explain, but the half-made folder must still go.
            Wipe(root);
            throw;
        }
    }

    private static void Unpack(string package, string root, long maxBytes, int maxFiles)
    {
        var info = new FileInfo(package);
        if (!info.Exists)
            throw new Refuse("That file could not be found.");

        // The unpacked size is capped below; a file that is already past it cannot pass.
        if (info.Length > maxBytes)
            throw new Refuse(Big);

        using var file = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);

        // The name decides how the file is read: a .crx must be a .crx, and a .zip must be a plain zip.
        var crx = package.EndsWith(".crx", StringComparison.OrdinalIgnoreCase);
        var from = crx ? ZipStart(file) : 0;

        if (!Zipped(file, from))
            throw new Refuse(crx ? "That .crx file is damaged: the package inside it was not found." : "That file is not a valid .zip file.");

        using var zip = new ZipArchive(new Slice(file, from, file.Length - from), ZipArchiveMode.Read, leaveOpen: true);

        Write(Plan(zip, maxBytes, maxFiles), root, maxBytes);
    }

    // ---- the .crx header ----

    /// <summary>
    /// Where the zip starts in a .crx. Version 2 has the key and the signature in front of it
    /// (their sizes are in the header), version 3 has one header block (its size is in the header).
    /// Anything that does not add up is refused.
    /// </summary>
    private static long ZipStart(FileStream file)
    {
        var head = new byte[16];
        var got = ReadAt(file, 0, head);

        if (got < 12 || head[0] != 'C' || head[1] != 'r' || head[2] != '2' || head[3] != '4')
            throw new Refuse("That file is not a Chrome extension package (.crx).");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
        long start;

        if (version == 2)
        {
            if (got < 16)
                throw new Refuse("That .crx file is damaged.");

            start = 16L + BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8)) + BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(12));
        }
        else if (version == 3)
        {
            start = 12L + BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
        }
        else
        {
            throw new Refuse("That .crx file is in a format NEAT does not know (version " + version + ").");
        }

        // The smallest zip is 22 bytes (just its end record); less than that left means the sizes lie.
        if (start > file.Length - 22)
            throw new Refuse("That .crx file is damaged.");

        return start;
    }

    // True when a zip's first entry starts at this place.
    private static bool Zipped(Stream s, long at)
    {
        var b = new byte[4];
        return ReadAt(s, at, b) == 4 && b[0] == 'P' && b[1] == 'K' && b[2] == 3 && b[3] == 4;
    }

    private static int ReadAt(Stream s, long at, byte[] into)
    {
        s.Position = at;

        var have = 0;
        while (have < into.Length)
        {
            var n = s.Read(into, have, into.Length - have);
            if (n <= 0)
                break;

            have += n;
        }

        return have;
    }

    // ---- judging the names ----

    /// <summary>Decides what to write, and refuses the package if any name is not allowed. Writes nothing.</summary>
    private static List<Item> Plan(ZipArchive zip, long maxBytes, int maxFiles)
    {
        if (zip.Entries.Count > maxFiles)
            throw new Refuse("That package has too many files to be an extension.");

        var plan = new List<Item>();
        long total = 0;

        foreach (var e in zip.Entries)
        {
            // Every name is judged, including the ones that are skipped below.
            var parts = Parts(e.FullName) ?? throw new Refuse(Bad);
            if (parts.Length == 0)
                continue;

            // Chrome seals the packages it downloads with a _metadata folder; WebView2 refuses a
            // folder that holds one (names that start with an underscore are reserved).
            if (parts.Any(p => p.Equals("_metadata", StringComparison.OrdinalIgnoreCase)))
                continue;

            var dir = e.FullName.EndsWith('/') || e.FullName.EndsWith('\\');
            if (!dir)
            {
                // What the header says. It is counted again while copying, because it can lie.
                total += e.Length;
                if (total > maxBytes)
                    throw new Refuse(Big);
            }

            plan.Add(new Item(e, parts, dir));
        }

        // A folder zipped up with its own name round it ("tidy/manifest.json") has one folder round
        // everything. That folder is dropped, so that manifest.json ends up at the top, where it must be.
        if (!plan.Any(x => IsManifest(x.Parts, 0)))
        {
            var tops = plan.Select(x => x.Parts[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (tops == 1 && plan.Any(x => x.Parts.Length == 2 && IsManifest(x.Parts, 1)))
                plan = plan.Select(x => x with { Parts = x.Parts[1..] }).Where(x => x.Parts.Length > 0).ToList();
        }

        if (!plan.Any(x => IsManifest(x.Parts, 0)))
            throw new Refuse("That package has no manifest.json at its top, so it is not a browser extension.");

        return plan;
    }

    private static bool IsManifest(string[] parts, int at)
    {
        return parts.Length == at + 1 && parts[at].Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The path of an entry as its parts, or null when the name is not allowed. An empty list is
    /// the folder itself ("./"), which has nothing to write.
    /// </summary>
    private static string[]? Parts(string name)
    {
        // A zip separates with "/", but one made on Windows may use "\", and Windows takes both.
        var s = name.Replace('\\', '/');

        // Empty, or absolute ("/x", "//server/share").
        if (s.Length == 0 || s[0] == '/')
            return null;

        var raw = s.Split('/');
        var list = new List<string>();

        for (var i = 0; i < raw.Length; i++)
        {
            var seg = raw[i];

            if (seg.Length == 0)
            {
                // The slash that ends a folder entry is fine; one in the middle ("a//b") is not.
                if (i == raw.Length - 1)
                    continue;

                return null;
            }

            if (seg == ".")
                continue;

            if (!Fine(seg))
                return null;

            list.Add(seg);
        }

        return list.ToArray();
    }

    // One part of a path. The rules are Windows' (they are the strictest), on every system, so a
    // package is judged the same wherever it is checked.
    private static bool Fine(string seg)
    {
        // "..", "..." and a name that ends in a space all lose the end when Windows saves them,
        // so "manifest.json." would be manifest.json.
        if (seg.EndsWith('.') || seg.EndsWith(' '))
            return false;

        // ':' is a drive ("C:") or a hidden stream ("a.txt:x"); the others are not allowed in a name at all.
        foreach (var c in seg)
        {
            if (c < ' ' || c is '<' or '>' or ':' or '"' or '|' or '?' or '*')
                return false;
        }

        var dot = seg.IndexOf('.');
        return !Devices.Contains(dot < 0 ? seg : seg[..dot]);
    }

    // ---- writing ----

    private static void Write(List<Item> plan, string root, long maxBytes)
    {
        var top = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var buf = new byte[81920];
        long total = 0;

        foreach (var x in plan)
        {
            var full = Path.GetFullPath(Path.Combine(root, Path.Combine(x.Parts)));

            // Parts has already refused every way out. This asks the file system what the name
            // comes to, and is the check that would still hold if Parts had a hole in it.
            if (!full.StartsWith(top, StringComparison.OrdinalIgnoreCase))
                throw new Refuse(Bad);

            if (x.Dir)
            {
                Directory.CreateDirectory(full);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            // CreateNew: never over a file that is already there.
            using var to = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var from = x.Entry.Open();

            int n;
            while ((n = from.Read(buf, 0, buf.Length)) > 0)
            {
                total += n;
                if (total > maxBytes)
                    throw new Refuse(Big);

                to.Write(buf, 0, n);
            }
        }
    }

    private static void Wipe(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What stays is in the store's staging folder, which is swept later (see ExtStore).
        }
    }

    // ---- the zip inside a .crx ----

    /// <summary>
    /// A view of a part of a file, so that a zip that starts in the middle of a .crx can be read.
    /// The offsets inside a zip count from its own first byte, which a plain read of the whole
    /// file would get wrong. Closing it leaves the file open: the file is the caller's.
    /// </summary>
    private sealed class Slice : Stream
    {
        private readonly Stream _s;
        private readonly long _from;
        private readonly long _len;
        private long _at;

        public Slice(Stream s, long from, long len)
        {
            _s = s;
            _from = from;
            _len = len;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _len;

        public override long Position
        {
            get => _at;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var left = _len - _at;
            if (left <= 0)
                return 0;

            _s.Position = _from + _at;
            var n = _s.Read(buffer, offset, (int)Math.Min(count, left));
            _at += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var to = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _at + offset,
                _ => _len + offset,
            };

            if (to < 0)
                throw new IOException("Seek before the start.");

            _at = to;
            return _at;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
