namespace WguLti.Core;

public sealed class LtiOptions
{
    public const string SectionName = "Lti";

    public string Issuer { get; set; } = "https://localhost:7090";
    public string PublicBaseUrl { get; set; } = "https://localhost:7090";
    public string PlatformName { get; set; } = "WguLti";
    public string ProductFamilyCode { get; set; } = Domain.PlatformInfo.ProductFamilyCode;
    public string ProductVersion { get; set; } = Domain.PlatformInfo.Version;
    public bool DevAdminEnabled { get; set; }
    public string? AdminApiKey { get; set; }
    public bool SeedDemoData { get; set; }
    public bool AllowLoopbackToolUrls { get; set; }
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;
    public int IdTokenLifetimeSeconds { get; set; } = 300;
    public int LoginSessionLifetimeSeconds { get; set; } = 300;
    public int JwksMaxAgeSeconds { get; set; } = 3600;
    public SigningOptions Signing { get; set; } = new();

    public string TokenEndpoint => Combine(PublicBaseUrl, "/api/lti/token");
    public string AuthorizationEndpoint => Combine(PublicBaseUrl, "/api/lti/authorize");
    public string JwksUri => Combine(PublicBaseUrl, "/.well-known/jwks.json");
    public string RegistrationEndpoint => Combine(PublicBaseUrl, "/api/lti/register");

    public static string Combine(string baseUrl, string path)
    {
        var trimmed = (baseUrl ?? "").TrimEnd('/');
        return trimmed + path;
    }
}

public sealed class SigningOptions
{
    public string Provider { get; set; } = "File";
    public string? KeyPath { get; set; }
}
