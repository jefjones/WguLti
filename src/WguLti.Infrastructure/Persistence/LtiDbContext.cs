using Microsoft.EntityFrameworkCore;
using WguLti.Domain;

namespace WguLti.Infrastructure.Persistence;

/// <summary>
/// Maps the existing <c>lti</c> schema. Do not generate EF migrations;
/// <c>db/V001__baseline.sql</c> is the source of truth.
/// </summary>
public sealed class LtiDbContext : DbContext
{
    public LtiDbContext(DbContextOptions<LtiDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Context> Contexts => Set<Context>();
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<ToolRegistration> ToolRegistrations => Set<ToolRegistration>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<Placement> Placements => Set<Placement>();
    public DbSet<CustomParam> CustomParams => Set<CustomParam>();
    public DbSet<ResourceLink> ResourceLinks => Set<ResourceLink>();
    public DbSet<OidcLoginSession> OidcLoginSessions => Set<OidcLoginSession>();
    public DbSet<UsedNonce> UsedNonces => Set<UsedNonce>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<LtiUserId> LtiUserIds => Set<LtiUserId>();
    public DbSet<ToolJwksCache> ToolJwksCaches => Set<ToolJwksCache>();
    public DbSet<OutboxJob> OutboxJobs => Set<OutboxJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("lti");
        modelBuilder.Entity<Tenant>().ToTable("tenants");
        modelBuilder.Entity<Account>().ToTable("accounts");
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<Role>().ToTable("roles");
        modelBuilder.Entity<Context>().ToTable("contexts");
        modelBuilder.Entity<CourseSection>().ToTable("course_sections");
        modelBuilder.Entity<Enrollment>().ToTable("enrollments");
        modelBuilder.Entity<ToolRegistration>().ToTable("tool_registrations");
        modelBuilder.Entity<Deployment>().ToTable("deployments");
        modelBuilder.Entity<Placement>().ToTable("placements");
        modelBuilder.Entity<CustomParam>().ToTable("custom_params");
        modelBuilder.Entity<ResourceLink>().ToTable("resource_links");
        modelBuilder.Entity<OidcLoginSession>().ToTable("oidc_login_sessions");
        modelBuilder.Entity<UsedNonce>().ToTable("used_nonces");
        modelBuilder.Entity<AccessToken>().ToTable("access_tokens");
        modelBuilder.Entity<LtiUserId>().ToTable("lti_user_ids");
        modelBuilder.Entity<ToolJwksCache>().ToTable("tool_jwks_cache");
        modelBuilder.Entity<OutboxJob>().ToTable("outbox_jobs");

        modelBuilder.Entity<Placement>().Property(p => p.PlacementKey).HasColumnName("placement");
        modelBuilder.Entity<UsedNonce>().HasKey(n => new { n.RegistrationId, n.Purpose, n.Nonce });
        modelBuilder.Entity<LtiUserId>().HasKey(x => new { x.RegistrationId, x.UserId });
        modelBuilder.Entity<ToolJwksCache>().HasKey(x => x.RegistrationId);
        modelBuilder.Entity<OutboxJob>().Property(x => x.Id).UseIdentityAlwaysColumn();

        modelBuilder.Entity<ToolRegistration>().Property(x => x.PublicJwk).HasColumnType("jsonb");
        modelBuilder.Entity<ResourceLink>().Property(x => x.Custom).HasColumnType("jsonb");
        modelBuilder.Entity<ToolJwksCache>().Property(x => x.Jwks).HasColumnType("jsonb");
        modelBuilder.Entity<OutboxJob>().Property(x => x.Payload).HasColumnType("jsonb");
        modelBuilder.Entity<AccessToken>().Property(x => x.ClientIp).HasColumnType("inet");
        modelBuilder.Entity<Account>().Property(x => x.Path).HasColumnType("uuid[]");
    }
}
