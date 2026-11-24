using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace neat;

/// <summary>The General page: what opens at startup, the home page and the search engine.</summary>
public sealed partial class Opts
{
    // The controls of this page while it is on show; null when another page is.
    private TextBox? _home;
    private ComboBox? _box;

    private UIElement BuildGen()
    {
        _home = new TextBox { Width = 280, Text = App.Cfg.Cur.Home };

        // Saved on Enter or when the box loses focus, never on every key.
        _home.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                Flush();
        };
        _home.LostFocus += (s, e) => Flush();

        var box = new ComboBox { Width = 230 };
        foreach (var x in App.Cfg.Cur.Engines)
            box.Items.Add(x.Name);

        // Show the engine that is really in use: when the saved name matches
        // nothing, Search falls back to the first engine, and so does the list.
        // The index is set before the handler is added, so filling the list saves nothing.
        var use = App.Find.Use.Name;
        box.SelectedIndex = App.Cfg.Cur.Engines.FindIndex(x => x.Name == use);
        box.SelectionChanged += (s, e) =>
        {
            var prefs = App.Cfg.Cur;
            var i = box.SelectedIndex;
            if (i < 0 || i >= prefs.Engines.Count || prefs.Engines[i].Name == prefs.Use)
                return;

            prefs.Use = prefs.Engines[i].Name;
            App.Cfg.Save();
        };
        _box = box;

        // Button that opens the engine manager.
        var manage = new Button { Content = "管理…", Width = 78 };
        manage.Click += async (s, e) => await ManageEngines();

        var engineControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        engineControls.Children.Add(box);
        engineControls.Children.Add(manage);

        // Which of the two is on is set before the handlers are added, so building the page saves nothing.
        var start = App.Cfg.Cur.Start;
        var home = Block.Choice(_skin, "打开主页", "start", start == StartMode.Home);
        var last = Block.Choice(_skin, "继续上次的会话", "start", start == StartMode.Last);
        home.Checked += (s, e) => SetStart(StartMode.Home);
        last.Checked += (s, e) => SetStart(StartMode.Last);

        // Privacy. Each control is set before its handler is added, so building the page saves nothing.
        var fp = App.Cfg.Cur.Fp;
        var level = new ComboBox { Width = 160 };
        foreach (var n in new[] { "关闭", "标准", "严格" })
            level.Items.Add(n);
        level.SelectedIndex = FpLevel.Clamp(fp.Default);
        level.SelectionChanged += (s, e) =>
        {
            if (level.SelectedIndex < 0 || level.SelectedIndex == App.Cfg.Cur.Fp.Default)
                return;

            App.Cfg.Cur.Fp.Default = level.SelectedIndex;
            App.Cfg.Save();
            Fp.RaiseChanged();
        };

        var gpc = new ToggleSwitch { IsOn = fp.Gpc, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        gpc.Toggled += (s, e) =>
        {
            App.Cfg.Cur.Fp.Gpc = gpc.IsOn;
            App.Cfg.Save();
            Fp.RaiseChanged();
        };

        var rtc = new ToggleSwitch { IsOn = fp.WebRtc, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        rtc.Toggled += (s, e) =>
        {
            App.Cfg.Cur.Fp.WebRtc = rtc.IsOn;
            App.Cfg.Save();
        };

        return Block.Page(
            Block.Head(_skin, "启动时"),
            Block.Card(_skin, home, last),
            Block.Head(_skin, "主页"),
            Block.Card(_skin,
                Block.Row(_skin, "主页", "当上方选择\"打开主页\"时启动打开，按 Alt+Home 也可打开。", _home)),
            Block.Head(_skin, "搜索"),
            Block.Card(_skin,
                Block.Row(_skin, "搜索引擎", "用于地址栏中输入的非网址内容。", engineControls)),
            Block.Head(_skin, "隐私"),
            Block.Card(_skin,
                Block.Row(_skin, "指纹保护",
                    "网站可以通过绘制、声音、屏幕等细节识别设备。此功能会改变它们读取到的信息，且每个网站、每次启动 NEAT 都不同。标准模式保证页面正常；严格模式更彻底，但可能让某些页面出错。可在 NEAT 拦截面板中按网站单独设置。",
                    level),
                Block.Row(_skin, "不要出售或共享我的数据",
                    "向受保护页面上的网站发送全局隐私控制（GPC）信号。",
                    gpc),
                Block.Row(_skin, "阻止 WebRTC 暴露我的网络地址",
                    "浏览器中的通话将只走普通流量路由，因此不会绕过 VPN。重启 NEAT 后生效。",
                    rtc)));
    }

    /// <summary>
    /// Opens a dialog to add, edit and remove search engines. Changes are saved
    /// to settings.json and the drop-down on this page is refreshed in place.
    /// </summary>
    private async Task ManageEngines()
    {
        var list = new List<Engine>(App.Cfg.Cur.Engines.Select(e => new Engine
        {
            Name = e.Name, Url = e.Url, Key = e.Key,
        }));

        var panel = new StackPanel { Spacing = 10 };
        var rows = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "网址模板中的 %s 会被替换为搜索词。快捷键用于 !键 快速搜索。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        panel.Children.Add(rows);

        // Rebuild the editable rows from the working copy.
        void Render()
        {
            rows.Children.Clear();
            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                var name = new TextBox { Text = list[i].Name, PlaceholderText = "名称", Header = "名称" };
                var url = new TextBox { Text = list[i].Url, PlaceholderText = "https://...%s", Header = "网址模板" };
                var key = new TextBox { Text = list[i].Key, PlaceholderText = "a", Header = "快捷键", MaxLength = 8 };
                var del = new Button { Content = "删除" };

                name.TextChanged += (s, e) => list[index].Name = name.Text;
                url.TextChanged += (s, e) => list[index].Url = url.Text;
                key.TextChanged += (s, e) => list[index].Key = key.Text.Trim();
                del.Click += (s, e) => { list.RemoveAt(index); Render(); };

                var grid = new Grid { ColumnSpacing = 8 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                grid.Children.Add(name); Grid.SetColumn(name, 0);
                grid.Children.Add(url); Grid.SetColumn(url, 1);
                grid.Children.Add(key); Grid.SetColumn(key, 2);
                grid.Children.Add(del); Grid.SetColumn(del, 3);
                Grid.SetRow(del, 0);
                del.VerticalAlignment = VerticalAlignment.Bottom;
                rows.Children.Add(grid);
            }
        }
        Render();

        var add = new Button { Content = "添加引擎" };
        add.Click += (s, e) => { list.Add(new Engine { Name = "", Url = "https://", Key = "" }); Render(); };
        panel.Children.Add(add);

        var dialog = new ContentDialog
        {
            XamlRoot = Content?.XamlRoot,
            Title = "管理搜索引擎",
            Content = new ScrollViewer { Content = panel, MaxHeight = 380 },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = App.Look.Cur.Dark ? ElementTheme.Dark : ElementTheme.Light,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        // Keep only complete entries, and make sure names and keys are unique.
        var clean = new List<Engine>();
        foreach (var e in list)
        {
            var n = e.Name?.Trim() ?? "";
            var u = e.Url?.Trim() ?? "";
            var k = (e.Key?.Trim() ?? "").ToLowerInvariant();
            if (n.Length == 0 || u.Length == 0 || !u.Contains("%s"))
                continue;
            if (clean.Any(x => x.Name == n) || (k.Length > 0 && clean.Any(x => x.Key == k)))
                continue;
            clean.Add(new Engine { Name = n, Url = u, Key = k });
        }
        if (clean.Count == 0)
            return;

        var prefs = App.Cfg.Cur;
        var stillUsed = prefs.Engines.FirstOrDefault(x => x.Name == prefs.Use)?.Name;
        prefs.Engines = clean;
        if (stillUsed is null || !clean.Any(x => x.Name == stillUsed))
            prefs.Use = clean[0].Name;
        App.Cfg.Save();

        // Refresh the drop-down without leaving the page.
        if (_box is not null)
        {
            var chosen = App.Find.Use.Name;
            _box.Items.Clear();
            foreach (var x in clean)
                _box.Items.Add(x.Name);
            _box.SelectedIndex = clean.FindIndex(x => x.Name == chosen);
        }
    }

    private static void SetStart(StartMode mode)
    {
        if (App.Cfg.Cur.Start == mode)
            return;

        App.Cfg.Cur.Start = mode;
        App.Cfg.Save();
    }

    /// <summary>
    /// Saves the home page if it was edited. The text is stored as typed; it is
    /// turned into an address (App.Find.Resolve) when it is used.
    /// </summary>
    private void Flush()
    {
        if (_home is null)
            return;

        var text = _home.Text;

        // An empty home page is not allowed: the store would put the default back at the
        // next start. So the box goes back to what is saved instead of saving nothing.
        if (string.IsNullOrWhiteSpace(text))
        {
            _home.Text = App.Cfg.Cur.Home;
            return;
        }

        if (text == App.Cfg.Cur.Home)
            return;

        App.Cfg.Cur.Home = text;
        App.Cfg.Save();
    }
}
