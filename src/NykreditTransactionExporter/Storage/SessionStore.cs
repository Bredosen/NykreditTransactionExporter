using System.Text.Json;

namespace NykreditTransactionExporter.Storage;

internal sealed class SessionStore
{
    #region Fields
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
    #endregion

    #region Create session store
    public SessionStore(string path)
    {
        _path = path;
    }
    #endregion

    #region Load session
    public async Task<SessionState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException("No saved banking session was found. Run the authorize command first.", _path);
        }

        string json = await File.ReadAllTextAsync(_path, cancellationToken);
        SessionState state = JsonSerializer.Deserialize<SessionState>(json, _jsonOptions)
            ?? throw new InvalidOperationException("The saved banking session could not be parsed.");

        if (string.IsNullOrWhiteSpace(state.SessionId))
        {
            throw new InvalidOperationException("The saved banking session does not contain a session id.");
        }

        return state;
    }
    #endregion

    #region Save session
    public async Task SaveAsync(SessionState state, CancellationToken cancellationToken)
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
