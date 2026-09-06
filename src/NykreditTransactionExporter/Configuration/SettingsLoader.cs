using System.Text.Json;

namespace NykreditTransactionExporter.Configuration;

internal static class SettingsLoader
{
    #region Load settings
    public static AppSettings Load()
    {
        string configPath = ResolveConfigPath();
        string json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, options)
            ?? throw new InvalidOperationException("appsettings.json could not be parsed.");

        ApplyEnvironmentOverrides(settings);
        ResolvePaths(settings, Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory);
        Validate(settings);
        return settings;
    }
    #endregion

    #region Resolve config path
    private static string ResolveConfigPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("NYKREDIT_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            string expanded = ExpandPath(overridePath, Directory.GetCurrentDirectory());
            if (!File.Exists(expanded))
            {
                throw new FileNotFoundException("NYKREDIT_CONFIG_PATH does not point to an existing file.", expanded);
            }

            return expanded;
        }

        string baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(baseDirectoryPath))
        {
            return baseDirectoryPath;
        }

        string currentDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        throw new FileNotFoundException(
            "appsettings.json was not found. Copy appsettings.example.json to appsettings.json and configure it.");
    }
    #endregion

    #region Apply environment overrides
    private static void ApplyEnvironmentOverrides(AppSettings settings)
    {
        Override("NYKREDIT_APPLICATION_ID", value => settings.EnableBanking.ApplicationId = value);
        Override("NYKREDIT_PRIVATE_KEY_PATH", value => settings.EnableBanking.PrivateKeyPath = value);
        Override("NYKREDIT_API_BASE_URL", value => settings.EnableBanking.ApiBaseUrl = value);
        Override("NYKREDIT_SESSION_FILE", value => settings.Storage.SessionFile = value);
        Override("NYKREDIT_EXPORT_DIRECTORY", value => settings.Storage.ExportDirectory = value);
        Override("NYKREDIT_WATCH_STATE_FILE", value => settings.Watcher.StateFile = value);
        Override("NYKREDIT_WEBHOOK_URL", value => settings.Watcher.WebhookUrl = value);
        Override("NYKREDIT_WEBHOOK_SECRET", value => settings.Watcher.WebhookSecret = value);
        OverrideInt("NYKREDIT_WATCH_INTERVAL_MINUTES", value => settings.Watcher.IntervalMinutes = value);
        OverrideInt("NYKREDIT_WATCH_LOOKBACK_DAYS", value => settings.Watcher.LookbackDays = value);
        OverrideBoolean(
            "NYKREDIT_WATCH_NOTIFY_EXISTING",
            value => settings.Watcher.NotifyExistingOnFirstRun = value);
    }
    #endregion

    #region Apply one string override
    private static void Override(string variableName, Action<string> setter)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            setter(value.Trim());
        }
    }
    #endregion

    #region Apply one integer override
    private static void OverrideInt(string variableName, Action<int> setter)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!int.TryParse(value.Trim(), out int parsed))
        {
            throw new InvalidOperationException($"{variableName} must be an integer.");
        }

        setter(parsed);
    }
    #endregion

    #region Apply one boolean override
    private static void OverrideBoolean(string variableName, Action<bool> setter)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!bool.TryParse(value.Trim(), out bool parsed))
        {
            throw new InvalidOperationException($"{variableName} must be true or false.");
        }

        setter(parsed);
    }
    #endregion

    #region Resolve file paths
    private static void ResolvePaths(AppSettings settings, string configDirectory)
    {
        settings.EnableBanking.PrivateKeyPath = ExpandPath(settings.EnableBanking.PrivateKeyPath, configDirectory);
        settings.Storage.SessionFile = ExpandPath(settings.Storage.SessionFile, configDirectory);
        settings.Storage.ExportDirectory = ExpandPath(settings.Storage.ExportDirectory, configDirectory);
        settings.Watcher.StateFile = ExpandPath(settings.Watcher.StateFile, configDirectory);
    }
    #endregion

    #region Expand file path
    private static string ExpandPath(string path, string baseDirectory)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded == "~")
        {
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (expanded.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                 expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = Path.Combine(home, expanded[2..]);
        }

        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDirectory, expanded));
    }
    #endregion

    #region Validate settings
    private static void Validate(AppSettings settings)
    {
        if (!Uri.TryCreate(settings.EnableBanking.ApiBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("EnableBanking.ApiBaseUrl must be an absolute URL.");
        }

        if (!Uri.TryCreate(settings.EnableBanking.RedirectUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("EnableBanking.RedirectUrl must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(settings.EnableBanking.ApplicationId))
        {
            throw new InvalidOperationException("EnableBanking.ApplicationId is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.EnableBanking.PrivateKeyPath) ||
            !File.Exists(settings.EnableBanking.PrivateKeyPath))
        {
            throw new FileNotFoundException(
                "EnableBanking.PrivateKeyPath must point to the private PEM key for the Enable Banking application.",
                settings.EnableBanking.PrivateKeyPath);
        }

        if (settings.EnableBanking.PreferredConsentDays <= 0)
        {
            throw new InvalidOperationException("EnableBanking.PreferredConsentDays must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(settings.Watcher.WebhookUrl) &&
            (!Uri.TryCreate(settings.Watcher.WebhookUrl, UriKind.Absolute, out Uri? webhookUri) ||
             (webhookUri.Scheme != Uri.UriSchemeHttp && webhookUri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("Watcher.WebhookUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (settings.Watcher.IntervalMinutes <= 0)
        {
            throw new InvalidOperationException("Watcher.IntervalMinutes must be greater than zero.");
        }

        if (settings.Watcher.LookbackDays <= 0)
        {
            throw new InvalidOperationException("Watcher.LookbackDays must be greater than zero.");
        }
    }
    #endregion
}
