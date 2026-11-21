using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WguLti.Core;
using WguLti.Core.Launch;
using WguLti.Core.Registration;
using WguLti.Core.Security;
using WguLti.Core.Stores;
using WguLti.Core.Tokens;
using WguLti.Infrastructure.Jobs;
using WguLti.Infrastructure.Keys;
using WguLti.Infrastructure.Persistence;
using WguLti.Infrastructure.Seeding;

namespace WguLti.Infrastructure;

public static class DependencyInjection
{
    public const string FallbackConnectionString =
        "Host=127.0.0.1;Port=5432;Database=wgulti;Username=wgulti_app;Password=unset;Timeout=2;Search Path=lti";

    public static IServiceCollection AddWguLtiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LtiOptions>(configuration.GetSection(LtiOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LtiOptions>>().Value);

        var connectionString = configuration.GetConnectionString("Lti");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = FallbackConnectionString;

        services.AddDbContext<LtiDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddScoped<IPlatformStore, PlatformStore>();
        services.AddScoped<IBackgroundMaintenance, PlatformMaintenance>();
        services.AddScoped<IToolKeyResolver, ToolKeyResolver>();
        services.AddHttpClient("tool-jwks", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WguLti/1.0");
        });

        var provider = configuration["Lti:Signing:Provider"];
        if (string.Equals(provider, "Kms", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPlatformKeySource, KmsPlatformKeySource>();
        else
            services.AddSingleton<IPlatformKeySource, FilePlatformKeySource>();

        services.AddScoped<LaunchService>();
        services.AddScoped<TokenService>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<DeploymentService>();
        services.AddScoped<DemoDataSeeder>();
        return services;
    }
}
