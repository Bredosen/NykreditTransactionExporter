using NykreditTransactionExporter.Application;
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
            var application = new ExporterApplication(
                settings,
                client,
                sessionStore,
                csvExporter,
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
}
