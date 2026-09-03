using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;

namespace GDK.TimeSync.Tempo;

public sealed class TempoClient : ITempoClient
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

    public async Task<IReadOnlyList<TempoAttribute>> GetWorkAttributesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => httpClient.GetAsync("rest/tempo-core/1/work-attribute", cancellationToken));
        return await ReadJsonAsync<List<TempoAttribute>>(response, cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<TempoWorklog>> GetExistingWorklogsAsync(string originTaskId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originTaskId);

        using var response = await SendAsync(() => httpClient.GetAsync($"rest/tempo-timesheets/4/worklogs?originTaskId={Uri.EscapeDataString(originTaskId)}", cancellationToken));
        return await ReadJsonAsync<List<TempoWorklog>>(response, cancellationToken) ?? [];
    }

    public async Task<TempoWorklog> CreateWorklogAsync(TempoWorklogRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        using var response = await SendAsync(() => httpClient.PostAsJsonAsync("rest/tempo-timesheets/4/worklogs", CreatePayload(request), JsonOptions, cancellationToken));
        return await ReadRequiredJsonAsync(response, cancellationToken);
    }

    public async Task<TempoWorklog?> GetWorklogAsync(long worklogId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worklogId);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync($"rest/tempo-timesheets/4/worklogs/{worklogId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new TempoApiException("Unable to reach Tempo.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new TempoApiException("Tempo returned an unsuccessful response.", response.StatusCode);
            }

            return await ReadRequiredJsonAsync(response, cancellationToken);
        }
    }

    public async Task<TempoWorklog> UpdateWorklogAsync(long worklogId, TempoWorklogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worklogId);
        Validate(request);

        using var response = await SendAsync(() => httpClient.PutAsJsonAsync($"rest/tempo-timesheets/4/worklogs/{worklogId}", CreatePayload(request), JsonOptions, cancellationToken));
        return await ReadRequiredJsonAsync(response, cancellationToken);
    }

    public Task<TempoWorklog> CreateWorklogAsync(TempoWorklogCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateWorklogAsync(new TempoWorklogRequest(request.Worker, request.OriginTaskId, request.Started, request.TimeSpentSeconds, request.Comment, request.WorkCategory), cancellationToken);
    }

    public void Dispose() => httpClient.Dispose();

    private static void Validate(TempoWorklogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OriginTaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Comment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.TimeSpentSeconds);
    }

    // The Tempo work-category attribute, in the shape uTempoClient.pas sends -- captured from Tempo's
    // own UI and known to work against this instance.
    private const string WorkCategoryKey = "_WorkCategory_";
    private const string WorkCategoryName = "Work-Category";

    // ponytail: the attribute id is fixed at 4 to match the working reference client. ITempoClient
    // exposes GetWorkAttributesAsync if another Tempo instance ever numbers it differently.
    private const int WorkCategoryAttributeId = 4;

    private static object CreatePayload(TempoWorklogRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["worker"] = request.Worker,
            ["originTaskId"] = request.OriginTaskId,
            ["started"] = request.Started.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["timeSpentSeconds"] = request.TimeSpentSeconds,
            ["comment"] = request.Comment
        };

        // Omitted rather than sent blank: an empty attribute writes an empty category over the row.
        if (!string.IsNullOrWhiteSpace(request.WorkCategory))
        {
            payload["attributes"] = new Dictionary<string, object>
            {
                [WorkCategoryKey] = new
                {
                    name = WorkCategoryName,
                    workAttributeId = WorkCategoryAttributeId,
                    value = request.WorkCategory.Trim().ToUpperInvariant()
                }
            };
        }

        return payload;
    }

    private static async Task<TempoWorklog> ReadRequiredJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await ReadJsonAsync<TempoWorklog>(response, cancellationToken)
        ?? throw new TempoApiException("Tempo returned an empty response.", response.StatusCode);

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            throw new TempoApiException("Tempo returned an invalid response.", response.StatusCode);
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
            throw new TempoApiException("Tempo returned an unsuccessful response.", response.StatusCode);
        }
        catch (HttpRequestException)
        {
            throw new TempoApiException("Unable to reach Tempo.");
        }
    }
}
