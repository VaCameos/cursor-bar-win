using System.Globalization;
using CursorBar.Core;

namespace CursorBar;

internal static class L10n
{
    public static bool IsChinese =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    public static string AppName => "Cursor Bar";
    public static string Included => IsChinese ? "套餐用量" : "Included";
    public static string CursorModels => IsChinese ? "Cursor 模型" : "Cursor Models";
    public static string OtherModels => IsChinese ? "其他模型" : "Other Models";
    public static string OnDemand => IsChinese ? "按需用量" : "On-demand";
    public static string Unlimited => IsChinese ? "不限量" : "Unlimited";
    public static string Refresh => IsChinese ? "刷新" : "Refresh";
    public static string Refreshing => IsChinese ? "刷新中…" : "Refreshing…";
    public static string Dashboard => IsChinese ? "打开用量页" : "Open Dashboard";
    public static string Quit => IsChinese ? "退出 Cursor Bar" : "Quit Cursor Bar";
    public static string Settings => IsChinese ? "设置" : "Settings";
    public static string LaunchAtLogin => IsChinese ? "登录时启动" : "Launch at login";
    public static string ShowFloatingBall => IsChinese ? "显示悬浮球" : "Show floating ball";
    public static string FloatingBallHint => IsChinese
        ? "拖动可换位置，点击打开用量面板"
        : "Drag to move, click to open the usage panel";
    public static string ShowPercent => IsChinese ? "托盘提示显示百分比" : "Show % in tray tip";
    public static string ShowAmount => IsChinese ? "托盘提示显示金额" : "Show $ in tray tip";
    public static string ShowLabel => IsChinese ? "托盘提示显示 Cursor 标识" : "Show Cursor label";
    public static string RefreshEvery => IsChinese ? "刷新间隔" : "Refresh every";
    public static string Advanced => IsChinese ? "高级" : "Advanced";
    public static string ManualCookie => IsChinese ? "手动 Cookie（可选）" : "Manual cookie (optional)";
    public static string CookieHint => IsChinese
        ? "留空则自动读取本机 Cursor 登录。也可粘贴 cursor.com 的 Cookie。"
        : "Leave empty to use the local Cursor session. You can also paste a cursor.com cookie.";
    public static string SignedOutTitle => IsChinese ? "未检测到 Cursor 登录" : "No Cursor session";
    public static string SignedOutBody => IsChinese
        ? "先打开 Cursor 并登录，或在下方粘贴 cursor.com 的 Cookie。"
        : "Open Cursor and sign in, or paste a cursor.com cookie below.";
    public static string ExpiredTitle => IsChinese ? "登录已过期" : "Session expired";
    public static string ExpiredBody => IsChinese
        ? "请在 Cursor 里重新登录后再刷新。"
        : "Sign in again inside Cursor, then refresh.";
    public static string Waiting => IsChinese ? "正在读取 Cursor 用量…" : "Reading Cursor usage…";
    public static string SourceCursorApp => IsChinese ? "来自 Cursor 应用" : "From Cursor app";
    public static string SourceAgent => IsChinese ? "来自 cursor-agent" : "From cursor-agent";
    public static string SourceManual => IsChinese ? "来自手动 Cookie" : "From manual cookie";

    public static string MinutesValue(int value) => IsChinese ? $"{value} 分钟" : $"{value} min";

    public static string SourceLabel(AuthSource source) => source switch
    {
        AuthSource.CursorApp => SourceCursorApp,
        AuthSource.CursorAgent => SourceAgent,
        AuthSource.ManualCookie => SourceManual,
        _ => SourceCursorApp,
    };
}
