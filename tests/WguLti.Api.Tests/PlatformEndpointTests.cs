using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WguLti.Api.Tests;

public class PlatformEndpointTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public PlatformEndpointTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Jwks_publishes_an_rsa_key()
    {
        var response = await _client.GetAsync("/.well-known/jwks.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = doc.RootElement.GetProperty("keys")[0];
        key.GetProperty("kty").GetString().Should().Be("RSA");
        key.GetProperty("alg").GetString().Should().Be("RS256");
        key.GetProperty("kid").GetString().Should().StartWith("wgulti-");
        key.GetProperty("n").GetString().Should().NotBeNullOrEmpty();
        response.Headers.CacheControl?.ToString().Should().Contain("max-age");
    }

    [Fact]
    public async Task Security_jwks_alias_matches_the_well_known_document()
    {
        var wellKnown = await _client.GetStringAsync("/.well-known/jwks.json");
        var alias = await _client.GetStringAsync("/api/lti/security/jwks");
        alias.Should().Be(wellKnown);
    }

    [Fact]
    public async Task Openid_configuration_names_the_platform()
    {
        var response = await _client.GetAsync("/.well-known/openid-configuration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("issuer").GetString().Should().Be("https://lti.wgu.example");
        doc.RootElement.GetProperty("authorization_endpoint").GetString().Should().Be("https://lti.wgu.example/api/lti/authorize");
        doc.RootElement.GetProperty("token_endpoint").GetString().Should().Be("https://lti.wgu.example/api/lti/token");
        doc.RootElement.GetProperty("jwks_uri").GetString().Should().Contain("/.well-known/jwks.json");
    }

    [Fact]
    public async Task Advantage_routes_are_explicitly_not_implemented()
    {
        var response = await _client.GetAsync("/api/lti/courses/ctx/line_items");
        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Phase 3");

        (await _client.GetAsync("/api/lti/courses/ctx/names_and_roles")).StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await _client.PostAsync("/api/lti/deep_linking/return", new StringContent(""))).StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await _client.PostAsync("/api/lti/register", new StringContent(""))).StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Token_endpoint_rejects_a_missing_assertion()
    {
        var response = await _client.PostAsync("/api/lti/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
        }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_request");
    }

    [Fact]
    public async Task Authorize_without_a_client_is_rejected_without_a_redirect()
    {
        var response = await _client.GetAsync("/api/lti/authorize");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("unauthorized_client");
    }

    [Fact]
    public async Task Admin_api_requires_a_key_outside_development()
    {
        var response = await _client.GetAsync("/api/admin/registrations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
