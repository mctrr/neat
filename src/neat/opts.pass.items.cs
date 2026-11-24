using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace neat;

/// <summary>
/// The Passwords page, fourth part: saved cards and identities (vault/items.cs). They are listed with a
/// name and a short line (the last four digits of a card), and editing or deleting one passes the gate,
/// as for a login. A task opens as a card on the page itself, like adding a login.
/// </summary>
public sealed partial class Opts
{
    private List<ItemInfo> _cards = new();
    private List<ItemInfo> _identities = new();

    // What the form of a kind asks for: the saved field, its title, a note, and whether it is hidden as typed.
    private static readonly (string Key, string Title, string? Note, bool Secret)[] CardForm =
    {
        ("label", "Name", "列表显示的名称，留空则用卡号命名。", false),
        ("holder", "持卡人姓名", null, false),
        ("number", "卡号", "12 to 19 digits.", false),
        ("expiry", "Expires", "月份和年份，例如 08/27。", false),
        ("cvc", "安全码", "卡上的 3 或 4 位数字，可选。", true),
    };

    private static readonly (string Key, string Title, string? Note, bool Secret)[] IdentityForm =
    {
        ("label", "Name", "列表显示的名称，留空则用姓名命名。", false),
        ("first", "名", null, false),
        ("last", "姓", null, false),
        ("email", "邮箱", null, false),
        ("phone", "电话", null, false),
        ("org", "公司", null, false),
        ("street", "Street", null, false),
        ("street2", "Street, line 2", null, false),
        ("city", "城市", null, false),
        ("state", "州/省", null, false),
        ("zip", "邮政编码", null, false),
        ("country", "国家", null, false),
    };

    private static string Noun(string kind)
    {
        return kind == ItemKind.Card ? "card" : "identity";
    }

    private UIElement ItemsCard(string kind)
    {
        var items = kind == ItemKind.Card ? _cards : _identities;
        var rows = new List<UIElement>
        {
            Block.Row(_skin, "Add a " + Noun(kind),
                kind == ItemKind.Card
                    ? "像密码一样加密保存在此，只有在输入框点击密钥按钮选择后才会填入支付表单。"
                    : "像密码一样加密保存在此，只有在输入框点击密钥按钮选择后才会填入表单。",
                Btn("Add", () => Start(() => TaskItem(kind, null, null)))),
        };

        foreach (var item in items)
        {
            rows.Add(new Border { Height = 1, Background = _skin.Line });
            rows.Add(ItemRow(item));
        }

        return Block.Card(_skin, rows.ToArray());
    }

    private UIElement ItemRow(ItemInfo it)
    {
        var usable = _prot != Protection.None;

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Children.Add(IconBtn("", "Edit", () => EditItem(it), usable && !it.Damaged));
        tools.Children.Add(IconBtn("", "Delete", () => DeleteItem(it), usable));

        return Block.Row(_skin, it.Label, it.Damaged ? "此项校验失败：它在 NEAT 之外被修改过。" : it.Summary, tools);
    }

    private async Task EditItem(ItemInfo it)
    {
        var access = await Authorize("Edit the saved " + Noun(it.Kind) + " " + it.Label);
        if (access is null || _body is null)
            return;

        var fields = App.Vault.ReadItem(it.Id, access);
        if (fields is null)
        {
            Say("无法打开此项，它可能已在 NEAT 之外被修改。");
            return;
        }

        TaskItem(it.Kind, it, fields);
    }

    private async Task DeleteItem(ItemInfo it)
    {
        var access = await Authorize("Delete the saved " + Noun(it.Kind) + " " + it.Label);
        if (access is null || _body is null)
            return;

        TaskItemDelete(it);
    }

    private void TaskItem(string kind, ItemInfo? existing, Dictionary<string, string>? values)
    {
        var form = kind == ItemKind.Card ? CardForm : IdentityForm;
        var boxes = new Dictionary<string, Control>();
        var rows = new List<UIElement>();

        foreach (var (key, title, note, secret) in form)
        {
            var text = values is not null && values.TryGetValue(key, out var have) ? have : string.Empty;

            Control box = secret
                ? new PasswordBox { Width = 260, Password = text }
                : new TextBox { Width = 260, Text = text };

            boxes[key] = box;
            rows.Add(Block.Row(_skin, title, note, box));
        }

        string Read(string key)
        {
            return boxes[key] switch
            {
                PasswordBox p => p.Password,
                TextBox t => t.Text,
                _ => string.Empty,
            };
        }

        async Task Save()
        {
            var fields = form.ToDictionary(f => f.Key, f => Read(f.Key));

            try
            {
                if (existing is null)
                {
                    App.Vault.AddItem(kind, fields);
                }
                else if (!App.Vault.UpdateItem(existing.Id, fields))
                {
                    Fail("That " + Noun(kind) + " could not be changed. It may have been deleted.");
                    return;
                }
            }
            catch (ArgumentException ex)
            {
                // The sentence is meant for the person (vault/items.cs).
                Fail(ex.Message);
                return;
            }

            CloseTask();
            Say("Saved.");
            await RefreshPass();
        }

        ShowTask(
            (existing is null ? "Add a " : "Edit a ") + Noun(kind),
            rows,
            Btn("Save", Save),
            Btn("Cancel", () => Start(CloseTask)));
    }

    private void TaskItemDelete(ItemInfo it)
    {
        async Task Save()
        {
            var access = await Authorize("Delete the saved " + Noun(it.Kind) + " " + it.Label);
            if (access is null || _body is null)
                return;

            App.Vault.RemoveItem(it.Id);

            CloseTask();
            Say("Deleted.");
            await RefreshPass();
        }

        ShowTask(
            "Delete a " + Noun(it.Kind),
            new UIElement[] { Explain("Delete " + it.Label + "？此操作无法撤销。") },
            Btn("Delete", Save),
            Btn("Cancel", () => Start(CloseTask)));
    }
}
