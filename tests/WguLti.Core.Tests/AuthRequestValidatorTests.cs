using FluentAssertions;
using WguLti.Core.Launch;
using WguLti.Domain;

namespace WguLti.Core.Tests;

public class AuthRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const string ClientId = "1000000000000001";
    private const string Redirect = "https://tool.example/launch";

    [Fact]
    public void Accepts_a_matching_login_session_and_fresh_nonce()
    {
        var result = AuthRequestValidator.Validate(Request(), Registration(), Session(), nonceAlreadyUsed: false, Now);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_state()
    {
        var result = AuthRequestValidator.Validate(Request() with { State = "  " }, Registration(), Session(), false, Now);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(AuthErrorCode.InvalidRequest);
        result.OidcError.Should().Be("invalid_request");
        result.Description.Should().Contain("state");
        result.RedirectTrusted.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_nonce()
    {
        var result = AuthRequestValidator.Validate(Request() with { Nonce = null }, Registration(), Session(), false, Now);
        result.Error.Should().Be(AuthErrorCode.InvalidRequest);
        result.Description.Should().Contain("nonce");
    }

    [Fact]
    public void Rejects_a_reused_nonce()
    {
        var result = AuthRequestValidator.Validate(Request(), Registration(), Session(), nonceAlreadyUsed: true, Now);
        result.Error.Should().Be(AuthErrorCode.InvalidRequest);
        result.Description.Should().Contain("already been used");
        result.RedirectTrusted.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_redirect_uri_that_is_not_registered()
    {
        var result = AuthRequestValidator.Validate(
            Request() with { RedirectUri = "https://evil.example/launch" }, Registration(), Session(), false, Now);
        result.Error.Should().Be(AuthErrorCode.UnauthorizedClient);
        result.RedirectTrusted.Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_unknown_client()
    {
        var result = AuthRequestValidator.Validate(Request(), registration: null, Session(), false, Now);
        result.Error.Should().Be(AuthErrorCode.UnauthorizedClient);
        result.RedirectTrusted.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_disabled_registration()
    {
        var result = AuthRequestValidator.Validate(
            Request(), Registration() with { State = RegistrationStates.Off }, Session(), false, Now);
        result.Error.Should().Be(AuthErrorCode.UnauthorizedClient);
        result.Description.Should().Contain("not enabled");
    }

    [Fact]
    public void Rejects_an_expired_session()
    {
        var result = AuthRequestValidator.Validate(Request(), Registration(), Session() with { ExpiresAt = Now }, false, Now);
        result.Error.Should().Be(AuthErrorCode.LoginRequired);
        result.OidcError.Should().Be("login_required");
    }

    [Fact]
    public void Rejects_a_consumed_session()
    {
        var result = AuthRequestValidator.Validate(Request(), Registration(), Session() with { Consumed = true }, false, Now);
        result.Error.Should().Be(AuthErrorCode.InvalidRequest);
        result.Description.Should().Contain("already used");
    }

    [Fact]
    public void Rejects_a_login_hint_mismatch()
    {
        var result = AuthRequestValidator.Validate(Request() with { LoginHint = "other" }, Registration(), Session(), false, Now);
        result.Error.Should().Be(AuthErrorCode.LoginRequired);
        result.Description.Should().Contain("login_hint");
    }

    [Theory]
    [InlineData("prompt", "login")]
    [InlineData("response_type", "code")]
    [InlineData("response_mode", "fragment")]
    public void Rejects_protocol_values_outside_the_lti_profile(string field, string value)
    {
        var request = field switch
        {
            "prompt" => Request() with { Prompt = value },
            "response_type" => Request() with { ResponseType = value },
            _ => Request() with { ResponseMode = value },
        };
        var result = AuthRequestValidator.Validate(request, Registration(), Session(), false, Now);
        result.Error.Should().Be(AuthErrorCode.InvalidRequest);
        result.RedirectTrusted.Should().BeTrue();
    }

    private static AuthRequestInput Request() => new(
        "openid", "id_token", "form_post", "none", ClientId, Redirect,
        "login-hint", "message-hint", "state-1", "nonce-1", null);

    private static RegistrationSnapshot Registration() =>
        new(ClientId, RegistrationStates.On, new[] { Redirect });

    private static SessionSnapshot Session() =>
        new(ClientId, "login-hint", "message-hint", "https://tool.example/launch", Now.AddMinutes(5), false);
}
