using System.Runtime.InteropServices;
using CursorBar.Core;

namespace CursorBar;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--once", StringComparer.OrdinalIgnoreCase))
        {
            return RunOnce();
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }

    private static int RunOnce()
    {
        AttachConsole(-1);
        try
        {
            var preferences = new PreferenceStore().Load();
            var session = CursorSessionResolver.Resolve(manualCookie: preferences.ManualCookie);
            using var client = new CursorClient();
            var snapshot = client.FetchUsageAsync(session).GetAwaiter().GetResult();
            Console.Out.Write(OnceDescription(snapshot));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static string OnceDescription(UsageSnapshot snapshot)
    {
        var plan = snapshot.IsUnlimited ? "unlimited" : Formatters.PercentLabel(snapshot.PlanPercent);
        var amount = Formatters.Money(snapshot.IncludedAmountCents) ?? "—";
        var cursor = Formatters.Money(snapshot.CursorAmountCents) ?? "—";
        var other = Formatters.Money(snapshot.OtherAmountCents) ?? "—";
        var email = snapshot.Email ?? "unknown";
        var reset = Formatters.ResetCountdown(snapshot.BillingEnd, chinese: false) ?? "reset unknown";
        return $"{snapshot.Membership}  {amount}  {plan}  cursor {cursor}  other {other}  {email}  {reset}\n";
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
}
