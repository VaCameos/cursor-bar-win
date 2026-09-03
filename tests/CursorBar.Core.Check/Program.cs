using System.Text;
using System.Text.Json;
using CursorBar.Core;

var failures = 0;

void Expect(bool condition, string message)
{
    if (condition) return;
    failures++;
    Console.Error.WriteLine($"FAIL {message}");
}

void ExpectEqual<T>(T actual, T expected, string message)
{
    Expect(EqualityComparer<T>.Default.Equals(actual, expected), $"{message}: {actual} != {expected}");
}

var json = """
    {
      "billingCycleStart": "2026-07-04T00:35:51.000Z",
      "billingCycleEnd": "2026-08-04T00:35:51.000Z",
      "membershipType": "ultra",
      "isUnlimited": false,
      "individualUsage": {
        "plan": {
          "enabled": true,
          "used": 40000,
          "limit": 40000,
          "autoPercentUsed": 98.1,
          "apiPercentUsed": 100,
          "totalPercentUsed": 42.4
        },
        "onDemand": { "enabled": true, "used": 320, "limit": 1000 }
      }
    }
    """u8.ToArray();

try
{
    var summary = JsonSerializer.Deserialize<UsageSummaryResponse>(json, JsonOptions.Default)
        ?? throw new InvalidOperationException("summary");
    var snapshot = UsageMapping.Snapshot(
        summary,
        period: null,
        amounts: null,
        me: new AuthMeResponse { Email = "you@example.com", Name = "Ray" },
        cachedEmail: "cached@example.com",
        authSource: AuthSource.CursorApp,
        fetchedAt: DateTimeOffset.FromUnixTimeSeconds(0));
    ExpectEqual(snapshot.Membership, "Ultra", "membership");
    ExpectEqual(snapshot.Email, "you@example.com", "email");
    ExpectEqual(snapshot.PlanPercent, (double?)42.4, "plan percent");
    ExpectEqual(snapshot.AutoPercent, (double?)98.1, "auto percent");
    ExpectEqual(snapshot.ApiPercent, (double?)100.0, "api percent");
    ExpectEqual(snapshot.OnDemandUsedCents, (int?)320, "on-demand used");
    ExpectEqual(snapshot.HeadlinePercent, (double?)42.4, "headline");
    ExpectEqual(snapshot.SecondaryPercent, (double?)32.0, "secondary");
    Expect(snapshot.BillingEnd is not null, "billing end parsed");
}
catch (Exception ex)
{
    failures++;
    Console.Error.WriteLine($"FAIL decode sample: {ex}");
}

var fallback = UsageMapping.Snapshot(
    new UsageSummaryResponse
    {
        MembershipType = "pro",
        IndividualUsage = new IndividualUsage { Plan = new PlanUsage { Used = 900, Limit = 2000 } },
        AutoModelSelectedDisplayMessage = "You've used 18% of your included total usage",
        NamedModelSelectedDisplayMessage = "You've used 7% of your included API usage",
    },
    period: null,
    amounts: null,
    me: null,
    cachedEmail: "cached@example.com",
    authSource: AuthSource.CursorAgent);
ExpectEqual(fallback.PlanPercent, (double?)45.0, "cents fallback");
ExpectEqual(fallback.AutoPercent, (double?)18.0, "message auto");
ExpectEqual(fallback.ApiPercent, (double?)7.0, "message api");
ExpectEqual(fallback.Email, "cached@example.com", "cached email");
ExpectEqual(fallback.Membership, "Pro", "pro title");

ExpectEqual(UsageMapping.PercentFromMessage("You've used 98% of your included total usage"), (double?)98.0, "parse 98");
ExpectEqual(UsageMapping.PercentFromMessage("You've used 12.5% extra"), (double?)12.5, "parse 12.5");
ExpectEqual(UsageMapping.PercentFromMessage("unavailable"), (double?)null, "parse none");
ExpectEqual(UsageMapping.TitleCase("business_plan"), "Business Plan", "title case");

ExpectEqual(Formatters.PercentLabel(42.4), "42%", "percent label");
ExpectEqual(Formatters.PercentLabel(98.6), "99%", "percent round");
ExpectEqual(Formatters.PercentLabel(null), "—", "percent empty");
ExpectEqual(Formatters.Dollars(320), "$3.20", "dollars");
ExpectEqual(Formatters.Dollars(2000), "$20", "dollars whole");
ExpectEqual(Formatters.Money(840), "$8.40", "money");
ExpectEqual(Formatters.Estimate(8400), "~$84.0 @100%", "estimate");
ExpectEqual(Formatters.AmountDetail(840, 8400), "$8.40  ·  ~$84.0 @100%", "amount detail");
ExpectEqual(Formatters.PercentLabel1(10.44), "10.4%", "percent1");

var aggregated = new AggregatedUsageResponse
{
    Aggregations =
    [
        new AggregationItem { ModelIntent = "composer-2", Tier = 2, TotalCents = 840 },
        new AggregationItem { ModelIntent = "claude-4", Tier = 1, TotalCents = 210 },
        new AggregationItem { ModelIntent = "sand-bot", Tier = 2, TotalCents = 50 },
    ],
};
var amounts = AmountAggregation.Amounts(aggregated);
ExpectEqual(amounts.CursorCents, (int?)890, "cursor cents include sand");
ExpectEqual(amounts.OtherCents, (int?)210, "other cents");
ExpectEqual(amounts.CursorEstimateCents, (int?)840, "cursor estimate excludes sand");
ExpectEqual(amounts.IncludedCents, (int?)1100, "included cents");
ExpectEqual(AmountAggregation.EstimateAtFull(840, 10), (int?)8400, "full estimate");
ExpectEqual(TeamIdParser.Parse("{\"id\":2168997}"), (int?)2168997, "team json");
Expect(AmountAggregation.IsSandModel("sand-1.0"), "sand model");

var periodJson = """
    {"billingCycleEnd":1775451599999,"autoBucketModels":["composer-2"],"planUsage":{"autoPercentUsed":10.4,"apiPercentUsed":2.1}}
    """u8.ToArray();
try
{
    var period = JsonSerializer.Deserialize<PeriodUsageResponse>(periodJson, JsonOptions.Default)
        ?? throw new InvalidOperationException("period");
    ExpectEqual(period.AutoPercentUsed, (double?)10.4, "period auto");
    ExpectEqual(period.ApiPercentUsed, (double?)2.1, "period api");
    Expect(period.BillingCycleEnd is not null, "period end");
}
catch (Exception ex)
{
    failures++;
    Console.Error.WriteLine($"FAIL period decode: {ex}");
}

ExpectEqual(Formatters.UsageColorThreshold(10), UsageTone.Ok, "tone ok");
ExpectEqual(Formatters.UsageColorThreshold(70), UsageTone.Watch, "tone watch");
ExpectEqual(Formatters.UsageColorThreshold(90), UsageTone.High, "tone high");
ExpectEqual(Formatters.UsageColorThreshold(99), UsageTone.Critical, "tone critical");

var now = DateTimeOffset.FromUnixTimeSeconds(0);
var end = now.AddDays(2).AddHours(3);
ExpectEqual(Formatters.ResetCountdown(end, now, chinese: false), "Resets in 2d 3h", "reset en");
ExpectEqual(Formatters.ResetCountdown(end, now, chinese: true), "还有 2 天 3 小时重置", "reset zh");

var jwt = FakeJwt("user_01ABC", 1_900_000_000);
try
{
    var result = CursorSessionResolver.CookieHeaderFromJwt(jwt);
    ExpectEqual(result.Header, $"WorkosCursorSessionToken=user_01ABC%3A%3A{jwt}", "cookie header");
    ExpectEqual(result.ExpiresAt, (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(1_900_000_000), "exp");
}
catch (Exception ex)
{
    failures++;
    Console.Error.WriteLine($"FAIL jwt cookie: {ex}");
}

ExpectEqual(
    CursorSessionResolver.NormalizeCookieHeader("Cookie: WorkosCursorSessionToken=user_1::abc"),
    "WorkosCursorSessionToken=user_1%3A%3Aabc",
    "normalize cookie");
ExpectEqual(
    CursorSessionResolver.NormalizeCookieHeader("user_1::abc"),
    "WorkosCursorSessionToken=user_1%3A%3Aabc",
    "normalize token");

var utf16 = Encoding.Unicode.GetBytes("eyJhbGciOiJnone");
ExpectEqual(CursorSessionResolver.DecodeStateValue(utf16), "eyJhbGciOiJnone", "utf16 token");
ExpectEqual(CursorSessionResolver.DecodeStateValue("\"quoted-token\""u8.ToArray()), "quoted-token", "quoted token");

try
{
    var session = CursorSessionResolver.Resolve(manualCookie: "user_9::tok");
    ExpectEqual(session.Source, AuthSource.ManualCookie, "manual source");
    Expect(session.CookieHeader.Contains("user_9%3A%3Atok"), "manual cookie value");
}
catch (Exception ex)
{
    failures++;
    Console.Error.WriteLine($"FAIL manual cookie: {ex}");
}

var emptyHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
try
{
    CursorSessionResolver.Resolve(home: emptyHome);
    failures++;
    Console.Error.WriteLine("FAIL expected missing session");
}
catch (SessionException ex)
{
    ExpectEqual(ex.Kind, SessionError.MissingSession, "missing session");
}
catch (Exception ex)
{
    failures++;
    Console.Error.WriteLine($"FAIL unexpected {ex}");
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} check(s) failed");
    return 1;
}

Console.WriteLine("All CursorBarCore checks passed");
return 0;

static string FakeJwt(string sub, long exp)
{
    var payload = JsonSerializer.SerializeToUtf8Bytes(new { sub, exp });
    var header = "{\"alg\":\"none\"}"u8.ToArray();
    return $"{Base64Url(header)}.{Base64Url(payload)}.x";
}

static string Base64Url(byte[] data)
    => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
