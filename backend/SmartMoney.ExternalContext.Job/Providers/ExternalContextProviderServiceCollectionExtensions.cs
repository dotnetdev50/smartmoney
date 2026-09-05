using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartMoney.ExternalContext.Providers;

public static class ExternalContextProviderServiceCollectionExtensions
{
    public static IServiceCollection AddExternalContextProviders(this IServiceCollection services, IConfiguration configuration)
    {
        var providers = configuration.GetSection("ExternalContext:Providers");

        services.AddOptions<RbiNewsSourceOptions>()
            .Bind(providers.GetSection("RBI"));
        services.AddOptions<FederalReserveNewsSourceOptions>()
            .Bind(providers.GetSection("FederalReserve"));
        services.AddOptions<GdacsNewsSourceOptions>()
            .Bind(providers.GetSection("GDACS"));

        services.AddHttpClient<RbiNewsSourceProvider>();
        services.AddHttpClient<FederalReserveNewsSourceProvider>();
        services.AddHttpClient<GdacsNewsSourceProvider>();

        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<RbiNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<FederalReserveNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<GdacsNewsSourceProvider>());

        return services;
    }
}
