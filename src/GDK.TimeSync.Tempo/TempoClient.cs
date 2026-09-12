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
    // Public so Diagnostics can name it beside the ids the instance actually reports: a mismatch is
    // the whole failure, and until it was shown it had to be known about to be looked for.
    public const int WorkCategoryAttributeId = 4;

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

    // Read field by field rather than deserialised into the record: a strict bind failed on a 200
    // that had already created the worklog, and the only thing anyone does with the result is take
    // the id (plus TimeSpentSeconds, for the Live Validation readback). Tolerating an array wrapper,
    // a missing field and an id sent as a string removes the whole class of failure rather than the
    // one field that happened to differ.
    private static async Task<TempoWorklog> ReadRequiredJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            throw new TempoApiException("Tempo returned an empty response.", response.StatusCode);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().FirstOrDefault()
                : document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && ReadId(root, "tempoWorklogId") is { } worklogId)
                return new TempoWorklog(
                    worklogId,
                    ReadString(root, "worker"),
                    ReadString(root, "originTaskId"),
                    ReadStarted(root),
                    ReadInt(root, "timeSpentSeconds"),
                    ReadString(root, "comment"));
        }
        catch (JsonException)
        {
            throw Unreadable(response, body);
        }

        throw Unreadable(response, body);
    }

    // The body travels in the message because that is what reaches the audit log: the delivery
    // service logs the whole exception, and nothing else here can see a response. Capped, because a
    // reason also shows on the review row.
    private static TempoApiException Unreadable(HttpResponseMessage response, string body) =>
        new($"Tempo returned a response that could not be read. Body: {Truncate(body)}", response.StatusCode);

    private static string Truncate(string body) =>
        body.Length > MaxLoggedBodyCharacters ? body[..MaxLoggedBodyCharacters] + " …(truncated)" : body;

    private const int MaxLoggedBodyCharacters = 1000;

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Checked rather than left to the read: buffered content completes without ever looking at the
        // token, so a cancelled readback finished as Succeeded instead of asking for reconciliation.
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new TempoApiException("Tempo's response could not be read.", response.StatusCode);
        }
    }

    // Tempo's own UI sends originTaskId as text, so an id coming back as a string is expected rather
    // than exceptional.
    private static long? ReadId(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        ? value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        }
        : null;

    private static string ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        }
        : "";

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static DateTime ReadStarted(JsonElement root) =>
        DateTime.TryParse(ReadString(root, "started"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var started) ? started : default;

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
