using System.Text.Json.Nodes;
using WguLti.Core;
using WguLti.Core.Security;
using WguLti.Domain;

namespace WguLti.Api;

public static class WellKnownEndpoints
{
    public static void MapWellKnown(this WebApplication app)
    {
        app.MapGet("/.well-known/jwks.json", Jwks);
        app.MapGet("/api/lti/security/jwks", Jwks);
        app.MapGet("/.well-known/openid-configuration", (LtiOptions options) =>
        {
            var variables = new JsonArray(
                "User.id", "Person.name.full", "Person.email.primary",
                "Context.id", "Context.title", "CourseSection.sourcedId",
                "ResourceLink.id", "ResourceLink.available.endDateTime",
                "Wgu.program.code", "Wgu.term");
            var body = new JsonObject
            {
                ["issuer"] = options.Issuer,
                ["authorization_endpoint"] = options.AuthorizationEndpoint,
                ["token_endpoint"] = options.TokenEndpoint,
                ["jwks_uri"] = options.JwksUri,
                ["registration_endpoint"] = options.RegistrationEndpoint,
                ["scopes_supported"] = new JsonArray(LtiScopes.Catalog.Select(s => (JsonNode)s).ToArray()),
                ["response_types_supported"] = new JsonArray("id_token"),
                ["response_modes_supported"] = new JsonArray("form_post"),
                ["grant_types_supported"] = new JsonArray("client_credentials"),
                ["subject_types_supported"] = new JsonArray("public"),
                ["id_token_signing_alg_values_supported"] = new JsonArray("RS256"),
                ["token_endpoint_auth_methods_supported"] = new JsonArray("private_key_jwt"),
                ["token_endpoint_auth_signing_alg_values_supported"] = new JsonArray("RS256"),
                ["claims_supported"] = new JsonArray(
                    "iss", "aud", "azp", "sub", "nonce", "iat", "exp",
                    LtiClaims.MessageType, LtiClaims.Version, LtiClaims.DeploymentId, LtiClaims.TargetLinkUri,
                    LtiClaims.ResourceLink, LtiClaims.Roles, LtiClaims.Context, LtiClaims.ToolPlatform),
                [LtiClaims.PlatformConfiguration] = new JsonObject
                {
                    ["product_family_code"] = options.ProductFamilyCode,
                    ["version"] = options.ProductVersion,
                    ["messages_supported"] = new JsonArray(
                        new JsonObject { ["type"] = LtiClaims.ResourceLinkRequest },
                        new JsonObject { ["type"] = LtiClaims.DeepLinkingRequest }),
                    ["variables"] = variables,
                },
            };
            return Results.Json(body);
        });
    }

    private static IResult Jwks(HttpContext http, IPlatformKeySource keys, LtiOptions options)
    {
        var document = new JsonObject
        {
            ["keys"] = new JsonArray(keys.GetPublicKeys().Select(key => (JsonNode)new JsonObject
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["kid"] = key.Kid,
                ["n"] = key.N,
                ["e"] = key.E,
            }).ToArray()),
        };
        http.Response.Headers.CacheControl = $"public, max-age={Math.Clamp(options.JwksMaxAgeSeconds, 60, 86400)}";
        return Results.Text(document.ToJsonString(), "application/json");
    }
}
