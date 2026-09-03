using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;

var options = BuildOptions(args);
if (!options.Enabled)
{
    Console.WriteLine("External Context job is disabled. Set --enabled true to generate market_news.json.");
    return;
}

var providers = new INewsSourceProvider[]
{
    new FixtureNewsSourceProvider()
};

var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
    ? ResolveDefaultOutputPath()
    : options.OutputPath;

if (string.IsNullOrWhiteSpace(options.OutputPath))
{
    Console.WriteLine($"Using default output path: {outputPath}");
}

var pipeline = new MarketNewsPipeline(
    providers,
    new DefaultNewsNormalizer(),
    new DefaultNewsDeduplicator(),
    new SimpleNewsRanker(),
    new JsonMarketNewsExporter(Path.GetFullPath(outputPath)));

var document = await pipeline.RunAsync(options, CancellationToken.None);

Console.WriteLine($"Generated {document.Items.Count} market news items at {document.GeneratedAtUtc:O}");

static string ResolveDefaultOutputPath()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "frontend", "public", "data", "market_news.json");
            if (Directory.Exists(Path.GetDirectoryName(candidate)))
            {
                return candidate;
            }

            current = current.Parent;
        }
    }

    return Path.Combine("frontend", "public", "data", "market_news.json");
}

static ExternalContextOptions BuildOptions(string[] args)
{
    var options = new ExternalContextOptions();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--enabled":
                if (i + 1 < args.Length && bool.TryParse(args[++i], out var enabled))
                {
                    options.Enabled = enabled;
                }
                break;
            case "--lookback-hours":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var lookback))
                {
                    options.LookbackHours = lookback;
                }
                break;
            case "--max-candidates":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var maxCandidates))
                {
                    options.MaxCandidates = maxCandidates;
                }
                break;
            case "--max-output-items":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var outputItems))
                {
                    options.MaxOutputItems = outputItems;
                }
                break;
            case "--output":
                if (i + 1 < args.Length)
                {
                    options.OutputPath = args[++i];
                }
                break;
            case "--help":
            case "-h":
                Console.WriteLine("Usage: SmartMoney.ExternalContext.Job --enabled true --output <file.json>");
                return options;
            default:
                break;
        }
    }

    return options;
}
