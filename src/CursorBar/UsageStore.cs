using System.ComponentModel;
using System.Runtime.CompilerServices;
using CursorBar.Core;

namespace CursorBar;

internal sealed class UsageStore : INotifyPropertyChanged, IDisposable
{
    private readonly PreferenceStore _preferenceStore;
    private readonly CursorClient _client;
    private readonly bool _ownsClient;
    private CancellationTokenSource? _refreshLoop;
    private Preferences _preferences;
    private UsageSnapshot? _snapshot;
    private string? _errorMessage;
    private bool _isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public UsageStore(PreferenceStore? preferenceStore = null, CursorClient? client = null)
    {
        _preferenceStore = preferenceStore ?? new PreferenceStore();
        _ownsClient = client is null;
        _client = client ?? new CursorClient();
        _preferences = _preferenceStore.Load();
    }

    public UsageSnapshot? Snapshot
    {
        get => _snapshot;
        private set => SetField(ref _snapshot, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public Preferences Preferences
    {
        get => _preferences;
        set
        {
            var shouldRestart = _preferences.RefreshMinutes != value.RefreshMinutes
                || _preferences.ManualCookie != value.ManualCookie;
            if (!SetField(ref _preferences, value)) return;
            _preferenceStore.Save(value);
            OnPropertyChanged(nameof(ShowPercentInMenuBar));
            OnPropertyChanged(nameof(ShowAmountInMenuBar));
            OnPropertyChanged(nameof(ShowLabelInMenuBar));
            OnPropertyChanged(nameof(RefreshMinutes));
            OnPropertyChanged(nameof(ManualCookie));
            OnPropertyChanged(nameof(StatusTitle));
            if (shouldRestart && _refreshLoop is not null)
            {
                Start();
            }
        }
    }

    public bool ShowPercentInMenuBar
    {
        get => Preferences.ShowPercentInMenuBar;
        set => Preferences = Preferences with { ShowPercentInMenuBar = value };
    }

    public bool ShowAmountInMenuBar
    {
        get => Preferences.ShowAmountInMenuBar;
        set => Preferences = Preferences with { ShowAmountInMenuBar = value };
    }

    public bool ShowLabelInMenuBar
    {
        get => Preferences.ShowLabelInMenuBar;
        set => Preferences = Preferences with { ShowLabelInMenuBar = value };
    }

    public int RefreshMinutes
    {
        get => Preferences.RefreshMinutes;
        set => Preferences = Preferences with { RefreshMinutes = Preferences.ClampedRefresh(value) };
    }

    public string ManualCookie
    {
        get => Preferences.ManualCookie;
        set => Preferences = Preferences with { ManualCookie = value };
    }

    public double? StatusPercent => Snapshot?.HeadlinePercent;
    public double? SecondaryPercent => Snapshot?.SecondaryPercent;

    public string StatusTitle
    {
        get
        {
            var parts = new List<string>();
            if (Preferences.ShowLabelInMenuBar) parts.Add("Cursor");
            if (Snapshot is { } snapshot)
            {
                if (Preferences.ShowAmountInMenuBar && Formatters.Money(snapshot.IncludedAmountCents) is string money)
                {
                    parts.Add(money);
                }
                if (Preferences.ShowPercentInMenuBar)
                {
                    parts.Add(snapshot.IsUnlimited ? L10n.Unlimited : Formatters.PercentLabel(snapshot.HeadlinePercent));
                }
            }
            return string.Join(" · ", parts);
        }
    }

    public void Start()
    {
        _refreshLoop?.Cancel();
        _refreshLoop?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshLoop = cts;
        _ = RunLoopAsync(cts.Token);
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var session = CursorSessionResolver.Resolve(manualCookie: Preferences.ManualCookie);
            Snapshot = await _client.FetchUsageAsync(session).ConfigureAwait(true);
            ErrorMessage = null;
        }
        catch (SessionException ex) when (ex.Kind == SessionError.MissingSession)
        {
            Snapshot = null;
            ErrorMessage = L10n.SignedOutBody;
        }
        catch (SessionException ex)
        {
            Snapshot = null;
            ErrorMessage = ex.Message;
        }
        catch (CursorClientException ex) when (ex.Kind == CursorClientError.Unauthorized)
        {
            ErrorMessage = L10n.ExpiredBody;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(StatusPercent));
            OnPropertyChanged(nameof(SecondaryPercent));
            OnPropertyChanged(nameof(StatusTitle));
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RefreshAsync().ConfigureAwait(true);
            var minutes = Preferences.ClampedRefresh(Preferences.RefreshMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _refreshLoop?.Cancel();
        _refreshLoop?.Dispose();
        if (_ownsClient) _client.Dispose();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
