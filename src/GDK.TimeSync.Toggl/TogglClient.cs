using System.Net.Http.Headers;
using System.Text.Json;

namespace GDK.TimeSync.Toggl;

public sealed class TogglClient : ITogglClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TogglClient(HttpClient httpClient, TogglOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiToken);

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("Toggl base URL must be an absolute URL.", nameof(options));
        }

        httpClient.BaseAddress ??= baseUri;
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{options.ApiToken}:api_token")));
        HttpClient = httpClient;
    }

    private HttpClient HttpClient { get; }

    public void Dispose() => HttpClient.Dispose();

    public async Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate));
        }

        var path = $"me/time_entries?start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";
        using var response = await SendAsync(() => HttpClient.GetAsync(path, cancellationToken));
        return await ReadJsonAsync<List<TogglTimeEntry>>(response, cancellationToken) ?? [];
    }

    public async Task<TogglTimeEntry> CreateTimeEntryAsync(TogglCreateTimeEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);
        if (request.Stop <= request.Start)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Stop must be after start.");
        }

        var payload = new Dictionary<string, object>
        {
            ["description"] = request.Description,
            ["start"] = request.Start,
            ["stop"] = request.Stop,
            ["duration"] = (long)(request.Stop - request.Start).TotalSeconds,
            ["workspace_id"] = request.WorkspaceId
        };
        if (request.ProjectId is { } projectId)
            payload["project_id"] = projectId;
        using var response = await SendAsync(() => HttpClient.PostAsJsonAsync($"workspaces/{request.WorkspaceId}/time_entries", payload, JsonOptions, cancellationToken));
        return await ReadJsonAsync<TogglTimeEntry>(response, cancellationToken)
            ?? throw new TogglApiException("Toggl returned an empty response.", response.StatusCode);
    }

    public async Task<IReadOnlyList<TogglProject>> GetProjectsAsync(long workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workspaceId);
        using var response = await SendAsync(() => HttpClient.GetAsync($"workspaces/{workspaceId}/projects", cancellationToken));
        return await ReadJsonAsync<List<TogglProject>>(response, cancellationToken) ?? [];
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            throw new TogglApiException("Toggl returned an invalid response.", response.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            var response = await send();
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            response.Dispose();
            throw new TogglApiException("Toggl returned an unsuccessful response.", response.StatusCode);
        }
        catch (HttpRequestException)
        {
            throw new TogglApiException("Unable to reach Toggl.");
        }
    }
}
