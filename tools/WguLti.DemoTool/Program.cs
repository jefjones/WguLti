using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("DemoTool"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DemoOptions>>().Value);
builder.Services.AddSingleton<DemoKeyRing>();
builder.Services.AddSingleton<PendingLogins>();
builder.Services.AddHttpClient("platform")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (builder.Environment.IsDevelopment())
        {
            handler.ServerCertificateCustomValidationCallback = (message, _, _, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                var host = message?.RequestUri?.Host;
                return host is "localhost" or "127.0.0.1";
            };
        }
        return handler;
    });

var app = builder.Build();
var keys = app.Services.GetRequiredService<DemoKeyRing>();

app.MapGet("/", (DemoOptions options) => Results.Content(HomePage(options, keys), "text/html; charset=utf-8"));
app.MapGet("/.well-known/jwks.json", () => Results.Text(keys.JwksJson(), "application/json"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapMethods("/oidc/login", new[] { "GET", "POST" }, async (HttpRequest http, DemoOptions options, PendingLogins pending, CancellationToken ct) =>
{
    var form = http.HasFormContentType ? await http.ReadFormAsync(ct) : null;
    string? Value(string name) => form?[name].FirstOrDefault() ?? http.Query[name].FirstOrDefault();
    var iss = Value("iss");
    var loginHint = Value("login_hint");
    var messageHint = Value("lti_message_hint");
    var target = Value("target_link_uri");
    var clientId = Value("client_id");
    var deploymentId = Value("lti_deployment_id");
    if (string.IsNullOrWhiteSpace(loginHint) || string.IsNullOrWhiteSpace(messageHint) || string.IsNullOrWhiteSpace(clientId))
        return Results.BadRequest("Missing OIDC login initiation parameters.");
    if (!string.IsNullOrWhiteSpace(options.PlatformIssuer)
        && !string.Equals(iss, options.PlatformIssuer, StringComparison.Ordinal))
        return Results.BadRequest("iss does not match the configured platform.");

    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    pending.Put(state, new PendingLogin(nonce, target ?? options.RedirectUri, DateTimeOffset.UtcNow.AddMinutes(5)));

    var redirect = options.RedirectUri;
    var query = new Dictionary<string, string?>
    {
        ["scope"] = "openid",
        ["response_type"] = "id_token",
        ["response_mode"] = "form_post",
        ["prompt"] = "none",
        ["client_id"] = string.IsNullOrWhiteSpace(options.ClientId) ? clientId : options.ClientId,
        ["redirect_uri"] = redirect,
        ["login_hint"] = loginHint,
        ["lti_message_hint"] = messageHint,
        ["state"] = state,
        ["nonce"] = nonce,
    };
    if (!string.IsNullOrWhiteSpace(target)) query["target_link_uri"] = target;
    var authorize = Query(options.AuthorizeEndpoint, query);
    return Results.Redirect(authorize);
}).DisableAntiforgery();

app.MapPost("/launch", async (HttpRequest http, DemoOptions options, PendingLogins pending, IHttpClientFactory httpFactory, CancellationToken ct) =>
{
    var form = await http.ReadFormAsync(ct);
    var idToken = form["id_token"].ToString();
    var state = form["state"].ToString();
    var error = form["error"].ToString();
    if (!string.IsNullOrEmpty(error))
    {
        return Results.Content(Page("Launch error", $"<p>{H(error)}</p><p>{H(form["error_description"].ToString())}</p>"), "text/html; charset=utf-8");
    }

    if (!pending.TryTake(state, out var pendingLogin) || pendingLogin is null || pendingLogin.ExpiresAt < DateTimeOffset.UtcNow)
        return Results.Content(Page("Launch error", "<p>state was missing, unknown, or expired.</p>"), "text/html; charset=utf-8");

    string details;
    try
    {
        var client = httpFactory.CreateClient("platform");
        var jwksJson = await client.GetStringAsync(options.JwksUri, ct);
        var jwks = new JsonWebKeySet(jwksJson);
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.PlatformIssuer,
            ValidateAudience = true,
            ValidAudience = string.IsNullOrWhiteSpace(options.ClientId) ? ReadAud(idToken) : options.ClientId,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });
        if (!result.IsValid)
            return Results.Content(Page("Launch rejected", $"<p>{H(result.Exception?.Message ?? "invalid id_token")}</p>"), "text/html; charset=utf-8");

        var jwt = (JsonWebToken)result.SecurityToken;
        if (!jwt.TryGetPayloadValue<string>("nonce", out var nonce) || nonce != pendingLogin.Nonce)
            return Results.Content(Page("Launch rejected", "<p>nonce did not match the login request.</p>"), "text/html; charset=utf-8");

        details = "<p>id_token validated.</p>" + ClaimsTable(jwt) + TokenButton(options);
    }
    catch (Exception ex)
    {
        details = $"<p>Could not validate id_token: {H(ex.Message)}</p>";
    }

    return Results.Content(Page("LTI launch", details), "text/html; charset=utf-8");
}).DisableAntiforgery();

app.MapPost("/access-token", async (DemoOptions options, DemoKeyRing ring, IHttpClientFactory httpFactory, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(options.ClientId))
        return Results.Content(Page("Access token", "<p>Set DemoTool:ClientId to the platform client_id first.</p>"), "text/html; charset=utf-8");

    var now = DateTimeOffset.UtcNow;
    var payload = JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["iss"] = options.ClientId,
        ["sub"] = options.ClientId,
        ["aud"] = options.TokenEndpoint,
        ["iat"] = now.ToUnixTimeSeconds(),
        ["exp"] = now.AddMinutes(4).ToUnixTimeSeconds(),
        ["jti"] = Guid.NewGuid().ToString("N"),
    });
    var assertion = ring.Sign(payload);
    var body = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = assertion,
        ["scope"] = "https://purl.imsglobal.org/spec/lti-ags/scope/score",
    });
    var client = httpFactory.CreateClient("platform");
    var response = await client.PostAsync(options.TokenEndpoint, body, ct);
    var text = await response.Content.ReadAsStringAsync(ct);
    var html = $"<p>HTTP {(int)response.StatusCode}</p><pre>{H(text)}</pre>";
    return Results.Content(Page("Access token", html), "text/html; charset=utf-8");
}).DisableAntiforgery();

app.Run();

static string ReadAud(string jwt)
{
    var token = new JsonWebToken(jwt);
    return token.Audiences.FirstOrDefault() ?? "";
}

static string Query(string endpoint, IReadOnlyDictionary<string, string?> values)
{
    var parts = values
        .Where(pair => !string.IsNullOrEmpty(pair.Value))
        .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
    return endpoint + "?" + string.Join('&', parts);
}

static string H(string? value) => WebUtility.HtmlEncode(value ?? "");

static string Page(string title, string body) =>
    $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{H(title)}</title>"
    + "<style>body{font-family:Georgia,serif;margin:2rem;background:#f4f1ea;color:#14222b}pre{white-space:pre-wrap;background:#fff;padding:1rem;border:1px solid #e4ddd0}</style>"
    + $"</head><body><h1>{H(title)}</h1>{body}<p><a href=\"/\">Demo tool home</a></p></body></html>";

static string HomePage(DemoOptions options, DemoKeyRing keys)
{
    var jwk = keys.PublicJwkJson();
    return Page("WguLti Demo Tool",
        "<p>Tiny LTI 1.3 tool for local launches. Register it on the platform with these values.</p><ul>"
        + $"<li>OIDC login: <code>https://localhost:7091/oidc/login</code></li>"
        + $"<li>Redirect / target link: <code>{H(options.RedirectUri)}</code></li>"
        + $"<li>JWKS: <code>https://localhost:7091/.well-known/jwks.json</code></li>"
        + $"<li>Configured client_id: <code>{H(string.IsNullOrWhiteSpace(options.ClientId) ? "(not set)" : options.ClientId)}</code></li>"
        + "</ul><p>Public JWK (paste into the registration if you do not want the platform to fetch the JWKS URL):</p>"
        + $"<pre>{H(jwk)}</pre>");
}

static string ClaimsTable(JsonWebToken jwt)
{
    var rows = new StringBuilder("<table>");
    foreach (var claim in jwt.Claims.OrderBy(c => c.Type, StringComparer.Ordinal))
        rows.Append($"<tr><th style=\"text-align:left;vertical-align:top\">{H(claim.Type)}</th><td>{H(claim.Value)}</td></tr>");
    rows.Append("</table>");
    return rows.ToString();
}

static string TokenButton(DemoOptions options) =>
    string.IsNullOrWhiteSpace(options.ClientId)
        ? "<p>Set DemoTool:ClientId to request an access token.</p>"
        : "<form method=\"post\" action=\"/access-token\"><button type=\"submit\">Request client_credentials token</button></form>";

sealed class DemoOptions
{
    public string PlatformIssuer { get; set; } = "https://localhost:7090";
    public string AuthorizeEndpoint { get; set; } = "https://localhost:7090/api/lti/authorize";
    public string JwksUri { get; set; } = "https://localhost:7090/.well-known/jwks.json";
    public string TokenEndpoint { get; set; } = "https://localhost:7090/api/lti/token";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "https://localhost:7091/launch";
    public string? KeyPath { get; set; }
}

sealed class PendingLogin(string nonce, string? targetLinkUri, DateTimeOffset expiresAt)
{
    public string Nonce { get; } = nonce;
    public string? TargetLinkUri { get; } = targetLinkUri;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
}

sealed class PendingLogins
{
    private readonly ConcurrentDictionary<string, PendingLogin> _items = new();

    public void Put(string state, PendingLogin login) => _items[state] = login;

    public bool TryTake(string? state, out PendingLogin? login)
    {
        login = null;
        if (string.IsNullOrWhiteSpace(state)) return false;
        return _items.TryRemove(state, out login);
    }
}

sealed class DemoKeyRing
{
    private readonly RSA _rsa;
    private readonly string _kid;

    public DemoKeyRing(IConfiguration configuration)
    {
        var path = configuration["DemoTool:KeyPath"];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wgulti", "demo-tool-key.pem");
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(full))
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(File.ReadAllText(full));
        }
        else
        {
            _rsa = RSA.Create(2048);
            File.WriteAllText(full, _rsa.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var modulus = _rsa.ExportParameters(false).Modulus!;
        _kid = "demotool-" + Convert.ToHexString(SHA256.HashData(modulus).AsSpan(0, 8)).ToLowerInvariant();
    }

    public string PublicJwkJson()
    {
        var p = _rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid = _kid,
            n = Base64UrlEncoder.Encode(p.Modulus),
            e = Base64UrlEncoder.Encode(p.Exponent),
        });
    }

    public string JwksJson() => "{\"keys\":[" + PublicJwkJson() + "]}";

    public string Sign(string payloadJson)
    {
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = _kid })));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        var input = Encoding.ASCII.GetBytes(header + "." + payload);
        var signature = _rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + Base64UrlEncoder.Encode(signature);
    }
}
