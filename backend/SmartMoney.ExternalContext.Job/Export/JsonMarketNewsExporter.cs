using System.Text.Json;
using System.Text.Json.Serialization;
using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Pipeline;

namespace SmartMoney.ExternalContext.Export;

public sealed class JsonMarketNewsExporter : IMarketNewsExporter
{
    private readonly string _outputPath;

    public JsonMarketNewsExporter(string? outputPath = null)
    {
        _outputPath = outputPath ?? Path.Combine(AppContext.BaseDirectory, "market_news.json");
    }

    public async Task ExportAsync(MarketNewsDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null,
            Converters = { new JsonStringEnumConverter() }
        };

        var serialized = JsonSerializer.Serialize(document, options);
        var tempPath = _outputPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, serialized, cancellationToken);

        if (File.Exists(_outputPath))
        {
            File.Move(_outputPath, _outputPath + ".bak", overwrite: true);
            try
            {
                File.Move(tempPath, _outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(_outputPath + ".bak"))
                {
                    File.Delete(_outputPath + ".bak");
                }
            }
        }
        else
        {
            File.Move(tempPath, _outputPath, overwrite: true);
        }
    }
}
