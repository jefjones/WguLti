using System.Text.Json.Nodes;
using WguLti.Domain;

namespace WguLti.Core.Claims;

public sealed record IdTokenModel(
    string Issuer,
    string ClientId,
    string Subject,
    string Nonce,
    string DeploymentId,
    string TargetLinkUri,
    string MessageType,
    string? ResourceLinkId,
    string? ResourceLinkTitle,
    string? ResourceLinkDescription,
    IReadOnlyList<string> Roles,
    string? ContextId,
    string? ContextLabel,
    string? ContextTitle,
    string? ContextType,
    string PlatformGuid,
    string PlatformName,
    string ProductFamilyCode,
    string ProductVersion,
    string? DocumentTarget,
    string? Locale,
    int? Width,
    int? Height,
    IReadOnlyDictionary<string, string>? Custom,
    string PrivacyLevel,
    string? GivenName,
    string? FamilyName,
    string? FullName,
    string? Email,
    string? PictureUrl,
    string? PersonSourcedId,
    IReadOnlyList<string> Scopes,
    string? AgsLineItemsUrl,
    string? NrpsMembershipUrl);

public static class IdTokenBuilder
{
    public static JsonObject Build(IdTokenModel model, DateTimeOffset now, int lifetimeSeconds = 300)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Issuer)) throw new ArgumentException("Issuer is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(model.ClientId)) throw new ArgumentException("Client id is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(model.Subject)) throw new ArgumentException("Subject is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(model.Nonce)) throw new ArgumentException("Nonce is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(model.DeploymentId)) throw new ArgumentException("Deployment id is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(model.TargetLinkUri)) throw new ArgumentException("Target link URI is required.", nameof(model));
        if (model.MessageType == LtiClaims.ResourceLinkRequest && string.IsNullOrWhiteSpace(model.ResourceLinkId))
            throw new ArgumentException("Resource link id is required for LtiResourceLinkRequest.", nameof(model));

        var iat = now.ToUnixTimeSeconds();
        var payload = new JsonObject
        {
            ["iss"] = model.Issuer,
            ["aud"] = model.ClientId,
            ["azp"] = model.ClientId,
            ["sub"] = model.Subject,
            ["nonce"] = model.Nonce,
            ["iat"] = iat,
            ["exp"] = iat + lifetimeSeconds,
            [LtiClaims.MessageType] = model.MessageType,
            [LtiClaims.Version] = LtiClaims.VersionValue,
            [LtiClaims.DeploymentId] = model.DeploymentId,
            [LtiClaims.TargetLinkUri] = model.TargetLinkUri,
        };

        var roles = new JsonArray();
        foreach (var role in model.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal))
            roles.Add(role);
        payload[LtiClaims.Roles] = roles;

        if (model.MessageType == LtiClaims.ResourceLinkRequest)
        {
            var link = new JsonObject
            {
                ["id"] = model.ResourceLinkId,
                ["title"] = model.ResourceLinkTitle ?? "Resource link",
            };
            if (!string.IsNullOrWhiteSpace(model.ResourceLinkDescription))
                link["description"] = model.ResourceLinkDescription;
            payload[LtiClaims.ResourceLink] = link;
        }

        if (!string.IsNullOrWhiteSpace(model.ContextId))
        {
            var type = model.ContextType == "CourseTemplate" ? LisRoles.CourseTemplate : LisRoles.CourseOffering;
            payload[LtiClaims.Context] = new JsonObject
            {
                ["id"] = model.ContextId,
                ["label"] = model.ContextLabel ?? "",
                ["title"] = model.ContextTitle ?? "",
                ["type"] = new JsonArray(type),
            };
        }

        payload[LtiClaims.ToolPlatform] = new JsonObject
        {
            ["guid"] = model.PlatformGuid,
            ["name"] = model.PlatformName,
            ["product_family_code"] = model.ProductFamilyCode,
            ["version"] = model.ProductVersion,
        };

        var presentation = new JsonObject();
        if (!string.IsNullOrWhiteSpace(model.DocumentTarget)) presentation["document_target"] = model.DocumentTarget;
        if (!string.IsNullOrWhiteSpace(model.Locale)) presentation["locale"] = model.Locale;
        if (model.Width is int width) presentation["width"] = width;
        if (model.Height is int height) presentation["height"] = height;
        if (presentation.Count > 0) payload[LtiClaims.LaunchPresentation] = presentation;

        if (model.Custom is { Count: > 0 })
        {
            var custom = new JsonObject();
            foreach (var pair in model.Custom.OrderBy(p => p.Key, StringComparer.Ordinal))
                custom[pair.Key] = pair.Value;
            payload[LtiClaims.Custom] = custom;
        }

        ApplyPrivacy(payload, model);
        ApplyServiceClaims(payload, model);
        return payload;
    }

    private static void ApplyPrivacy(JsonObject payload, IdTokenModel model)
    {
        var rank = PrivacyLevels.Rank(model.PrivacyLevel);
        if (rank >= PrivacyLevels.Rank(PrivacyLevels.NameOnly))
        {
            if (!string.IsNullOrWhiteSpace(model.GivenName)) payload["given_name"] = model.GivenName;
            if (!string.IsNullOrWhiteSpace(model.FamilyName)) payload["family_name"] = model.FamilyName;
            var name = model.FullName;
            if (string.IsNullOrWhiteSpace(name))
                name = string.Join(' ', new[] { model.GivenName, model.FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(name)) payload["name"] = name;
        }

        if (rank >= PrivacyLevels.Rank(PrivacyLevels.EmailOnly) && !string.IsNullOrWhiteSpace(model.Email))
            payload["email"] = model.Email;

        if (rank >= PrivacyLevels.Rank(PrivacyLevels.Public))
        {
            if (!string.IsNullOrWhiteSpace(model.PictureUrl)) payload["picture"] = model.PictureUrl;
            if (!string.IsNullOrWhiteSpace(model.PersonSourcedId))
            {
                payload[LtiClaims.Lis] = new JsonObject { ["person_sourcedid"] = model.PersonSourcedId };
            }
        }
    }

    private static void ApplyServiceClaims(JsonObject payload, IdTokenModel model)
    {
        var ags = model.Scopes.Where(LtiScopes.IsAgs).Distinct(StringComparer.Ordinal).ToArray();
        if (ags.Length > 0 && !string.IsNullOrWhiteSpace(model.AgsLineItemsUrl))
        {
            var scope = new JsonArray();
            foreach (var item in ags) scope.Add(item);
            payload[LtiClaims.AgsEndpoint] = new JsonObject
            {
                ["scope"] = scope,
                ["lineitems"] = model.AgsLineItemsUrl,
            };
        }

        if (model.Scopes.Contains(LtiScopes.MembershipReadonly, StringComparer.Ordinal)
            && !string.IsNullOrWhiteSpace(model.NrpsMembershipUrl))
        {
            payload[LtiClaims.NamesAndRoles] = new JsonObject
            {
                ["context_memberships_url"] = model.NrpsMembershipUrl,
                ["service_versions"] = new JsonArray("2.0"),
            };
        }
    }
}
