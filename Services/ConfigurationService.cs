using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AnkiPlus_MAUI.Services;

public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private AppSettings? _settings;

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        LoadConfiguration();
    }

    private void LoadConfiguration()
    {
        try
        {
            // appsettings.jsonファイルを読み込み
            using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").Result;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            
            _settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("設定ファイルの読み込みが完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定ファイルの読み込みに失敗しました");
            throw new InvalidOperationException("設定ファイルが見つからないか、形式が正しくありません。appsettings.jsonファイルを確認してください。", ex);
        }
    }

    public string GetFirebaseApiKey()
    {
        if (string.IsNullOrEmpty(_settings?.Firebase?.ApiKey))
        {
            throw new InvalidOperationException("Firebase APIキーが設定されていません");
        }
        return _settings.Firebase.ApiKey;
    }

    public string GetFirebaseAuthDomain()
    {
        if (string.IsNullOrEmpty(_settings?.Firebase?.AuthDomain))
        {
            throw new InvalidOperationException("Firebase AuthDomainが設定されていません");
        }
        return _settings.Firebase.AuthDomain;
    }

    public string GetAzureStorageConnectionString()
    {
        if (string.IsNullOrEmpty(_settings?.AzureStorage?.ConnectionString))
        {
            throw new InvalidOperationException("Azure Storage接続文字列が設定されていません");
        }
        return _settings.AzureStorage.ConnectionString;
    }
}

public class AppSettings
{
    public FirebaseSettings? Firebase { get; set; }
    public AzureStorageSettings? AzureStorage { get; set; }
}

public class FirebaseSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string AuthDomain { get; set; } = string.Empty;
}

public class AzureStorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
} 