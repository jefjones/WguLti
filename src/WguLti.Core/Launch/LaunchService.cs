using WguLti.Core.Claims;
using WguLti.Core.Models;
using WguLti.Core.Security;
using WguLti.Core.Stores;
using WguLti.Core.Substitution;
using WguLti.Domain;

namespace WguLti.Core.Launch;

public sealed class LaunchService
{
    private readonly IPlatformStore _store;
    private readonly IPlatformKeySource _keys;
    private readonly LtiOptions _options;

    public LaunchService(IPlatformStore store, IPlatformKeySource keys, LtiOptions options)
    {
        _store = store;
        _keys = keys;
        _options = options;
    }

    public async Task<LoginInitiation> StartAsync(StartLaunchRequest request, CancellationToken ct)
    {
        var launch = await _store.LoadLaunchAsync(request.DeploymentId, request.UserId, request.ContextId, request.ResourceLinkId, request.Placement, ct)
            ?? throw new LtiPlatformException(404, "not_found", "Deployment, user, or resource link was not found.");

        EnsureLaunchable(launch);
        if (launch.ContextId is null)
            throw new LtiPlatformException(400, "invalid_request", "A course context is required to launch.");
        if (launch.PlacementMessageType is not null
            && launch.PlacementMessageType != LtiClaims.ResourceLinkRequest)
        {
            throw new LtiPlatformException(501, "not_implemented", "Only LtiResourceLinkRequest launches are implemented in Phase 1.");
        }

        var target = ResolveTarget(launch);
        var now = DateTimeOffset.UtcNow;
        var session = new NewOidcSession(
            Guid.NewGuid(),
            OpaqueIds.NewHex(32),
            OpaqueIds.NewHex(32),
            launch.DeploymentRowId,
            launch.UserId,
            launch.ContextId,
            launch.ResourceLinkRowId,
            LtiClaims.ResourceLinkRequest,
            request.Placement ?? launch.PlacementKey,
            target,
            now.AddSeconds(Math.Clamp(_options.LoginSessionLifetimeSeconds, 60, 600)));

        await _store.CreateSessionAsync(session, ct);
        await _store.WriteAuditAsync(new AuditEntry(
            launch.TenantId, launch.UserId, launch.ClientId, "lti.launch.started",
            "deployment", launch.DeploymentId, null, null,
            "{\"message_type\":\"LtiResourceLinkRequest\"}"), ct);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["iss"] = launch.Issuer,
            ["login_hint"] = session.LoginHint,
            ["target_link_uri"] = target,
            ["lti_message_hint"] = session.MessageHint,
            ["client_id"] = launch.ClientId,
            ["lti_deployment_id"] = launch.DeploymentId,
        };
        return new LoginInitiation(launch.OidcInitiationUrl, fields);
    }

    public async Task<(bool Ok, string? Html, int Status, string Error, string Description)> AuthorizeAsync(
        AuthRequestInput request,
        string? correlationId,
        string? ip,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        RegistrationSnapshot? registration = null;
        AuthorizeSession? authorizeSession = null;
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            var tool = await _store.GetToolForAssertionAsync(request.ClientId, ct);
            if (tool is not null)
                registration = new RegistrationSnapshot(tool.ClientId, tool.State, tool.RedirectUris);
        }

        if (!string.IsNullOrWhiteSpace(request.LtiMessageHint))
            authorizeSession = await _store.LoadSessionAsync(request.LtiMessageHint, ct);

        var nonceUsed = false;
        if (registration is not null && !string.IsNullOrWhiteSpace(request.Nonce) && authorizeSession is not null)
        {
            nonceUsed = await _store.NonceExistsAsync(authorizeSession.Launch.RegistrationId, NoncePurposes.AuthRequest, request.Nonce, ct);
        }

        var sessionSnapshot = authorizeSession is null
            ? null
            : new SessionSnapshot(
                authorizeSession.Launch.ClientId,
                authorizeSession.LoginHint,
                authorizeSession.MessageHint,
                authorizeSession.TargetLinkUri,
                authorizeSession.ExpiresAt,
                authorizeSession.Consumed);

        var verdict = AuthRequestValidator.Validate(request, registration, sessionSnapshot, nonceUsed, now);
        if (!verdict.Succeeded)
        {
            if (verdict.RedirectTrusted && !string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                var fields = new Dictionary<string, string>
                {
                    ["error"] = verdict.OidcError,
                    ["error_description"] = verdict.Description ?? "invalid_request",
                };
                if (!string.IsNullOrWhiteSpace(request.State)) fields["state"] = request.State!;
                return (false, FormPostHtml.Build(request.RedirectUri!, fields), 200, verdict.OidcError, verdict.Description ?? "");
            }

            var status = verdict.Error == AuthErrorCode.UnauthorizedClient ? 401 : 400;
            return (false, null, status, verdict.OidcError, verdict.Description ?? "invalid_request");
        }

        var launch = authorizeSession!.Launch;
        var added = await _store.TryAddNonceAsync(
            launch.RegistrationId,
            request.Nonce!,
            NoncePurposes.AuthRequest,
            now.AddMinutes(10),
            ct);
        if (!added)
        {
            return ErrorResult(request, AuthErrorCode.InvalidRequest, "nonce has already been used.");
        }

        if (authorizeSession.MessageType != LtiClaims.ResourceLinkRequest)
            return ErrorResult(request, AuthErrorCode.InvalidRequest, "Only LtiResourceLinkRequest is implemented.");

        var subject = await _store.GetOrCreateSubjectAsync(launch.RegistrationId, launch.UserId, ct);
        var claimLinkId = launch.ResourceLinkClaimId
            ?? $"placement:{(launch.PlacementKey ?? "course_navigation")}:{(launch.ContextId?.ToString("N") ?? "none")}";
        var values = new SubstitutionValues(
            launch.UserId.ToString("D"),
            launch.DisplayName,
            launch.Email,
            launch.ContextId?.ToString("D"),
            launch.ContextTitle,
            launch.SectionSisId,
            claimLinkId,
            launch.ResourceLinkAvailableUntil?.ToString("o"),
            null,
            null,
            launch.PrivacyLevel,
            launch.ClaimsAllowed.ToHashSet(StringComparer.Ordinal));
        var custom = CustomParams.MergeAndSubstitute(launch, claimLinkId, values);
        var baseUrl = _options.PublicBaseUrl.TrimEnd('/');
        var contextPath = launch.ContextId?.ToString("D");
        var model = new IdTokenModel(
            launch.Issuer,
            launch.ClientId,
            subject,
            request.Nonce!,
            launch.DeploymentId,
            authorizeSession.TargetLinkUri,
            LtiClaims.ResourceLinkRequest,
            claimLinkId,
            launch.ResourceLinkTitle ?? launch.PlacementLabel ?? launch.PlatformName,
            launch.ResourceLinkDescription,
            launch.RoleUris,
            launch.ContextId?.ToString("D"),
            launch.CourseCode,
            launch.ContextTitle,
            launch.ContextType,
            launch.PlatformGuid,
            string.IsNullOrWhiteSpace(launch.PlatformName) ? _options.PlatformName : launch.PlatformName,
            _options.ProductFamilyCode,
            _options.ProductVersion,
            launch.PlacementWindow ?? "iframe",
            launch.Locale,
            launch.PlacementWidth,
            launch.PlacementHeight,
            custom,
            launch.PrivacyLevel,
            launch.GivenName,
            launch.FamilyName,
            launch.DisplayName,
            launch.Email,
            launch.PictureUrl,
            launch.SisId,
            launch.Scopes,
            contextPath is null ? null : $"{baseUrl}/api/lti/courses/{contextPath}/line_items",
            contextPath is null ? null : $"{baseUrl}/api/lti/courses/{contextPath}/names_and_roles");

        var payload = IdTokenBuilder.Build(model, now, Math.Clamp(_options.IdTokenLifetimeSeconds, 60, 600));
        var token = RsaJwtSigner.Sign(_keys.GetActive(), payload.ToJsonString());
        var consumed = await _store.ConsumeSessionAsync(authorizeSession.SessionId, now, ct);
        if (!consumed)
            return ErrorResult(request, AuthErrorCode.InvalidRequest, "The login session was already used.");

        await _store.WriteAuditAsync(new AuditEntry(
            launch.TenantId, launch.UserId, launch.ClientId, "lti.launch.completed",
            "client_id", launch.ClientId, correlationId, ip,
            "{\"message_type\":\"LtiResourceLinkRequest\"}"), ct);

        var body = FormPostHtml.Build(request.RedirectUri!, new Dictionary<string, string>
        {
            ["id_token"] = token,
            ["state"] = request.State!,
        });
        return (true, body, 200, "", "");
    }

    private static void EnsureLaunchable(LaunchBundle launch)
    {
        if (!RegistrationStates.CanLaunch(launch.RegistrationState))
            throw new LtiPlatformException(403, "access_denied", "Tool registration is not enabled.");
        if (!launch.DeploymentEnabled)
            throw new LtiPlatformException(403, "access_denied", "Deployment is disabled.");
        if (!string.Equals(launch.UserWorkflow, "active", StringComparison.Ordinal))
            throw new LtiPlatformException(403, "access_denied", "User is not active.");
    }

    private static string ResolveTarget(LaunchBundle launch)
    {
        var target = launch.ResourceLinkTarget ?? launch.PlacementTarget ?? launch.DefaultTargetLinkUri;
        if (string.IsNullOrWhiteSpace(target))
            throw new LtiPlatformException(400, "invalid_request", "The tool has no target_link_uri.");
        return target;
    }

    private static (bool Ok, string? Html, int Status, string Error, string Description) ErrorResult(
        AuthRequestInput request, AuthErrorCode code, string description)
    {
        var oidc = code switch
        {
            AuthErrorCode.UnauthorizedClient => "unauthorized_client",
            AuthErrorCode.LoginRequired => "login_required",
            _ => "invalid_request",
        };
        if (!string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            var fields = new Dictionary<string, string>
            {
                ["error"] = oidc,
                ["error_description"] = description,
            };
            if (!string.IsNullOrWhiteSpace(request.State)) fields["state"] = request.State!;
            return (false, FormPostHtml.Build(request.RedirectUri!, fields), 200, oidc, description);
        }
        return (false, null, 400, oidc, description);
    }
}
