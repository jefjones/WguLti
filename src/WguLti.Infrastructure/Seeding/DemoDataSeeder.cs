using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WguLti.Core;
using WguLti.Domain;
using WguLti.Infrastructure.Persistence;

namespace WguLti.Infrastructure.Seeding;

public sealed class DemoDataSeeder
{
    private readonly LtiDbContext _db;
    private readonly LtiOptions _options;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(LtiDbContext db, IOptions<LtiOptions> options, ILogger<DemoDataSeeder> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        try
        {
            if (!await _db.Database.CanConnectAsync(ct))
            {
                _logger.LogWarning("Postgres is not reachable. Set ConnectionStrings:Lti in user-secrets. JWKS still works without it.");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres is not reachable. Set ConnectionStrings:Lti in user-secrets. JWKS still works without it.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == "wgu", ct);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Slug = "wgu",
                Name = _options.PlatformName,
                Issuer = _options.Issuer,
                PlatformGuid = "wgu-lti-dev",
                DefaultTimeZone = "America/Denver",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.Equals(tenant.Issuer, _options.Issuer, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Tenant 'wgu' issuer is {Issuer} but Lti:Issuer is {Configured}. Launches use the tenant issuer.",
                tenant.Issuer, _options.Issuer);
        }

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Name == "WGU", ct);
        if (account is null)
        {
            account = new Account
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Name = "WGU",
                Path = [],
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(ct);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == "ada@wgu.example", ct);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                SisId = "ada",
                Email = "ada@wgu.example",
                GivenName = "Ada",
                FamilyName = "Lovelace",
                DisplayName = "Ada Lovelace",
                SortableName = "Lovelace, Ada",
                Locale = "en-US",
                WorkflowState = "active",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }

        var context = await _db.Contexts.FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.SisId == "C950", ct);
        if (context is null)
        {
            context = new Context
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                AccountId = account.Id,
                ContextType = "CourseOffering",
                CourseCode = "C950",
                Title = "Data Structures II",
                SisId = "C950",
                WorkflowState = "available",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Contexts.Add(context);
            await _db.SaveChangesAsync(ct);
        }

        var section = await _db.CourseSections.FirstOrDefaultAsync(s => s.ContextId == context.Id && s.IsDefault, ct);
        if (section is null)
        {
            _db.CourseSections.Add(new CourseSection
            {
                Id = Guid.NewGuid(),
                ContextId = context.Id,
                Name = "Default",
                SisId = "C950",
                IsDefault = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await _db.SaveChangesAsync(ct);
        }

        var student = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.IsBuiltIn && r.Name == "Student", ct);
        if (student is null)
        {
            _logger.LogWarning("Built-in Student role is missing. Apply db/V002__seed_reference.sql to enroll the demo user.");
            return;
        }

        var enrolled = await _db.Enrollments.AnyAsync(
            e => e.ContextId == context.Id && e.UserId == user.Id && e.RoleId == student.Id, ct);
        if (!enrolled)
        {
            _db.Enrollments.Add(new Enrollment
            {
                Id = Guid.NewGuid(),
                ContextId = context.Id,
                UserId = user.Id,
                RoleId = student.Id,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Demo tenant, account, user Ada Lovelace, and course C950 are ready.");
    }
}
