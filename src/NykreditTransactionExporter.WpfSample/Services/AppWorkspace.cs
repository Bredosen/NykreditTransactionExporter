using System.Collections.ObjectModel;
using NykreditTransactionExporter.WpfSample.Api;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Api.Models.Watcher;

namespace NykreditTransactionExporter.WpfSample.Services;

internal sealed class AppWorkspace : IDisposable
{
    #region Members
    #region Constants
    private const int ActivityLimit = 200;
    #endregion

    #region Readonly Fields
    private readonly CancellationToken _applicationToken;
    #endregion

    #region Fields
    private NykreditApiClient? _apiClient;
    private string? _apiBaseUrl;
    #endregion

    #region Events
    public event EventHandler? StateChanged;

    public event Action<string>? ActivityAdded;
    #endregion

    #region Properties
    public bool IsConnected { get; private set; }

    public SessionStatusModel Session { get; private set; } = new();

    public WatcherStatusModel WatcherStatus { get; private set; } = new();

    public IReadOnlyList<WatcherEventModel> WatcherEvents { get; private set; } = [];

    public ObservableCollection<string> Activity { get; } = [];

    public CancellationToken CancellationToken => _applicationToken;

    public NykreditApiClient ApiClient => _apiClient ??
        throw new InvalidOperationException("Connect to the local API first.");
    #endregion
    #endregion

    #region Create workspace
    public AppWorkspace(CancellationToken applicationToken)
    {
        _applicationToken = applicationToken;
    }
    #endregion

    #region Configure API connection
    public void ConfigureApi(string baseUrl)
    {
        string normalized = NormalizeBaseUrl(baseUrl);
        if (_apiClient is not null &&
            string.Equals(_apiBaseUrl, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _apiClient?.Dispose();
        _apiClient = new NykreditApiClient(normalized);
        _apiBaseUrl = normalized;
        IsConnected = false;
        Session = new SessionStatusModel();
        WatcherStatus = new WatcherStatusModel();
        WatcherEvents = [];
        RaiseStateChanged();
    }
    #endregion

    #region Refresh API state
    public async Task RefreshAllAsync()
    {
        NykreditApiClient client = ApiClient;
        try
        {
            await client.CheckHealthAsync(_applicationToken);
            Task<SessionStatusModel> sessionTask = client.GetSessionAsync(_applicationToken);
            Task<WatcherStatusModel> watcherTask = client.GetWatcherStatusAsync(_applicationToken);
            Task<List<WatcherEventModel>> eventsTask = client.GetWatcherEventsAsync(_applicationToken);
            await Task.WhenAll(sessionTask, watcherTask, eventsTask);

            Session = await sessionTask;
            WatcherStatus = await watcherTask;
            WatcherEvents = await eventsTask;
            IsConnected = true;
            RaiseStateChanged();
            Log("API state refreshed.");
        }
        catch
        {
            IsConnected = false;
            RaiseStateChanged();
            throw;
        }
    }
    #endregion

    #region Refresh watcher state
    public async Task RefreshWatcherAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        try
        {
            Task<WatcherStatusModel> watcherTask = _apiClient.GetWatcherStatusAsync(_applicationToken);
            Task<List<WatcherEventModel>> eventsTask = _apiClient.GetWatcherEventsAsync(_applicationToken);
            await Task.WhenAll(watcherTask, eventsTask);

            WatcherStatus = await watcherTask;
            WatcherEvents = await eventsTask;
            IsConnected = true;
            RaiseStateChanged();
        }
        catch
        {
            IsConnected = false;
            RaiseStateChanged();
            throw;
        }
    }
    #endregion

    #region Log activity
    public void Log(string message)
    {
        string line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        Activity.Insert(0, line);
        while (Activity.Count > ActivityLimit)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }

        ActivityAdded?.Invoke(line);
    }
    #endregion

    #region Dispose workspace
    public void Dispose()
    {
        _apiClient?.Dispose();
    }
    #endregion

    #region Raise state changed
    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Normalize API base URL
    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("API URL is required.", nameof(baseUrl));
        }

        string trimmed = baseUrl.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : $"{trimmed}/";
    }
    #endregion
}
