using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WguLti.Core;
using WguLti.Core.Models;
using WguLti.Core.Security;
using WguLti.Core.Stores;
using WguLti.Domain;

namespace WguLti.Infrastructure.Persistence;

public sealed class PlatformStore : IPlatformStore
{
    private readonly LtiDbContext _db;

    public PlatformStore(LtiDbContext db) => _db = db;

    public async Task<LaunchBundle?> LoadLaunchAsync(
        Guid deploymentId, Guid userId, Guid? contextId, Guid? resourceLinkId, string? placement, CancellationToken ct)
    {
        var deployment = await _db.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deploymentId, ct);
        if (deployment is null) return null;
        if (deployment.ContextId is Guid locked && contextId is Guid requested && locked != requested) return null;

        var registration = await _db.ToolRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == deployment.RegistrationId, ct);
        if (registration is null) return null;
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == registration.TenantId, ct);
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == registration.TenantId, ct);
        if (tenant is null || user is null) return null;

        var resolvedContextId = contextId ?? deployment.ContextId;
        Context? context = null;
        if (resolvedContextId is Guid cid)
        {
            context = await _db.Contexts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == cid && c.TenantId == tenant.Id, ct);
            if (context is null) return null;
        }

        ResourceLink? link = null;
        if (resourceLinkId is Guid rid)
        {
            link = await _db.ResourceLinks.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == rid && r.DeploymentId == deployment.Id && !r.IsDeleted, ct);
            if (link is null) return null;
        }

        Placement? placementRow = null;
        if (!string.IsNullOrWhiteSpace(placement))
        {
            placementRow = await _db.Placements.AsNoTracking()
                .FirstOrDefaultAsync(p => p.RegistrationId == registration.Id && p.PlacementKey == placement, ct);
        }

        var roleUris = new List<string>();
        string? sectionSis = null;
        if (context is not null)
        {
            var roleIds = await _db.Enrollments.AsNoTracking()
                .Where(e => e.ContextId == context.Id && e.UserId == user.Id && e.Status == "active")
                .Select(e => e.RoleId)
                .ToListAsync(ct);
            if (roleIds.Count > 0)
            {
                roleUris = await _db.Roles.AsNoTracking()
                    .Where(r => roleIds.Contains(r.Id) && r.LisRoleUri != null)
                    .Select(r => r.LisRoleUri!)
                    .Distinct()
                    .ToListAsync(ct);
            }

            sectionSis = await _db.CourseSections.AsNoTracking()
                .Where(s => s.ContextId == context.Id && s.IsDefault)
                .Select(s => s.SisId)
                .FirstOrDefaultAsync(ct);
        }

        var customs = await _db.CustomParams.AsNoTracking()
            .Where(c => c.RegistrationId == registration.Id)
            .Select(c => new CustomPair(c.Scope, c.ScopeRefId, c.Name, c.Value))
            .ToListAsync(ct);

        return new LaunchBundle(
            tenant.Id, tenant.Issuer, tenant.PlatformGuid, tenant.Name,
            registration.Id, registration.ClientId, registration.State, registration.PrivacyLevel,
            registration.Scopes, registration.ClaimsAllowed, registration.OidcInitiationUrl, registration.TargetLinkUri,
            registration.RedirectUris, deployment.Id, deployment.DeploymentId, deployment.IsEnabled,
            user.Id, user.GivenName, user.FamilyName, user.DisplayName, user.Email, user.PictureUrl, user.SisId,
            user.Locale, user.WorkflowState, context?.Id, context?.CourseCode, context?.Title, context?.ContextType,
            sectionSis, link?.Id, link?.LinkId, link?.Title, link?.Description, link?.TargetLinkUri, link?.AvailableUntil,
            ParseCustom(link?.Custom), placementRow?.Id, placementRow?.PlacementKey ?? placement, placementRow?.Label,
            placementRow?.TargetLinkUri, placementRow?.WindowTarget, placementRow?.MessageType, placementRow?.Width,
            placementRow?.Height, roleUris, customs);
    }

    public async Task CreateSessionAsync(NewOidcSession session, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        _db.OidcLoginSessions.Add(new OidcLoginSession
        {
            Id = session.Id,
            LtiMessageHint = session.MessageHint,
            LoginHint = session.LoginHint,
            DeploymentId = session.DeploymentRowId,
            UserId = session.UserId,
            ContextId = session.ContextId,
            ResourceLinkId = session.ResourceLinkId,
            MessageType = session.MessageType,
            Placement = session.Placement,
            TargetLinkUri = session.TargetLinkUri,
            CreatedAt = now,
            ExpiresAt = session.ExpiresAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AuthorizeSession?> LoadSessionAsync(string messageHint, CancellationToken ct)
    {
        var session = await _db.OidcLoginSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LtiMessageHint == messageHint, ct);
        if (session is null) return null;
        var launch = await LoadLaunchAsync(session.DeploymentId, session.UserId, session.ContextId, session.ResourceLinkId, session.Placement, ct);
        if (launch is null) return null;
        return new AuthorizeSession(
            session.Id, session.LoginHint, session.LtiMessageHint, session.ExpiresAt,
            session.ConsumedAt is not null, session.TargetLinkUri, session.MessageType, launch);
    }

    public Task<bool> NonceExistsAsync(Guid registrationId, string purpose, string nonce, CancellationToken ct) =>
        _db.UsedNonces.AsNoTracking().AnyAsync(
            n => n.RegistrationId == registrationId && n.Purpose == purpose && n.Nonce == nonce, ct);

    public async Task<bool> TryAddNonceAsync(Guid registrationId, string nonce, string purpose, DateTimeOffset expiresAt, CancellationToken ct)
    {
        _db.UsedNonces.Add(new UsedNonce
        {
            RegistrationId = registrationId,
            Nonce = nonce,
            Purpose = purpose,
            SeenAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> ConsumeSessionAsync(Guid sessionId, DateTimeOffset consumedAt, CancellationToken ct)
    {
        var updated = await _db.OidcLoginSessions
            .Where(s => s.Id == sessionId && s.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, consumedAt), ct);
        return updated == 1;
    }

    public async Task<string> GetOrCreateSubjectAsync(Guid registrationId, Guid userId, CancellationToken ct)
    {
        var existing = await _db.LtiUserIds.AsNoTracking()
            .Where(x => x.RegistrationId == registrationId && x.UserId == userId)
            .Select(x => x.Sub)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrEmpty(existing)) return existing;

        var sub = OpaqueIds.NewHex(16);
        _db.LtiUserIds.Add(new LtiUserId
        {
            RegistrationId = registrationId,
            UserId = userId,
            Sub = sub,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
            return sub;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return await _db.LtiUserIds.AsNoTracking()
                .Where(x => x.RegistrationId == registrationId && x.UserId == userId)
                .Select(x => x.Sub)
                .FirstAsync(ct);
        }
    }

    public async Task WriteAuditAsync(AuditEntry entry, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO lti.audit_log
              (tenant_id, actor_user_id, actor_client_id, event, target_type, target_id, correlation_id, ip, details)
            VALUES
              (@tenant, @user, @client, @event, @tt, @tid, @corr, @ip, @details)
            """, conn);
        cmd.Parameters.AddWithValue("tenant", (object?)entry.TenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("user", (object?)entry.ActorUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("client", (object?)entry.ActorClientId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("event", entry.Event);
        cmd.Parameters.AddWithValue("tt", (object?)entry.TargetType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tid", (object?)entry.TargetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("corr", (object?)entry.CorrelationId ?? DBNull.Value);
        var ip = cmd.Parameters.Add("ip", NpgsqlDbType.Inet);
        ip.Value = IPAddress.TryParse(entry.Ip, out var parsed) ? parsed : DBNull.Value;
        var details = cmd.Parameters.Add("details", NpgsqlDbType.Jsonb);
        details.Value = string.IsNullOrWhiteSpace(entry.DetailsJson) ? "{}" : entry.DetailsJson;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RegistrationRecord>> ListRegistrationsAsync(CancellationToken ct)
    {
        var rows = await _db.ToolRegistrations.AsNoTracking()
            .Where(r => r.State != RegistrationStates.Deleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<RegistrationRecord?> GetRegistrationAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.ToolRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToRecord(row);
    }

    public async Task<ToolAssertionContext?> GetToolForAssertionAsync(string clientId, CancellationToken ct)
    {
        var row = await _db.ToolRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.ClientId == clientId, ct);
        if (row is null) return null;
        return new ToolAssertionContext(
            row.Id, row.TenantId, row.ClientId, row.State, row.Scopes, row.RedirectUris, row.JwksUri, row.PublicJwk);
    }

    public async Task<Guid?> RootAccountIdAsync(CancellationToken ct) =>
        await _db.Accounts.AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken ct) =>
        _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId, ct);

    public Task<bool> ContextExistsAsync(Guid contextId, CancellationToken ct) =>
        _db.Contexts.AsNoTracking().AnyAsync(c => c.Id == contextId, ct);

    public async Task<Guid> InsertRegistrationAsync(RegistrationDraft draft, Guid ownerAccountId, string clientId, CancellationToken ct)
    {
        var account = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == ownerAccountId, ct);
        var now = DateTimeOffset.UtcNow;
        var entity = new ToolRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            OwnerAccountId = ownerAccountId,
            ClientId = clientId,
            Name = draft.Name.Trim(),
            Description = draft.Description,
            Vendor = draft.Vendor,
            ToolDomain = draft.ToolDomain.Trim(),
            OidcInitiationUrl = draft.OidcInitiationUrl.Trim(),
            TargetLinkUri = draft.TargetLinkUri.Trim(),
            RedirectUris = draft.RedirectUris.Select(u => u.Trim()).Distinct(StringComparer.Ordinal).ToArray(),
            JwksUri = string.IsNullOrWhiteSpace(draft.JwksUri) ? null : draft.JwksUri.Trim(),
            PublicJwk = string.IsNullOrWhiteSpace(draft.PublicJwk) ? null : draft.PublicJwk,
            Scopes = draft.Scopes.Distinct(StringComparer.Ordinal).ToArray(),
            PrivacyLevel = draft.PrivacyLevel,
            State = draft.Enabled ? RegistrationStates.On : RegistrationStates.Off,
            RegistrationSource = "manual",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.ToolRegistrations.Add(entity);
        foreach (var placement in draft.Placements)
            _db.Placements.Add(ToPlacement(entity.Id, placement));
        foreach (var pair in draft.CustomParameters)
        {
            _db.CustomParams.Add(new CustomParam
            {
                Id = Guid.NewGuid(),
                RegistrationId = entity.Id,
                Scope = "registration",
                Name = pair.Key,
                Value = pair.Value,
            });
        }
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> UpdateRegistrationAsync(Guid id, RegistrationDraft draft, CancellationToken ct)
    {
        var entity = await _db.ToolRegistrations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return false;
        entity.Name = draft.Name.Trim();
        entity.Description = draft.Description;
        entity.Vendor = draft.Vendor;
        entity.ToolDomain = draft.ToolDomain.Trim();
        entity.OidcInitiationUrl = draft.OidcInitiationUrl.Trim();
        entity.TargetLinkUri = draft.TargetLinkUri.Trim();
        entity.RedirectUris = draft.RedirectUris.Select(u => u.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        entity.JwksUri = string.IsNullOrWhiteSpace(draft.JwksUri) ? null : draft.JwksUri.Trim();
        entity.PublicJwk = string.IsNullOrWhiteSpace(draft.PublicJwk) ? null : draft.PublicJwk;
        entity.Scopes = draft.Scopes.Distinct(StringComparer.Ordinal).ToArray();
        entity.PrivacyLevel = draft.PrivacyLevel;
        entity.State = draft.Enabled ? RegistrationStates.On : RegistrationStates.Off;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await _db.CustomParams
            .Where(c => c.RegistrationId == id && c.Scope == "registration")
            .ToListAsync(ct);
        _db.CustomParams.RemoveRange(existing);
        foreach (var pair in draft.CustomParameters)
        {
            _db.CustomParams.Add(new CustomParam
            {
                Id = Guid.NewGuid(),
                RegistrationId = id,
                Scope = "registration",
                Name = pair.Key,
                Value = pair.Value,
            });
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SoftDeleteRegistrationAsync(Guid id, CancellationToken ct)
    {
        var updated = await _db.ToolRegistrations
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.State, RegistrationStates.Deleted)
                .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
        return updated == 1;
    }

    public async Task<IReadOnlyList<DeploymentRecord>> ListDeploymentsAsync(Guid? registrationId, CancellationToken ct)
    {
        var query = from d in _db.Deployments.AsNoTracking()
                    join r in _db.ToolRegistrations.AsNoTracking() on d.RegistrationId equals r.Id
                    join c in _db.Contexts.AsNoTracking() on d.ContextId equals (Guid?)c.Id into contexts
                    from c in contexts.DefaultIfEmpty()
                    where registrationId == null || d.RegistrationId == registrationId
                    orderby d.CreatedAt descending
                    select new DeploymentRecord(
                        d.Id, d.RegistrationId, r.ClientId, r.Name, d.DeploymentId, d.AccountId, d.ContextId,
                        c != null ? c.CourseCode : null, d.Label, d.IsEnabled, d.CreatedAt);
        return await query.ToListAsync(ct);
    }

    public async Task<DeploymentRecord?> GetDeploymentAsync(Guid id, CancellationToken ct)
    {
        var rows = await ListDeploymentsAsync(null, ct);
        return rows.FirstOrDefault(d => d.Id == id);
    }

    public async Task<(bool RegistrationExists, Guid Id)> InsertDeploymentAsync(DeploymentDraft draft, string deploymentId, CancellationToken ct)
    {
        var registration = await _db.ToolRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == draft.RegistrationId && r.State != RegistrationStates.Deleted, ct);
        if (registration is null) return (false, Guid.Empty);
        var now = DateTimeOffset.UtcNow;
        var entity = new Deployment
        {
            Id = Guid.NewGuid(),
            RegistrationId = draft.RegistrationId,
            DeploymentId = deploymentId,
            AccountId = draft.AccountId,
            ContextId = draft.ContextId,
            Label = draft.Label,
            IsEnabled = draft.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Deployments.Add(entity);
        await _db.SaveChangesAsync(ct);
        return (true, entity.Id);
    }

    public async Task<bool> UpdateDeploymentAsync(Guid id, DeploymentUpdate update, CancellationToken ct)
    {
        var updated = await _db.Deployments.Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Label, update.Label)
                .SetProperty(d => d.IsEnabled, update.IsEnabled)
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);
        return updated == 1;
    }

    public async Task<bool> DeleteDeploymentAsync(Guid id, CancellationToken ct)
    {
        var deleted = await _db.Deployments.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 1;
    }

    public async Task<IReadOnlyList<PlacementRecord>> ListPlacementsAsync(Guid registrationId, CancellationToken ct) =>
        await _db.Placements.AsNoTracking()
            .Where(p => p.RegistrationId == registrationId)
            .OrderBy(p => p.PlacementKey)
            .Select(p => new PlacementRecord(p.Id, p.RegistrationId, p.PlacementKey, p.MessageType, p.TargetLinkUri, p.Label, p.WindowTarget, p.DefaultEnabled))
            .ToListAsync(ct);

    public async Task<Guid> InsertPlacementAsync(Guid registrationId, PlacementDraft draft, CancellationToken ct)
    {
        if (!await _db.ToolRegistrations.AnyAsync(r => r.Id == registrationId, ct))
            throw new LtiPlatformException(404, "not_found", "Registration was not found.");
        var entity = ToPlacement(registrationId, draft);
        _db.Placements.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> DeletePlacementAsync(Guid id, CancellationToken ct)
    {
        var deleted = await _db.Placements.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 1;
    }

    public async Task StoreAccessTokenAsync(Guid registrationId, byte[] tokenHash, string[] scopes, DateTimeOffset expiresAt, IPAddress? ip, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        _db.AccessTokens.Add(new AccessToken
        {
            Id = Guid.NewGuid(),
            RegistrationId = registrationId,
            TokenHash = tokenHash,
            Scopes = scopes,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            ClientIp = ip,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AdminOverview> GetOverviewAsync(string issuer, CancellationToken ct)
    {
        var accounts = await _db.Accounts.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AccountBrief(a.Id, a.Name))
            .ToListAsync(ct);
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.WorkflowState != "deleted")
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserBrief(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);
        var contexts = await _db.Contexts.AsNoTracking()
            .Where(c => c.WorkflowState != "deleted")
            .OrderBy(c => c.CourseCode)
            .Select(c => new ContextBrief(c.Id, c.AccountId, c.CourseCode, c.Title, c.WorkflowState))
            .ToListAsync(ct);
        var registrations = await ListRegistrationsAsync(ct);
        var deployments = await ListDeploymentsAsync(null, ct);
        return new AdminOverview(issuer, accounts, users, contexts, registrations, deployments);
    }

    public async Task<string?> GetFreshJwksCacheAsync(Guid registrationId, DateTimeOffset now, CancellationToken ct) =>
        await _db.ToolJwksCaches.AsNoTracking()
            .Where(c => c.RegistrationId == registrationId && c.ExpiresAt > now)
            .Select(c => c.Jwks)
            .FirstOrDefaultAsync(ct);

    public async Task UpsertJwksCacheAsync(Guid registrationId, string jwks, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var row = await _db.ToolJwksCaches.FirstOrDefaultAsync(c => c.RegistrationId == registrationId, ct);
        if (row is null)
        {
            _db.ToolJwksCaches.Add(new ToolJwksCache
            {
                RegistrationId = registrationId,
                Jwks = jwks,
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
            });
        }
        else
        {
            row.Jwks = jwks;
            row.FetchedAt = DateTimeOffset.UtcNow;
            row.ExpiresAt = expiresAt;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static RegistrationRecord ToRecord(ToolRegistration r) => new(
        r.Id, r.TenantId, r.OwnerAccountId, r.ClientId, r.Name, r.Description, r.Vendor, r.ToolDomain,
        r.OidcInitiationUrl, r.TargetLinkUri, r.RedirectUris, r.JwksUri, r.PublicJwk, r.Scopes,
        r.PrivacyLevel, r.State, r.CreatedAt);

    private static Placement ToPlacement(Guid registrationId, PlacementDraft draft) => new()
    {
        Id = Guid.NewGuid(),
        RegistrationId = registrationId,
        PlacementKey = draft.Placement,
        MessageType = string.IsNullOrWhiteSpace(draft.MessageType) ? LtiClaims.ResourceLinkRequest : draft.MessageType,
        TargetLinkUri = draft.TargetLinkUri,
        Label = string.IsNullOrWhiteSpace(draft.Label) ? draft.Placement : draft.Label,
        WindowTarget = string.IsNullOrWhiteSpace(draft.WindowTarget) ? "iframe" : draft.WindowTarget,
        DefaultEnabled = true,
    };

    private static IReadOnlyDictionary<string, string> ParseCustom(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    map[property.Name] = property.Value.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            return map;
        }
        return map;
    }
}
