namespace WguLti.Core.Models;

public sealed record CustomPair(string Scope, Guid? ScopeRefId, string Name, string Value);

public sealed record LaunchBundle(
    Guid TenantId,
    string Issuer,
    string PlatformGuid,
    string PlatformName,
    Guid RegistrationId,
    string ClientId,
    string RegistrationState,
    string PrivacyLevel,
    string[] Scopes,
    string[] ClaimsAllowed,
    string OidcInitiationUrl,
    string DefaultTargetLinkUri,
    string[] RedirectUris,
    Guid DeploymentRowId,
    string DeploymentId,
    bool DeploymentEnabled,
    Guid UserId,
    string? GivenName,
    string? FamilyName,
    string? DisplayName,
    string? Email,
    string? PictureUrl,
    string? SisId,
    string Locale,
    string UserWorkflow,
    Guid? ContextId,
    string? CourseCode,
    string? ContextTitle,
    string? ContextType,
    string? SectionSisId,
    Guid? ResourceLinkRowId,
    string? ResourceLinkClaimId,
    string? ResourceLinkTitle,
    string? ResourceLinkDescription,
    string? ResourceLinkTarget,
    DateTimeOffset? ResourceLinkAvailableUntil,
    IReadOnlyDictionary<string, string> LinkCustom,
    Guid? PlacementId,
    string? PlacementKey,
    string? PlacementLabel,
    string? PlacementTarget,
    string? PlacementWindow,
    string? PlacementMessageType,
    int? PlacementWidth,
    int? PlacementHeight,
    IReadOnlyList<string> RoleUris,
    IReadOnlyList<CustomPair> CustomParams);

public sealed record AuthorizeSession(
    Guid SessionId,
    string LoginHint,
    string MessageHint,
    DateTimeOffset ExpiresAt,
    bool Consumed,
    string TargetLinkUri,
    string MessageType,
    LaunchBundle Launch);

public sealed record NewOidcSession(
    Guid Id,
    string MessageHint,
    string LoginHint,
    Guid DeploymentRowId,
    Guid UserId,
    Guid? ContextId,
    Guid? ResourceLinkId,
    string MessageType,
    string? Placement,
    string TargetLinkUri,
    DateTimeOffset ExpiresAt);

public sealed record AuditEntry(
    Guid? TenantId,
    Guid? ActorUserId,
    string? ActorClientId,
    string Event,
    string? TargetType,
    string? TargetId,
    string? CorrelationId,
    string? Ip,
    string DetailsJson);

public sealed record RegistrationRecord(
    Guid Id,
    Guid TenantId,
    Guid OwnerAccountId,
    string ClientId,
    string Name,
    string? Description,
    string? Vendor,
    string ToolDomain,
    string OidcInitiationUrl,
    string TargetLinkUri,
    string[] RedirectUris,
    string? JwksUri,
    string? PublicJwk,
    string[] Scopes,
    string PrivacyLevel,
    string State,
    DateTimeOffset CreatedAt);

public sealed record DeploymentRecord(
    Guid Id,
    Guid RegistrationId,
    string ClientId,
    string ToolName,
    string DeploymentId,
    Guid? AccountId,
    Guid? ContextId,
    string? CourseCode,
    string? Label,
    bool IsEnabled,
    DateTimeOffset CreatedAt);

public sealed record PlacementRecord(
    Guid Id,
    Guid RegistrationId,
    string Placement,
    string MessageType,
    string? TargetLinkUri,
    string Label,
    string WindowTarget,
    bool DefaultEnabled);

public sealed record ToolAssertionContext(
    Guid RegistrationId,
    Guid TenantId,
    string ClientId,
    string State,
    string[] Scopes,
    string[] RedirectUris,
    string? JwksUri,
    string? PublicJwk);

public sealed record AccountBrief(Guid Id, string Name);
public sealed record UserBrief(Guid Id, string? DisplayName, string? Email);
public sealed record ContextBrief(Guid Id, Guid AccountId, string CourseCode, string Title, string WorkflowState);

public sealed record AdminOverview(
    string Issuer,
    IReadOnlyList<AccountBrief> Accounts,
    IReadOnlyList<UserBrief> Users,
    IReadOnlyList<ContextBrief> Contexts,
    IReadOnlyList<RegistrationRecord> Registrations,
    IReadOnlyList<DeploymentRecord> Deployments);

public sealed class RegistrationDraft
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Vendor { get; set; }
    public string ToolDomain { get; set; } = "";
    public string OidcInitiationUrl { get; set; } = "";
    public string TargetLinkUri { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
    public string? JwksUri { get; set; }
    public string? PublicJwk { get; set; }
    public List<string> Scopes { get; set; } = [];
    public string PrivacyLevel { get; set; } = "anonymous";
    public bool Enabled { get; set; } = true;
    public Guid? OwnerAccountId { get; set; }
    public List<PlacementDraft> Placements { get; set; } = [];
    public Dictionary<string, string> CustomParameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PlacementDraft
{
    public string Placement { get; set; } = "course_navigation";
    public string MessageType { get; set; } = "LtiResourceLinkRequest";
    public string? TargetLinkUri { get; set; }
    public string? Label { get; set; }
    public string WindowTarget { get; set; } = "iframe";
}

public sealed class DeploymentDraft
{
    public Guid RegistrationId { get; set; }
    public Guid? ContextId { get; set; }
    public Guid? AccountId { get; set; }
    public string? Label { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class DeploymentUpdate
{
    public string? Label { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed record StartLaunchRequest(
    Guid DeploymentId,
    Guid UserId,
    Guid? ContextId,
    Guid? ResourceLinkId,
    string? Placement,
    string? ReturnUrl);

public sealed record LoginInitiation(string LoginInitiationUrl, IReadOnlyDictionary<string, string> Fields);
