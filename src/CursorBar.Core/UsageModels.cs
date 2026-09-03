using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CursorBar.Core;

public enum AuthSource
{
    CursorApp,
    CursorAgent,
    ManualCookie,
}

public enum UsageTone
{
    Ok,
    Watch,
    High,
    Critical,
    Unknown,
}

public sealed record UsageSnapshot
{
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public required string Membership { get; init; }
    public bool IsUnlimited { get; init; }
    public double? PlanPercent { get; init; }
    public double? AutoPercent { get; init; }
    public double? ApiPercent { get; init; }
    public int? PlanUsedCents { get; init; }
    public int? PlanLimitCents { get; init; }
    public bool OnDemandEnabled { get; init; }
    public int? OnDemandUsedCents { get; init; }
    public int? OnDemandLimitCents { get; init; }
    public int? CursorAmountCents { get; init; }
    public int? OtherAmountCents { get; init; }
    public int? IncludedAmountCents { get; init; }
    public int? CursorEstimateCents { get; init; }
    public int? OtherEstimateCents { get; init; }
    public int? IncludedEstimateCents { get; init; }
    public DateTimeOffset? BillingStart { get; init; }
    public DateTimeOffset? BillingEnd { get; init; }
    public DateTimeOffset FetchedAt { get; init; }
    public AuthSource AuthSource { get; init; }

    public double? HeadlinePercent => IsUnlimited ? 0 : PlanPercent ?? AutoPercent ?? ApiPercent;

    public double? SecondaryPercent
    {
        get
        {
            if (OnDemandEnabled && OnDemandUsedCents is int used && OnDemandLimitCents is int limit && limit > 0)
            {
                return (used / (double)limit) * 100;
            }
            return ApiPercent;
        }
    }
}

public sealed class UsageSummaryResponse
{
    public string? BillingCycleStart { get; set; }
    public string? BillingCycleEnd { get; set; }
    public string? MembershipType { get; set; }
    public string? LimitType { get; set; }
    public bool? IsUnlimited { get; set; }
    public IndividualUsage? IndividualUsage { get; set; }
    public TeamUsage? TeamUsage { get; set; }
    public string? AutoModelSelectedDisplayMessage { get; set; }
    public string? NamedModelSelectedDisplayMessage { get; set; }
}

public sealed class IndividualUsage
{
    public PlanUsage? Plan { get; set; }
    public MoneyUsage? OnDemand { get; set; }
    public MoneyUsage? Overall { get; set; }
}

public sealed class TeamUsage
{
    public MoneyUsage? OnDemand { get; set; }
}

public sealed class PlanUsage
{
    public bool? Enabled { get; set; }
    public int? Used { get; set; }
    public int? Limit { get; set; }
    public int? Remaining { get; set; }
    public double? AutoPercentUsed { get; set; }
    public double? ApiPercentUsed { get; set; }
    public double? TotalPercentUsed { get; set; }
    public PlanBreakdown? Breakdown { get; set; }
}

public sealed class PlanBreakdown
{
    public int? Included { get; set; }
    public int? Bonus { get; set; }
    public int? Total { get; set; }
}

public sealed class MoneyUsage
{
    public bool? Enabled { get; set; }
    public int? Used { get; set; }
    public int? Limit { get; set; }
    public int? Remaining { get; set; }
}

public sealed class AuthMeResponse
{
    public int? Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public int? TeamId { get; set; }
}

public static class UsageMapping
{
    public static UsageSnapshot Snapshot(
        UsageSummaryResponse summary,
        PeriodUsageResponse? period,
        AggregatedAmounts? amounts,
        AuthMeResponse? me,
        string? cachedEmail,
        AuthSource authSource,
        DateTimeOffset? fetchedAt = null)
    {
        var plan = summary.IndividualUsage?.Plan;
        var onDemand = summary.IndividualUsage?.OnDemand ?? summary.TeamUsage?.OnDemand;
        var planLimit = plan?.Limit ?? plan?.Breakdown?.Total;

        var autoPercent = FirstFinite(
            period?.AutoPercentUsed,
            plan?.AutoPercentUsed,
            PercentFromMessage(summary.AutoModelSelectedDisplayMessage));
        var apiPercent = FirstFinite(
            period?.ApiPercentUsed,
            plan?.ApiPercentUsed,
            PercentFromMessage(summary.NamedModelSelectedDisplayMessage));
        var planPercent = FirstFinite(
            plan?.TotalPercentUsed,
            Percent(plan?.Used, planLimit),
            BlendedPercent(autoPercent, apiPercent));

        var cursorEstimate = AmountAggregation.EstimateAtFull(amounts?.CursorEstimateCents, autoPercent);
        var otherEstimate = AmountAggregation.EstimateAtFull(amounts?.OtherEstimateCents, apiPercent);

        return new UsageSnapshot
        {
            Email = me?.Email ?? cachedEmail,
            DisplayName = me?.Name,
            Membership = TitleCase(summary.MembershipType ?? "Cursor"),
            IsUnlimited = summary.IsUnlimited ?? false,
            PlanPercent = planPercent,
            AutoPercent = autoPercent,
            ApiPercent = apiPercent,
            PlanUsedCents = plan?.Used,
            PlanLimitCents = planLimit,
            OnDemandEnabled = onDemand?.Enabled ?? false,
            OnDemandUsedCents = onDemand?.Used,
            OnDemandLimitCents = onDemand?.Limit,
            CursorAmountCents = amounts?.CursorCents,
            OtherAmountCents = amounts?.OtherCents,
            IncludedAmountCents = amounts?.IncludedCents,
            CursorEstimateCents = cursorEstimate,
            OtherEstimateCents = otherEstimate,
            IncludedEstimateCents = AmountAggregation.EstimateAtFull(amounts?.IncludedEstimateCents, planPercent)
                ?? SumCents(cursorEstimate, otherEstimate),
            BillingStart = ParseIso8601(summary.BillingCycleStart),
            BillingEnd = period?.BillingCycleEnd ?? ParseIso8601(summary.BillingCycleEnd),
            FetchedAt = fetchedAt ?? DateTimeOffset.Now,
            AuthSource = authSource,
        };
    }

    public static double? Percent(int? used, int? limit)
    {
        if (used is null || limit is null || limit <= 0) return null;
        return (used.Value / (double)limit.Value) * 100;
    }

    public static double? PercentFromMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        var idx = message.IndexOf('%');
        if (idx < 0) return null;
        var number = "";
        for (var i = idx - 1; i >= 0; i--)
        {
            var c = message[i];
            if (char.IsAscii(c) && (char.IsDigit(c) || c == '.'))
            {
                number = c + number;
            }
            else if (number.Length > 0)
            {
                break;
            }
        }
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static string TitleCase(string raw)
    {
        return string.Join(' ', raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0
                ? ""
                : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    public static DateTimeOffset? ParseIso8601(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
        {
            return date;
        }
        return null;
    }

    private static double? BlendedPercent(double? auto, double? api) => (auto, api) switch
    {
        (double a, double p) => Math.Max(a, p),
        (double a, null) => a,
        (null, double p) => p,
        _ => null,
    };

    private static int? SumCents(int? lhs, int? rhs) => (lhs, rhs) switch
    {
        (int a, int b) => a + b,
        (int a, null) => a,
        (null, int b) => b,
        _ => null,
    };

    private static double? FirstFinite(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value is double number && double.IsFinite(number)) return number;
        }
        return null;
    }
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new FlexibleIntConverter());
        options.Converters.Add(new FlexibleDoubleConverter());
        options.Converters.Add(new FlexibleDateConverter());
        options.Converters.Add(new AuthMeConverter());
        options.Converters.Add(new PeriodUsageConverter());
        return options;
    }
}

internal sealed class FlexibleIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => FlexibleJson.ReadInt(ref reader);

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}

internal sealed class FlexibleDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => FlexibleJson.ReadDouble(ref reader);

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}

internal sealed class FlexibleDateConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => FlexibleJson.ReadDate(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToUniversalTime().ToString("O"));
    }
}

internal sealed class AuthMeConverter : JsonConverter<AuthMeResponse>
{
    public override AuthMeResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var me = new AuthMeResponse
        {
            Id = FlexibleJson.Int(root, "id") ?? FlexibleJson.Int(root, "userId"),
            Email = FlexibleJson.String(root, "email"),
            Name = FlexibleJson.String(root, "name"),
            TeamId = FlexibleJson.Int(root, "teamId"),
        };
        if (me.TeamId is null && root.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Object)
        {
            me.TeamId = FlexibleJson.Int(team, "id");
        }
        return me;
    }

    public override void Write(Utf8JsonWriter writer, AuthMeResponse value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}

internal sealed class PeriodUsageConverter : JsonConverter<PeriodUsageResponse>
{
    public override PeriodUsageResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var response = new PeriodUsageResponse
        {
            BillingCycleEnd = FlexibleJson.Date(root, "billingCycleEnd"),
            AutoBucketModels = FlexibleJson.StringArray(root, "autoBucketModels"),
        };
        if (root.TryGetProperty("planUsage", out var plan) && plan.ValueKind == JsonValueKind.Object)
        {
            response.AutoPercentUsed = FlexibleJson.Double(plan, "autoPercentUsed");
            response.ApiPercentUsed = FlexibleJson.Double(plan, "apiPercentUsed");
            if (response.AutoBucketModels.Count == 0)
            {
                response.AutoBucketModels = FlexibleJson.StringArray(plan, "autoBucketModels");
            }
        }
        return response;
    }

    public override void Write(Utf8JsonWriter writer, PeriodUsageResponse value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}

internal static class FlexibleJson
{
    public static int? ReadInt(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
        JsonTokenType.Number => (int)Math.Round(reader.GetDouble()),
        JsonTokenType.String when int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => (int)Math.Round(d),
        _ => null,
    };

    public static double? ReadDouble(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetDouble(),
        JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public static DateTimeOffset? ReadDate(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return DateFromEpoch(reader.GetDouble());
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return DateFromEpoch(number);
            }
            return UsageMapping.ParseIso8601(raw);
        }
        return null;
    }

    public static int? Int(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)Math.Round(value.GetDouble()),
            JsonValueKind.String when int.TryParse(value.GetString(), out var i) => i,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => (int)Math.Round(d),
            _ => null,
        };
    }

    public static double? Double(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public static string? String(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static DateTimeOffset? Date(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => DateFromEpoch(value.GetDouble()),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => DateFromEpoch(n),
            JsonValueKind.String => UsageMapping.ParseIso8601(value.GetString()),
            _ => null,
        };
    }

    public static List<string> StringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrEmpty(item))
            .Cast<string>()
            .ToList();
    }

    private static DateTimeOffset? DateFromEpoch(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return null;
        var seconds = value > 1_000_000_000_000 ? value / 1000 : value;
        return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000));
    }
}
