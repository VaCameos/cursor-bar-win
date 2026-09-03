using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using CursorBar.Core;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace CursorBar;

public partial class PopupWindow : Window
{
    private readonly UsageStore _store;
    private bool _syncing;
    private bool _suppressDeactivate;

    internal PopupWindow(UsageStore store)
    {
        _store = store;
        InitializeComponent();
        ApplyStaticText();
        Deactivated += (_, _) =>
        {
            if (!_suppressDeactivate) Hide();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
        RefreshContent();
    }

    public void ShowNearTray()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        RefreshContent();
        Measure(new Size(Width, double.PositiveInfinity));
        Arrange(new Rect(0, 0, Width, DesiredSize.Height));
        var scale = DeviceScale();
        var mouse = Control.MousePosition;
        var screen = Screen.FromPoint(mouse);
        var width = ActualWidth > 1 ? ActualWidth : 332;
        var height = Math.Max(ActualHeight, DesiredSize.Height);
        if (height < 80) height = 280;
        var left = mouse.X / scale - width / 2;
        var top = mouse.Y / scale - height - 12;
        var waLeft = screen.WorkingArea.Left / scale;
        var waTop = screen.WorkingArea.Top / scale;
        var waRight = screen.WorkingArea.Right / scale;
        var waBottom = screen.WorkingArea.Bottom / scale;
        if (top < waTop) top = mouse.Y / scale + 12;
        left = Math.Clamp(left, waLeft + 8, waRight - width - 8);
        top = Math.Clamp(top, waTop + 8, waBottom - height - 8);
        Left = left;
        Top = top;
        _suppressDeactivate = true;
        Show();
        Activate();
        Dispatcher.BeginInvoke(() => _suppressDeactivate = false);
    }

    private static double DeviceScale()
    {
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96.0;
    }

    public void RefreshContent()
    {
        if (!IsInitialized) return;
        _syncing = true;
        try
        {
            EmailText.Text = _store.Snapshot?.Email ?? "";
            EmailText.Visibility = string.IsNullOrEmpty(EmailText.Text) ? Visibility.Collapsed : Visibility.Visible;
            if (_store.Snapshot is { } snapshot)
            {
                MembershipBadge.Visibility = Visibility.Visible;
                MembershipText.Text = snapshot.Membership;
            }
            else
            {
                MembershipBadge.Visibility = Visibility.Collapsed;
            }

            RebuildMeters();
            RefreshButton.Content = _store.IsLoading ? L10n.Refreshing : L10n.Refresh;
            RefreshButton.IsEnabled = !_store.IsLoading;
            ShowLabelBox.IsChecked = _store.ShowLabelInMenuBar;
            ShowAmountBox.IsChecked = _store.ShowAmountInMenuBar;
            ShowPercentBox.IsChecked = _store.ShowPercentInMenuBar;
            LaunchBox.IsChecked = LaunchAtLogin.IsEnabled;
            if (RefreshCombo.SelectedItem is ComboBoxItem selected
                && selected.Tag is int minutes
                && minutes != _store.RefreshMinutes)
            {
                SelectRefresh(_store.RefreshMinutes);
            }
            if (CookieBox.Password != _store.ManualCookie)
            {
                CookieBox.Password = _store.ManualCookie;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ApplyStaticText()
    {
        ShowLabelBox.Content = L10n.ShowLabel;
        ShowAmountBox.Content = L10n.ShowAmount;
        ShowPercentBox.Content = L10n.ShowPercent;
        LaunchBox.Content = L10n.LaunchAtLogin;
        RefreshLabel.Text = L10n.RefreshEvery;
        SettingsExpander.Header = L10n.Settings;
        AdvancedExpander.Header = L10n.Advanced;
        CookieLabel.Text = L10n.ManualCookie;
        CookieHint.Text = L10n.CookieHint;
        DashboardButton.Content = L10n.Dashboard;
        QuitButton.Content = L10n.Quit;
        RefreshCombo.Items.Clear();
        foreach (var minutes in Preferences.RefreshOptions)
        {
            RefreshCombo.Items.Add(new ComboBoxItem
            {
                Content = L10n.MinutesValue(minutes),
                Tag = minutes,
            });
        }
        SelectRefresh(_store.RefreshMinutes);
    }

    private void SelectRefresh(int minutes)
    {
        foreach (ComboBoxItem item in RefreshCombo.Items)
        {
            if (item.Tag is int value && value == minutes)
            {
                RefreshCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void RebuildMeters()
    {
        MetersHost.Children.Clear();
        if (_store.Snapshot is { } snapshot)
        {
            StateText.Visibility = Visibility.Collapsed;
            AddMeter(
                L10n.Included,
                snapshot.IsUnlimited ? null : snapshot.PlanPercent,
                Formatters.AmountDetail(snapshot.IncludedAmountCents, snapshot.IncludedEstimateCents)
                    ?? MoneyPair(snapshot.PlanUsedCents, snapshot.PlanLimitCents),
                snapshot.IsUnlimited);
            if ((snapshot.AutoPercent is not null || snapshot.CursorAmountCents is not null) && !snapshot.IsUnlimited)
            {
                AddMeter(
                    L10n.CursorModels,
                    snapshot.AutoPercent,
                    Formatters.AmountDetail(snapshot.CursorAmountCents, snapshot.CursorEstimateCents),
                    unlimited: false);
            }
            if ((snapshot.ApiPercent is not null || snapshot.OtherAmountCents is not null) && !snapshot.IsUnlimited)
            {
                AddMeter(
                    L10n.OtherModels,
                    snapshot.ApiPercent,
                    Formatters.AmountDetail(snapshot.OtherAmountCents, snapshot.OtherEstimateCents),
                    unlimited: false);
            }
            if (snapshot.OnDemandEnabled)
            {
                AddMeter(
                    L10n.OnDemand,
                    snapshot.SecondaryPercent,
                    MoneyPair(snapshot.OnDemandUsedCents, snapshot.OnDemandLimitCents)
                        ?? Formatters.Money(snapshot.OnDemandUsedCents),
                    unlimited: false);
            }

            var meta = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (Formatters.ResetCountdown(snapshot.BillingEnd, chinese: L10n.IsChinese) is string reset)
            {
                meta.Children.Add(Caption(reset));
            }
            meta.Children.Add(Caption(Formatters.RelativeUpdated(snapshot.FetchedAt, chinese: L10n.IsChinese)));
            meta.Children.Add(Caption(L10n.SourceLabel(snapshot.AuthSource)));
            if (!string.IsNullOrEmpty(_store.ErrorMessage))
            {
                meta.Children.Add(new TextBlock
                {
                    Text = _store.ErrorMessage,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 140, 40)),
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            MetersHost.Children.Add(meta);
            return;
        }

        if (_store.IsLoading)
        {
            StateText.Text = L10n.Waiting;
            StateText.Visibility = Visibility.Visible;
            return;
        }

        var title = _store.ErrorMessage == L10n.ExpiredBody ? L10n.ExpiredTitle : L10n.SignedOutTitle;
        StateText.Text = $"{title}\n{_store.ErrorMessage ?? L10n.SignedOutBody}";
        StateText.Visibility = Visibility.Visible;
    }

    private void AddMeter(string title, double? percent, string? detail, bool unlimited)
    {
        var tone = unlimited ? UsageTone.Ok : Formatters.UsageColorThreshold(percent);
        var color = TrayIconRenderer.MediaColor(tone);
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var header = new DockPanel { LastChildFill = true };
        var percentBlock = new TextBlock
        {
            Text = unlimited ? L10n.Unlimited : Formatters.PercentLabel1(percent),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(color),
        };
        DockPanel.SetDock(percentBlock, Dock.Right);
        header.Children.Add(percentBlock);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Medium,
            FontSize = 13,
        });
        block.Children.Add(header);
        block.Children.Add(new ProgressBar
        {
            Value = unlimited ? 0 : Math.Clamp(percent ?? 0, 0, 100),
            Foreground = new SolidColorBrush(color),
            Margin = new Thickness(0, 5, 0, 0),
        });
        if (!string.IsNullOrEmpty(detail))
        {
            block.Children.Add(Caption(detail));
        }
        MetersHost.Children.Add(block);
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Margin = new Thickness(0, 3, 0, 0),
        Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
        TextWrapping = TextWrapping.Wrap,
    };

    private static string? MoneyPair(int? used, int? limit) => (Formatters.Dollars(used), Formatters.Dollars(limit)) switch
    {
        (string usedText, string limitText) => $"{usedText} / {limitText}",
        (string usedText, null) => usedText,
        _ => null,
    };

    private async void RefreshClick(object sender, RoutedEventArgs e)
        => await _store.RefreshAsync();

    private void DashboardClick(object sender, RoutedEventArgs e)
        => TrayController.OpenDashboard();

    private void QuitClick(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();

    private void PreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _store.ShowLabelInMenuBar = ShowLabelBox.IsChecked == true;
        _store.ShowAmountInMenuBar = ShowAmountBox.IsChecked == true;
        _store.ShowPercentInMenuBar = ShowPercentBox.IsChecked == true;
    }

    private void LaunchChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            LaunchAtLogin.SetEnabled(LaunchBox.IsChecked == true);
        }
        catch
        {
            // keep the checkbox in sync with the registry even if the write fails
        }
        _syncing = true;
        LaunchBox.IsChecked = LaunchAtLogin.IsEnabled;
        _syncing = false;
    }

    private void RefreshMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (RefreshCombo.SelectedItem is ComboBoxItem item && item.Tag is int minutes)
        {
            _store.RefreshMinutes = minutes;
        }
    }

    private void CookieChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _store.ManualCookie = CookieBox.Password;
    }
}
