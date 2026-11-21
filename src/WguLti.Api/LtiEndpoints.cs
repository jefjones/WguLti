using WguLti.Core.Launch;
using WguLti.Core.Models;
using WguLti.Core.Tokens;

namespace WguLti.Api;

public static class LtiEndpoints
{
    public static void MapLti(this WebApplication app)
    {
        app.MapPost("/api/lti/launches", async (StartLaunchRequest request, LaunchService launches, CancellationToken ct) =>
        {
            var initiation = await launches.StartAsync(request, ct);
            return Results.Ok(new
            {
                loginInitiationUrl = initiation.LoginInitiationUrl,
                fields = initiation.Fields,
            });
        });

        app.MapMethods("/api/lti/authorize", new[] { "GET", "POST" }, async (
            HttpRequest http, LaunchService launches, CancellationToken ct) =>
        {
            var form = http.HasFormContentType ? await http.ReadFormAsync(ct) : null;
            string? Value(string name) => form?[name].FirstOrDefault() ?? http.Query[name].FirstOrDefault();
            var input = new AuthRequestInput(
                Value("scope"), Value("response_type"), Value("response_mode"), Value("prompt"),
                Value("client_id"), Value("redirect_uri"), Value("login_hint"), Value("lti_message_hint"),
                Value("state"), Value("nonce"), Value("target_link_uri"));
            var correlation = http.HttpContext.Items["correlationId"] as string;
            var result = await launches.AuthorizeAsync(input, correlation, http.HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            http.HttpContext.Response.Headers.CacheControl = "no-store";
            if (result.Html is not null)
                return Results.Content(result.Html, "text/html; charset=utf-8");
            return Results.Json(new { error = result.Error, error_description = result.Description }, statusCode: result.Status);
        }).DisableAntiforgery();

        app.MapPost("/api/lti/token", async (HttpRequest http, TokenService tokens, CancellationToken ct) =>
        {
            var form = http.HasFormContentType ? await http.ReadFormAsync(ct) : null;
            string? Value(string name) => form?[name].FirstOrDefault() ?? http.Query[name].FirstOrDefault();
            var response = await tokens.IssueAsync(new TokenRequest(
                Value("grant_type"),
                Value("client_assertion_type"),
                Value("client_assertion"),
                Value("scope"),
                Value("client_id"),
                http.HttpContext.Connection.RemoteIpAddress,
                http.HttpContext.Items["correlationId"] as string), ct);
            http.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!response.Ok)
                return Results.Json(new { error = response.Error, error_description = response.Description }, statusCode: response.Status);
            return Results.Json(new
            {
                access_token = response.AccessToken,
                token_type = "Bearer",
                expires_in = response.ExpiresIn,
                scope = response.Scope,
            });
        }).RequireRateLimiting("token").DisableAntiforgery();
    }
}
