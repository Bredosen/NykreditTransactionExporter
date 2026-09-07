using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NykreditTransactionExporter.Application.Api.Data;
using NykreditTransactionExporter.Application.Operations;
using NykreditTransactionExporter.Configuration;

namespace NykreditTransactionExporter.Application.Api;

internal sealed class ApiHost
{
    #region Members
    #region Static Fields
    private static readonly TimeSpan AuthorizationStateLifetime = TimeSpan.FromMinutes(30);
    #endregion

    #region Readonly Fields
    private readonly AppSettings _settings;
    private readonly BankingOperations _operations;
    private readonly WatcherCoordinator _watcherCoordinator;
    private readonly AccountDataApi _accountDataApi;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingAuthorizationStates = new(StringComparer.Ordinal);
    #endregion
    #endregion

    #region Create API host
    public ApiHost(
        AppSettings settings,
        BankingOperations operations,
        WatcherCoordinator watcherCoordinator)
    {
        _settings = settings;
        _operations = operations;
        _watcherCoordinator = watcherCoordinator;
        _accountDataApi = new AccountDataApi(operations);
    }
    #endregion

    #region Run local API
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Uri redirectUri = ValidateRedirectUri(_settings.EnableBanking.RedirectUrl);
        string apiOrigin = BuildOrigin(redirectUri);
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(apiOrigin);

        WebApplication app = builder.Build();
        app.Use(async (context, next) => await HandleErrorsAsync(context, next));
        MapEndpoints(app, redirectUri.AbsolutePath);

        Console.WriteLine($"Local API: {apiOrigin}");
        Console.WriteLine($"Authorization callback: {_settings.EnableBanking.RedirectUrl}");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            await app.StartAsync(cancellationToken);
            await app.WaitForShutdownAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _watcherCoordinator.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }
    #endregion

    #region Map API endpoints
    private void MapEndpoints(WebApplication app, string callbackPath)
    {
        app.MapGet("/", () => Results.Ok(new
        {
            name = "Nykredit Transaction Exporter API",
            version = "1",
            localOnly = true
        }));
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/session", GetSessionAsync);
        app.MapGet("/api/accounts", GetAccountsAsync);
        app.MapPost("/api/authorization/start", StartAuthorizationAsync);
        app.MapGet(callbackPath, CompleteAuthorizationAsync);
        app.MapPost("/api/export", ExportAsync);
        app.MapGet("/api/watcher/status", GetWatcherStatusAsync);
        app.MapGet("/api/watcher/events", GetWatcherEventsAsync);
        app.MapPost("/api/watcher/start", StartWatcherAsync);
        app.MapPost("/api/watcher/stop", StopWatcherAsync);
        app.MapPost("/api/watcher/once", RunWatcherOnceAsync);
        _accountDataApi.MapEndpoints(app);
    }
    #endregion

    #region Get session
    private async Task<IResult> GetSessionAsync(CancellationToken cancellationToken)
    {
        SessionInfo session = await _operations.GetSessionInfoOrEmptyAsync(cancellationToken);
        return Results.Ok(session);
    }
    #endregion

    #region Get accounts
    private async Task<IResult> GetAccountsAsync(CancellationToken cancellationToken)
    {
        SessionInfo session = await _operations.GetSessionInfoOrEmptyAsync(cancellationToken);
        return Results.Ok(session.Accounts);
    }
    #endregion

    #region Start authorization
    private async Task<IResult> StartAuthorizationAsync(CancellationToken cancellationToken)
    {
        CleanupExpiredAuthorizationStates();
        AuthorizationChallenge challenge = await _operations.StartAuthorizationAsync(cancellationToken);
        _pendingAuthorizationStates[challenge.State] = DateTimeOffset.UtcNow.Add(AuthorizationStateLifetime);
        return Results.Ok(challenge);
    }
    #endregion

    #region Complete authorization callback
    private async Task<IResult> CompleteAuthorizationAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        string? state = context.Request.Query["state"].FirstOrDefault();
        string? error = context.Request.Query["error"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(error))
        {
            if (!string.IsNullOrWhiteSpace(state))
            {
                _pendingAuthorizationStates.TryRemove(state, out _);
            }

            string description = context.Request.Query["error_description"].FirstOrDefault() ?? error;
            return HtmlResult("Authorization failed", description, StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(state) ||
            !_pendingAuthorizationStates.TryRemove(state, out DateTimeOffset expiresAt) ||
            expiresAt < DateTimeOffset.UtcNow)
        {
            return HtmlResult(
                "Authorization rejected",
                "The authorization state is missing, expired, or was not started by this API instance.",
                StatusCodes.Status400BadRequest);
        }

        string? code = context.Request.Query["code"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(code))
        {
            return HtmlResult(
                "Authorization failed",
                "The callback did not contain an authorization code.",
                StatusCodes.Status400BadRequest);
        }

        SessionInfo session = await _operations.CompleteAuthorizationAsync(code, cancellationToken);
        return HtmlResult(
            "Authorization completed",
            $"Nykredit access is authorized for {session.Accounts.Count} account(s). You can close this window.",
            StatusCodes.Status200OK);
    }
    #endregion

    #region Export transactions
    private async Task<IResult> ExportAsync(
        ExportApiRequest request,
        CancellationToken cancellationToken)
    {
        ExportResult result = await _operations.ExportAsync(
            request.Month,
            request.AccountUid,
            request.OutputPath,
            cancellationToken);
        return Results.Ok(result);
    }
    #endregion

    #region Get watcher status
    private async Task<IResult> GetWatcherStatusAsync(CancellationToken cancellationToken)
    {
        WatcherControllerStatus status = await _watcherCoordinator.GetStatusAsync(cancellationToken);
        return Results.Ok(status);
    }
    #endregion

    #region Get recent watcher events
    private async Task<IResult> GetWatcherEventsAsync(CancellationToken cancellationToken)
    {
        var events = await _watcherCoordinator.GetRecentEventsAsync(cancellationToken);
        return Results.Ok(events);
    }
    #endregion

    #region Start watcher
    private async Task<IResult> StartWatcherAsync(
        WatcherApiRequest request,
        CancellationToken cancellationToken)
    {
        WatcherControllerStatus status = await _watcherCoordinator.StartAsync(
            request.AccountUid,
            cancellationToken);
        return Results.Accepted("/api/watcher/status", status);
    }
    #endregion

    #region Stop watcher
    private async Task<IResult> StopWatcherAsync(CancellationToken cancellationToken)
    {
        WatcherControllerStatus status = await _watcherCoordinator.StopAsync(cancellationToken);
        return Results.Ok(status);
    }
    #endregion

    #region Run watcher once
    private async Task<IResult> RunWatcherOnceAsync(
        WatcherApiRequest request,
        CancellationToken cancellationToken)
    {
        WatcherControllerStatus status = await _watcherCoordinator.RunOnceAsync(
            request.AccountUid,
            cancellationToken);
        return Results.Ok(status);
    }
    #endregion

    #region Handle API errors
    private static async Task HandleErrorsAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            int statusCode = exception switch
            {
                ArgumentException => StatusCodes.Status400BadRequest,
                FileNotFoundException => StatusCodes.Status409Conflict,
                InvalidOperationException => StatusCodes.Status409Conflict,
                HttpRequestException => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new
                {
                    title = "Request failed",
                    status = statusCode,
                    detail = exception.Message
                },
                context.RequestAborted);
        }
    }
    #endregion

    #region Validate redirect URI
    private static Uri ValidateRedirectUri(string redirectUrl)
    {
        var redirectUri = new Uri(redirectUrl, UriKind.Absolute);
        bool loopback = string.Equals(redirectUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                        IPAddress.TryParse(redirectUri.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        bool validCallbackPath = !string.Equals(redirectUri.AbsolutePath, "/", StringComparison.Ordinal) &&
                                 string.IsNullOrEmpty(redirectUri.Query) &&
                                 string.IsNullOrEmpty(redirectUri.Fragment);
        if (!loopback ||
            !string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !validCallbackPath)
        {
            throw new InvalidOperationException(
                "The api command requires EnableBanking.RedirectUrl to be an HTTPS loopback callback URL without query or fragment, for example https://localhost:53682/callback/.");
        }

        return redirectUri;
    }
    #endregion

    #region Build API origin
    private static string BuildOrigin(Uri redirectUri)
    {
        return redirectUri.GetLeftPart(UriPartial.Authority);
    }
    #endregion

    #region Remove expired authorization states
    private void CleanupExpiredAuthorizationStates()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string state, DateTimeOffset expiresAt) in _pendingAuthorizationStates)
        {
            if (expiresAt < now)
            {
                _pendingAuthorizationStates.TryRemove(state, out _);
            }
        }
    }
    #endregion

    #region Create HTML callback result
    private static IResult HtmlResult(string title, string message, int statusCode)
    {
        string encodedTitle = HtmlEncoder.Default.Encode(title);
        string encodedMessage = HtmlEncoder.Default.Encode(message);
        string html = $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{encodedTitle}</title>
            </head>
            <body>
              <main>
                <h1>{encodedTitle}</h1>
                <p>{encodedMessage}</p>
              </main>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, statusCode);
    }
    #endregion
}
