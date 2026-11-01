using System.Text.Json;
using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Configuration;

namespace Restaurant_Aqa_Framework.Core;

public static class PerformanceReport
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static async Task<string> WriteAsync(IPage page, string scenarioName)
    {
        var settings = Settings.Current;
        var timings = await page.EvaluateAsync<BrowserTimings>(
            """
            ({ apiBaseUrl }) => {
                const navigation = performance.getEntriesByType('navigation')[0];
                const apiRequests = performance.getEntriesByType('resource')
                    .filter(entry =>
                        entry.name.startsWith(apiBaseUrl) &&
                        (entry.initiatorType === 'fetch' || entry.initiatorType === 'xmlhttprequest'))
                    .map(entry => entry.duration);

                return {
                    pageLoadMs: navigation ? navigation.duration : null,
                    apiRequestDurationsMs: apiRequests
                };
            }
            """,
            new { apiBaseUrl = settings.ApiBaseUrl.TrimEnd('/') });

        var apiDurations = timings.ApiRequestDurationsMs;
        var report = new
        {
            scenario = scenarioName,
            collectedAtUtc = DateTimeOffset.UtcNow,
            pageLoad = new
            {
                durationMs = timings.PageLoadMs,
                thresholdMs = settings.Performance.PageLoadMs,
                withinThreshold = timings.PageLoadMs is null
                    || timings.PageLoadMs <= settings.Performance.PageLoadMs,
            },
            apiRequests = new
            {
                count = apiDurations.Count,
                averageDurationMs = apiDurations.Count == 0 ? 0 : apiDurations.Average(),
                maxDurationMs = apiDurations.Count == 0 ? 0 : apiDurations.Max(),
                thresholdMs = settings.Performance.ApiResponseMs,
                withinThreshold = apiDurations.All(duration => duration <= settings.Performance.ApiResponseMs),
            },
        };

        var safeScenarioName = string.Concat(
            scenarioName.Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
        var path = Path.Combine(
            ArtifactPaths.Performance,
            $"{safeScenarioName[..Math.Min(safeScenarioName.Length, 60)]}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, SerializerOptions));
        return path;
    }

    private sealed record BrowserTimings(double? PageLoadMs, List<double> ApiRequestDurationsMs);
}
