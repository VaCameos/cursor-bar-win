using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using CursorBar.Core;
using Size = System.Windows.Size;

namespace CursorBar;

public partial class FloatingBallWindow : Window
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int GwlExStyle = -20;
    private const double DragThreshold = 4;

    private readonly UsageStore _store;
    private readonly Action _onClick;
    private Point _dragStart;
    private bool _dragging;
    private bool _moved;

    internal static DateTime LastPointerDownUtc { get; private set; }

    internal FloatingBallWindow(UsageStore store, Action onClick)
    {
        _store = store;
        _onClick = onClick;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyToolWindowStyle();
        Loaded += (_, _) => RestorePosition();
        PreviewMouseLeftButtonDown += OnPointerDown;
        PreviewMouseMove += OnPointerMove;
        PreviewMouseLeftButtonUp += OnPointerUp;
        MouseLeave += OnMouseLeave;
        MouseEnter += (_, _) => SetHover(true);
        ToolTip = L10n.FloatingBallHint;
        Render();
    }

    public void Render()
    {
        if (!IsInitialized) return;
        var unlimited = _store.Snapshot?.IsUnlimited == true;
        var dimmed = _store.Snapshot is null;
        var primary = _store.StatusPercent;
        var secondary = _store.SecondaryPercent;
        const double trackWidth = 34;
        Opacity = dimmed ? 0.78 : 1;
        if (unlimited)
        {
            TopFill.Width = trackWidth;
            TopFill.Background = new SolidColorBrush(TrayIconRenderer.MediaColor(UsageTone.Ok));
            BottomFill.Width = 0;
            PercentText.Text = "∞";
            PercentText.Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            return;
        }
        TopFill.Width = FillWidth(primary, trackWidth, 7);
        TopFill.Background = new SolidColorBrush(TrayIconRenderer.MediaColor(Formatters.UsageColorThreshold(primary)));
        BottomFill.Width = FillWidth(secondary, trackWidth, 3);
        BottomFill.Background = new SolidColorBrush(TrayIconRenderer.MediaColor(Formatters.UsageColorThreshold(secondary)));
        PercentText.Text = dimmed ? "—" : Formatters.PercentLabel(primary);
        PercentText.Foreground = new SolidColorBrush(TrayIconRenderer.MediaColor(Formatters.UsageColorThreshold(primary)));
    }

    internal void ApplyVisibility()
    {
        if (_store.ShowFloatingBall)
        {
            if (!IsVisible) Show();
            Render();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private void RestorePosition()
    {
        var prefs = _store.Preferences;
        if (prefs.FloatingBallLeft is double left && prefs.FloatingBallTop is double top)
        {
            Left = left;
            Top = top;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 18;
            Top = work.Top + work.Height * 0.38;
        }
        ClampToWorkArea();
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        LastPointerDownUtc = DateTime.UtcNow;
        _dragStart = e.GetPosition(this);
        _dragging = true;
        _moved = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(this);
        if (!_moved && (Math.Abs(now.X - _dragStart.X) > DragThreshold || Math.Abs(now.Y - _dragStart.Y) > DragThreshold))
        {
            _moved = true;
        }
        if (!_moved) return;
        var screen = PointToScreen(now);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var dip = transform.Transform(screen);
        Left = dip.X - _dragStart.X;
        Top = dip.Y - _dragStart.Y;
        ClampToWorkArea();
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        var wasMoved = _moved;
        EndDrag(save: wasMoved);
        if (!wasMoved) _onClick();
        e.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        EndDrag(save: _moved);
        SetHover(false);
    }

    private void SetHover(bool on)
    {
        HoverScale.ScaleX = on ? 1.06 : 1;
        HoverScale.ScaleY = on ? 1.06 : 1;
    }

    private void EndDrag(bool save)
    {
        if (!_dragging) return;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (save)
        {
            ClampToWorkArea();
            _store.SaveFloatingBallPosition(Left, Top);
        }
    }

    private void ClampToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;
        Left = Math.Clamp(Left, work.Left + 4, work.Right - width - 4);
        Top = Math.Clamp(Top, work.Top + 4, work.Bottom - height - 4);
    }

    internal Size AnchorSize()
    {
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;
        return new Size(width, height);
    }

    private static double FillWidth(double? percent, double track, double min)
    {
        if (percent is not double value || !double.IsFinite(value) || value <= 0) return 0;
        return Math.Max(min, track * Math.Clamp(value, 0, 100) / 100);
    }

    private void ApplyToolWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlExStyle);
        _ = SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
