using System.Diagnostics;
using System.Net;
using NykreditTransactionExporter.Application.Operations;
using NykreditTransactionExporter.Application.Watching;
using NykreditTransactionExporter.Configuration;

namespace NykreditTransactionExporter.Application;

internal sealed class ExporterApplication
{
    #region Readonly Fields
    private readonly AppSettings _settings;
    private readonly BankingOperations _operations;
    private readonly CallbackReceiver _callbackReceiver;
    private readonly TransactionWatcher _transactionWatcher;
    #endregion

    #region Create application
    public ExporterApplication(
        AppSettings settings,
        BankingOperations operations,
        CallbackReceiver callbackReceiver,
        TransactionWatcher transactionWatcher)
    {
        _settings = settings;
        _operations = operations;
        _callbackReceiver = callbackReceiver;
        _transactionWatcher = transactionWatcher;
    }
    #endregion

    #region Run command
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);
        switch (options.Command)
        {
            case "authorize":
                await AuthorizeAsync(cancellationToken);
                return 0;
            case "status":
                await ShowStatusAsync(cancellationToken);
                return 0;
            case "export":
                await ExportAsync(options, cancellationToken);
                return 0;
            case "watch":
                await _transactionWatcher.RunAsync(options.AccountUid, options.Once, cancellationToken);
                return 0;
            case "help":
                ShowHelp();
                return 0;
            default:
                throw new ArgumentException($"Unknown command: {options.Command}");
        }
    }
    #endregion

    #region Authorize bank access
    private async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        AuthorizationChallenge challenge = await _operations.StartAuthorizationAsync(cancellationToken);
        var redirectUri = new Uri(_settings.EnableBanking.RedirectUrl, UriKind.Absolute);

        Console.WriteLine("Open this URL to authorize access with Nykredit/MitID:");
        Console.WriteLine(challenge.Url);
        TryOpenBrowser(challenge.Url);

        string code;
        if (_callbackReceiver.CanListen(redirectUri))
        {
            try
            {
                code = await _callbackReceiver.ListenAsync(redirectUri, challenge.State, cancellationToken);
            }
            catch (HttpListenerException exception)
            {
                Console.WriteLine($"Local callback listener could not start: {exception.Message}");
                code = _callbackReceiver.ReadFromConsole(challenge.State);
            }
        }
        else
        {
            code = _callbackReceiver.ReadFromConsole(challenge.State);
        }

        SessionInfo session = await _operations.CompleteAuthorizationAsync(code, cancellationToken);
        Console.WriteLine($"Session saved. Accessible accounts: {session.Accounts.Count}.");
        if (session.ValidUntil.HasValue)
        {
            Console.WriteLine($"Session valid until: {session.ValidUntil.Value:O}");
        }

        foreach (AccountInfo account in session.Accounts)
        {
            Console.WriteLine($"- {account.DisplayName} [{account.Uid}]");
        }
    }
    #endregion

    #region Show session status
    private async Task ShowStatusAsync(CancellationToken cancellationToken)
    {
        SessionInfo session = await _operations.GetSessionInfoAsync(cancellationToken);
        Console.WriteLine($"Status: {session.Status}");
        Console.WriteLine($"Valid until: {session.ValidUntil?.ToString("O") ?? "unknown"}");
        Console.WriteLine($"Saved accounts: {session.Accounts.Count}");
    }
    #endregion

    #region Export monthly transactions
    private async Task ExportAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        ExportResult result = await _operations.ExportAsync(
            options.Month,
            options.AccountUid,
            options.OutputPath,
            cancellationToken);

        foreach (AccountExportResult account in result.Accounts)
        {
            Console.WriteLine($"{account.DisplayName}: {account.TransactionCount} booked transactions.");
        }

        Console.WriteLine($"Exported {result.TransactionCount} transactions to {result.OutputPath}");
    }
    #endregion

    #region Open authorization URL
    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            Console.WriteLine("The browser could not be opened automatically. Open the URL above manually.");
        }
    }
    #endregion

    #region Show command help
    private static void ShowHelp()
    {
        Console.WriteLine("NykreditTransactionExporter (.NET 10)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  authorize");
        Console.WriteLine("      Create or renew the Nykredit account-information session using MitID.");
        Console.WriteLine();
        Console.WriteLine("  status");
        Console.WriteLine("      Show the current Enable Banking session status.");
        Console.WriteLine();
        Console.WriteLine("  export [--month yyyy-MM] [--account uid] [--output path]");
        Console.WriteLine("      Export booked transactions. The previous calendar month is the default.");
        Console.WriteLine();
        Console.WriteLine("  watch [--account uid] [--once]");
        Console.WriteLine("      Detect newly booked transactions locally and optionally POST transaction.booked webhooks.");
        Console.WriteLine("      --once performs one poll; otherwise the configured interval is used continuously.");
        Console.WriteLine();
        Console.WriteLine("  api");
        Console.WriteLine("      Start the local HTTPS REST API and authorization callback host.");
    }
    #endregion
}
