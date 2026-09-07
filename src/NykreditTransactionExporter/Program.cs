using NykreditTransactionExporter.Application;
using NykreditTransactionExporter.Application.Api;
using NykreditTransactionExporter.Application.Operations;
using NykreditTransactionExporter.Application.Watching;
using NykreditTransactionExporter.Configuration;
using NykreditTransactionExporter.EnableBanking;
using NykreditTransactionExporter.Export;
using NykreditTransactionExporter.Storage;

namespace NykreditTransactionExporter;

internal static class Program
{
    #region Run application
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            AppSettings settings = SettingsLoader.Load();
            var tokenFactory = new JwtTokenFactory(
                settings.EnableBanking.ApplicationId,
                settings.EnableBanking.PrivateKeyPath);

            using var enableBankingHttpClient = new HttpClient
            {
                BaseAddress = new Uri(settings.EnableBanking.ApiBaseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(60)
            };
            using var webhookHttpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var client = new EnableBankingClient(enableBankingHttpClient, tokenFactory);
            var sessionStore = new SessionStore(settings.Storage.SessionFile);
            var csvExporter = new CsvExporter(settings.Storage.ExportDirectory);
            var callbackReceiver = new CallbackReceiver();
            var watcherStateStore = new WatcherStateStore(settings.Watcher.StateFile);
            var webhookNotifier = new WebhookNotifier(webhookHttpClient, settings.Watcher);
            var transactionWatcher = new TransactionWatcher(
                settings.Watcher,
                client,
                sessionStore,
                watcherStateStore,
                webhookNotifier);
            var operations = new BankingOperations(settings, client, sessionStore, csvExporter);

            if (IsApiCommand(args))
            {
                var watcherCoordinator = new WatcherCoordinator(
                    transactionWatcher,
                    watcherStateStore,
                    cancellation.Token);
                var apiHost = new ApiHost(settings, operations, watcherCoordinator);
                await apiHost.RunAsync(cancellation.Token);
                return 0;
            }

            var application = new ExporterApplication(
                settings,
                operations,
                callbackReceiver,
                transactionWatcher);
            return await application.RunAsync(args, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
    #endregion

    #region Check API command
    private static bool IsApiCommand(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], "api", StringComparison.OrdinalIgnoreCase);
    }
    #endregion
}
