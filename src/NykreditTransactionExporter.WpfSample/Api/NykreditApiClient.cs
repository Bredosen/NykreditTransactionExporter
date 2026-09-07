using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Api.Models.Data;
using NykreditTransactionExporter.WpfSample.Api.Models.Watcher;

namespace NykreditTransactionExporter.WpfSample.Api;

internal sealed class NykreditApiClient : IDisposable
{
    #region Members
    #region Static Fields
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    #endregion

    #region Readonly Fields
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    #endregion
    #endregion

    #region Create API client
    public NykreditApiClient(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsLoopback(baseUri) ||
            !string.Equals(baseUri.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ArgumentException(
                "API URL must be an HTTPS loopback origin such as https://localhost:53682/.",
                nameof(baseUrl));
        }

        _httpClient = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(baseUri),
            Timeout = RequestTimeout
        };
    }
    #endregion

    #region Check API health
    public async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/health", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }
    #endregion

    #region Get session status
    public Task<SessionStatusModel> GetSessionAsync(CancellationToken cancellationToken)
    {
        return GetAsync<SessionStatusModel>("api/session", cancellationToken);
    }
    #endregion

    #region Start authorization
    public Task<AuthorizationStartModel> StartAuthorizationAsync(CancellationToken cancellationToken)
    {
        return PostAsync<AuthorizationStartModel>("api/authorization/start", cancellationToken);
    }
    #endregion

    #region Export transactions
    public Task<ExportResultModel> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        return PostJsonAsync<ExportRequest, ExportResultModel>("api/export", request, cancellationToken);
    }
    #endregion

    #region Get watcher status
    public Task<WatcherStatusModel> GetWatcherStatusAsync(CancellationToken cancellationToken)
    {
        return GetAsync<WatcherStatusModel>("api/watcher/status", cancellationToken);
    }
    #endregion

    #region Get recent watcher events
    public Task<List<WatcherEventModel>> GetWatcherEventsAsync(CancellationToken cancellationToken)
    {
        return GetAsync<List<WatcherEventModel>>("api/watcher/events", cancellationToken);
    }
    #endregion

    #region Start watcher
    public Task<WatcherStatusModel> StartWatcherAsync(
        WatcherRequest request,
        CancellationToken cancellationToken)
    {
        return PostJsonAsync<WatcherRequest, WatcherStatusModel>("api/watcher/start", request, cancellationToken);
    }
    #endregion

    #region Stop watcher
    public Task<WatcherStatusModel> StopWatcherAsync(CancellationToken cancellationToken)
    {
        return PostAsync<WatcherStatusModel>("api/watcher/stop", cancellationToken);
    }
    #endregion

    #region Run watcher once
    public Task<WatcherStatusModel> RunWatcherOnceAsync(
        WatcherRequest request,
        CancellationToken cancellationToken)
    {
        return PostJsonAsync<WatcherRequest, WatcherStatusModel>("api/watcher/once", request, cancellationToken);
    }
    #endregion

    #region Get application metadata
    public Task<JsonElement> GetApplicationDataAsync(CancellationToken cancellationToken)
    {
        return GetAsync<JsonElement>("api/data/application", cancellationToken);
    }
    #endregion

    #region Get ASPSP metadata
    public Task<JsonElement> GetAspspsDataAsync(
        string? country,
        string? psuType,
        string? service,
        string? paymentType,
        CancellationToken cancellationToken)
    {
        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("country", country),
            new("psu_type", psuType),
            new("service", service),
            new("payment_type", paymentType)
        };
        return GetAsync<JsonElement>(BuildQuery("api/data/aspsps", parameters), cancellationToken);
    }
    #endregion

    #region Get raw session data
    public Task<JsonElement> GetRawSessionDataAsync(CancellationToken cancellationToken)
    {
        return GetAsync<JsonElement>("api/data/session", cancellationToken);
    }
    #endregion

    #region Get saved authorization response
    public Task<JsonElement> GetAuthorizationDataAsync(CancellationToken cancellationToken)
    {
        return GetAsync<JsonElement>("api/data/session/authorization", cancellationToken);
    }
    #endregion

    #region Get account details
    public Task<JsonElement> GetAccountDetailsAsync(
        string accountUid,
        CancellationToken cancellationToken)
    {
        return GetAsync<JsonElement>(
            $"api/data/accounts/{Uri.EscapeDataString(accountUid)}/details",
            cancellationToken);
    }
    #endregion

    #region Get account balances
    public Task<JsonElement> GetAccountBalancesAsync(
        string accountUid,
        CancellationToken cancellationToken)
    {
        return GetAsync<JsonElement>(
            $"api/data/accounts/{Uri.EscapeDataString(accountUid)}/balances",
            cancellationToken);
    }
    #endregion

    #region Get all matching transactions
    public Task<TransactionCollectionModel> GetTransactionsAsync(
        string accountUid,
        TransactionQueryModel query,
        CancellationToken cancellationToken)
    {
        string path = $"api/data/accounts/{Uri.EscapeDataString(accountUid)}/transactions/all";
        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("date_from", query.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("date_to", query.DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("transaction_status", query.TransactionStatus),
            new("strategy", query.Strategy)
        };
        return GetAsync<TransactionCollectionModel>(BuildQuery(path, parameters), cancellationToken);
    }
    #endregion

    #region Get transaction details
    public Task<JsonElement> GetTransactionDetailsAsync(
        string accountUid,
        string transactionId,
        CancellationToken cancellationToken)
    {
        return GetAsync<JsonElement>(
            $"api/data/accounts/{Uri.EscapeDataString(accountUid)}/transactions/{Uri.EscapeDataString(transactionId)}",
            cancellationToken);
    }
    #endregion

    #region Dispose API client
    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion

    #region Send GET request
    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }
    #endregion

    #region Send empty POST request
    private async Task<T> PostAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(relativeUrl, null, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }
    #endregion

    #region Send JSON POST request
    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string relativeUrl,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            relativeUrl,
            request,
            _jsonOptions,
            cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }
    #endregion

    #region Read API response
    private async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        T? value = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        return value ?? throw new InvalidOperationException("The API returned an empty or invalid JSON response.");
    }
    #endregion

    #region Ensure successful API response
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string detail = TryReadProblemDetail(body) ?? body;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = response.ReasonPhrase ?? "Unknown API error";
        }

        throw new InvalidOperationException(
            $"API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }
    #endregion

    #region Read problem detail
    private static string? TryReadProblemDetail(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("detail", out JsonElement detail) &&
                   detail.ValueKind == JsonValueKind.String
                ? detail.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
    #endregion

    #region Build query string
    private static string BuildQuery(
        string path,
        IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        string[] values = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value!.Trim())}")
            .ToArray();
        return values.Length == 0 ? path : $"{path}?{string.Join("&", values)}";
    }
    #endregion

    #region Check loopback API address
    private static bool IsLoopback(Uri uri)
    {
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
    }
    #endregion

    #region Ensure trailing slash
    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string value = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : $"{uri.AbsoluteUri}/";
        return new Uri(value, UriKind.Absolute);
    }
    #endregion
}
