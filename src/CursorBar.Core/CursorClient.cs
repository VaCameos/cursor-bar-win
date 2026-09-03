using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CursorBar.Core;

public sealed class CursorClientException : Exception
{
    public CursorClientError Kind { get; }
    public int StatusCode { get; }

    public CursorClientException(CursorClientError kind, int statusCode = 0)
        : base(kind switch
        {
            CursorClientError.Unauthorized => "Cursor session expired",
            CursorClientError.BadResponse => $"Cursor API returned {statusCode}",
            CursorClientError.EmptyBody => "Cursor API returned an empty response",
            CursorClientError.DecodingFailed => "Could not parse Cursor usage",
            _ => "Cursor API error",
        })
    {
        Kind = kind;
        StatusCode = statusCode;
    }
}

public enum CursorClientError
{
    Unauthorized,
    BadResponse,
    EmptyBody,
    DecodingFailed,
}

public sealed class CursorClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _origin;
    private readonly bool _ownsClient;

    public CursorClient(HttpClient? http = null, Uri? origin = null)
    {
        _origin = origin ?? new Uri("https://cursor.com");
        if (http is null)
        {
            _http = new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
            })
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            _ownsClient = true;
        }
        else
        {
            _http = http;
        }
    }

    public async Task<UsageSnapshot> FetchUsageAsync(CursorSession session, CancellationToken cancellationToken = default)
    {
        var summaryTask = GetAsync("/api/usage-summary", session.CookieHeader, cancellationToken);
        var meTask = OptionalGetAsync("/api/auth/me", session.CookieHeader, cancellationToken);
        var summaryData = await summaryTask.ConfigureAwait(false);
        var meData = await meTask.ConfigureAwait(false);

        var summary = Decode<UsageSummaryResponse>(summaryData);
        AuthMeResponse? me = null;
        if (meData is not null)
        {
            try { me = Decode<AuthMeResponse>(meData); }
            catch (CursorClientException) { /* optional */ }
        }

        var teamId = me?.TeamId ?? session.CachedTeamId;
        var userId = me?.Id;
        var start = UsageMapping.ParseIso8601(summary.BillingCycleStart)
            ?? DateTimeOffset.Now.AddDays(-30);
        var end = DateTimeOffset.Now;

        var periodTask = OptionalPeriodAsync(session.CookieHeader, teamId, userId, cancellationToken);
        var aggregatedTask = OptionalAggregatedAsync(session.CookieHeader, teamId, userId, start, end, cancellationToken);
        var period = await periodTask.ConfigureAwait(false);
        var aggregated = await aggregatedTask.ConfigureAwait(false);
        var amounts = aggregated is null
            ? null
            : AmountAggregation.Amounts(aggregated, period?.AutoBucketModels ?? []);

        return UsageMapping.Snapshot(summary, period, amounts, me, session.CachedEmail, session.Source);
    }

    private async Task<PeriodUsageResponse?> OptionalPeriodAsync(
        string cookie,
        int? teamId,
        int? userId,
        CancellationToken cancellationToken)
    {
        var bodies = UniqueBodies([
            CompactBody(("teamId", teamId), ("userId", userId)),
            CompactBody(("teamId", teamId)),
            CompactBody(("teamId", 0), ("userId", userId)),
            [],
        ]);
        foreach (var body in bodies)
        {
            try
            {
                var data = await PostAsync("/api/dashboard/get-current-period-usage", body, cookie, cancellationToken)
                    .ConfigureAwait(false);
                return Decode<PeriodUsageResponse>(data);
            }
            catch
            {
                // try the next body shape Cursor still accepts
            }
        }
        return null;
    }

    private async Task<AggregatedUsageResponse?> OptionalAggregatedAsync(
        string cookie,
        int? teamId,
        int? userId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var startMs = start.ToUnixTimeMilliseconds();
        var endMs = end.ToUnixTimeMilliseconds();
        var variants = UniqueBodies([
            CompactBody(("teamId", teamId), ("userId", userId), ("startDate", startMs), ("endDate", endMs)),
            CompactBody(("teamId", 0), ("userId", userId), ("startDate", startMs), ("endDate", endMs)),
            CompactBody(("teamId", -1), ("startDate", startMs), ("endDate", endMs)),
            CompactBody(("teamId", 0), ("userId", userId), ("startDate", startMs.ToString()), ("endDate", endMs.ToString())),
        ]);
        foreach (var body in variants)
        {
            try
            {
                var data = await PostAsync("/api/dashboard/get-aggregated-usage-events", body, cookie, cancellationToken)
                    .ConfigureAwait(false);
                var decoded = Decode<AggregatedUsageResponse>(data);
                if (decoded.Aggregations.Count > 0) return decoded;
            }
            catch
            {
                // try the next body shape Cursor still accepts
            }
        }
        return null;
    }

    private async Task<byte[]> GetAsync(string path, string cookie, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_origin, path));
        ApplyHeaders(request, cookie);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Validate(response, data);
        return data;
    }

    private async Task<byte[]?> OptionalGetAsync(string path, string cookie, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync(path, cookie, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]> PostAsync(
        string path,
        Dictionary<string, object?> body,
        string cookie,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_origin, path));
        ApplyHeaders(request, cookie);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Validate(response, data);
        return data;
    }

    private void ApplyHeaders(HttpRequestMessage request, string cookie)
    {
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Origin", _origin.AbsoluteUri.TrimEnd('/'));
        request.Headers.Referrer = new Uri(_origin, "/dashboard?tab=usage");
        request.Headers.TryAddWithoutValidation("User-Agent", "CursorBar/0.1");
    }

    private static Dictionary<string, object?> CompactBody(params (string Key, object? Value)[] pairs)
    {
        var body = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs)
        {
            if (value is not null) body[key] = value;
        }
        return body;
    }

    private static List<Dictionary<string, object?>> UniqueBodies(IEnumerable<Dictionary<string, object?>> bodies)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<Dictionary<string, object?>>();
        foreach (var body in bodies)
        {
            var key = string.Join('|', body.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .Concat(body.Values.Select(v => v?.ToString() ?? "")));
            if (seen.Add(key)) unique.Add(body);
        }
        return unique;
    }

    private static void Validate(HttpResponseMessage response, byte[] data)
    {
        var status = (int)response.StatusCode;
        if (status is 401 or 403)
        {
            throw new CursorClientException(CursorClientError.Unauthorized, status);
        }
        if (status is < 200 or >= 300)
        {
            throw new CursorClientException(CursorClientError.BadResponse, status);
        }
        if (data.Length == 0)
        {
            throw new CursorClientException(CursorClientError.EmptyBody, status);
        }
    }

    private static T Decode<T>(byte[] data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, JsonOptions.Default)
                ?? throw new CursorClientException(CursorClientError.DecodingFailed);
        }
        catch (JsonException)
        {
            throw new CursorClientException(CursorClientError.DecodingFailed);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
