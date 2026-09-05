using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var options = BuildOptions(args);
if (!options.Enabled)
{
    Console.WriteLine("External Context job is disabled. Set --enabled true to generate market_news.json.");
    return;
}

var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
    ? ResolveDefaultOutputPath()
    : options.OutputPath;

if (string.IsNullOrWhiteSpace(options.OutputPath))
{
    Console.WriteLine($"Using default output path: {outputPath}");
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddExternalContextProviders(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<INewsNormalizer, DefaultNewsNormalizer>();
builder.Services.AddSingleton<INewsDeduplicator, DefaultNewsDeduplicator>();
builder.Services.AddSingleton<INewsRanker, SimpleNewsRanker>();
builder.Services.AddSingleton<IMarketNewsExporter>(_ => new JsonMarketNewsExporter(Path.GetFullPath(outputPath)));
builder.Services.AddSingleton<MarketNewsPipeline>();

using var host = builder.Build();
var pipeline = host.Services.GetRequiredService<MarketNewsPipeline>();

var document = await pipeline.RunAsync(options, CancellationToken.None);

foreach (var result in pipeline.LastProviderResults)
{
    var fetchedItemCount = result.FetchedItemCount?.ToString() ?? "Unknown";
    var diagnosticCode = result.DiagnosticCode ?? "None";
    Console.WriteLine($"Provider={result.ProviderName} Status={result.Status} FetchedItems={fetchedItemCount} AcceptedCandidates={result.Candidates.Count} Diagnostic={diagnosticCode}");
}

foreach (var entry in pipeline.LastSelectedCandidates.Select((candidate, index) => (candidate, index)))
{
    Console.WriteLine($"Rank={entry.index + 1} Score={entry.candidate.MarketRelevanceScore} Impact={entry.candidate.Impact} Sentiment={entry.candidate.Sentiment} Scope={entry.candidate.NormalizedCandidate.Scope} Category={entry.candidate.NormalizedCandidate.Category} Provider={entry.candidate.NormalizedCandidate.SourceName} Headline={entry.candidate.NormalizedCandidate.Headline} WhyItMatters={entry.candidate.WhyItMatters}");
}

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
