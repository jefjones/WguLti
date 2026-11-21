using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using WguLti.Core.Security;
using WguLti.Core.Tokens;
using WguLti.Domain;

namespace WguLti.Core.Tests;

public class ClientAssertionValidatorTests
{
    private const string ClientId = "1000000000000001";
    private const string Audience = "https://lti.wgu.example/api/lti/token";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Accepts_a_well_formed_assertion()
    {
        var (material, key) = Key();
        var token = Sign(material, ClientId, ClientId, Audience, Now.ToUnixTimeSeconds(), Now.AddMinutes(4).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { key }, false, Now);
        result.Succeeded.Should().BeTrue();
        result.Jti.Should().Be("jti-1");
        result.ClientId.Should().Be(ClientId);
    }

    [Fact]
    public async Task Rejects_the_wrong_audience()
    {
        var (material, key) = Key();
        var token = Sign(material, ClientId, ClientId, "https://other.example/token", Now.ToUnixTimeSeconds(), Now.AddMinutes(4).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { key }, false, Now);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("invalid_client");
    }

    [Fact]
    public async Task Rejects_when_sub_does_not_equal_client_id()
    {
        var (material, key) = Key();
        var token = Sign(material, ClientId, "someone-else", Audience, Now.ToUnixTimeSeconds(), Now.AddMinutes(4).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { key }, false, Now);
        result.Error.Should().Be("invalid_client");
        result.Description.Should().Contain("sub");
    }

    [Fact]
    public async Task Rejects_a_reused_jti()
    {
        var (material, key) = Key();
        var token = Sign(material, ClientId, ClientId, Audience, Now.ToUnixTimeSeconds(), Now.AddMinutes(4).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { key }, jtiAlreadyUsed: true, Now);
        result.Error.Should().Be("invalid_grant");
        result.Description.Should().Contain("already used");
    }

    [Fact]
    public async Task Rejects_exp_more_than_five_minutes_ahead()
    {
        var (material, key) = Key();
        var token = Sign(material, ClientId, ClientId, Audience, Now.ToUnixTimeSeconds(), Now.AddMinutes(10).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { key }, false, Now);
        result.Error.Should().Be("invalid_grant");
        result.Description.Should().Contain("5 minutes");
    }

    [Fact]
    public async Task Rejects_an_expired_assertion()
    {
        var (material, key) = Key();
        var token = Sign(material, ClientId, ClientId, Audience, Now.AddMinutes(-10).ToUnixTimeSeconds(), Now.AddMinutes(-5).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { key }, false, Now);
        result.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Rejects_a_bad_signature()
    {
        var (material, _) = Key();
        var (_, other) = Key();
        var token = Sign(material, ClientId, ClientId, Audience, Now.ToUnixTimeSeconds(), Now.AddMinutes(4).ToUnixTimeSeconds(), "jti-1");
        var result = await ClientAssertionValidator.ValidateAsync(token, Audience, ClientId, new[] { other }, false, Now);
        result.Error.Should().Be("invalid_client");
    }

    [Fact]
    public void Downscopes_to_the_intersection_of_requested_and_granted_scopes()
    {
        var selected = ScopeSelector.Select(
            $"{LtiScopes.Score} {LtiScopes.LineItem}",
            new[] { LtiScopes.Score });
        selected.Ok.Should().BeTrue();
        selected.Scopes.Should().Equal(LtiScopes.Score);
    }

    [Fact]
    public void Rejects_when_no_requested_scope_is_granted()
    {
        var selected = ScopeSelector.Select(LtiScopes.Score, new[] { LtiScopes.MembershipReadonly });
        selected.Ok.Should().BeFalse();
        selected.Error.Should().Contain("granted");
    }

    private static (SigningMaterial Material, RsaSecurityKey PublicKey) Key()
    {
        var rsa = RSA.Create(2048);
        var material = new SigningMaterial { Kid = RsaJwtSigner.KidFor(rsa), Rsa = rsa };
        var pub = RSA.Create();
        pub.ImportParameters(rsa.ExportParameters(false));
        return (material, new RsaSecurityKey(pub));
    }

    private static string Sign(SigningMaterial material, string iss, string sub, string aud, long iat, long exp, string jti)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = iss,
            ["sub"] = sub,
            ["aud"] = aud,
            ["iat"] = iat,
            ["exp"] = exp,
            ["jti"] = jti,
        });
        return RsaJwtSigner.Sign(material, payload);
    }
}
