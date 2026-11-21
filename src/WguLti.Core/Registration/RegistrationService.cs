using System.Text.Json;
using WguLti.Core.Models;
using WguLti.Core.Security;
using WguLti.Core.Stores;
using WguLti.Domain;

namespace WguLti.Core.Registration;

public sealed class RegistrationService
{
    private readonly IPlatformStore _store;

    public RegistrationService(IPlatformStore store) => _store = store;

    public Task<IReadOnlyList<RegistrationRecord>> ListAsync(CancellationToken ct) =>
        _store.ListRegistrationsAsync(ct);

    public async Task<RegistrationRecord> GetAsync(Guid id, CancellationToken ct) =>
        await _store.GetRegistrationAsync(id, ct)
        ?? throw new LtiPlatformException(404, "not_found", "Registration was not found.");

    public async Task<RegistrationRecord> CreateAsync(RegistrationDraft draft, CancellationToken ct)
    {
        Validate(draft);
        var owner = draft.OwnerAccountId ?? await _store.RootAccountIdAsync(ct)
            ?? throw new LtiPlatformException(400, "invalid_request", "No account exists yet. Start the API against a seeded database.");
        if (!await _store.AccountExistsAsync(owner, ct))
            throw new LtiPlatformException(400, "invalid_request", "ownerAccountId was not found.");

        if (draft.Placements.Count == 0)
        {
            draft.Placements.Add(new PlacementDraft
            {
                Placement = "course_navigation",
                Label = draft.Name,
                MessageType = LtiClaims.ResourceLinkRequest,
                WindowTarget = "iframe",
            });
        }

        var clientId = OpaqueIds.NewClientId();
        var id = await _store.InsertRegistrationAsync(draft, owner, clientId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<RegistrationRecord> UpdateAsync(Guid id, RegistrationDraft draft, CancellationToken ct)
    {
        Validate(draft);
        var updated = await _store.UpdateRegistrationAsync(id, draft, ct);
        if (!updated) throw new LtiPlatformException(404, "not_found", "Registration was not found.");
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!await _store.SoftDeleteRegistrationAsync(id, ct))
            throw new LtiPlatformException(404, "not_found", "Registration was not found.");
    }

    public static void Validate(RegistrationDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new LtiPlatformException(400, "invalid_request", "name is required.");
        if (string.IsNullOrWhiteSpace(draft.ToolDomain) || draft.ToolDomain.Contains("://", StringComparison.Ordinal))
            throw new LtiPlatformException(400, "invalid_request", "toolDomain must be a hostname.");
        if (!IsHttps(draft.OidcInitiationUrl))
            throw new LtiPlatformException(400, "invalid_request", "oidcInitiationUrl must be an https URL.");
        if (!IsAbsoluteHttp(draft.TargetLinkUri))
            throw new LtiPlatformException(400, "invalid_request", "targetLinkUri must be an absolute http(s) URL.");
        if (draft.RedirectUris.Count == 0 || draft.RedirectUris.Any(uri => !IsAbsoluteHttp(uri)))
            throw new LtiPlatformException(400, "invalid_request", "redirectUris must contain at least one absolute http(s) URL.");
        if (string.IsNullOrWhiteSpace(draft.JwksUri) && string.IsNullOrWhiteSpace(draft.PublicJwk))
            throw new LtiPlatformException(400, "invalid_request", "jwksUri or publicJwk is required.");
        if (!string.IsNullOrWhiteSpace(draft.JwksUri) && !IsHttps(draft.JwksUri))
            throw new LtiPlatformException(400, "invalid_request", "jwksUri must be an https URL.");
        if (!string.IsNullOrWhiteSpace(draft.PublicJwk) && !LooksLikeJwk(draft.PublicJwk))
            throw new LtiPlatformException(400, "invalid_request", "publicJwk must be a JSON object with kty.");
        if (!PrivacyLevels.IsKnown(draft.PrivacyLevel))
            throw new LtiPlatformException(400, "invalid_request", "privacyLevel must be anonymous, name_only, email_only, or public.");
        foreach (var scope in draft.Scopes)
        {
            if (!LtiScopes.IsKnown(scope))
                throw new LtiPlatformException(400, "invalid_request", $"Unknown scope '{scope}'.");
        }
        foreach (var placement in draft.Placements)
        {
            if (!PlacementKinds.All.Contains(placement.Placement))
                throw new LtiPlatformException(400, "invalid_request", $"Unknown placement '{placement.Placement}'.");
            if (placement.WindowTarget is not ("iframe" or "window" or "tray"))
                throw new LtiPlatformException(400, "invalid_request", "windowTarget must be iframe, window, or tray.");
            if (string.IsNullOrWhiteSpace(placement.Label))
                placement.Label = draft.Name;
        }
        foreach (var pair in draft.CustomParameters)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^[a-z0-9_]+$"))
                throw new LtiPlatformException(400, "invalid_request", $"Custom parameter '{pair.Key}' must match [a-z0-9_]+.");
        }
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsAbsoluteHttp(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static bool LooksLikeJwk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && (doc.RootElement.TryGetProperty("kty", out _) || doc.RootElement.TryGetProperty("keys", out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed class DeploymentService
{
    private readonly IPlatformStore _store;

    public DeploymentService(IPlatformStore store) => _store = store;

    public Task<IReadOnlyList<DeploymentRecord>> ListAsync(Guid? registrationId, CancellationToken ct) =>
        _store.ListDeploymentsAsync(registrationId, ct);

    public async Task<DeploymentRecord> CreateAsync(DeploymentDraft draft, CancellationToken ct)
    {
        var hasContext = draft.ContextId is not null;
        var hasAccount = draft.AccountId is not null;
        if (hasContext == hasAccount)
            throw new LtiPlatformException(400, "invalid_request", "Set exactly one of contextId or accountId.");
        if (draft.ContextId is Guid contextId && !await _store.ContextExistsAsync(contextId, ct))
            throw new LtiPlatformException(400, "invalid_request", "contextId was not found.");
        if (draft.AccountId is Guid accountId && !await _store.AccountExistsAsync(accountId, ct))
            throw new LtiPlatformException(400, "invalid_request", "accountId was not found.");

        var (exists, id) = await _store.InsertDeploymentAsync(draft, OpaqueIds.NewDeploymentId(), ct);
        if (!exists) throw new LtiPlatformException(404, "not_found", "Registration was not found.");
        return await _store.GetDeploymentAsync(id, ct)
            ?? throw new LtiPlatformException(500, "server_error", "Deployment could not be reloaded.");
    }

    public async Task<DeploymentRecord> UpdateAsync(Guid id, DeploymentUpdate update, CancellationToken ct)
    {
        if (!await _store.UpdateDeploymentAsync(id, update, ct))
            throw new LtiPlatformException(404, "not_found", "Deployment was not found.");
        return await _store.GetDeploymentAsync(id, ct)
            ?? throw new LtiPlatformException(404, "not_found", "Deployment was not found.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!await _store.DeleteDeploymentAsync(id, ct))
            throw new LtiPlatformException(404, "not_found", "Deployment was not found.");
    }

    public Task<IReadOnlyList<PlacementRecord>> ListPlacementsAsync(Guid registrationId, CancellationToken ct) =>
        _store.ListPlacementsAsync(registrationId, ct);

    public async Task<PlacementRecord> AddPlacementAsync(Guid registrationId, PlacementDraft draft, CancellationToken ct)
    {
        if (!PlacementKinds.All.Contains(draft.Placement))
            throw new LtiPlatformException(400, "invalid_request", "Unknown placement.");
        var id = await _store.InsertPlacementAsync(registrationId, draft, ct);
        var all = await _store.ListPlacementsAsync(registrationId, ct);
        return all.FirstOrDefault(p => p.Id == id)
            ?? throw new LtiPlatformException(404, "not_found", "Registration was not found.");
    }
}
