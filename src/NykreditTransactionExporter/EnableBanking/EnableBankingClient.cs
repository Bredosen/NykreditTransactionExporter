using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NykreditTransactionExporter.Configuration;
using NykreditTransactionExporter.Storage;

namespace NykreditTransactionExporter.EnableBanking;

internal sealed class EnableBankingClient
{
    #region Fields
    private readonly HttpClient _httpClient;
    private readonly JwtTokenFactory _tokenFactory;
    #endregion

    #region Create API client
    public EnableBankingClient(HttpClient httpClient, JwtTokenFactory tokenFactory)
    {
        _httpClient = httpClient;
        _tokenFactory = tokenFactory;
    }
    #endregion

    #region Find configured bank
    public async Task<BankInfo> GetBankAsync(
        string name,
        string country,
        string psuType,
        CancellationToken cancellationToken)
    {
        string url = $"aspsps?country={Uri.EscapeDataString(country)}&psu_type={Uri.EscapeDataString(psuType)}";
        using JsonDocument document = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
        JsonElement banks = document.RootElement.GetProperty("aspsps");

        foreach (JsonElement bank in banks.EnumerateArray())
        {
            string? bankName = GetOptionalString(bank, "name");
            string? bankCountry = GetOptionalString(bank, "country");
            if (!string.Equals(bankName, name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(bankCountry, country, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long maximumConsentValidity = bank.TryGetProperty("maximum_consent_validity", out JsonElement validity)
                ? validity.GetInt64()
                : 0;

            if (maximumConsentValidity <= 0)
            {
                throw new InvalidOperationException($"{name} did not report a usable maximum consent validity.");
            }

            return new BankInfo(bankName ?? name, bankCountry ?? country, maximumConsentValidity);
        }

        throw new InvalidOperationException($"Bank '{name}' in country '{country}' was not returned by Enable Banking.");
    }
    #endregion

    #region Start account authorization
    public async Task<AuthorizationStart> StartAuthorizationAsync(
        EnableBankingSettings settings,
        BankInfo bank,
        string state,
        DateTimeOffset validUntil,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            access = new
            {
                transactions = true,
                valid_until = validUntil.ToString("O", CultureInfo.InvariantCulture)
            },
            aspsp = new
            {
                name = bank.Name,
                country = bank.Country
            },
            state,
            redirect_url = settings.RedirectUrl,
            psu_type = settings.PsuType,
            language = "da"
        };

        using JsonDocument document = await SendAsync(HttpMethod.Post, "auth", payload, cancellationToken);
        string url = document.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Enable Banking did not return an authorization URL.");
        string authorizationId = document.RootElement.TryGetProperty("authorization_id", out JsonElement id)
            ? id.GetString() ?? string.Empty
            : string.Empty;

        return new AuthorizationStart(url, authorizationId);
    }
    #endregion

    #region Authorize session
    public async Task<SessionState> AuthorizeSessionAsync(string code, CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendAsync(
            HttpMethod.Post,
            "sessions",
            new { code },
            cancellationToken);

        JsonElement root = document.RootElement;
        string sessionId = root.GetProperty("session_id").GetString()
            ?? throw new InvalidOperationException("Enable Banking did not return a session id.");

        var session = new SessionState
        {
            SessionId = sessionId,
            BankName = GetNestedString(root, "aspsp", "name") ?? string.Empty,
            Country = GetNestedString(root, "aspsp", "country") ?? string.Empty,
            PsuType = GetOptionalString(root, "psu_type") ?? string.Empty,
            ValidUntil = ParseDateTimeOffset(GetNestedString(root, "access", "valid_until")),
            SavedAt = DateTimeOffset.UtcNow
        };

        if (root.TryGetProperty("accounts", out JsonElement accounts))
        {
            foreach (JsonElement account in accounts.EnumerateArray())
            {
                string? uid = GetOptionalString(account, "uid");
                if (string.IsNullOrWhiteSpace(uid))
                {
                    continue;
                }

                session.Accounts.Add(new AccountState
                {
                    Uid = uid,
                    Iban = GetNestedString(account, "account_id", "iban"),
                    Name = GetOptionalString(account, "name"),
                    Product = GetOptionalString(account, "product"),
                    Details = GetOptionalString(account, "details"),
                    Currency = GetOptionalString(account, "currency")
                });
            }
        }

        if (session.Accounts.Count == 0)
        {
            throw new InvalidOperationException("The authorized session did not contain any accessible accounts.");
        }

        return session;
    }
    #endregion

    #region Get session status
    public Task<JsonDocument> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        return SendAsync(
            HttpMethod.Get,
            $"sessions/{Uri.EscapeDataString(sessionId)}",
            null,
            cancellationToken);
    }
    #endregion

    #region Get booked transactions
    public async Task<IReadOnlyList<JsonElement>> GetBookedTransactionsAsync(
        string accountUid,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        var transactions = new List<JsonElement>();
        string? continuationKey = null;

        do
        {
            string query = $"date_from={dateFrom:yyyy-MM-dd}&date_to={dateTo:yyyy-MM-dd}&transaction_status=BOOK&strategy=default";
            if (!string.IsNullOrWhiteSpace(continuationKey))
            {
                query += $"&continuation_key={Uri.EscapeDataString(continuationKey)}";
            }

            string url = $"accounts/{Uri.EscapeDataString(accountUid)}/transactions?{query}";
            using JsonDocument document = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("transactions", out JsonElement pageTransactions))
            {
                foreach (JsonElement transaction in pageTransactions.EnumerateArray())
                {
                    transactions.Add(transaction.Clone());
                }
            }

            continuationKey = root.TryGetProperty("continuation_key", out JsonElement continuation) &&
                              continuation.ValueKind == JsonValueKind.String
                ? continuation.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(continuationKey));

        return transactions;
    }
    #endregion

    #region Send API request
    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenFactory.CreateToken());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorText = content.Length == 0 ? "<empty response>" : System.Text.Encoding.UTF8.GetString(content);
            throw new InvalidOperationException(
                $"Enable Banking request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {errorText}");
        }

        return JsonDocument.Parse(content);
    }
    #endregion

    #region Read optional string
    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
    #endregion

    #region Read nested string
    private static string? GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        return element.TryGetProperty(objectName, out JsonElement nested) &&
               nested.ValueKind == JsonValueKind.Object
            ? GetOptionalString(nested, propertyName)
            : null;
    }
    #endregion

    #region Parse date and time
    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }
    #endregion
}
