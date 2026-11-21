using WguLti.Domain;

namespace WguLti.Core.Launch;

public enum AuthErrorCode
{
    None = 0,
    InvalidRequest,
    UnauthorizedClient,
    LoginRequired,
    AccessDenied,
}

public sealed record AuthRequestInput(
    string? Scope,
    string? ResponseType,
    string? ResponseMode,
    string? Prompt,
    string? ClientId,
    string? RedirectUri,
    string? LoginHint,
    string? LtiMessageHint,
    string? State,
    string? Nonce,
    string? TargetLinkUri);

public sealed record RegistrationSnapshot(string ClientId, string State, IReadOnlyCollection<string> RedirectUris);

public sealed record SessionSnapshot(
    string ClientId,
    string LoginHint,
    string MessageHint,
    string TargetLinkUri,
    DateTimeOffset ExpiresAt,
    bool Consumed);

public sealed record AuthValidationResult(bool Succeeded, AuthErrorCode Error, string? Description, bool RedirectTrusted)
{
    public string OidcError => Error switch
    {
        AuthErrorCode.UnauthorizedClient => "unauthorized_client",
        AuthErrorCode.LoginRequired => "login_required",
        AuthErrorCode.AccessDenied => "access_denied",
        AuthErrorCode.None => "",
        _ => "invalid_request",
    };

    public static AuthValidationResult Ok() => new(true, AuthErrorCode.None, null, true);

    public static AuthValidationResult Fail(AuthErrorCode error, string description, bool redirectTrusted) =>
        new(false, error, description, redirectTrusted);
}

/// <summary>
/// Pure checks for the OIDC authentication request: client, redirect, login hints, state, and nonce.
/// Persistence (storing the nonce, consuming the session) stays outside this type.
/// </summary>
public static class AuthRequestValidator
{
    public const int MaxHintLength = 512;

    public static AuthValidationResult Validate(
        AuthRequestInput request,
        RegistrationSnapshot? registration,
        SessionSnapshot? session,
        bool nonceAlreadyUsed,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ClientId) || registration is null
            || !string.Equals(registration.ClientId, request.ClientId, StringComparison.Ordinal))
        {
            return AuthValidationResult.Fail(AuthErrorCode.UnauthorizedClient, "Unknown client_id.", false);
        }

        if (!RegistrationStates.CanLaunch(registration.State))
        {
            return AuthValidationResult.Fail(AuthErrorCode.UnauthorizedClient, "Tool registration is not enabled.", false);
        }

        if (string.IsNullOrWhiteSpace(request.RedirectUri)
            || !registration.RedirectUris.Contains(request.RedirectUri, StringComparer.Ordinal))
        {
            return AuthValidationResult.Fail(
                AuthErrorCode.UnauthorizedClient,
                "redirect_uri is not registered for this client.",
                false);
        }

        if (!ScopeContainsOpenId(request.Scope))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "scope must include openid.", true);
        if (!string.Equals(request.ResponseType, "id_token", StringComparison.Ordinal))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "response_type must be id_token.", true);
        if (!string.Equals(request.ResponseMode, "form_post", StringComparison.Ordinal))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "response_mode must be form_post.", true);
        if (!string.Equals(request.Prompt, "none", StringComparison.Ordinal))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "prompt must be none.", true);

        if (!ValidToken(request.State))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "state is required.", true);
        if (!ValidToken(request.Nonce))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "nonce is required.", true);
        if (!ValidToken(request.LoginHint))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "login_hint is required.", true);
        if (!ValidToken(request.LtiMessageHint))
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "lti_message_hint is required.", true);

        if (session is null)
            return AuthValidationResult.Fail(AuthErrorCode.LoginRequired, "Unknown or mismatched login session.", true);

        if (!string.Equals(session.ClientId, request.ClientId, StringComparison.Ordinal))
            return AuthValidationResult.Fail(AuthErrorCode.UnauthorizedClient, "client_id does not match the login session.", true);

        if (!string.Equals(session.LoginHint, request.LoginHint, StringComparison.Ordinal)
            || !string.Equals(session.MessageHint, request.LtiMessageHint, StringComparison.Ordinal))
        {
            return AuthValidationResult.Fail(AuthErrorCode.LoginRequired, "login_hint does not match the login session.", true);
        }

        if (session.Consumed)
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "The login session was already used.", true);

        if (session.ExpiresAt <= utcNow)
            return AuthValidationResult.Fail(AuthErrorCode.LoginRequired, "The login session has expired.", true);

        if (!string.IsNullOrEmpty(request.TargetLinkUri)
            && !string.Equals(request.TargetLinkUri, session.TargetLinkUri, StringComparison.Ordinal))
        {
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "target_link_uri does not match login initiation.", true);
        }

        if (nonceAlreadyUsed)
            return AuthValidationResult.Fail(AuthErrorCode.InvalidRequest, "nonce has already been used.", true);

        return AuthValidationResult.Ok();
    }

    private static bool ScopeContainsOpenId(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return false;
        return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("openid", StringComparer.Ordinal);
    }

    private static bool ValidToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxHintLength;
}
