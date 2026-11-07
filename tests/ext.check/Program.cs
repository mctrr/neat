using System.Runtime.InteropServices;
using neat;

// Self-check of the extension code that needs no WebView2: the wrappers (src/neat/web/ext.cs), the
// manifest reader (extmeta.cs) and the folder store (extstore.cs). Plain asserts, no test package: it
// prints one line per check and exits with 1 if any failed, which fails the CI build.
//
// The wrappers reach a profile only through IExtProf, so a fake stands in for it here. What this
// proves is the contract the Settings page will rely on: a fresh profile lists nothing, nothing
// throws, and a failure comes back as a sentence.

internal static class Program
{
    private static int _ran;
    private static int _failed;

    // The temporary folders the checks made; they are deleted at the end.
    private static readonly List<string> Made = new();

    private static int Main()
    {
        Run("a fresh profile lists no extensions", FreshProfile);
        Run("nothing works, and nothing throws, while extensions are off", WhileOff);
        Run("the reason is kept, and cleared when extensions come on", Reason);
        Run("without a live tab there is nothing to list and a clear message", NoTab);
        Run("add, switch and remove an extension", AddSwitchRemove);
        Run("an unknown id is reported, not thrown", UnknownId);
        Run("a failing add comes back as a sentence", FailingAdd);
        Run("a failing list is an empty list", FailingList);
        Run("a resolver that throws is the same as no tab", ThrowingResolver);
        Run("an empty folder name is refused", EmptyFolder);
        Run("Live needs extensions on and a tab", LiveState);

        Run("a manifest is read: name, version, description, format", ManifestBasics);
        Run("a translated name comes from _locales, and a hostile locale is not followed", ManifestTranslated);
        Run("a folder that is not an extension is explained", ManifestBad);
        Run("permissions come out as plain words, the sites first", PermissionWords);
        Run("sites are named, or counted when there are many", PermissionSites);

        Run("a copy skips _metadata, keeps _locales, and does not follow links", StoreCopy);
        Run("a copy that is too big is refused and leaves nothing", StoreTooBig);
        Run("a copy is linked, read back and forgotten with its folder", StoreLink);
        Run("a list entry that leads out of the store is ignored", StoreEscape);
        Run("a folder that was left over is deleted by the sweep", StoreSweep);

        foreach (var dir in Made)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temporary folder that stays is not a failure.
            }
        }

        Console.WriteLine($"{_ran - _failed} of {_ran} checks passed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Func<Task> body)
    {
        _ran++;
        try
        {
            // The state is static, so every check starts from the same one.
            Ext.Set(true, null);
            Ext.Src = null;
            body().GetAwaiter().GetResult();
            Console.WriteLine("ok    " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + ": " + ex.Message);
        }
    }

    private static void Need(bool ok, string what)
    {
        if (!ok)
            throw new Exception(what);
    }

    private static async Task FreshProfile()
    {
        Ext.Src = () => new Fake();
        var all = await Ext.All();
        Need(all.Count == 0, "expected an empty list, got " + all.Count);
    }

    private static async Task WhileOff()
    {
        Ext.Set(false, "off for the check");
        Ext.Src = () => new Fake();

        Need((await Ext.All()).Count == 0, "list should be empty while off");

        var add = await Ext.Add(@"C:\x");
        Need(!add.Ok && add.Err == "off for the check", "add should say why it cannot");

        var on = await Ext.Enable("a", true);
        var gone = await Ext.Remove("a");
        Need(!on.Ok && !gone.Ok, "enable and remove should fail while off");
    }

    private static Task Reason()
    {
        Ext.Set(false, null);
        Need(!Ext.Can && Ext.Why.Length > 0, "a reason is needed even when none was given");

        Ext.Set(false, "because");
        Need(Ext.Why == "because", "the given reason should be kept");

        Ext.Set(true, "ignored");
        Need(Ext.Can && Ext.Why == "", "the reason should be cleared when extensions are on");
        return Task.CompletedTask;
    }

    private static async Task NoTab()
    {
        Need((await Ext.All()).Count == 0, "no tab means an empty list");

        var add = await Ext.Add(@"C:\x");
        Need(!add.Ok && !string.IsNullOrEmpty(add.Err), "add without a tab should explain");
    }

    private static async Task AddSwitchRemove()
    {
        var fake = new Fake();
        Ext.Src = () => fake;

        var add = await Ext.Add(@"C:\ext\one");
        Need(add.Ok && add.Item is not null && add.Item.Name == "one" && add.Item.On, "added extension should be on");

        var all = await Ext.All();
        Need(all.Count == 1 && all[0].Id == add.Item!.Id, "the list should hold the new extension");

        var off = await Ext.Enable(add.Item!.Id, false);
        Need(off.Ok && !(await Ext.All())[0].On, "extension should be off after switching");

        var gone = await Ext.Remove(add.Item!.Id);
        Need(gone.Ok && (await Ext.All()).Count == 0, "extension should be gone after removing");
    }

    private static async Task UnknownId()
    {
        Ext.Src = () => new Fake();

        var on = await Ext.Enable("nope", true);
        var gone = await Ext.Remove("nope");
        Need(!on.Ok && !gone.Ok && on.Err is not null, "an unknown id should be an error result");
    }

    private static async Task FailingAdd()
    {
        // The HRESULT that WebView2 gives for a folder without a manifest.
        Ext.Src = () => new Fake { Boom = new COMException("x", unchecked((int)0x80070002)) };
        var add = await Ext.Add(@"C:\x");
        Need(!add.Ok && add.Err!.Contains("manifest.json"), "a missing manifest should be named: " + add.Err);

        Ext.Src = () => new Fake { Boom = new InvalidOperationException("odd") };
        add = await Ext.Add(@"C:\x");
        Need(!add.Ok && add.Err == "The extension could not be added.", "an unknown failure gets the plain sentence");
    }

    private static async Task FailingList()
    {
        Ext.Src = () => new Fake { Boom = new InvalidOperationException("closed") };
        Need((await Ext.All()).Count == 0, "a failing list should be empty");
    }

    private static async Task ThrowingResolver()
    {
        Ext.Src = () => throw new ObjectDisposedException("tab");
        Need((await Ext.All()).Count == 0, "list should be empty");
        Need(!(await Ext.Add(@"C:\x")).Ok, "add should fail without throwing");
    }

    private static async Task EmptyFolder()
    {
        Ext.Src = () => new Fake();
        Need(!(await Ext.Add("  ")).Ok, "a blank folder name should be refused");
    }

    private static async Task LiveState()
    {
        Ext.Set(false, "off");
        Ext.Src = () => new Fake();
        Need(!Ext.Live, "not live while extensions are off");

        Ext.Set(true, null);
        Ext.Src = null;
        Need(!Ext.Live, "not live without a tab");

        Ext.Src = () => new Fake();
        Need(Ext.Live, "live with extensions on and a tab");
        await Task.CompletedTask;
    }

    // ---- the manifest and the store (they work on real folders, so each check has a temporary one) ----

    private static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neat-extcheck-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        Made.Add(dir);
        return dir;
    }

    private static void Write(string folder, string file, string text)
    {
        var path = Path.Combine(folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Manifest(string extra = "") =>
        """{ "manifest_version": 3, "name": "Tidy", "version": "1.2.3", "description": "Tidies pages" """ + extra + "}";

    private static Task ManifestBasics()
    {
        var dir = Temp();
        // Chrome allows comments and a trailing comma in a manifest, and extensions use them.
        Write(dir, "manifest.json", "{ // the name\n \"manifest_version\": 3, \"name\": \"Tidy\", \"version\": \"1.2.3\", \"description\": \"Tidies pages\", }");

        var (m, err) = ExtMeta.Read(dir);
        Need(m is not null && err is null, "should read: " + err);
        Need(m!.Name == "Tidy" && m.Version == "1.2.3" && m.Description == "Tidies pages" && m.Mv == 3, "wrong facts");
        Need(m.Can.Count == 0, "no permissions means nothing to warn about");
        return Task.CompletedTask;
    }

    private static Task ManifestTranslated()
    {
        var dir = Temp();
        Write(dir, "manifest.json", Manifest().Replace("\"Tidy\"", "\"__MSG_extName__\"").Replace("}", ", \"default_locale\": \"vi\" }"));
        Write(dir, "_locales/vi/messages.json", """{ "EXTNAME": { "message": "Gọn gàng" } }""");

        var (m, _) = ExtMeta.Read(dir);
        Need(m is not null && m.Name == "Gọn gàng", "the name should come from _locales, any case: " + m?.Name);

        // A locale that tries to leave the folder is not used; the placeholder stays as it is.
        var evil = Temp();
        Write(evil, "manifest.json", Manifest().Replace("\"Tidy\"", "\"__MSG_extName__\"").Replace("}", ", \"default_locale\": \"..\\\\..\" }"));
        var (e, _) = ExtMeta.Read(evil);
        Need(e is not null && e.Name == "__MSG_extName__", "a hostile locale must not be followed");
        return Task.CompletedTask;
    }

    private static Task ManifestBad()
    {
        var none = Temp();
        var (m1, e1) = ExtMeta.Read(none);
        Need(m1 is null && e1!.Contains("manifest.json"), "no manifest should be named: " + e1);

        var broken = Temp();
        Write(broken, "manifest.json", "{ not json");
        var (m2, e2) = ExtMeta.Read(broken);
        Need(m2 is null && !string.IsNullOrEmpty(e2), "broken json should be explained");

        var empty = Temp();
        Write(empty, "manifest.json", """{ "name": "x" }""");
        var (m3, e3) = ExtMeta.Read(empty);
        Need(m3 is null && !string.IsNullOrEmpty(e3), "a manifest without a version is not an extension");

        var array = Temp();
        Write(array, "manifest.json", "[1,2]");
        Need(ExtMeta.Read(array).Meta is null, "a manifest that is not an object is refused");
        return Task.CompletedTask;
    }

    private static Task PermissionWords()
    {
        var dir = Temp();
        Write(dir, "manifest.json", Manifest(""", "permissions": ["storage", "tabs", "history", "webNavigation", "declarativeNetRequest", "madeUp"], "host_permissions": ["<all_urls>"] """));

        var can = ExtMeta.Read(dir).Meta!.Can;
        Need(can[0] == "Read and change all your data on all websites", "all sites should come first: " + can[0]);
        Need(!can.Any(c => c.Contains("storage")), "storage is not warned about");
        Need(can.Contains("Block content on any page"), "declarativeNetRequest should be explained");
        Need(can.Count(c => c == "Read your browsing history") == 1 && can.Contains("Read and change your browsing history"), "history lines");
        Need(can.Contains("Use the \"madeUp\" permission"), "an unknown permission is named as it is");
        return Task.CompletedTask;
    }

    private static Task PermissionSites()
    {
        var some = Temp();
        Write(some, "manifest.json", Manifest(""", "host_permissions": ["https://a.com/*", "https://*.b.org/x", "https://a.com/other"], "content_scripts": [{ "matches": ["https://c.net/*"] }] """));
        var can = ExtMeta.Read(some).Meta!.Can;
        Need(can.Count == 1 && can[0] == "Read and change your data on a.com, *.b.org, c.net", "named sites: " + string.Join("|", can));

        var many = Temp();
        Write(many, "manifest.json", Manifest(""", "host_permissions": ["https://a.com/*", "https://b.com/*", "https://c.com/*", "https://d.com/*"] """));
        Need(ExtMeta.Read(many).Meta!.Can[0] == "Read and change your data on 4 websites", "counted sites");

        // A content script on every page is the same as asking for every site.
        var all = Temp();
        Write(all, "manifest.json", Manifest(""", "content_scripts": [{ "matches": ["*://*/*"] }] """));
        Need(ExtMeta.Read(all).Meta!.Can[0] == "Read and change all your data on all websites", "content scripts count");
        return Task.CompletedTask;
    }

    private static Task StoreCopy()
    {
        var src = Temp();
        Write(src, "manifest.json", Manifest());
        Write(src, "_locales/en/messages.json", "{}");
        Write(src, "_metadata/verified_contents.json", "{}");
        Write(src, "js/a.js", "1");

        var outside = Temp();
        Write(outside, "secret.txt", "no");
        var linked = false;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(src, "link"), outside);
            linked = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // No permission to make links here: the rest is still checked.
        }

        var store = new ExtStore(Temp());
        var dir = store.Put(src, "Tidy Up!");
        var to = store.Full(dir);

        Need(dir.StartsWith("tidy-up-"), "folder name should come from the extension's name: " + dir);
        Need(File.Exists(Path.Combine(to, "manifest.json")) && File.Exists(Path.Combine(to, "js", "a.js")), "files should be copied");
        Need(File.Exists(Path.Combine(to, "_locales", "en", "messages.json")), "_locales must be kept");
        Need(!Directory.Exists(Path.Combine(to, "_metadata")), "_metadata must be left out");
        Need(!linked || !File.Exists(Path.Combine(to, "link", "secret.txt")), "links must not be followed");
        return Task.CompletedTask;
    }

    private static Task StoreTooBig()
    {
        var src = Temp();
        Write(src, "manifest.json", Manifest());
        Write(src, "big.bin", new string('x', 5000));

        var root = Temp();
        var store = new ExtStore(root) { MaxBytes = 1000 };

        try
        {
            store.Put(src, "Big");
            throw new Exception("should have been refused");
        }
        catch (InvalidOperationException ex)
        {
            Need(ex.Message.Contains("too big"), "the refusal should say why: " + ex.Message);
        }

        Need(Directory.GetDirectories(root).Length == 0, "a refused copy must leave no folder behind");
        return Task.CompletedTask;
    }

    private static Task StoreLink()
    {
        var src = Temp();
        Write(src, "manifest.json", Manifest());
        var root = Temp();

        var store = new ExtStore(root);
        var dir = store.Put(src, "Tidy");
        store.Link("liveid", dir);

        // A second store on the same folder is what the next start of the app sees.
        var again = new ExtStore(root);
        Need(again.Meta("liveid")?.Version == "1.2.3", "the version should be found again after a restart");
        Need(again.Meta("other") is null, "an extension without a copy has no details");

        again.Forget("liveid");
        Need(!Directory.Exists(again.Full(dir)), "forgetting deletes the copy");
        Need(again.Meta("liveid") is null, "and the link");
        Need(Directory.Exists(src), "the folder it was copied from is never touched");
        return Task.CompletedTask;
    }

    private static Task StoreEscape()
    {
        var root = Temp();
        var victim = Temp();
        Write(victim, "keep.txt", "mine");

        // A list that was edited by hand (or by something else) to point out of the store.
        Write(root, "extensions.json", $$"""{ "v": 1, "dirs": { "a": "..", "b": "{{victim.Replace("\\", "\\\\")}}" }, "gone": ["..", "{{victim.Replace("\\", "\\\\")}}"] }""");

        var store = new ExtStore(root);
        Need(store.Meta("a") is null && store.Meta("b") is null, "entries that are not plain folder names are ignored");

        store.Forget("a");
        store.Forget("b");
        store.Sweep();
        Need(File.Exists(Path.Combine(victim, "keep.txt")), "nothing outside the store may be deleted");
        return Task.CompletedTask;
    }

    private static Task StoreSweep()
    {
        var root = Temp();
        Directory.CreateDirectory(Path.Combine(root, "old-1234"));
        Write(root, "old-1234/manifest.json", "{}");
        Write(root, "extensions.json", """{ "v": 1, "dirs": {}, "gone": ["old-1234"] }""");

        new ExtStore(root).Sweep();
        Need(!Directory.Exists(Path.Combine(root, "old-1234")), "a folder on the gone list should be deleted");

        var left = File.ReadAllText(Path.Combine(root, "extensions.json"));
        Need(!left.Contains("old-1234"), "and taken off the list");
        return Task.CompletedTask;
    }

    // A profile in memory. Boom, when set, is thrown by every call.
    private sealed class Fake : IExtProf
    {
        private readonly List<ExtItem> _all = new();

        public Exception? Boom;

        public Task<List<ExtItem>> All()
        {
            if (Boom is not null)
                throw Boom;
            return Task.FromResult(_all.ToList());
        }

        public Task<ExtItem> Add(string folder)
        {
            if (Boom is not null)
                throw Boom;
            var x = new ExtItem { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileName(folder.Replace('\\', '/')), On = true };
            _all.Add(x);
            return Task.FromResult(x);
        }

        public Task<bool> Enable(string id, bool on)
        {
            if (Boom is not null)
                throw Boom;
            var i = _all.FindIndex(x => x.Id == id);
            if (i < 0)
                return Task.FromResult(false);
            _all[i] = new ExtItem { Id = id, Name = _all[i].Name, On = on };
            return Task.FromResult(true);
        }

        public Task<bool> Remove(string id)
        {
            if (Boom is not null)
                throw Boom;
            return Task.FromResult(_all.RemoveAll(x => x.Id == id) > 0);
        }
    }
}
