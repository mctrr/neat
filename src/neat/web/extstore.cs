using System.Text.Json;

namespace neat;

/// <summary>
/// NEAT's own copies of the extensions, in one folder (Extensions in the data folder), and the
/// list that says which copy belongs to which extension.
///
/// WebView2 does not copy an extension into the profile: it remembers the folder it was added
/// from, and it drops the extension when that folder's contents change or the folder goes away.
/// So an extension must be added from a folder that NEAT owns and never touches again. Each copy
/// lives in a folder of its own, named from the extension's name and a random part, and a copy is
/// never moved or renamed once it has been added (a move makes the extension vanish at the next
/// start). The id WebView2 gives an extension is not the one the Chrome Web Store uses, so a small
/// file (extensions.json) keeps the link from that id to its folder; it is how the version and the
/// description are found again later.
///
/// Used from the UI thread, and from a pool thread for the slow parts (copying, deleting); every
/// access to the list takes a lock. Nothing here depends on WinUI or WebView2, so tests/ext.check
/// runs it as it is.
/// </summary>
internal sealed class ExtStore
{
    private const string MapFile = "extensions.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly object _gate = new();
    private MapData? _map;

    public ExtStore(string root) => _root = root;

    /// <summary>The folder that holds the copies.</summary>
    public string Root => _root;

    /// <summary>The most a copied extension may weigh, and how many files it may have. Settable for tests.</summary>
    public long MaxBytes { get; init; } = 256L * 1024 * 1024;

    public int MaxFiles { get; init; } = 20000;

    /// <summary>The list on disk. Dirs: extension id to the folder name. Gone: folders still to delete.</summary>
    private sealed class MapData
    {
        public int V { get; set; } = 1;
        public Dictionary<string, string> Dirs { get; set; } = new(StringComparer.Ordinal);
        public List<string> Gone { get; set; } = new();
    }

    /// <summary>
    /// Copies an extension's folder into the store and returns the name of the new folder. The copy
    /// is what gets added to WebView2 (see <see cref="Full"/>). Runs on a pool thread; a failure leaves
    /// nothing behind.
    /// </summary>
    public string Put(string source, string name)
    {
        Directory.CreateDirectory(_root);

        var dir = Slug(name) + "-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            Copy(Path.GetFullPath(source), Path.Combine(_root, dir));
        }
        catch
        {
            Discard(dir);
            throw;
        }

        return dir;
    }

    /// <summary>The full path of a folder in the store.</summary>
    public string Full(string dir) => Path.Combine(_root, dir);

    /// <summary>Records that the extension WebView2 knows by this id lives in this folder.</summary>
    public void Link(string id, string dir)
    {
        lock (_gate)
        {
            var map = Load();
            map.Dirs[id] = dir;
            Save(map);
        }
    }

    /// <summary>The facts about an extension from its copy; null when there is no copy or it cannot be read.</summary>
    public ExtMeta? Meta(string id)
    {
        string? dir;
        lock (_gate)
            Load().Dirs.TryGetValue(id, out dir);

        if (dir is null || !Plain(dir))
            return null;

        return ExtMeta.Read(Path.Combine(_root, dir)).Meta;
    }

    /// <summary>
    /// Forgets an extension and deletes its copy. A folder that cannot be deleted right now (the
    /// browser may still hold a file open) is kept on a list and deleted by <see cref="Sweep"/> later.
    /// </summary>
    public void Forget(string id)
    {
        string? dir;
        lock (_gate)
        {
            var map = Load();
            if (!map.Dirs.Remove(id, out dir))
                return;

            if (Plain(dir))
                map.Gone.Add(dir);

            Save(map);
        }

        Sweep();
    }

    /// <summary>Deletes a copy whose registration failed, so no folder is left that nothing knows about.</summary>
    public void Discard(string dir)
    {
        if (Plain(dir))
            TryDelete(Path.Combine(_root, dir));
    }

    /// <summary>Deletes the copies that Forget could not delete at the time.</summary>
    public void Sweep()
    {
        List<string> todo;
        lock (_gate)
            todo = Load().Gone.ToList();

        if (todo.Count == 0)
            return;

        var done = todo.Where(d => Plain(d) && TryDelete(Path.Combine(_root, d))).ToList();

        // Names that are not plain are dropped without being touched: they are not ours to delete.
        var bad = todo.Where(d => !Plain(d));

        lock (_gate)
        {
            var map = Load();
            map.Gone.RemoveAll(d => done.Contains(d) || bad.Contains(d));
            Save(map);
        }
    }

    // ---- the folder ----

    private void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);

        // Links are not followed: a folder that points out of itself, or round to itself, is not copied.
        var opt = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        long bytes = 0;
        var files = 0;

        foreach (var path in Directory.EnumerateFileSystemEntries(from, "*", opt))
        {
            var rel = Path.GetRelativePath(from, path);

            // Chrome puts _metadata (what it checked when it downloaded the extension) in the folders
            // it keeps, and WebView2 refuses a folder that holds one: names that start with an
            // underscore are reserved, apart from _locales.
            if (rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s.Equals("_metadata", StringComparison.OrdinalIgnoreCase)))
                continue;

            var target = Path.Combine(to, rel);

            if (Directory.Exists(path))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            bytes += new FileInfo(path).Length;
            files++;

            if (bytes > MaxBytes || files > MaxFiles)
                throw new InvalidOperationException("That folder is too big to be an extension.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The name of a folder that this store made: no path in it, so it cannot lead out of the store.
    private static bool Plain(string? dir)
    {
        return !string.IsNullOrEmpty(dir) && dir != "." && dir != ".." && Path.GetFileName(dir) == dir;
    }

    // Letters and digits of the name, for a folder a person may one day look at; never empty.
    private static string Slug(string name)
    {
        var s = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (s.Length > 24)
            s = s[..24].Trim('-');

        return s.Length == 0 ? "ext" : s;
    }

    // ---- the list ----

    // Called with the lock held. An unreadable list is the same as none: the extensions are still
    // in the profile, only their details are missing.
    private MapData Load()
    {
        if (_map is not null)
            return _map;

        var map = new MapData();
        var file = Path.Combine(_root, MapFile);

        try
        {
            if (File.Exists(file))
            {
                var read = JsonSerializer.Deserialize<MapData>(File.ReadAllText(file), Json);
                if (read is not null)
                {
                    foreach (var (id, dir) in read.Dirs)
                    {
                        if (Plain(dir))
                            map.Dirs[id] = dir;
                    }

                    map.Gone = read.Gone.Where(d => Plain(d)).ToList();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return _map = map;
    }

    // Called with the lock held. Written to a temporary file first, so a crash cannot leave half a list.
    private void Save(MapData map)
    {
        Directory.CreateDirectory(_root);

        var file = Path.Combine(_root, MapFile);
        var temp = file + ".tmp";

        File.WriteAllText(temp, JsonSerializer.Serialize(map, Json));
        File.Move(temp, file, true);
    }
}
