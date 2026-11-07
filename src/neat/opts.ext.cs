using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace neat;

/// <summary>
/// The Extensions page: the extensions that are installed, each with a switch and a remove button,
/// and a way to add one from a folder. What is installed and what it may do come from web/ext.cs
/// (the profile) and web/extmeta.cs (the manifest); where the copies live is web/extstore.cs.
/// This file only draws them and passes on what the person does.
///
/// It keeps the rules of the Passwords page (opts.pass.cs): every handler goes through Safe, slow
/// work runs on a pool thread, and after every wait the page checks it is still on show before it
/// touches the controls. A task (adding, removing) opens as a card on the page, like there, and the
/// page shares that page's feedback line and card (_note, _task) because only one is on show.
/// </summary>
public sealed partial class Opts
{
    // Where the page stands in Pages (opts.xaml.cs).
    private const int ExtAt = 3;

    // What the person is told, once, about what an extension can do here.
    private const string ExtLimits =
        "NEAT runs an extension's pages, its content scripts and its background code in every tab. "
        + "WebView2 supports only part of the Chrome extension API, though, and it draws none of an extension's own "
        + "buttons or popups, so what needs those cannot be used. A page that is already open needs a reload "
        + "before it sees a new extension.";

    // The copies NEAT keeps of the extensions. One for the whole app, used from the UI thread and, for the slow parts, a pool thread.
    private static ExtStore? _xs;

    private static ExtStore Xs => _xs ??= new ExtStore(Path.Combine(Env.Root, "Extensions"));

    /// <summary>
    /// Runs bookkeeping on a pool thread: writing the list of copies, deleting a folder. A failure
    /// is logged and goes no further, because the extension itself has already been added or
    /// removed by then and the page must go on.
    /// </summary>
    private static Task Keep(Action work)
    {
        return Task.Run(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("ext", ex, "extension copies could not be kept up to date");
            }
        });
    }

    // ---- building ----

    private UIElement BuildExt()
    {
        _note = new TextBlock
        {
            FontSize = 12,
            Foreground = _skin.Dim,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 12, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _task = new StackPanel();
        _body = new StackPanel();

        _ = Safe(RefreshExt);

        return Block.Page(_note, _task, _body);
    }

    /// <summary>Leaves the page and lets go of its controls.</summary>
    private void LeaveExt()
    {
        _body = null;
        _task = null;
        _note = null;
        _taskMsg = null;
    }

    /// <summary>Reads what is installed and draws the page again.</summary>
    private async Task RefreshExt()
    {
        var body = _body;
        if (body is null)
            return;

        // Copies left over from an earlier removal that the browser still held are deleted now.
        await Keep(() => Xs.Sweep());

        var live = Ext.Live;
        var all = live ? await Ext.All() : new List<ExtItem>();

        // The details come from the manifests, which are files: read off the UI thread.
        var found = await Task.Run(() => all.Select(x => (Item: x, Meta: Xs.Meta(x.Id))).ToList());

        // The person may have gone to another page while this was reading.
        if (_body != body)
            return;

        var rows = new List<UIElement>();

        if (!Ext.Can)
        {
            rows.Add(Explain(Ext.Why));
        }
        else if (!live)
        {
            rows.Add(Explain("The browser is still starting. Open this page again in a moment."));
        }
        else
        {
            rows.Add(Block.Row(_skin, "Add an extension",
                "Pick the folder that holds the extension's manifest.json. NEAT keeps its own copy, so the folder can be moved or deleted afterwards.",
                Btn("Add...", AddExt)));

            if (found.Count == 0)
                rows.Add(Explain("No extensions yet."));

            foreach (var (item, meta) in found)
                rows.Add(ExtRow(item, meta));
        }

        body.Children.Clear();
        body.Children.Add(Block.Head(_skin, "Extensions"));
        body.Children.Add(Block.Card(_skin, rows.ToArray()));
        body.Children.Add(Block.Head(_skin, "What to expect"));
        body.Children.Add(Block.Card(_skin, Explain(ExtLimits)));
    }

    private UIElement ExtRow(ExtItem x, ExtMeta? m)
    {
        var name = x.Name.Length > 0 ? x.Name : m?.Name ?? "Extension";

        // The set-before-handler rule of the other pages: building the row switches nothing.
        var sw = new ToggleSwitch { IsOn = x.On, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        var quiet = false;
        sw.Toggled += async (s, e) =>
        {
            if (quiet)
                return;

            await Safe(async () =>
            {
                var res = await Ext.Enable(x.Id, sw.IsOn);
                if (res.Ok)
                {
                    Say(string.Empty);
                    return;
                }

                // It did not change, so the switch goes back to what the browser has.
                quiet = true;
                sw.IsOn = !sw.IsOn;
                quiet = false;
                Say(res.Err ?? "That did not work.");
            });
        };

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Children.Add(sw);
        tools.Children.Add(IconBtn("\uE74D", "Remove", () => Start(() => TaskRemoveExt(x, name, m is not null)), true));

        return Block.Row(_skin, name, Describe(m), tools);
    }

    // "Version 1.2.3 · what it does", or nothing when NEAT has no copy to read.
    private static string? Describe(ExtMeta? m)
    {
        if (m is null)
            return null;

        var desc = m.Description.Length > 160 ? m.Description[..160].TrimEnd() + "..." : m.Description;
        return desc.Length == 0 ? "Version " + m.Version : "Version " + m.Version + Sep + desc;
    }

    // ---- adding ----

    /// <summary>Asks for a folder, reads its manifest and shows what the extension is and what it may do.</summary>
    private async Task AddExt()
    {
        var body = _body;

        // A picker in an unpackaged app needs the window it belongs to.
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || _body != body)
            return;

        var path = folder.Path;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            Say("NEAT can only read a folder on this PC.");
            return;
        }

        var (meta, err) = await Task.Run(() => ExtMeta.Read(path));
        if (_body != body)
            return;

        if (meta is null)
        {
            Say(err ?? "That folder is not an extension.");
            return;
        }

        Say(string.Empty);
        TaskAddExt(path, meta);
    }

    private void TaskAddExt(string path, ExtMeta meta)
    {
        var busy = false;

        async Task Go()
        {
            // A second click while the first is working would add it twice.
            if (busy)
                return;

            busy = true;
            var body = _body;

            // The copy is what is added, at the place it will stay: WebView2 remembers the folder,
            // and the extension is gone the day that folder moves or changes.
            string dir;
            try
            {
                dir = await Task.Run(() => Xs.Put(path, meta.Name));
            }
            catch (Exception ex)
            {
                Log.Error("ext", ex, "copying an extension failed");
                Fail(ex is InvalidOperationException ? ex.Message : "NEAT could not copy that folder.");
                busy = false;
                return;
            }

            var res = await Ext.Add(Xs.Full(dir));
            if (!res.Ok || res.Item is null)
            {
                await Task.Run(() => Xs.Discard(dir));
                Fail(res.Err ?? "The extension could not be added.");
                busy = false;
                return;
            }

            // Without this link the extension still works; only its version and description are lost.
            await Keep(() => Xs.Link(res.Item.Id, dir));

            if (_body != body)
                return;

            CloseTask();
            Say("Added " + (res.Item.Name.Length > 0 ? res.Item.Name : meta.Name) + ".");
            await RefreshExt();
        }

        var rows = new List<UIElement>
        {
            Block.Row(_skin, meta.Name, meta.Description.Length > 0 ? meta.Description : null, Block.Value(_skin, "Version " + meta.Version)),
            Explain(meta.Can.Count == 0
                ? "It asks for no special permissions."
                : "It can:\n" + string.Join("\n", meta.Can.Select(c => "\u2022 " + c))),
        };

        if (meta.Mv == 2)
            rows.Add(Explain("This extension uses the older Manifest V2 format, which current versions of Chromium may refuse to run."));

        ShowTask("Add an extension", rows, Btn("Add extension", Go), Btn("Cancel", () => Start(CloseTask)));
    }

    // ---- removing ----

    private void TaskRemoveExt(ExtItem x, string name, bool hasCopy)
    {
        async Task Go()
        {
            var body = _body;

            var res = await Ext.Remove(x.Id);
            if (!res.Ok)
            {
                Fail(res.Err ?? "The extension could not be removed.");
                return;
            }

            // It is gone from the browser, so its copy goes too, whether or not the page is still on show.
            await Keep(() => Xs.Forget(x.Id));

            if (_body != body)
                return;

            CloseTask();
            Say("Removed " + name + ".");
            await RefreshExt();
        }

        ShowTask(
            "Remove an extension",
            new UIElement[]
            {
                Explain(hasCopy
                    ? "Remove " + name + "? NEAT deletes its own copy of the extension. This cannot be undone."
                    : "Remove " + name + "? This cannot be undone."),
            },
            Btn("Remove", Go),
            Btn("Cancel", () => Start(CloseTask)));
    }
}
