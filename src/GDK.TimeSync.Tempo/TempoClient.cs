using System.Net.Http.Headers;
using System.Text.Json;

namespace GDK.TimeSync.Tempo;

public sealed class TempoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public TempoClient(HttpClient httpClient, TempoOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("Tempo base URL must be an absolute URL.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.PersonalAccessToken))
        {
            throw new ArgumentException("Tempo personal access token is required.", nameof(options));
        }

        this.httpClient = httpClient;
        this.httpClient.BaseAddress ??= new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.PersonalAccessToken);
    }

    public async Task<JsonElement> GetWorkAttributesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => httpClient.GetAsync("rest/tempo-core/1/work-attribute", cancellationToken));
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> CreateWorklogAsync(TempoWorklogCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OriginTaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Comment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.TimeSpentSeconds);

        var payload = new
        {
            worker = request.Worker,
            originTaskId = request.OriginTaskId,
            started = request.Started.ToString("yyyy-MM-dd'T'HH:mm:ss.fff"),
            timeSpentSeconds = request.TimeSpentSeconds,
            comment = request.Comment
        };

        using var response = await SendAsync(() => httpClient.PostAsJsonAsync("rest/tempo-timesheets/4/worklogs", payload, JsonOptions, cancellationToken));
        return await ReadJsonAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            var response = await send();
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new TempoApiException("Tempo returned an unsuccessful response.", response.StatusCode);
            }

            return response;
        }
        catch (HttpRequestException exception)
        {
            throw new TempoApiException("Unable to reach Tempo.", innerException: exception);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            return result.Clone();
        }
        catch (JsonException exception)
        {
            throw new TempoApiException("Tempo returned an invalid response.", response.StatusCode, exception);
        }
    }
}
