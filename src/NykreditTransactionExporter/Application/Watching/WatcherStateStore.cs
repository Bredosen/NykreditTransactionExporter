using System.Text.Json;

namespace NykreditTransactionExporter.Application.Watching;

internal sealed class WatcherStateStore
{
    #region Fields
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
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
            return new WatcherState();
        }

        string json = await File.ReadAllTextAsync(_path, cancellationToken);
        WatcherState state = JsonSerializer.Deserialize<WatcherState>(json, _jsonOptions)
            ?? throw new InvalidOperationException("The saved watcher state could not be parsed.");

        if (state.InitializedAccountUids is null || state.SeenTransactionKeys is null)
        {
            throw new InvalidOperationException("The saved watcher state does not contain valid watcher collections.");
        }

        return state;
    }
    #endregion

    #region Save watcher state
    public async Task SaveAsync(WatcherState state, CancellationToken cancellationToken)
    {
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
}
