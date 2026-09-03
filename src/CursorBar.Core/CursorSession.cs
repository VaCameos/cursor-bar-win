using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CursorBar.Core;

public sealed record CursorSession
{
    public required string CookieHeader { get; init; }
    public required AuthSource Source { get; init; }
    public string? CachedEmail { get; init; }
    public string? MembershipHint { get; init; }
    public int? CachedTeamId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsExpiring(TimeSpan? within = null, DateTimeOffset? now = null)
    {
        if (ExpiresAt is null) return false;
        return ExpiresAt.Value - (now ?? DateTimeOffset.Now) <= (within ?? TimeSpan.FromSeconds(60));
    }
}

public sealed class SessionException : Exception
{
    public SessionError Kind { get; }

    public SessionException(SessionError kind)
        : base(kind switch
        {
            SessionError.MissingSession => "No Cursor session found",
            SessionError.InvalidToken => "Cursor session token is invalid",
            SessionError.DatabaseUnreadable => "Could not read Cursor local state",
            _ => "Cursor session error",
        })
    {
        Kind = kind;
    }
}

public enum SessionError
{
    MissingSession,
    InvalidToken,
    DatabaseUnreadable,
}

public sealed record JwtClaims
{
    [System.Text.Json.Serialization.JsonPropertyName("sub")]
    public string? Sub { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("exp")]
    public double? Exp { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? Type { get; init; }
}

public static class CursorSessionResolver
{
    public static CursorSession Resolve(string? home = null, string manualCookie = "")
    {
        var trimmed = manualCookie.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            return new CursorSession
            {
                CookieHeader = NormalizeCookieHeader(trimmed),
                Source = AuthSource.ManualCookie,
            };
        }

        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (TryReadCursorAppSession(home, out var appSession) && !appSession.IsExpiring())
        {
            return appSession;
        }
        if (TryReadAgentSession(home, out var agentSession) && !agentSession.IsExpiring())
        {
            return agentSession;
        }
        if (appSession is not null) return appSession;
        if (agentSession is not null) return agentSession;
        throw new SessionException(SessionError.MissingSession);
    }

    public static string NormalizeCookieHeader(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..].Trim();
        }
        if (value.Contains("WorkosCursorSessionToken=", StringComparison.Ordinal)
            || value.Contains("__Secure-next-auth.session-token=", StringComparison.Ordinal)
            || value.Contains("next-auth.session-token=", StringComparison.Ordinal))
        {
            return RewritePlainDoubleColon(value);
        }
        return $"WorkosCursorSessionToken={RewritePlainDoubleColon(value)}";
    }

    public static (string Header, DateTimeOffset? ExpiresAt) CookieHeaderFromJwt(string jwt)
    {
        var claims = DecodeJwtPayload(jwt);
        if (string.IsNullOrEmpty(claims.Sub))
        {
            throw new SessionException(SessionError.InvalidToken);
        }
        DateTimeOffset? expires = claims.Exp is double exp
            ? DateTimeOffset.FromUnixTimeSeconds((long)exp)
            : null;
        return ($"WorkosCursorSessionToken={claims.Sub}%3A%3A{jwt}", expires);
    }

    public static JwtClaims DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new SessionException(SessionError.InvalidToken);
        var data = Base64UrlDecode(parts[1]) ?? throw new SessionException(SessionError.InvalidToken);
        try
        {
            return JsonSerializer.Deserialize<JwtClaims>(data, JsonOptions.Default) ?? new JwtClaims();
        }
        catch (JsonException)
        {
            throw new SessionException(SessionError.InvalidToken);
        }
    }

    public static string? DecodeStateValue(byte[] data)
    {
        var utf8 = Encoding.UTF8.GetString(data);
        if (!utf8.Contains('\0'))
        {
            return UnwrapJsonString(utf8);
        }

        var utf16 = Encoding.Unicode.GetString(data).Replace("\0", "", StringComparison.Ordinal);
        return UnwrapJsonString(TrimControls(utf16));
    }

    public static IEnumerable<string> CursorStateDatabases(string home)
    {
        yield return Path.Combine(home, "AppData", "Roaming", "Cursor", "User", "globalStorage", "state.vscdb");
        yield return Path.Combine(home, "AppData", "Local", "Cursor", "User", "globalStorage", "state.vscdb");
        yield return Path.Combine(home, "Library", "Application Support", "Cursor", "User", "globalStorage", "state.vscdb");
    }

    public static IEnumerable<string> AgentAuthFiles(string home)
    {
        yield return Path.Combine(home, ".cursor", "auth.json");
        yield return Path.Combine(home, ".config", "cursor", "auth.json");
    }

    private static bool TryReadCursorAppSession(string home, out CursorSession? session)
    {
        session = null;
        foreach (var dbPath in CursorStateDatabases(home).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                session = ReadCursorAppSession(dbPath);
                return true;
            }
            catch (SessionException)
            {
                // try the next known Cursor state location
            }
        }
        return false;
    }

    private static CursorSession ReadCursorAppSession(string dbPath)
    {
        var values = ReadItemTable(dbPath, [
            "cursorAuth/accessToken",
            "cursorAuth/cachedEmail",
            "cursorAuth/stripeMembershipType",
            "cursorAuth/cachedTeam",
        ]);
        if (!values.TryGetValue("cursorAuth/accessToken", out var rawToken) || string.IsNullOrEmpty(rawToken))
        {
            throw new SessionException(SessionError.MissingSession);
        }
        var cookie = CookieHeaderFromJwt(rawToken);
        values.TryGetValue("cursorAuth/cachedEmail", out var email);
        values.TryGetValue("cursorAuth/stripeMembershipType", out var membership);
        values.TryGetValue("cursorAuth/cachedTeam", out var team);
        return new CursorSession
        {
            CookieHeader = cookie.Header,
            Source = AuthSource.CursorApp,
            CachedEmail = email,
            MembershipHint = membership is null ? null : UsageMapping.TitleCase(membership),
            CachedTeamId = TeamIdParser.Parse(team),
            ExpiresAt = cookie.ExpiresAt,
        };
    }

    private static bool TryReadAgentSession(string home, out CursorSession? session)
    {
        session = null;
        foreach (var path in AgentAuthFiles(home))
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("accessToken", out var tokenElement))
                {
                    continue;
                }
                var token = tokenElement.GetString();
                if (string.IsNullOrEmpty(token)) continue;
                var cookie = CookieHeaderFromJwt(token);
                session = new CursorSession
                {
                    CookieHeader = cookie.Header,
                    Source = AuthSource.CursorAgent,
                    ExpiresAt = cookie.ExpiresAt,
                };
                return true;
            }
            catch (SessionException)
            {
                // keep looking
            }
            catch (JsonException)
            {
                // keep looking
            }
            catch (IOException)
            {
                // keep looking
            }
        }
        return false;
    }

    public static Dictionary<string, string> ReadItemTable(string dbPath, IReadOnlyList<string> keys)
    {
        if (!File.Exists(dbPath))
        {
            throw new SessionException(SessionError.MissingSession);
        }

        try
        {
            var values = ReadItemTableFile(dbPath, keys);
            if (values.Count > 0) return values;
        }
        catch (SqliteException)
        {
            // Cursor may hold a write lock; try a temp copy next.
        }

        var temp = Path.Combine(Path.GetTempPath(), $"cursorbar-{Guid.NewGuid():N}.vscdb");
        try
        {
            File.Copy(dbPath, temp, overwrite: true);
            var copied = Path.ChangeExtension(dbPath, ".vscdb-wal");
            var shm = Path.ChangeExtension(dbPath, ".vscdb-shm");
            if (File.Exists(copied)) File.Copy(copied, temp + "-wal", overwrite: true);
            if (File.Exists(shm)) File.Copy(shm, temp + "-shm", overwrite: true);
            var values = ReadItemTableFile(temp, keys);
            if (values.Count > 0) return values;
        }
        catch (IOException)
        {
            throw new SessionException(SessionError.DatabaseUnreadable);
        }
        catch (SqliteException)
        {
            throw new SessionException(SessionError.DatabaseUnreadable);
        }
        finally
        {
            TryDelete(temp);
            TryDelete(temp + "-wal");
            TryDelete(temp + "-shm");
        }

        throw new SessionException(SessionError.DatabaseUnreadable);
    }

    private static Dictionary<string, string> ReadItemTableFile(string path, IReadOnlyList<string> keys)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM ItemTable WHERE key = $key";
        var keyParam = command.CreateParameter();
        keyParam.ParameterName = "$key";
        command.Parameters.Add(keyParam);

        foreach (var key in keys)
        {
            keyParam.Value = key;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) continue;
            var decoded = DecodeColumn(reader, 1);
            if (!string.IsNullOrEmpty(decoded))
            {
                values[key] = decoded;
            }
        }
        return values;
    }

    private static string? DecodeColumn(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var fieldType = reader.GetFieldType(ordinal);
        if (fieldType == typeof(byte[]))
        {
            var data = (byte[])reader.GetValue(ordinal);
            return DecodeStateValue(data);
        }
        var text = reader.GetString(ordinal);
        return UnwrapJsonString(text);
    }

    private static string TrimControls(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && (char.IsControl(value[start]) || char.IsWhiteSpace(value[start]))) start++;
        while (end > start && (char.IsControl(value[end - 1]) || char.IsWhiteSpace(value[end - 1]))) end--;
        return value[start..end];
    }

    private static string UnwrapJsonString(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('"'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string>(trimmed);
                if (parsed is not null) return parsed;
            }
            catch (JsonException)
            {
                // fall through
            }
        }
        return trimmed;
    }

    private static string RewritePlainDoubleColon(string value)
    {
        if (!value.Contains("::", StringComparison.Ordinal) || value.Contains("%3A%3A", StringComparison.Ordinal))
        {
            return value;
        }
        return value.Replace("::", "%3A%3A", StringComparison.Ordinal);
    }

    private static byte[]? Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var remainder = base64.Length % 4;
        if (remainder > 0) base64 = base64.PadRight(base64.Length + (4 - remainder), '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // temp cleanup is best-effort
        }
    }
}
