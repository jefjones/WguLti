using System.Net;
using WguLti.Core.Models;

namespace WguLti.Core.Stores;

public interface IPlatformStore
{
    Task<LaunchBundle?> LoadLaunchAsync(Guid deploymentId, Guid userId, Guid? contextId, Guid? resourceLinkId, string? placement, CancellationToken ct);
    Task CreateSessionAsync(NewOidcSession session, CancellationToken ct);
    Task<AuthorizeSession?> LoadSessionAsync(string messageHint, CancellationToken ct);
    Task<bool> NonceExistsAsync(Guid registrationId, string purpose, string nonce, CancellationToken ct);
    Task<bool> TryAddNonceAsync(Guid registrationId, string nonce, string purpose, DateTimeOffset expiresAt, CancellationToken ct);
    Task<bool> ConsumeSessionAsync(Guid sessionId, DateTimeOffset consumedAt, CancellationToken ct);
    Task<string> GetOrCreateSubjectAsync(Guid registrationId, Guid userId, CancellationToken ct);
    Task WriteAuditAsync(AuditEntry entry, CancellationToken ct);

    Task<IReadOnlyList<RegistrationRecord>> ListRegistrationsAsync(CancellationToken ct);
    Task<RegistrationRecord?> GetRegistrationAsync(Guid id, CancellationToken ct);
    Task<ToolAssertionContext?> GetToolForAssertionAsync(string clientId, CancellationToken ct);
    Task<Guid?> RootAccountIdAsync(CancellationToken ct);
    Task<bool> AccountExistsAsync(Guid accountId, CancellationToken ct);
    Task<bool> ContextExistsAsync(Guid contextId, CancellationToken ct);
    Task<Guid> InsertRegistrationAsync(RegistrationDraft draft, Guid ownerAccountId, string clientId, CancellationToken ct);
    Task<bool> UpdateRegistrationAsync(Guid id, RegistrationDraft draft, CancellationToken ct);
    Task<bool> SoftDeleteRegistrationAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<DeploymentRecord>> ListDeploymentsAsync(Guid? registrationId, CancellationToken ct);
    Task<DeploymentRecord?> GetDeploymentAsync(Guid id, CancellationToken ct);
    Task<(bool RegistrationExists, Guid Id)> InsertDeploymentAsync(DeploymentDraft draft, string deploymentId, CancellationToken ct);
    Task<bool> UpdateDeploymentAsync(Guid id, DeploymentUpdate update, CancellationToken ct);
    Task<bool> DeleteDeploymentAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<PlacementRecord>> ListPlacementsAsync(Guid registrationId, CancellationToken ct);
    Task<Guid> InsertPlacementAsync(Guid registrationId, PlacementDraft draft, CancellationToken ct);
    Task<bool> DeletePlacementAsync(Guid id, CancellationToken ct);

    Task StoreAccessTokenAsync(Guid registrationId, byte[] tokenHash, string[] scopes, DateTimeOffset expiresAt, IPAddress? ip, CancellationToken ct);
    Task<AdminOverview> GetOverviewAsync(string issuer, CancellationToken ct);
    Task<string?> GetFreshJwksCacheAsync(Guid registrationId, DateTimeOffset now, CancellationToken ct);
    Task UpsertJwksCacheAsync(Guid registrationId, string jwks, DateTimeOffset expiresAt, CancellationToken ct);
}

public interface IBackgroundMaintenance
{
    Task PurgeExpiredAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> EnsureAuditPartitionsAsync(CancellationToken ct);
    Task<int> DispatchOutboxOnceAsync(string workerId, CancellationToken ct);
}
