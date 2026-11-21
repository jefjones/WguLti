using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WguLti.Core.Claims;
using WguLti.Core.Security;
using WguLti.Domain;

namespace WguLti.Core.Tests;

public class IdTokenBuilderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Resource_link_request_includes_required_claims()
    {
        var payload = IdTokenBuilder.Build(Sample(), Now);

        payload["iss"]!.GetValue<string>().Should().Be("https://lti.wgu.example");
        payload["aud"]!.GetValue<string>().Should().Be("1000000000000001");
        payload["azp"]!.GetValue<string>().Should().Be("1000000000000001");
        payload["sub"]!.GetValue<string>().Should().Be("opaque-subject");
        payload["nonce"]!.GetValue<string>().Should().Be("nonce-1");
        payload[LtiClaims.MessageType]!.GetValue<string>().Should().Be(LtiClaims.ResourceLinkRequest);
        payload[LtiClaims.Version]!.GetValue<string>().Should().Be("1.3.0");
        payload[LtiClaims.DeploymentId]!.GetValue<string>().Should().Be("d-c950");
        payload[LtiClaims.TargetLinkUri]!.GetValue<string>().Should().Be("https://tool.example/launch");
        payload[LtiClaims.ResourceLink]!["id"]!.GetValue<string>().Should().Be("rl-1");
        payload[LtiClaims.ResourceLink]!["title"]!.GetValue<string>().Should().Be("Practice set 3");
        payload[LtiClaims.Roles]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(LisRoles.Learner);
        payload["exp"]!.GetValue<long>().Should().Be(payload["iat"]!.GetValue<long>() + 300);
        payload[LtiClaims.Context]!["label"]!.GetValue<string>().Should().Be("C950");
        payload[LtiClaims.ToolPlatform]!["product_family_code"]!.GetValue<string>().Should().Be("wgulti");
    }

    [Fact]
    public void Anonymous_privacy_omits_name_email_and_lis()
    {
        var payload = IdTokenBuilder.Build(Sample() with
        {
            PrivacyLevel = PrivacyLevels.Anonymous,
            GivenName = "Ada",
            FamilyName = "Lovelace",
            FullName = "Ada Lovelace",
            Email = "ada@wgu.example",
            PersonSourcedId = "sis-1",
        }, Now);

        payload["sub"]!.GetValue<string>().Should().Be("opaque-subject");
        payload.ContainsKey("name").Should().BeFalse();
        payload.ContainsKey("email").Should().BeFalse();
        payload.ContainsKey(LtiClaims.Lis).Should().BeFalse();
    }

    [Fact]
    public void Public_privacy_includes_name_email_and_lis()
    {
        var payload = IdTokenBuilder.Build(Sample() with
        {
            PrivacyLevel = PrivacyLevels.Public,
            GivenName = "Ada",
            FamilyName = "Lovelace",
            FullName = "Ada Lovelace",
            Email = "ada@wgu.example",
            PersonSourcedId = "sis-1",
        }, Now);

        payload["name"]!.GetValue<string>().Should().Be("Ada Lovelace");
        payload["email"]!.GetValue<string>().Should().Be("ada@wgu.example");
        payload[LtiClaims.Lis]!["person_sourcedid"]!.GetValue<string>().Should().Be("sis-1");
    }

    [Fact]
    public void Service_claims_follow_granted_scopes()
    {
        var without = IdTokenBuilder.Build(Sample(), Now);
        without.ContainsKey(LtiClaims.AgsEndpoint).Should().BeFalse();
        without.ContainsKey(LtiClaims.NamesAndRoles).Should().BeFalse();

        var withScopes = IdTokenBuilder.Build(Sample() with
        {
            Scopes = new[] { LtiScopes.Score, LtiScopes.MembershipReadonly },
            AgsLineItemsUrl = "https://lti.wgu.example/api/lti/courses/ctx/line_items",
            NrpsMembershipUrl = "https://lti.wgu.example/api/lti/courses/ctx/names_and_roles",
        }, Now);

        withScopes[LtiClaims.AgsEndpoint]!["lineitems"]!.GetValue<string>().Should().Contain("/line_items");
        withScopes[LtiClaims.NamesAndRoles]!["context_memberships_url"]!.GetValue<string>().Should().Contain("/names_and_roles");
        withScopes[LtiClaims.NamesAndRoles]!["service_versions"]!.AsArray()[0]!.GetValue<string>().Should().Be("2.0");
    }

    [Fact]
    public void Resource_link_launch_requires_a_link_id()
    {
        var act = () => IdTokenBuilder.Build(Sample() with { ResourceLinkId = null }, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Signed_payload_round_trips_with_the_public_key()
    {
        using var rsa = RSA.Create(2048);
        var material = new SigningMaterial { Kid = RsaJwtSigner.KidFor(rsa), Rsa = rsa };
        var model = Sample();
        var token = RsaJwtSigner.Sign(material, IdTokenBuilder.Build(model, Now).ToJsonString());

        using var pub = RSA.Create();
        pub.ImportParameters(rsa.ExportParameters(false));
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = model.Issuer,
            ValidAudience = model.ClientId,
            IssuerSigningKey = new RsaSecurityKey(pub),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
        });

        result.IsValid.Should().BeTrue(result.Exception?.ToString());
        new JsonWebToken(token).GetHeaderValue<string>("kid").Should().Be(material.Kid);
        new JsonWebToken(token).TryGetPayloadValue<string>(LtiClaims.MessageType, out var messageType).Should().BeTrue();
        messageType.Should().Be(LtiClaims.ResourceLinkRequest);
    }

    private static IdTokenModel Sample() => new(
        Issuer: "https://lti.wgu.example",
        ClientId: "1000000000000001",
        Subject: "opaque-subject",
        Nonce: "nonce-1",
        DeploymentId: "d-c950",
        TargetLinkUri: "https://tool.example/launch",
        MessageType: LtiClaims.ResourceLinkRequest,
        ResourceLinkId: "rl-1",
        ResourceLinkTitle: "Practice set 3",
        ResourceLinkDescription: null,
        Roles: new[] { LisRoles.Learner },
        ContextId: "ctx-1",
        ContextLabel: "C950",
        ContextTitle: "Data Structures II",
        ContextType: "CourseOffering",
        PlatformGuid: "wgu-lti-dev",
        PlatformName: "WguLti",
        ProductFamilyCode: "wgulti",
        ProductVersion: "1.0",
        DocumentTarget: "iframe",
        Locale: "en-US",
        Width: null,
        Height: null,
        Custom: null,
        PrivacyLevel: PrivacyLevels.Anonymous,
        GivenName: null,
        FamilyName: null,
        FullName: null,
        Email: null,
        PictureUrl: null,
        PersonSourcedId: null,
        Scopes: Array.Empty<string>(),
        AgsLineItemsUrl: null,
        NrpsMembershipUrl: null);
}
