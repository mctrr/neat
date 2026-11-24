# -*- coding: utf-8 -*-
"""Translation script for NEAT browser UI.
Only replaces C# string literals that EXACTLY match a key in the per-file map.
Never touches log messages, identifiers, or partial substrings.
"""
import os, re

ROOT = r'D:\neat-rebuild\src\neat'

M = {}

M['opts.xaml.cs'] = {
    'General': '常规', 'Appearance': '外观', 'Passwords': '密码',
    'Extensions': '扩展', 'Shortcuts': '快捷键', 'About': '关于',
    'Settings': '设置',
}

M['opts.gen.cs'] = {
    'Open the home page': '打开主页',
    'Continue where you left off': '继续上次的会话',
    'On startup': '启动时',
    'Home page': '主页',
    'Opens at startup when \\"Open the home page\\" is chosen above, and with Alt+Home.':
        '当上方选择\\"打开主页\\"时启动打开，按 Alt+Home 也可打开。',
    'Search engine': '搜索引擎',
    'Used for anything typed in the address bar that is not a web address.':
        '用于地址栏中输入的非网址内容。',
    'Fingerprint protection': '指纹保护',
    'Do not sell or share my data': '不要出售或共享我的数据',
    'Sends the Global Privacy Control signal to sites on pages that are protected.':
        '向受保护页面上的网站发送全局隐私控制（GPC）信号。',
    'Keep WebRTC from showing my network addresses': '阻止 WebRTC 暴露我的网络地址',
    'Calls in the browser then only use the route normal traffic takes, so a VPN is not bypassed. Takes effect after NEAT is restarted.':
        '浏览器中的通话将只走普通流量路由，因此不会绕过 VPN。重启 NEAT 后生效。',
    'Off': '关闭', 'Standard': '标准', 'Strict': '严格',
}

M['opts.look.cs'] = {
    'Window colour': '窗口颜色',
}

M['opts.about.cs'] = {
    'Not loaded yet': '尚未加载',
    'Data folder': '数据文件夹',
    'Settings, history, bookmarks and the web profile.': '设置、历史记录、书签和网页配置。',
    'Bundled ships inside NEAT; System is the one installed in Windows.':
        '内置版随 NEAT 一起提供；系统版是安装在 Windows 中的那个。',
    'Bundled': '内置', 'System': '系统', 'Version': '版本',
    'Open folder': '打开文件夹', 'Open': '打开',
}

M['opts.ext.cs'] = {
    'An extension can read and change the pages you visit, so only install ones you trust. ':
        '扩展可以读取和修改你访问的页面，请只安装你信任的扩展。',
    'NEAT runs an extension\'s pages, its content scripts and its background code in every tab. ':
        'NEAT 会在每个标签页中运行扩展的页面、内容脚本和后台代码。',
    'It supports only part of the Chrome extension API, though, and it does not draw an extension\'s own ':
        '不过它只支持部分 Chrome 扩展 API，也不会绘制扩展自己的',
    'toolbar button or popup, so whatever needs those cannot be used.':
        '工具栏按钮或弹窗，因此依赖这些功能的应用无法使用。',
    'Open pages may need a reload for this to apply.': '已打开的页面可能需要刷新才能生效。',
    'The browser is still starting. Open this page again in a moment.': '浏览器正在启动，请稍后重新打开此页。',
    'Before you add one': '添加之前',
    'From folder...': '从文件夹...',
    'From file...': '从文件...',
    'Add an extension': '添加扩展',
    'Add extension': '添加扩展',
    'Pick the folder that holds the extension\'s manifest.json, or a .crx or .zip file. NEAT keeps its own copy, so the original can be moved or deleted afterwards.':
        '选择包含扩展 manifest.json 的文件夹，或 .crx / .zip 文件。NEAT 会保留自己的副本，因此原件之后可以移动或删除。',
    'No extensions installed': '未安装扩展',
    'turned on ': '已启用 ',
    'turned off ': '已停用 ',
    'Turned on ': '已启用 ',
    'Turned off ': '已停用 ',
    'That did not work.': '操作失败。',
    'NEAT can only read a folder on this PC.': 'NEAT 只能读取此电脑上的文件夹。',
    'That folder is not an extension.': '该文件夹不是扩展。',
    'NEAT can only read a file on this PC.': 'NEAT 只能读取此电脑上的文件。',
    'That file could not be opened.': '无法打开该文件。',
    'That package is not an extension.': '该安装包不是扩展。',
    'NEAT could not keep a copy of that extension.': 'NEAT 无法保存该扩展的副本。',
    'The extension could not be added.': '无法添加该扩展。',
    'It asks for no special permissions.': '它不请求任何特殊权限。',
    'This extension uses the older Manifest V2 format, which current versions of Chromium may refuse to run.':
        '此扩展使用较旧的 Manifest V2 格式，当前版本的 Chromium 可能拒绝运行。',
    'The extension could not be removed.': '无法移除该扩展。',
    'Remove an extension': '移除扩展',
    '? NEAT deletes its own copy of the extension. This cannot be undone.':
        '？NEAT 将删除该扩展的自身副本，此操作无法撤销。',
    '? This cannot be undone.': '？此操作无法撤销。',
    'Remove': '移除', 'Keep': '保留', 'Cancel': '取消', 'OK': '确定',
}

M['win.bar.cs'] = {
    'Copy link': '复制链接',
    'New tab': '新建标签页',
    'New Tab': '新建标签页',
    'Search or enter address': '搜索或输入网址',
    'Search or enter address (Ctrl+L)': '搜索或输入网址（Ctrl+L）',
}
M['win.side.cs'] = {
    'Toggle sidebar': '切换侧边栏',
    'Show sidebar over the page': '在页面上显示侧边栏',
}
M['tab.cs'] = {'New Tab': '新建标签页'}
M['win.keys.cs'] = {
    'Close tab': '关闭标签页',
    'Next tab': '下一个标签页',
    'Previous tab': '上一个标签页',
    'Reopen closed tab': '重新打开关闭的标签页',
    'Go to tab': '跳转到标签页',
    'Go to last tab': '跳转到最后一个标签页',
    'Go to home page': '前往主页',
    'Reload without the cache': '强制刷新（不用缓存）',
    'Bookmark this page': '收藏此页面',
    'Close window': '关闭窗口',
    'Full screen': '全屏',
    'Open History': '打开历史记录',
    'Open Downloads': '打开下载',
    'Open Bookmarks': '打开书签',
}
M['win.field.cs'] = {
    'Suggest a password': '建议密码',
    'Fill a one-time code': '填入一次性验证码',
    'Fill a saved card': '填入已保存的银行卡',
    'Fill a saved identity': '填入已保存的身份信息',
    'Fill a saved login': '填入已保存的登录信息',
}

M['libhist.cs'] = {
    'Search history': '搜索历史记录',
    'Clear all history...': '清除所有历史记录...',
    'Clear all history?': '清除所有历史记录？',
    'Every page in your history will be removed. Bookmarks are not affected.':
        '历史记录中的所有页面都会被删除，书签不受影响。',
    'No history found': '未找到历史记录',
    'No results': '无结果',
    'History': '历史记录',
    'All': '全部', 'Today': '今天', 'Last 7 days': '最近 7 天', 'Last 30 days': '最近 30 天',
}
M['libmark.cs'] = {
    'Search bookmarks': '搜索书签',
    'No bookmarks found': '未找到书签',
    'No results': '无结果',
    'Title A-Z': '标题 A-Z',
}
M['libdl.cs'] = {
    'Search downloads': '搜索下载',
    'Clear completed': '清除已完成',
    'Show in folder': '在文件夹中显示',
    'Remove from list': '从列表中移除',
    'No results': '无结果',
    'No downloads found': '未找到下载',
}
M['libmedia.cs'] = {
    'Search media': '搜索媒体',
    'No results': '无结果',
    'No media found': '未找到媒体',
    'Open in new tab': '在新标签页打开',
    'Open with default app': '用默认应用打开',
    'Show in folder': '在文件夹中显示',
    'Copy path': '复制路径',
    'All': '全部', 'Images': '图片', 'Videos': '视频', 'Audio': '音频', 'Documents': '文档',
    'Depth': '深度', 'Chunk': '分块',
}
M['libsoon.cs'] = {'Coming in a later update': '即将在后续更新中推出'}
M['dllist.xaml.cs'] = {
    'Could not copy the link': '无法复制链接',
    'copy link': '复制链接',
}

M['win.items.cs'] = {
    'No saved card': '没有已保存的银行卡',
    'No saved identity': '没有已保存的身份信息',
    'Add one in Settings > Passwords.': '请在"设置 > 密码"中添加。',
    'Could not fill': '无法填入',
    'That item could not be opened.': '无法打开该项目。',
    'The page did not accept it.': '页面不接受该内容。',
    'There is nothing here that can be filled.': '此处没有可填入的内容。',
    'The page changed, so nothing was filled.': '页面已变化，未填入任何内容。',
    'Nothing was filled.': '未填入任何内容。',
    'Could not fill this page.': '无法填入此页面。',
    'Save card?': '保存银行卡？',
    'Not now': '暂不',
    'Could not save': '无法保存',
    'The password vault was locked in the meantime.': '密码库此时已锁定。',
    'Card saved': '银行卡已保存',
    'OK': '确定',
}
M['win.fill.cs'] = {
    'Could not open the password vault': '无法打开密码库',
    'Details are in Settings > Passwords.': '详情见"设置 > 密码"。',
    'Windows would not open its key. Details are in Settings > Passwords.':
        'Windows 未能打开密钥。详情见"设置 > 密码"。',
    'No one-time code key for this site': '此网站没有一次性验证码密钥',
    'No saved login for this site': '此网站没有已保存的登录信息',
    '(no user name)': '（无用户名）',
    'This page is not secure (http): anything typed into it can be read on the way.':
        '此页面不安全（http）：输入的内容可能在传输途中被读取。',
    'That is not the password.': '密码不正确。',
    'Your password vault is locked': '你的密码库已锁定',
    'That login is not for the page that is open now.': '该登录信息不属于当前打开的页面。',
    'Password filled': '密码已填入',
    'User name filled. Fill again on the next page for the password.':
        '用户名已填入。在下一页再次填入密码。',
    'There is no login form here that can be filled.': '此处没有可填入的登录表单。',
    'Code filled': '验证码已填入',
    'Click the code field first.': '请先点击验证码输入框。',
}

M['downloads.cs'] = {
    'File was moved or deleted': '文件已被移动或删除',
    'Windows will not open this kind of file from here. Use Show in folder.':
        'Windows 不允许从这里打开此类文件，请使用"在文件夹中显示"。',
    'Could not open the file': '无法打开文件',
    'Could not open Explorer': '无法打开文件资源管理器',
}

M['opts.pass.cs'] = {
    'What the vault protects': '密码库保护的范围',
    'Never saved for': '从不保存的网站',
    'Saved logins': '已保存的登录',
    'Import and export': '导入和导出',
    'Windows Hello': 'Windows Hello',
    'Showing, copying or deleting a password asks for your fingerprint, face or PIN first.':
        '查看、复制或删除密码前需要验证指纹、面部或 PIN。',
    'Use a NEAT password': '使用 NEAT 密码',
    'The vault opens only with your password, and shuts again after ': '密码库仅在输入密码后打开，并在闲置 ',
    ' minutes without use.': ' 分钟后自动关闭。',
    'Change password': '修改密码',
    'Lock now': '立即锁定',
    'Use Windows Hello': '使用 Windows Hello',
    'Not protected yet': '尚未保护',
    ' Until you set a NEAT password, saved passwords cannot be shown, ':
        ' 在你设置 NEAT 密码之前，无法查看、',
    'copied or deleted, and anyone who can sign in to this Windows account can read the vault.':
        '复制或删除已保存的密码，任何能登录此 Windows 账户的人都能读取密码库。',
    'Set a NEAT password': '设置 NEAT 密码',
    'Protected by': '保护方式：',
    'Windows Hello is not set up for your Windows account.': '你的 Windows 账户未设置 Windows Hello。',
    'This PC has nothing Windows Hello can use (no fingerprint reader, camera or PIN).':
        '此电脑没有 Windows Hello 可用的设备（无指纹识别器、摄像头或 PIN）。',
    'Windows Hello is turned off by a policy on this PC.': '此电脑的策略已关闭 Windows Hello。',
    'Windows Hello could not be used just now.': '当前无法使用 Windows Hello。',
    'Your saved logins are locked. A forgotten password cannot be recovered.':
        '你的登录信息已锁定，忘记密码无法找回。',
    'Search logins': '搜索登录信息',
    'No saved logins yet. Add one with the Add button.': '还没有保存的登录信息，请点击"添加"按钮。',
    'Nothing matches your search.': '没有匹配的搜索结果。',
    'This entry does not check out: it was changed outside NEAT.': '此项校验失败：它在 NEAT 之外被修改过。',
    'one-time code': '一次性验证码密钥',
    'Set a NEAT password first: saved passwords cannot be shown or copied without one.':
        '请先设置 NEAT 密码：没有密码无法查看或复制已保存的密码。',
    'This entry could not be opened. It may have been changed outside NEAT.':
        '无法打开此项，它可能已在 NEAT 之外被修改。',
    'Copied. It is taken off the clipboard after ': '已复制，将在 ',
    ' seconds.': ' 秒后从剪贴板清除。',
    'Add': '添加', 'Save': '保存', 'Delete': '删除', 'Edit': '编辑', 'Close': '关闭',
    'Show': '查看', 'Copy': '复制', 'Login': '登录', 'Logins': '登录信息',
}

M['opts.pass.task.cs'] = {
    'Too many wrong tries. Try again in ': '错误次数过多，请在 ',
    ' seconds.': ' 秒后重试。',
    'The new password needs at least ': '新密码至少需要 ',
    ' characters.': ' 个字符。',
    'Some saved logins do not check out (they were changed outside NEAT), ':
        '有些已保存的登录信息校验失败（在 NEAT 之外被修改），',
    'so the vault cannot be protected another way without losing them. Nothing was changed.':
        '因此无法在不丢失它们的情况下改用其他保护方式。未做任何更改。',
    'Windows would not open the key to the vault.': 'Windows 未能打开密码库密钥。',
    'That cannot be done right now.': '当前无法执行此操作。',
    'Enter the address of a web site, like example.com.': '输入网站地址，例如 example.com。',
    'Enter the password.': '输入密码。',
    'Add a login': '添加登录信息',
    'Web site': '网站',
    'User name': '用户名',
    'The two passwords are not the same.': '两次输入的密码不一致。',
    'Protect the saved logins with a NEAT password': '用 NEAT 密码保护已保存的登录信息',
    'Your saved logins are now protected by your NEAT password.': '你的登录信息现已由 NEAT 密码保护。',
    'New password': '新密码',
    'Repeat it': '重复输入',
    'NEAT cannot recover a forgotten password. If you forget it, the saved logins are lost. ':
        'NEAT 无法找回忘记的密码。如果忘记，已保存的登录信息将丢失。',
    'The vault will no longer open by itself for your Windows account.': '密码库将不再对此 Windows 账户自动打开。',
    'Protect with this password': '用此密码保护',
    'The two new passwords are not the same.': '两次输入的新密码不一致。',
    'Your NEAT password was changed.': 'NEAT 密码已修改。',
    'Change the NEAT password': '修改 NEAT 密码',
    'Current password': '当前密码',
    'Use Windows Hello instead': '改用 Windows Hello',
    'Windows will protect the key to your saved logins again, and Windows Hello will be ':
        'Windows 将重新保护登录信息密钥，并在查看或复制密码前',
    'asked before a password is shown or copied. The vault then opens by itself for your ':
        '要求 Windows Hello 验证。此后密码库会对此 Windows 账户自动打开，',
    'Windows account, so the NEAT password stops being needed.': '因此不再需要 NEAT 密码。',
    'the login': '该登录信息',
    'the login for ': '登录信息：',
    'Delete a login': '删除登录信息',
    '? This cannot be undone.': '？此操作无法撤销。',
    'At least ': '至少 ',
    'OK': '确定',
}

M['opts.pass.exchange.cs'] = {
    'No sites. When you choose Never for this site on the save question, the site is listed here.':
        '无。当你在保存提示中选择"此网站从不保存"时，该网站会列在这里。',
    'NEAT does not offer to save a password here.': 'NEAT 不会在此网站提示保存密码。',
    'Remove the saved code key': '移除已保存的验证码密钥',
    'Enter the address of a web site, like example.com.': '输入网站地址，例如 example.com。',
    'That is not a one-time code key. Paste the key the site shows (letters and digits), or the otpauth:// address behind its QR code.':
        '这不是一次性验证码密钥。请粘贴网站显示的密钥（字母和数字），或其二维码对应的 otpauth:// 地址。',
    'That login could not be changed. It may have been deleted, or changed outside NEAT.':
        '无法修改该登录信息，它可能已被删除或在 NEAT 之外被修改。',
    'Web site': '网站', 'User name': '用户名',
    'Leave empty to keep the saved password.': '留空则保留已保存的密码。',
    'One-time code key': '一次性验证码密钥',
    'For sites with two-step sign-in: the key the site shows when you set up an authenticator app. Leave empty to keep what is saved.':
        '用于两步验证的网站：设置验证器应用时网站显示的密钥。留空则保留已保存的内容。',
    'Import from a file': '从文件导入',
    'A CSV file exported from Chrome, Edge or Firefox, or from NEAT (logins with their one-time-code keys, cards, identities).':
        '从 Chrome、Edge、Firefox 或 NEAT 导出的 CSV 文件（登录信息及其验证码密钥、银行卡、身份信息）。',
    'Export to a file': '导出到文件',
    'Set a NEAT password first: nothing can be exported without a way to prove who is asking.':
        '请先设置 NEAT 密码：没有身份验证就无法导出任何内容。',
    'Writes every saved password and one-time-code key to a plain text file, and saved cards and identities to a second one.':
        '将所有密码和验证码密钥写入一个文本文件，银行卡和身份信息写入另一个文件。',
    'NEAT can only read a file from a folder on this PC.': 'NEAT 只能读取此电脑上文件夹中的文件。',
    'That file is too big to be a password export.': '该文件过大，不可能是密码导出文件。',
    'The file you imported holds every password in plain text.': '你导入的文件以明文保存了所有密码。',
    ' logins in the file: ': ' 文件中的登录信息：',
    ' already saved': ' 已保存',
    ' with another password than the one saved (the saved ones stay as they are)':
        ' 密码与已保存的不同（已保存的保持不变）',
    ' skipped (not web sites, or no password)': ' 已跳过（非网站或无密码）',
    'Import logins': '导入登录信息',
    'The file holds every password in plain text. After the import NEAT offers to delete it.':
        '该文件以明文保存了所有密码。导入后 NEAT 会建议删除它。',
    'The file you imported holds every card number in plain text.': '你导入的文件以明文保存了所有银行卡号。',
    ' cards and identities in the file: ': ' 文件中的银行卡和身份信息：',
    ' skipped (not a card or identity)': ' 已跳过（非银行卡或身份信息）',
    'Import cards and identities': '导入银行卡和身份信息',
    'Delete the file?': '删除该文件？',
    ' Anyone who can open it can read all of them. Delete it as soon as you are done with it.':
        ' 任何能打开它的人都能读取全部内容，用完请尽快删除。',
    'Delete the file': '删除文件',
    'The file was deleted.': '文件已删除。',
    'could not delete the file (': '无法删除文件（',
    'The file could not be deleted. Delete it yourself.': '无法删除该文件，请手动删除。',
    'Keep it': '保留',
    'Export all saved passwords': '导出所有已保存的密码',
    ' logins and ': ' 条登录信息和 ',
    ' cards and identities': ' 条银行卡和身份信息',
    'Export logins': '导出登录信息',
    'This writes every saved password, and the keys of your one-time codes, to a plain text file in ':
        '这会把所有密码和一次性验证码密钥写入下载文件夹中的文本文件，',
    'your Downloads folder, and saved cards and identities to a second one. Anyone who can open ':
        '银行卡和身份信息写入第二个文件。任何能打开这些文件的人',
    'those files can read all of them, and they can end up in backups and cloud folders. ':
        '都能读取全部内容，而且它们可能进入备份和云盘。',
    'Windows Hello (or your NEAT password) is asked first.': '系统会先要求验证 Windows Hello（或 NEAT 密码）。',
    'The files hold every password and card number in plain text. Delete them as soon as you have used them.':
        '这些文件以明文保存了所有密码和卡号，用完请尽快删除。',
    'Passwords exported': '密码已导出',
    'Show in folder': '在文件夹中显示',
    'Delete the files': '删除文件',
    'The files were deleted.': '文件已删除。',
    'Some files could not be deleted. Delete them yourself.': '部分文件无法删除，请手动删除。',
    'OK': '确定',
}

M['opts.pass.items.cs'] = {
    'How it is listed. Left empty, it is named after the number.': '列表显示的名称，留空则用卡号命名。',
    'Name on card': '持卡人姓名',
    'Card number': '卡号',
    'Month and year, like 08/27.': '月份和年份，例如 08/27。',
    'Security code': '安全码',
    'The 3 or 4 digits on the card. Optional.': '卡上的 3 或 4 位数字，可选。',
    'How it is listed. Left empty, it is named after the person.': '列表显示的名称，留空则用姓名命名。',
    'First name': '名',
    'Last name': '姓',
    'State or province': '州/省',
    'Postal code': '邮政编码',
    'Saved here, sealed like a password. It is filled into a payment form only when you pick it from the key at the field.':
        '像密码一样加密保存在此，只有在输入框点击密钥按钮选择后才会填入支付表单。',
    'Saved here, sealed like a password. It is filled into a form only when you pick it from the key at the field.':
        '像密码一样加密保存在此，只有在输入框点击密钥按钮选择后才会填入表单。',
    'This entry does not check out: it was changed outside NEAT.': '此项校验失败：它在 NEAT 之外被修改过。',
    'This entry could not be opened. It may have been changed outside NEAT.': '无法打开此项，它可能已在 NEAT 之外被修改。',
    '? This cannot be undone.': '？此操作无法撤销。',
    'City': '城市', 'Country': '国家', 'Phone': '电话', 'Email': '邮箱',
    'Company': '公司', 'Label': '名称', 'Address': '地址',
}

SUBDIR = {
    'tab.cs': 'web', 'libhist.cs': 'lib', 'libmark.cs': 'lib',
    'libdl.cs': 'lib', 'libmedia.cs': 'lib', 'libsoon.cs': 'lib',
    'dllist.xaml.cs': 'lib', 'downloads.cs': 'data',
}

LIT = re.compile(r'"((?:[^"\\]|\\.)*)"')

def translate_file(path, mapping):
    with open(path, 'r', encoding='utf-8-sig') as f:
        text = f.read()
    count = [0]
    def repl(m):
        body = m.group(1)
        if body in mapping:
            count[0] += 1
            val = mapping[body]
            # Protect already-escaped quotes, convert bare quotes to Chinese quotes
            val = val.replace('\\"', '\x01')
            out = []
            open_q = True
            for ch in val:
                if ch == '"':
                    out.append('\u201c' if open_q else '\u201d')
                    open_q = not open_q
                else:
                    out.append(ch)
            val = ''.join(out).replace('\x01', '\\"')
            return '"' + val + '"'
        return m.group(0)
    out = LIT.sub(repl, text)
    if count[0]:
        with open(path, 'w', encoding='utf-8') as f:
            f.write(out)
    return count[0]

total = 0
for name, mp in M.items():
    path = os.path.join(ROOT, SUBDIR.get(name, ''), name)
    if not os.path.exists(path):
        print(f'MISSING {name}')
        continue
    n = translate_file(path, mp)
    total += n
    print(f'{n:4}  {name}')
print('TOTAL replaced:', total)
