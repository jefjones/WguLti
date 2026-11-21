using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WguLti.Core.Tokens;

public sealed record ClientAssertionResult(
    bool Succeeded,
    string Error,
    string? Description,
    string? ClientId,
    string? Jti,
    DateTimeOffset? ExpiresAt)
{
    public static ClientAssertionResult Ok(string clientId, string jti, DateTimeOffset expiresAt) =>
        new(true, "", null, clientId, jti, expiresAt);

    public static ClientAssertionResult Fail(string error, string description) =>
        new(false, error, description, null, null, null);
}

public static class ScopeSelector
{
    public static (bool Ok, string? Error, IReadOnlyList<string> Scopes) Select(
        string? requested,
        IReadOnlyCollection<string> granted)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return (false, "scope is required.", Array.Empty<string>());

        var grantedSet = granted as IReadOnlySet<string> ?? granted.ToHashSet(StringComparer.Ordinal);
        var selected = new List<string>();
        foreach (var scope in requested.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (grantedSet.Contains(scope) && !selected.Contains(scope, StringComparer.Ordinal))
                selected.Add(scope);
        }

        if (selected.Count == 0)
            return (false, "None of the requested scopes are granted to this client.", Array.Empty<string>());

        return (true, null, selected);
    }
}

/// <summary>
/// Validates a private_key_jwt client assertion (IMS Security Framework 1.0 / RFC 7523).
/// </summary>
public static class ClientAssertionValidator
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxFuture = TimeSpan.FromMinutes(5);

    public static async Task<ClientAssertionResult> ValidateAsync(
        string? token,
        string expectedAudience,
        string expectedClientId,
        IReadOnlyCollection<SecurityKey> signingKeys,
        bool jtiAlreadyUsed,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ClientAssertionResult.Fail("invalid_client", "client_assertion is missing.");
        if (string.IsNullOrWhiteSpace(expectedClientId))
            return ClientAssertionResult.Fail("invalid_client", "client_id is missing.");
        if (signingKeys.Count == 0)
            return ClientAssertionResult.Fail("invalid_client", "No public key is available for this client.");

        JsonWebToken jwt;
        try
        {
            jwt = new JsonWebToken(token);
        }
        catch (Exception)
        {
            return ClientAssertionResult.Fail("invalid_client", "client_assertion is not a JWT.");
        }

        if (!string.Equals(jwt.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            return ClientAssertionResult.Fail("invalid_client", "client_assertion must be signed with RS256.");

        var handler = new JsonWebTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = expectedClientId,
            ValidateAudience = true,
            ValidAudience = expectedAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ClockSkew = ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        };

        var result = await handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            var expired = result.Exception is SecurityTokenExpiredException;
            return ClientAssertionResult.Fail(
                expired ? "invalid_grant" : "invalid_client",
                expired ? "client_assertion has expired." : "client_assertion was rejected.");
        }

        if (!string.Equals(jwt.Subject, expectedClientId, StringComparison.Ordinal))
            return ClientAssertionResult.Fail("invalid_client", "client_assertion sub must equal the client_id.");

        if (jwt.IssuedAt == DateTime.MinValue)
            return ClientAssertionResult.Fail("invalid_grant", "client_assertion iat is required.");

        var iat = AsUtc(jwt.IssuedAt);
        if (iat > utcNow.Add(ClockSkew))
            return ClientAssertionResult.Fail("invalid_grant", "client_assertion iat is in the future.");

        var exp = AsUtc(jwt.ValidTo);
        if (exp > utcNow.Add(MaxFuture).Add(ClockSkew))
            return ClientAssertionResult.Fail("invalid_grant", "client_assertion exp is more than 5 minutes ahead.");

        if (!jwt.TryGetPayloadValue<string>("jti", out var jti) || string.IsNullOrWhiteSpace(jti))
            return ClientAssertionResult.Fail("invalid_grant", "client_assertion jti is required.");
        if (jti.Length > 255)
            return ClientAssertionResult.Fail("invalid_grant", "client_assertion jti is too long.");
        if (jtiAlreadyUsed)
            return ClientAssertionResult.Fail("invalid_grant", "client_assertion jti was already used.");

        return ClientAssertionResult.Ok(expectedClientId, jti, exp);
    }

    private static DateTimeOffset AsUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return new DateTimeOffset(utc);
    }
}
