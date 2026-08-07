using System.Net.Http.Headers;

namespace GDK.TimeSync.Toggl;

public sealed class TogglClient : ITogglClient
{
    public TogglClient(HttpClient httpClient, TogglOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiToken);

        httpClient.BaseAddress ??= new Uri(options.BaseUrl, UriKind.Absolute);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{options.ApiToken}:api_token")));
        HttpClient = httpClient;
    }

    private HttpClient HttpClient { get; }

    public async Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate));
        }

        var path = $"me/time_entries?start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";
        using var response = await HttpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TogglTimeEntry>>(cancellationToken)
            ?? [];
    }
}
