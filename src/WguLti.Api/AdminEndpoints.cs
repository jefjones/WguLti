using System.Security.Cryptography;
using System.Text;
using WguLti.Core;
using WguLti.Core.Models;
using WguLti.Core.Registration;

namespace WguLti.Api;

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<LtiOptions>();
            if (!Allowed(context.HttpContext, options))
            {
                return Results.Json(
                    new { error = "unauthorized", error_description = "Admin API key required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            return await next(context);
        });

        admin.MapGet("/overview", async (LtiOptions options, WguLti.Core.Stores.IPlatformStore store, CancellationToken ct) =>
            Results.Ok(await store.GetOverviewAsync(options.Issuer, ct)));

        admin.MapGet("/registrations", async (RegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));
        admin.MapGet("/registrations/{id:guid}", async (Guid id, RegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));
        admin.MapPost("/registrations", async (RegistrationDraft draft, RegistrationService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(draft, ct);
            return Results.Created($"/api/admin/registrations/{created.Id}", created);
        });
        admin.MapPut("/registrations/{id:guid}", async (Guid id, RegistrationDraft draft, RegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, draft, ct)));
        admin.MapDelete("/registrations/{id:guid}", async (Guid id, RegistrationService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapGet("/deployments", async (Guid? registrationId, DeploymentService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(registrationId, ct)));
        admin.MapPost("/deployments", async (DeploymentDraft draft, DeploymentService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(draft, ct);
            return Results.Created($"/api/admin/deployments/{created.Id}", created);
        });
        admin.MapPut("/deployments/{id:guid}", async (Guid id, DeploymentUpdate update, DeploymentService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, update, ct)));
        admin.MapDelete("/deployments/{id:guid}", async (Guid id, DeploymentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapGet("/placements", async (Guid registrationId, DeploymentService service, CancellationToken ct) =>
            Results.Ok(await service.ListPlacementsAsync(registrationId, ct)));
        admin.MapPost("/placements", async (Guid registrationId, PlacementDraft draft, DeploymentService service, CancellationToken ct) =>
        {
            var created = await service.AddPlacementAsync(registrationId, draft, ct);
            return Results.Created($"/api/admin/placements/{created.Id}", created);
        });
    }

    private static bool Allowed(HttpContext http, LtiOptions options)
    {
        if (options.DevAdminEnabled) return true;
        if (string.IsNullOrEmpty(options.AdminApiKey)) return false;
        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(options.AdminApiKey);
        return presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
