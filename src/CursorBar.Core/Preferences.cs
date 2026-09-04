using System.Text.Json;

namespace CursorBar.Core;

public sealed record Preferences
{
    public static readonly int[] RefreshOptions = [1, 2, 5, 10, 15, 30];

    public int RefreshMinutes { get; init; } = 5;
    public bool ShowPercentInMenuBar { get; init; } = true;
    public bool ShowAmountInMenuBar { get; init; } = true;
    public bool ShowLabelInMenuBar { get; init; }
    public bool ShowFloatingBall { get; init; }
    public double? FloatingBallLeft { get; init; }
    public double? FloatingBallTop { get; init; }
    public string ManualCookie { get; init; } = "";

    public Preferences With(
        int? refreshMinutes = null,
        bool? showPercentInMenuBar = null,
        bool? showAmountInMenuBar = null,
        bool? showLabelInMenuBar = null,
        bool? showFloatingBall = null,
        double? floatingBallLeft = null,
        double? floatingBallTop = null,
        string? manualCookie = null)
        => this with
        {
            RefreshMinutes = ClampedRefresh(refreshMinutes ?? RefreshMinutes),
            ShowPercentInMenuBar = showPercentInMenuBar ?? ShowPercentInMenuBar,
            ShowAmountInMenuBar = showAmountInMenuBar ?? ShowAmountInMenuBar,
            ShowLabelInMenuBar = showLabelInMenuBar ?? ShowLabelInMenuBar,
            ShowFloatingBall = showFloatingBall ?? ShowFloatingBall,
            FloatingBallLeft = floatingBallLeft ?? FloatingBallLeft,
            FloatingBallTop = floatingBallTop ?? FloatingBallTop,
            ManualCookie = manualCookie ?? ManualCookie,
        };

    public static int ClampedRefresh(int minutes)
        => RefreshOptions.Contains(minutes) ? minutes : 5;
}

public sealed class PreferenceStore
{
    private readonly string _path;

    public PreferenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CursorBar",
            "preferences.json");
    }

    public Preferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Preferences();
            var loaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_path), JsonOptions.Default);
            if (loaded is null) return new Preferences();
            return loaded with { RefreshMinutes = Preferences.ClampedRefresh(loaded.RefreshMinutes) };
        }
        catch
        {
            return new Preferences();
        }
    }

    public void Save(Preferences preferences)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var payload = preferences with { RefreshMinutes = Preferences.ClampedRefresh(preferences.RefreshMinutes) };
        File.WriteAllText(_path, JsonSerializer.Serialize(payload, JsonOptions.Default));
    }
}
