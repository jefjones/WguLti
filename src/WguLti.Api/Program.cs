using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using WguLti.Api;
using WguLti.Core;
using WguLti.Domain;
using WguLti.Infrastructure;
using WguLti.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWguLtiInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddCors(options => options.AddPolicy("client", policy => policy
    .WithOrigins("http://localhost:3010", "https://localhost:3010")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "rate_limited", error_description = "Too many token requests." }, token);
    };
    options.AddPolicy("token", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();
var enforceHttps = !app.Environment.IsEnvironment("Testing");

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Lti:SeedDemoData", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is WguLti.Core.LtiPlatformException lti)
    {
        context.Response.StatusCode = lti.StatusCode;
        await context.Response.WriteAsJsonAsync(new { error = lti.Error, error_description = lti.Message });
        return;
    }

    var dbDown = IsDatabaseDown(error);
    context.Response.StatusCode = dbDown ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new
    {
        error = dbDown ? "database_unavailable" : "server_error",
        error_description = dbDown
            ? "Postgres is not reachable. Set ConnectionStrings:Lti in user-secrets or ConnectionStrings__Lti."
            : "Unexpected error.",
    });
}));

app.Use(async (context, next) =>
{
    if (enforceHttps && !context.Request.IsHttps && !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "https_required",
            error_description = "HTTP port 5090 serves /health only.",
        });
        return;
    }

    var correlation = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlation)) correlation = context.TraceIdentifier;
    context.Items["correlationId"] = correlation;
    context.Response.Headers["X-Correlation-Id"] = correlation;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("client");
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapWellKnown();
app.MapLti();
app.MapStubs();
app.MapAdmin();

app.Run();

static bool IsDatabaseDown(Exception? error)
{
    while (error is not null)
    {
        if (error is Npgsql.NpgsqlException or TimeoutException) return true;
        error = error.InnerException;
    }
    return false;
}

public partial class Program;
