using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AnkiPlus_MAUI.Services;

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private const string UpdateCheckUrl = "https://your-server.com/api/version"; // 実際のサーバーURLに変更

    public UpdateService(HttpClient httpClient, ILogger<UpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            var response = await _httpClient.GetStringAsync(UpdateCheckUrl);
            var latestVersion = JsonSerializer.Deserialize<VersionInfo>(response);

            if (latestVersion != null && IsNewVersionAvailable(currentVersion, latestVersion.Version))
            {
                return new UpdateInfo
                {
                    IsUpdateAvailable = true,
                    LatestVersion = latestVersion.Version,
                    DownloadUrl = latestVersion.DownloadUrl,
                    ReleaseNotes = latestVersion.ReleaseNotes
                };
            }

            return new UpdateInfo { IsUpdateAvailable = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート確認中にエラーが発生しました");
            return null;
        }
    }

    private string GetCurrentVersion()
    {
        return VersionHelper.GetCurrentVersion();
    }

    private bool IsNewVersionAvailable(string currentVersion, string latestVersion)
    {
        var current = Version.Parse(currentVersion);
        var latest = Version.Parse(latestVersion);
        return latest > current;
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            // Windows 10/11のMSIXアップデート機能を使用
            var tempPath = Path.GetTempFileName();
            var fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempPath, fileBytes);

            // MSIXアプリケーションの場合、Windowsが自動的にアップデートを処理
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデートのダウンロード・インストール中にエラーが発生しました");
            return false;
        }
    }
}

public class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string? LatestVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTime? ReleaseDate { get; set; }
}

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
} 