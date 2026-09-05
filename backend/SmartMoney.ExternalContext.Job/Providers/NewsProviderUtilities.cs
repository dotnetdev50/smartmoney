using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;

namespace SmartMoney.ExternalContext.Providers;

internal static class NewsProviderUtilities
{
    public static async Task<string> GetResponseTextAsync(HttpClient httpClient, string endpoint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var response = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeoutCts.Token);
    }

    public static string? GetElementValue(XElement parent, string elementName)
    {
        var element = parent.Element(elementName)
            ?? parent.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        return element?.Value.Trim();
    }

    public static bool TryParseFeedDate(string? rawValue, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var input = rawValue.Trim();
        var cleaned = input;
        var commaIndex = cleaned.IndexOf(',');
        if (commaIndex >= 0)
        {
            cleaned = cleaned[(commaIndex + 1)..].Trim();
        }

        if (cleaned.EndsWith("GMT", StringComparison.OrdinalIgnoreCase)
            || cleaned.EndsWith("UTC", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^3].Trim();
        }

        foreach (var candidate in new[] { input, cleaned, $"{cleaned} +00:00" })
        {
            if (DateTimeOffset.TryParse(candidate, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildStableId(string prefix, string sourceIdentity, DateTimeOffset publishedAtUtc)
    {
        var seed = $"{sourceIdentity}-{publishedAtUtc:O}";
        var value = string.Concat(seed.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        return string.IsNullOrWhiteSpace(value) ? prefix : $"{prefix}-{value}";
    }
}
