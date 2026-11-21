using System.Net;

namespace WguLti.Domain;

// Persistence shapes for the tables Phase 1 reads and writes.
// Column names follow snake_case via EFCore.NamingConventions.
// db/V001__baseline.sql remains the source of truth; these types do not create schema.

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string PlatformGuid { get; set; } = "";
    public string DefaultTimeZone { get; set; } = "America/Denver";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Account
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ParentAccountId { get; set; }
    public string Name { get; set; } = "";
    public string? SisId { get; set; }
    public Guid[] Path { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? SisId { get; set; }
    public string? Email { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? DisplayName { get; set; }
    public string? SortableName { get; set; }
    public string Locale { get; set; } = "en-US";
    public string? TimeZone { get; set; }
    public string? PictureUrl { get; set; }
    public string WorkflowState { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Role
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? AccountId { get; set; }
    public string Name { get; set; } = "";
    public string BaseRoleType { get; set; } = "";
    public string? LisRoleUri { get; set; }
    public bool IsBuiltIn { get; set; }
    public string WorkflowState { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Context
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public string ContextType { get; set; } = "CourseOffering";
    public string CourseCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SisId { get; set; }
    public string? TimeZone { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string WorkflowState { get; set; } = "unpublished";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CourseSection
{
    public Guid Id { get; set; }
    public Guid ContextId { get; set; }
    public string Name { get; set; } = "";
    public string? SisId { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Enrollment
{
    public Guid Id { get; set; }
    public Guid ContextId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? AssociatedUserId { get; set; }
    public string Status { get; set; } = "active";
    public string? SisId { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ToolRegistration
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OwnerAccountId { get; set; }
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Vendor { get; set; }
    public string? LogoUri { get; set; }
    public string ToolDomain { get; set; } = "";
    public string OidcInitiationUrl { get; set; } = "";
    public string TargetLinkUri { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];
    public string? JwksUri { get; set; }
    public string? PublicJwk { get; set; }
    public string TokenEndpointAuthMethod { get; set; } = "private_key_jwt";
    public string[] Scopes { get; set; } = [];
    public string PrivacyLevel { get; set; } = PrivacyLevels.Anonymous;
    public string[] ClaimsAllowed { get; set; } = [];
    public string RegistrationSource { get; set; } = "manual";
    public string? ConfigUrl { get; set; }
    public string State { get; set; } = RegistrationStates.Off;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Deployment
{
    public Guid Id { get; set; }
    public Guid RegistrationId { get; set; }
    public string DeploymentId { get; set; } = "";
    public Guid? AccountId { get; set; }
    public Guid? ContextId { get; set; }
    public string? Label { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Placement
{
    public Guid Id { get; set; }
    public Guid RegistrationId { get; set; }
    public string PlacementKey { get; set; } = "course_navigation";
    public string MessageType { get; set; } = LtiClaims.ResourceLinkRequest;
    public string? TargetLinkUri { get; set; }
    public string Label { get; set; } = "";
    public string? IconUrl { get; set; }
    public string WindowTarget { get; set; } = "iframe";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string[] RolesAllowed { get; set; } = [];
    public bool DefaultEnabled { get; set; } = true;
}

public sealed class CustomParam
{
    public Guid Id { get; set; }
    public Guid RegistrationId { get; set; }
    public string Scope { get; set; } = "registration";
    public Guid? ScopeRefId { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class ResourceLink
{
    public Guid Id { get; set; }
    public Guid DeploymentId { get; set; }
    public Guid? ContextId { get; set; }
    public Guid? AssignmentId { get; set; }
    public Guid? ContentItemId { get; set; }
    public string LinkId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? TargetLinkUri { get; set; }
    public string Custom { get; set; } = "{}";
    public DateTimeOffset? AvailableUntil { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OidcLoginSession
{
    public Guid Id { get; set; }
    public string LtiMessageHint { get; set; } = "";
    public string LoginHint { get; set; } = "";
    public Guid DeploymentId { get; set; }
    public Guid UserId { get; set; }
    public Guid? UserSessionId { get; set; }
    public Guid? ContextId { get; set; }
    public Guid? ResourceLinkId { get; set; }
    public Guid? DeepLinkRequestId { get; set; }
    public string MessageType { get; set; } = LtiClaims.ResourceLinkRequest;
    public string? Placement { get; set; }
    public string TargetLinkUri { get; set; } = "";
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UsedNonce
{
    public Guid RegistrationId { get; set; }
    public string Nonce { get; set; } = "";
    public string Purpose { get; set; } = "";
    public DateTimeOffset SeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AccessToken
{
    public Guid Id { get; set; }
    public Guid RegistrationId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public IPAddress? ClientIp { get; set; }
}

public sealed class LtiUserId
{
    public Guid RegistrationId { get; set; }
    public Guid UserId { get; set; }
    public string Sub { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ToolJwksCache
{
    public Guid RegistrationId { get; set; }
    public string Jwks { get; set; } = "{}";
    public string? Etag { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class OutboxJob
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset RunAfter { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 8;
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
