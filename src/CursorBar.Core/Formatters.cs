using System.Globalization;

namespace CursorBar.Core;

public static class Formatters
{
    public static string? Dollars(int? cents)
    {
        if (cents is null) return null;
        var value = cents.Value;
        if (value % 100 == 0) return $"${value / 100}";
        return $"${value / 100}.{Math.Abs(value % 100):D2}";
    }

    public static string? Money(int? cents)
    {
        if (cents is null) return null;
        var negative = cents < 0;
        var absolute = Math.Abs(cents.Value);
        var text = $"${absolute / 100}.{absolute % 100:D2}";
        return negative ? $"-{text}" : text;
    }

    public static string? Estimate(int? cents)
    {
        if (cents is null) return null;
        return string.Create(CultureInfo.InvariantCulture, $"~${cents.Value / 100.0:0.0} @100%");
    }

    public static string? AmountDetail(int? used, int? estimateCents)
    {
        return (Money(used), Estimate(estimateCents)) switch
        {
            (string usedText, string estimate) => $"{usedText}  ·  {estimate}",
            (string usedText, null) => usedText,
            (null, string estimate) => estimate,
            _ => null,
        };
    }

    public static string PercentLabel(double? value)
    {
        if (value is not double number || !double.IsFinite(number)) return "—";
        return $"{(int)Math.Round(number)}%";
    }

    public static string PercentLabel1(double? value)
    {
        if (value is not double number || !double.IsFinite(number)) return "—";
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(number * 10) / 10:0.0}%");
    }

    public static string? ResetCountdown(DateTimeOffset? end, DateTimeOffset? now = null, bool chinese = false)
    {
        if (end is null) return null;
        var current = now ?? DateTimeOffset.Now;
        var interval = end.Value - current;
        if (interval <= TimeSpan.Zero)
        {
            return chinese ? "本周期已结束" : "Cycle ended";
        }
        var totalMinutes = (int)interval.TotalMinutes;
        var days = totalMinutes / (60 * 24);
        var hours = (totalMinutes / 60) % 24;
        var minutes = totalMinutes % 60;
        if (chinese)
        {
            if (days > 0) return $"还有 {days} 天 {hours} 小时重置";
            if (hours > 0) return $"还有 {hours} 小时 {minutes} 分钟重置";
            return $"还有 {Math.Max(minutes, 1)} 分钟重置";
        }
        if (days > 0) return $"Resets in {days}d {hours}h";
        if (hours > 0) return $"Resets in {hours}h {minutes}m";
        return $"Resets in {Math.Max(minutes, 1)}m";
    }

    public static string RelativeUpdated(DateTimeOffset date, DateTimeOffset? now = null, bool chinese = false)
    {
        var seconds = Math.Max(0, (int)((now ?? DateTimeOffset.Now) - date).TotalSeconds);
        if (chinese)
        {
            if (seconds < 15) return "刚刚更新";
            if (seconds < 60) return $"{seconds} 秒前更新";
            var minutes = seconds / 60;
            if (minutes < 60) return $"{minutes} 分钟前更新";
            return $"{minutes / 60} 小时前更新";
        }
        if (seconds < 15) return "Updated just now";
        if (seconds < 60) return $"Updated {seconds}s ago";
        var minutesEn = seconds / 60;
        if (minutesEn < 60) return $"Updated {minutesEn}m ago";
        return $"Updated {minutesEn / 60}h ago";
    }

    public static UsageTone UsageColorThreshold(double? percent)
    {
        if (percent is not double value || !double.IsFinite(value)) return UsageTone.Unknown;
        return value switch
        {
            < 60 => UsageTone.Ok,
            < 80 => UsageTone.Watch,
            < 95 => UsageTone.High,
            _ => UsageTone.Critical,
        };
    }
}
