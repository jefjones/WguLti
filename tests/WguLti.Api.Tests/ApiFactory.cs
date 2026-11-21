using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace WguLti.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), "wgulti-tests", Guid.NewGuid().ToString("N"), "key.pem");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lti:Issuer"] = "https://lti.wgu.example",
                ["Lti:PublicBaseUrl"] = "https://lti.wgu.example",
                ["Lti:Signing:Provider"] = "File",
                ["Lti:Signing:KeyPath"] = _keyPath,
                ["Lti:DevAdminEnabled"] = "false",
                ["Lti:SeedDemoData"] = "false",
                ["Lti:AllowLoopbackToolUrls"] = "false",
                ["ConnectionStrings:Lti"] = "Host=127.0.0.1;Port=1;Database=wgulti;Username=wgulti_app;Password=unused;Timeout=1;Search Path=lti",
            });
        });
    }
}
