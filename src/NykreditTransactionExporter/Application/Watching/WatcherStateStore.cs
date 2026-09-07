using System.Text.Json;

namespace NykreditTransactionExporter.Application.Watching;

internal sealed class WatcherStateStore
{
    #region Members
    #region Constants
    private const int CurrentSchemaVersion = 2;
    #endregion

    #region Fields
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
    #endregion
    #endregion

    #region Create watcher state store
    public WatcherStateStore(string path)
    {
        _path = path;
    }
    #endregion

    #region Load watcher state
    public async Task<WatcherState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return Normalize(new WatcherState());
        }

        string json = await File.ReadAllTextAsync(_path, cancellationToken);
        WatcherState state = JsonSerializer.Deserialize<WatcherState>(json, _jsonOptions)
            ?? throw new InvalidOperationException("The saved watcher state could not be parsed.");

        return Normalize(state);
    }
    #endregion

    #region Save watcher state
    public async Task SaveAsync(WatcherState state, CancellationToken cancellationToken)
    {
        Normalize(state);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{_path}.tmp";
        string json = JsonSerializer.Serialize(state, _jsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _path, true);
    }
    #endregion

    #region Normalize watcher state
    private static WatcherState Normalize(WatcherState state)
    {
        state.InitializedAccountUids ??= [];
        state.SeenTransactionKeys ??= [];
        state.WebhookHandledTransactionKeys ??= [];
        state.RecentEvents ??= [];

        if (state.SchemaVersion < CurrentSchemaVersion)
        {
            foreach (string transactionKey in state.SeenTransactionKeys)
            {
                state.WebhookHandledTransactionKeys.Add(transactionKey);
            }

            state.SchemaVersion = CurrentSchemaVersion;
        }

        return state;
    }
    #endregion
}
