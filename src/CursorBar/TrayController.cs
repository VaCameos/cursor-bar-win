using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using CursorBar.Core;
using Application = System.Windows.Application;

namespace CursorBar;

internal sealed class TrayController : IDisposable
{
    private readonly UsageStore _store = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly PopupWindow _popup;
    private readonly FloatingBallWindow _ball;
    private Icon? _currentIcon;

    public TrayController()
    {
        _popup = new PopupWindow(_store);
        _ball = new FloatingBallWindow(_store, ShowFromBall);
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = L10n.AppName,
            ContextMenuStrip = BuildMenu(),
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _store.PropertyChanged += OnStoreChanged;
        Render();
    }

    public void Start()
    {
        _store.Start();
        _ball.ApplyVisibility();
    }

    public void ShowPopup()
    {
        _popup.ShowNearTray();
    }

    private void ShowFromBall()
    {
        var size = _ball.AnchorSize();
        _popup.ShowNearAnchor(_ball.Left, _ball.Top, size.Width, size.Height);
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Application.Current.Dispatcher.Invoke(ShowPopup);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(L10n.Refresh, null, async (_, _) => await _store.RefreshAsync());
        menu.Items.Add(L10n.Dashboard, null, (_, _) => OpenDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L10n.Quit, null, (_, _) => Application.Current.Shutdown());
        return menu;
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) Render();
        else app.Dispatcher.Invoke(Render);
    }

    private void Render()
    {
        _currentIcon?.Dispose();
        _currentIcon = TrayIconRenderer.Create(
            _store.StatusPercent,
            _store.SecondaryPercent,
            _store.Snapshot?.IsUnlimited == true,
            _store.Snapshot is null);
        _notifyIcon.Icon = _currentIcon;

        var tip = BuildTip();
        _notifyIcon.Text = tip.Length <= 63 ? tip : tip[..63];
        _popup.RefreshContent();
        _ball.Render();
        _ball.ApplyVisibility();
    }

    private string BuildTip()
    {
        if (_store.Snapshot is { } snapshot)
        {
            var title = _store.StatusTitle;
            if (!string.IsNullOrEmpty(title))
            {
                return $"{snapshot.Membership} · {title}";
            }
            var plan = snapshot.IsUnlimited ? L10n.Unlimited : Formatters.PercentLabel(snapshot.HeadlinePercent);
            return $"{snapshot.Membership} · {plan}";
        }
        return _store.ErrorMessage is null ? L10n.AppName : L10n.SignedOutTitle;
    }

    internal static void OpenDashboard()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://cursor.com/dashboard?tab=usage",
            UseShellExecute = true,
        });
    }

    public void Dispose()
    {
        _store.PropertyChanged -= OnStoreChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        _store.Dispose();
        _ball.Close();
        _popup.Close();
    }
}
