using System.Text.Json;

namespace CursorBar.Core;

public sealed class AggregationItem
{
    public string? ModelIntent { get; set; }
    public int? Tier { get; set; }
    public double? TotalCents { get; set; }
}

public sealed class AggregatedUsageResponse
{
    public List<AggregationItem> Aggregations { get; set; } = [];
}

public sealed class PeriodUsageResponse
{
    public DateTimeOffset? BillingCycleEnd { get; set; }
    public double? AutoPercentUsed { get; set; }
    public double? ApiPercentUsed { get; set; }
    public List<string> AutoBucketModels { get; set; } = [];
}

public sealed record AggregatedAmounts
{
    public int? CursorCents { get; init; }
    public int? OtherCents { get; init; }
    public int? CursorEstimateCents { get; init; }
    public int? OtherEstimateCents { get; init; }

    public int? IncludedCents => (CursorCents, OtherCents) switch
    {
        (int cursor, int other) => cursor + other,
        (int cursor, null) => cursor,
        (null, int other) => other,
        _ => null,
    };

    public int? IncludedEstimateCents => (CursorEstimateCents, OtherEstimateCents) switch
    {
        (int cursor, int other) => cursor + other,
        (int cursor, null) => cursor,
        (null, int other) => other,
        _ => null,
    };
}

public static class AmountAggregation
{
    public static bool IsSandModel(string? name)
        => name?.Trim().StartsWith("sand-", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsCursorModel(AggregationItem item, IReadOnlyList<string> autoBucketModels)
    {
        if (item.Tier == 2) return true;
        if (item.Tier == 1) return false;
        return item.ModelIntent is string model && autoBucketModels.Contains(model);
    }

    public static int? Cents(double? totalCents)
    {
        if (totalCents is not double value || !double.IsFinite(value)) return null;
        return (int)Math.Round(value);
    }

    public static int? EstimateAtFull(int? cents, double? percent)
    {
        if (cents is null || percent is not double p || p <= 0 || !double.IsFinite(p)) return null;
        return (int)Math.Round(cents.Value / (p / 100));
    }

    public static AggregatedAmounts Amounts(AggregatedUsageResponse response, IReadOnlyList<string>? autoBucketModels = null)
    {
        var buckets = autoBucketModels ?? [];
        var cursor = 0;
        var other = 0;
        var cursorEstimate = 0;
        var otherEstimate = 0;
        var hasCursor = false;
        var hasOther = false;

        foreach (var item in response.Aggregations)
        {
            if (item.ModelIntent is not string model || Cents(item.TotalCents) is not int cents) continue;
            if (IsCursorModel(item, buckets))
            {
                cursor += cents;
                hasCursor = true;
                if (!IsSandModel(model)) cursorEstimate += cents;
            }
            else
            {
                other += cents;
                hasOther = true;
                if (!IsSandModel(model)) otherEstimate += cents;
            }
        }

        return new AggregatedAmounts
        {
            CursorCents = hasCursor ? cursor : null,
            OtherCents = hasOther ? other : null,
            CursorEstimateCents = hasCursor ? cursorEstimate : null,
            OtherEstimateCents = hasOther ? otherEstimate : null,
        };
    }
}

public static class TeamIdParser
{
    public static int? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (int.TryParse(trimmed, out var id)) return id;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return FirstInt(doc.RootElement, ["id", "teamId", "team_id"]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? FirstInt(JsonElement obj, string[] keys)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
        {
            if (!obj.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.Number) return (int)Math.Round(value.GetDouble());
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        if (obj.TryGetProperty("team", out var nested))
        {
            return FirstInt(nested, keys);
        }
        return null;
    }
}
