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
        services.AddOptions<SebiNewsSourceOptions>()
            .Bind(providers.GetSection("SEBI"));
        services.AddOptions<PibNewsSourceOptions>()
            .Bind(providers.GetSection("PIB"));
        services.AddOptions<NseNewsSourceOptions>()
            .Bind(providers.GetSection("NSE"));

        services.AddHttpClient<RbiNewsSourceProvider>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });
        services.AddHttpClient<FederalReserveNewsSourceProvider>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });
        services.AddHttpClient<GdacsNewsSourceProvider>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });
        services.AddHttpClient<SebiNewsSourceProvider>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });
        services.AddHttpClient<PibNewsSourceProvider>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });
        services.AddHttpClient<NseNewsSourceProvider>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });

        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<RbiNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<FederalReserveNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<GdacsNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<SebiNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<PibNewsSourceProvider>());
        services.AddSingleton<INewsSourceProvider>(serviceProvider => serviceProvider.GetRequiredService<NseNewsSourceProvider>());

        return services;
    }
}
