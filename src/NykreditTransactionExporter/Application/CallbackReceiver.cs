using System.Net;
using System.Text;

namespace NykreditTransactionExporter.Application;

internal sealed class CallbackReceiver
{
    #region Check local callback support
    public bool CanListen(Uri redirectUri)
    {
        return string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(redirectUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(redirectUri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));
    }
    #endregion

    #region Wait for local callback
    public async Task<string> ListenAsync(
        Uri redirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        string prefix = BuildPrefix(redirectUri);
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"Waiting for the authorization callback on {prefix}");
        HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        try
        {
            string? error = context.Request.QueryString["error"];
            if (!string.IsNullOrWhiteSpace(error))
            {
                string description = context.Request.QueryString["error_description"] ?? error;
                await WriteResponseAsync(context.Response, "Authorization failed. You can close this window.", cancellationToken);
                throw new InvalidOperationException($"Bank authorization failed: {description}");
            }

            string? state = context.Request.QueryString["state"];
            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                await WriteResponseAsync(context.Response, "Authorization state validation failed.", cancellationToken);
                throw new InvalidOperationException("The authorization callback contained an unexpected state value.");
            }

            string code = context.Request.QueryString["code"]
                ?? throw new InvalidOperationException("The authorization callback did not contain a code.");
            await WriteResponseAsync(context.Response, "Authorization completed. You can close this window.", cancellationToken);
            return code;
        }
        finally
        {
            listener.Stop();
        }
    }
    #endregion

    #region Read pasted callback
    public string ReadFromConsole(string expectedState)
    {
        Console.WriteLine("Paste the complete redirect URL returned after bank authorization:");
        string value = Console.ReadLine()?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("A complete absolute redirect URL is required.");
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);
        if (query.TryGetValue("error", out string? error))
        {
            query.TryGetValue("error_description", out string? description);
            throw new InvalidOperationException($"Bank authorization failed: {description ?? error}");
        }

        if (!query.TryGetValue("state", out string? state) ||
            !string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The pasted redirect URL contained an unexpected state value.");
        }

        return query.TryGetValue("code", out string? code) && !string.IsNullOrWhiteSpace(code)
            ? code
            : throw new InvalidOperationException("The pasted redirect URL did not contain an authorization code.");
    }
    #endregion

    #region Build listener prefix
    private static string BuildPrefix(Uri redirectUri)
    {
        string path = redirectUri.AbsolutePath;
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        return $"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}{path}";
    }
    #endregion

    #region Write browser response
    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        string message,
        CancellationToken cancellationToken)
    {
        string html = $"<!doctype html><html><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        byte[] content = Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = content.Length;
        await response.OutputStream.WriteAsync(content, cancellationToken);
        response.Close();
    }
    #endregion

    #region Parse query string
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string value = parts.Length == 2
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : string.Empty;
            values[key] = value;
        }

        return values;
    }
    #endregion
}
