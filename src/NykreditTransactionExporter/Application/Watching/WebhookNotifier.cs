using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NykreditTransactionExporter.Configuration;
using NykreditTransactionExporter.Export;

namespace NykreditTransactionExporter.Application.Watching;

internal sealed class WebhookNotifier
{
    #region Members
    #region Static Fields
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    #endregion

    #region Fields
    private readonly HttpClient _httpClient;
    private readonly WatcherSettings _settings;
    #endregion
    #endregion

    #region Create webhook notifier
    public WebhookNotifier(HttpClient httpClient, WatcherSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }
    #endregion

    #region Send booked transaction event
    public async Task SendBookedTransactionAsync(
        string transactionKey,
        ExportTransaction transaction,
        CancellationToken cancellationToken)
    {
        string eventId = TransactionIdentity.CreateEventId(transactionKey);
        var webhookEvent = new TransactionBookedEvent(eventId, DateTimeOffset.UtcNow, transaction);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(webhookEvent, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.WebhookUrl);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        request.Headers.TryAddWithoutValidation("X-Nykredit-Event", webhookEvent.Type);
        request.Headers.TryAddWithoutValidation("X-Nykredit-Event-Id", eventId);

        if (!string.IsNullOrWhiteSpace(_settings.WebhookSecret))
        {
            request.Headers.TryAddWithoutValidation("X-Nykredit-Signature", CreateSignature(payload));
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Webhook delivery failed ({(int)response.StatusCode} {response.ReasonPhrase}): {responseBody}");
    }
    #endregion

    #region Create webhook signature
    private string CreateSignature(byte[] payload)
    {
        byte[] key = Encoding.UTF8.GetBytes(_settings.WebhookSecret);
        byte[] signature = HMACSHA256.HashData(key, payload);
        return $"sha256={Convert.ToHexString(signature).ToLowerInvariant()}";
    }
    #endregion
}
