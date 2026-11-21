using System.Net;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using WguLti.Core.Models;
using WguLti.Core.Security;
using WguLti.Core.Stores;
using WguLti.Domain;

namespace WguLti.Core.Tokens;

public sealed record TokenRequest(
    string? GrantType,
    string? ClientAssertionType,
    string? ClientAssertion,
    string? Scope,
    string? ClientId,
    IPAddress? ClientIp,
    string? CorrelationId);

public sealed record TokenResponse(bool Ok, int Status, string Error, string Description, string? AccessToken, int ExpiresIn, string? Scope);

public interface IToolKeyResolver
{
    Task<IReadOnlyList<SecurityKey>> ResolveAsync(ToolAssertionContext tool, CancellationToken ct);
}

public sealed class TokenService
{
    private readonly IPlatformStore _store;
    private readonly IToolKeyResolver _keys;
    private readonly LtiOptions _options;

    public TokenService(IPlatformStore store, IToolKeyResolver keys, LtiOptions options)
    {
        _store = store;
        _keys = keys;
        _options = options;
    }

    public async Task<TokenResponse> IssueAsync(TokenRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal))
            return Fail(400, "unsupported_grant_type", "grant_type must be client_credentials.");
        if (!string.Equals(request.ClientAssertionType, PlatformInfo.ClientAssertionType, StringComparison.Ordinal))
            return Fail(400, "invalid_request", "client_assertion_type must be urn:ietf:params:oauth:client-assertion-type:jwt-bearer.");
        if (string.IsNullOrWhiteSpace(request.ClientAssertion))
            return Fail(400, "invalid_request", "client_assertion is required.");

        string? iss;
        try
        {
            iss = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(request.ClientAssertion).Issuer;
        }
        catch (Exception)
        {
            return Fail(401, "invalid_client", "client_assertion is not a JWT.");
        }

        if (string.IsNullOrWhiteSpace(iss))
            return Fail(401, "invalid_client", "client_assertion iss is required.");
        if (!string.IsNullOrWhiteSpace(request.ClientId) && !string.Equals(request.ClientId, iss, StringComparison.Ordinal))
            return Fail(401, "invalid_client", "client_id does not match client_assertion iss.");

        var tool = await _store.GetToolForAssertionAsync(iss, ct);
        if (tool is null || !RegistrationStates.CanLaunch(tool.State))
            return Fail(401, "invalid_client", "Unknown or disabled client.");

        var signingKeys = await _keys.ResolveAsync(tool, ct);
        var replay = false;
        try
        {
            var peek = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(request.ClientAssertion);
            if (peek.TryGetPayloadValue<string>("jti", out var peekJti) && !string.IsNullOrWhiteSpace(peekJti))
                replay = await _store.NonceExistsAsync(tool.RegistrationId, NoncePurposes.ClientAssertionJti, peekJti, ct);
        }
        catch (Exception)
        {
            return Fail(401, "invalid_client", "client_assertion is not a JWT.");
        }

        var verdict = await ClientAssertionValidator.ValidateAsync(
            request.ClientAssertion,
            _options.TokenEndpoint,
            tool.ClientId,
            signingKeys,
            replay,
            DateTimeOffset.UtcNow);
        if (!verdict.Succeeded)
        {
            var status = verdict.Error == "invalid_client" ? 401 : 400;
            return Fail(status, verdict.Error, verdict.Description ?? "client_assertion was rejected.");
        }

        var scopes = ScopeSelector.Select(request.Scope, tool.Scopes);
        if (!scopes.Ok)
            return Fail(400, "invalid_scope", scopes.Error ?? "invalid_scope");

        var stored = await _store.TryAddNonceAsync(
            tool.RegistrationId,
            verdict.Jti!,
            NoncePurposes.ClientAssertionJti,
            verdict.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
            ct);
        if (!stored)
            return Fail(400, "invalid_grant", "client_assertion jti was already used.");

        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var lifetime = Math.Clamp(_options.AccessTokenLifetimeSeconds, 60, 86400);
        await _store.StoreAccessTokenAsync(
            tool.RegistrationId,
            OpaqueIds.Sha256(raw),
            scopes.Scopes.ToArray(),
            DateTimeOffset.UtcNow.AddSeconds(lifetime),
            request.ClientIp,
            ct);
        await _store.WriteAuditAsync(new AuditEntry(
            tool.TenantId, null, tool.ClientId, "lti.token.issued",
            "client_id", tool.ClientId, request.CorrelationId, request.ClientIp?.ToString(),
            "{\"scope_count\":" + scopes.Scopes.Count + "}"), ct);

        return new TokenResponse(true, 200, "", "", raw, lifetime, string.Join(' ', scopes.Scopes));
    }

    private static TokenResponse Fail(int status, string error, string description) =>
        new(false, status, error, description, null, 0, null);
}
