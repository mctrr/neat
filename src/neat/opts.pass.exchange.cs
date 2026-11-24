using System.Diagnostics;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace neat;

/// <summary>
/// The Passwords page, third part: the list of sites never to offer to save for, editing a login,
/// and moving logins in and out as CSV files (the format Chrome, Edge and Firefox use, with one more
/// column for the key of one-time codes), and saved cards and identities in and out as a second CSV file
/// of NEAT's own (no other browser has one).
///
/// A CSV file holds every password in plain text, and a file of cards every card number. NEAT says so each time it makes one or reads
/// one, makes the person pass the gate before exporting, never overwrites an existing file, and
/// offers to delete the file as soon as it has been written or imported.
/// </summary>
public sealed partial class Opts
{
    // A password export is a few hundred kilobytes at most; anything much bigger is not one.
    private const long ImportMaxBytes = 8L * 1024 * 1024;

    // ---- sites never to offer to save for ----

    private UIElement NeverCard()
    {
        var sites = App.Cfg.Cur.NeverSave;

        if (sites.Count == 0)
        {
            return Block.Card(_skin,
                Explain("无。当你在保存提示中选择“此网站从不保存”时，该网站会列在这里。"));
        }

        var rows = new List<UIElement>();
        foreach (var site in sites.OrderBy(s => s, StringComparer.Ordinal).ToList())
        {
            var origin = site;

            rows.Add(Block.Row(_skin, origin, "NEAT 不会在此网站提示保存密码。",
                IconBtn("\uE74D", "Offer to save passwords here again", () => ForgetNever(origin), true)));
        }

        return Block.Card(_skin, rows.ToArray());
    }

    private async Task ForgetNever(string origin)
    {
        if (Never.Remove(App.Cfg.Cur.NeverSave, origin))
            App.Cfg.Save();

        await RefreshPass();
    }

    // ---- editing a login ----

    private void TaskEdit(LoginInfo it)
    {
        var site = new TextBox { Width = 260, Text = it.Site };
        var user = new TextBox { Width = 260, Text = it.User };
        var pass = new PasswordBox { Width = 260 };

        // The key of the one-time codes: a secret like the password, so it is typed into a box that hides it,
        // and never shown again. An empty box keeps what is saved.
        var otp = new PasswordBox { Width = 260 };
        var drop = new CheckBox { Content = "移除已保存的验证码密钥", Visibility = it.Otp ? Visibility.Visible : Visibility.Collapsed };

        async Task Save()
        {
            var typed = site.Text.Trim();
            var origin = Origin.Of(typed.Contains("://") ? typed : "https://" + typed);

            if (origin is null)
            {
                Fail("输入网站地址，例如 example.com。");
                return;
            }

            var key = otp.Password.Trim();
            if (key.Length > 0 && drop.IsChecked != true && Totp.Parse(key) is null)
            {
                Fail("这不是一次性验证码密钥。请粘贴网站显示的密钥（字母和数字），或其二维码对应的 otpauth:// 地址。");
                return;
            }

            // An empty password box keeps the saved password.
            if (!App.Vault.Update(it.Id, origin, user.Text.Trim(), pass.Password.Length == 0 ? null : pass.Password))
            {
                Fail("无法修改该登录信息，它可能已被删除或在 NEAT 之外被修改。");
                return;
            }

            if (drop.IsChecked == true)
                App.Vault.SetOtp(it.Id, null);
            else if (key.Length > 0)
                App.Vault.SetOtp(it.Id, key);

            CloseTask();
            Say("Saved.");
            await RefreshPass();
        }

        ShowTask(
            "Edit a login",
            new UIElement[]
            {
                Block.Row(_skin, "网站", null, site),
                Block.Row(_skin, "用户名", null, user),
                Block.Row(_skin, "Password", "留空则保留已保存的密码。", pass),
                Block.Row(_skin, "一次性验证码密钥",
                    "用于两步验证的网站：设置验证器应用时网站显示的密钥。留空则保留已保存的内容。", otp),
                drop,
            },
            Btn("Save", Save),
            Btn("Cancel", () => Start(CloseTask)));
    }

    // ---- import and export ----

    private UIElement ExchangeCard()
    {
        var export = Btn("Export...", () => Start(TaskExport));
        export.IsEnabled = _prot != Protection.None;

        return Block.Card(_skin,
            Block.Row(_skin, "从文件导入",
                "从 Chrome、Edge、Firefox 或 NEAT 导出的 CSV 文件（登录信息及其验证码密钥、银行卡、身份信息）。",
                Btn("Import...", ImportStart)),
            Block.Row(_skin, "导出到文件",
                _prot == Protection.None
                    ? "请先设置 NEAT 密码：没有身份验证就无法导出任何内容。"
                    : "将所有密码和验证码密钥写入一个文本文件，银行卡和身份信息写入另一个文件。",
                export));
    }

    /// <summary>Asks for a CSV file, reads it and shows what importing it would do.</summary>
    private async Task ImportStart()
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".csv");

        var file = await picker.PickSingleFileAsync();
        if (file is null || _body is null)
            return;

        var path = file.Path;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Say("NEAT 只能读取此电脑上文件夹中的文件。");
            return;
        }

        if (new FileInfo(path).Length > ImportMaxBytes)
        {
            Say("该文件过大，不可能是密码导出文件。");
            return;
        }

        var text = await File.ReadAllTextAsync(path);

        // A file of cards and identities (made by NEAT) has its own way in.
        if (Exchange.LooksLikeItems(text))
        {
            ImportItemsStart(path, text);
            return;
        }

        var read = Exchange.Read(text);

        if (read.Problem is { } problem)
        {
            Say(problem);
            return;
        }

        var preview = App.Vault.Import(read.Rows, false);
        Say(string.Empty);
        TaskImport(path, read, preview);
    }

    private void TaskImport(string path, ReadResult read, ImportResult preview)
    {
        async Task Go()
        {
            var body = _body;
            var rows = read.Rows;

            var result = await Task.Run(() => App.Vault.Import(rows, true));
            if (_body != body)
                return;

            CloseTask();
            Say("Imported " + result.Added + " login" + (result.Added == 1 ? "." : "s."));
            await RefreshPass();

            TaskDeleteFile(path, "你导入的文件以明文保存了所有密码。");
        }

        var lines = new StringBuilder();
        lines.Append(read.Rows.Count + preview.Skipped).Append(" 文件中的登录信息：");
        lines.Append(preview.Added).Append(" new, ");
        lines.Append(preview.Already).Append(" 已保存");

        if (preview.Conflicts > 0)
            lines.Append(", ").Append(preview.Conflicts).Append(" 密码与已保存的不同（已保存的保持不变）");

        if (preview.Skipped + read.Skipped > 0)
            lines.Append(", ").Append(preview.Skipped + read.Skipped).Append(" 已跳过（非网站或无密码）");

        lines.Append('.');

        ShowTask(
            "导入登录信息",
            new UIElement[]
            {
                Explain(lines.ToString()),
                Explain("该文件以明文保存了所有密码。导入后 NEAT 会建议删除它。"),
            },
            Btn("Import", Go),
            Btn("Cancel", () => Start(CloseTask)));
    }

    /// <summary>Shows what importing a file of cards and identities would do.</summary>
    private void ImportItemsStart(string path, string text)
    {
        var read = Exchange.ReadItems(text);
        if (read.Problem is { } problem)
        {
            Say(problem);
            return;
        }

        var preview = App.Vault.ImportItems(read.Rows, false);
        Say(string.Empty);

        async Task Go()
        {
            var body = _body;
            var rows = read.Rows;

            var result = await Task.Run(() => App.Vault.ImportItems(rows, true));
            if (_body != body)
                return;

            CloseTask();
            Say("Imported " + result.Added + " card" + (result.Added == 1 ? " or identity." : "s and identities."));
            await RefreshPass();

            TaskDeleteFile(path, "你导入的文件以明文保存了所有银行卡号。");
        }

        var lines = new StringBuilder();
        lines.Append(read.Rows.Count + preview.Skipped + read.Skipped).Append(" 文件中的银行卡和身份信息：");
        lines.Append(preview.Added).Append(" new, ").Append(preview.Already).Append(" 已保存");

        if (preview.Skipped + read.Skipped > 0)
            lines.Append(", ").Append(preview.Skipped + read.Skipped).Append(" 已跳过（非银行卡或身份信息）");

        lines.Append('.');

        ShowTask(
            "导入银行卡和身份信息",
            new UIElement[]
            {
                Explain(lines.ToString()),
                Explain("The file holds every card number in plain text. After the import NEAT offers to delete it."),
            },
            Btn("Import", Go),
            Btn("Cancel", () => Start(CloseTask)));
    }

    /// <summary>The card that offers to delete a plain-text password file that is no longer needed.</summary>
    private void TaskDeleteFile(string path, string why)
    {
        ShowTask(
            "删除该文件？",
            new UIElement[]
            {
                Explain(why + " 任何能打开它的人都能读取全部内容，用完请尽快删除。"),
                Explain(path),
            },
            Btn("删除文件", () =>
            {
                try
                {
                    File.Delete(path);
                    CloseTask();
                    Say("文件已删除。");
                }
                catch (Exception ex)
                {
                    Log.Write("pass", "无法删除文件（" + ex.GetType().Name + ")");
                    Fail("无法删除该文件，请手动删除。");
                }

                return Task.CompletedTask;
            }),
            Btn("保留", () => Start(CloseTask)));
    }

    private void TaskExport()
    {
        async Task Go()
        {
            var access = await Authorize("导出所有已保存的密码");
            if (access is null || _body is null)
                return;

            var result = App.Vault.Export(access);
            var items = App.Vault.ExportItems(access);

            // Never over a file that is already there.
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(folder, "neat-passwords-" + stamp + ".csv");

            var bytes = new UTF8Encoding(false).GetBytes(Exchange.Write(result.Rows));
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(bytes);

            var paths = new List<string> { path };

            // Cards and identities are their own file: no other browser reads them, and a file that holds
            // card numbers is better not mixed with the logins.
            if (items.Rows.Count > 0)
            {
                var itemsPath = Path.Combine(folder, "neat-cards-" + stamp + ".csv");
                var itemBytes = new UTF8Encoding(false).GetBytes(Exchange.WriteItems(items.Rows));
                await using (var stream = new FileStream(itemsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await stream.WriteAsync(itemBytes);

                paths.Add(itemsPath);
            }

            // The counts only: what was exported is not logged.
            Log.Write("pass", "exported " + result.Rows.Count + " 条登录信息和 " + items.Rows.Count + " 条银行卡和身份信息");

            var damaged = result.Damaged + items.Damaged;
            CloseTask();
            Say("Exported " + result.Rows.Count + " login" + (result.Rows.Count == 1 ? string.Empty : "s")
                + (items.Rows.Count > 0 ? " and " + items.Rows.Count + " card" + (items.Rows.Count == 1 ? " or identity" : "s and identities") : string.Empty) + "."
                + (damaged > 0 ? " " + damaged + " could not be read and were left out." : string.Empty));

            TaskExported(paths);
        }

        ShowTask(
            "导出登录信息",
            new UIElement[]
            {
                Explain("这会把所有密码和一次性验证码密钥写入下载文件夹中的文本文件，"
                    + "银行卡和身份信息写入第二个文件。任何能打开这些文件的人"
                    + "都能读取全部内容，而且它们可能进入备份和云盘。"
                    + "系统会先要求验证 Windows Hello（或 NEAT 密码）。"),
            },
            Btn("Export", Go),
            Btn("Cancel", () => Start(CloseTask)));
    }

    private void TaskExported(IReadOnlyList<string> paths)
    {
        var shown = new List<UIElement>();
        foreach (var path in paths)
            shown.Add(Explain(path));

        shown.Add(Explain("这些文件以明文保存了所有密码和卡号，用完请尽快删除。"));

        ShowTask(
            "密码已导出",
            shown.ToArray(),
            Btn("在文件夹中显示", () =>
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + paths[0] + "\"") { UseShellExecute = true });
                return Task.CompletedTask;
            }),
            Btn(paths.Count == 1 ? "删除文件" : "删除文件", () =>
            {
                var left = 0;
                foreach (var path in paths)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        left++;
                        Log.Write("pass", "无法删除文件（" + ex.GetType().Name + ")");
                    }
                }

                CloseTask();

                if (left == 0)
                    Say(paths.Count == 1 ? "文件已删除。" : "文件已删除。");
                else
                    Fail("部分文件无法删除，请手动删除。");

                return Task.CompletedTask;
            }),
            Btn("Done", () => Start(CloseTask)));
    }
}
