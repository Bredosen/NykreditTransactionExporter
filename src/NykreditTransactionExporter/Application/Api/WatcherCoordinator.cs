using NykreditTransactionExporter.Application.Watching;

namespace NykreditTransactionExporter.Application.Api;

internal sealed class WatcherCoordinator
{
    #region Members
    #region Readonly Fields
    private readonly object _syncRoot = new();
    private readonly TransactionWatcher _watcher;
    private readonly WatcherStateStore _stateStore;
    private readonly CancellationToken _applicationStopping;
    #endregion

    #region Fields
    private CancellationTokenSource? _watcherCancellation;
    private Task? _watcherTask;
    private string? _accountUid;
    private string? _lastError;
    #endregion
    #endregion

    #region Create watcher coordinator
    public WatcherCoordinator(
        TransactionWatcher watcher,
        WatcherStateStore stateStore,
        CancellationToken applicationStopping)
    {
        _watcher = watcher;
        _stateStore = stateStore;
        _applicationStopping = applicationStopping;
    }
    #endregion

    #region Start continuous watcher
    public async Task<WatcherControllerStatus> StartAsync(
        string? accountUid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (_watcherTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("The API watcher is already running.");
            }

            _watcherCancellation?.Dispose();
            CancellationTokenSource ownedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
            _watcherCancellation = ownedCancellation;
            _accountUid = NormalizeAccountUid(accountUid);
            _lastError = null;
            _watcherTask = Task.Run(() => RunBackgroundAsync(_accountUid, ownedCancellation), CancellationToken.None);
        }

        return await GetStatusAsync(cancellationToken);
    }
    #endregion

    #region Stop continuous watcher
    public async Task<WatcherControllerStatus> StopAsync(CancellationToken cancellationToken)
    {
        Task? task;
        CancellationTokenSource? watcherCancellation;
        lock (_syncRoot)
        {
            task = _watcherTask;
            watcherCancellation = _watcherCancellation;
            watcherCancellation?.Cancel();
        }
        if (task is not null)
        {
            try
            {
                await task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (watcherCancellation?.IsCancellationRequested == true)
            {
            }
        }

        return await GetStatusAsync(cancellationToken);
    }
    #endregion

    #region Run one watcher poll
    public async Task<WatcherControllerStatus> RunOnceAsync(
        string? accountUid,
        CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (_watcherTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Stop the continuous API watcher before running a one-shot poll.");
            }
        }

        string? normalizedAccountUid = NormalizeAccountUid(accountUid);
        lock (_syncRoot)
        {
            _accountUid = normalizedAccountUid;
            _lastError = null;
        }

        await _watcher.RunAsync(normalizedAccountUid, true, cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }
    #endregion

    #region Get watcher status
    public async Task<WatcherControllerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        bool running;
        string? accountUid;
        string? lastError;
        lock (_syncRoot)
        {
            running = _watcherTask is { IsCompleted: false };
            accountUid = _accountUid;
            lastError = _lastError;
        }

        WatcherState state = await _stateStore.LoadAsync(cancellationToken);
        return new WatcherControllerStatus(running, accountUid, state.LastSuccessfulPollAt, lastError);
    }
    #endregion

    #region Get recent watcher events
    public async Task<IReadOnlyList<TransactionBookedEvent>> GetRecentEventsAsync(
        CancellationToken cancellationToken)
    {
        WatcherState state = await _stateStore.LoadAsync(cancellationToken);
        return state.RecentEvents
            .OrderByDescending(item => item.OccurredAt)
            .ToArray();
    }
    #endregion

    #region Run watcher in background
    private async Task RunBackgroundAsync(
        string? accountUid,
        CancellationTokenSource ownedCancellation)
    {
        try
        {
            await _watcher.RunAsync(accountUid, false, ownedCancellation.Token);
        }
        catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_syncRoot)
            {
                _lastError = exception.Message;
            }
        }
        finally
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_watcherCancellation, ownedCancellation))
                {
                    _watcherTask = null;
                    _watcherCancellation = null;
                    ownedCancellation.Dispose();
                }
            }
        }
    }
    #endregion

    #region Normalize account uid
    private static string? NormalizeAccountUid(string? accountUid)
    {
        return string.IsNullOrWhiteSpace(accountUid) ? null : accountUid.Trim();
    }
    #endregion
}

internal sealed class WatcherControllerStatus
{
    #region Properties
    public bool Running { get; }

    public string? AccountUid { get; }

    public DateTimeOffset? LastSuccessfulPollAt { get; }

    public string? LastError { get; }
    #endregion

    #region Create watcher status
    public WatcherControllerStatus(
        bool running,
        string? accountUid,
        DateTimeOffset? lastSuccessfulPollAt,
        string? lastError)
    {
        Running = running;
        AccountUid = accountUid;
        LastSuccessfulPollAt = lastSuccessfulPollAt;
        LastError = lastError;
    }
    #endregion
}
