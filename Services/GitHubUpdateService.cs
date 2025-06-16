using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AnkiPlus_MAUI.Services;

public class GitHubUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateService> _logger;
    
    // GitHubリポジトリの設定（実際の値に変更してください）
    private const string GITHUB_OWNER = "winmac924"; // GitHubユーザー名
    private const string GITHUB_REPO = "AnkiPlus_MAUI"; // リポジトリ名
    private const string GITHUB_API_BASE = "https://api.github.com";

    public GitHubUpdateService(HttpClient httpClient, ILogger<GitHubUpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // GitHub API用のUser-Agentヘッダーを設定（必須）
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AnkiPlus-MAUI-UpdateClient");
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = VersionHelper.GetCurrentVersion();
            var latestRelease = await GetLatestReleaseAsync();

            if (latestRelease != null && IsNewVersionAvailable(currentVersion, latestRelease.TagName))
            {
                return new UpdateInfo
                {
                    IsUpdateAvailable = true,
                    LatestVersion = latestRelease.TagName,
                    DownloadUrl = GetMsixDownloadUrl(latestRelease),
                    ReleaseNotes = latestRelease.Body ?? "新しいバージョンが利用可能です",
                    ReleaseDate = latestRelease.PublishedAt
                };
            }

            return new UpdateInfo { IsUpdateAvailable = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub からのアップデート確認中にエラーが発生しました");
            return null;
        }
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            var url = $"{GITHUB_API_BASE}/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);
            
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            
            return JsonSerializer.Deserialize<GitHubRelease>(response, options);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "最新リリース情報の取得に失敗しました");
            return null;
        }
    }

    private string? GetMsixDownloadUrl(GitHubRelease release)
    {
        // .exe ファイルを最優先で検索
        var exeAsset = release.Assets?.FirstOrDefault(asset => 
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (exeAsset != null)
        {
            return exeAsset.BrowserDownloadUrl;
        }

        // .msix ファイルを検索（後方互換性）
        var msixAsset = release.Assets?.FirstOrDefault(asset => 
            asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));

        if (msixAsset != null)
        {
            return msixAsset.BrowserDownloadUrl;
        }

        // .zip ファイルも検索（実行ファイルが含まれている可能性）
        var zipAsset = release.Assets?.FirstOrDefault(asset => 
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("windows", StringComparison.OrdinalIgnoreCase));

        return zipAsset?.BrowserDownloadUrl;
    }

    private bool IsNewVersionAvailable(string currentVersion, string latestVersion)
    {
        try
        {
            // "v1.0.0" -> "1.0.0" の形式変換
            var cleanCurrent = currentVersion.TrimStart('v');
            var cleanLatest = latestVersion.TrimStart('v');
            
            var current = Version.Parse(cleanCurrent);
            var latest = Version.Parse(cleanLatest);
            
            return latest > current;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "バージョン比較中にエラー: current={Current}, latest={Latest}", 
                currentVersion, latestVersion);
            return false;
        }
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            _logger.LogInformation("アップデートをダウンロード中: {Url}", downloadUrl);
            
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            
            // プログレス付きダウンロード
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;
            
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            
            var buffer = new byte[8192];
            int bytesRead;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;
                
                if (totalBytes > 0)
                {
                    var progress = (double)downloadedBytes / totalBytes * 100;
                    _logger.LogDebug("ダウンロード進行状況: {Progress:F1}%", progress);
                }
            }
            
            _logger.LogInformation("ダウンロード完了: {Path}", tempPath);
            
            // EXEファイルの場合、直接実行
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallExeAsync(tempPath);
            }

            // MSIXファイルの場合、直接インストール
            if (fileName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallMsixAsync(tempPath);
            }
            
            // ZIPファイルの場合、解凍して実行ファイルを探す
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return await ExtractAndInstallFromZipAsync(tempPath);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデートのダウンロード・インストール中にエラーが発生しました");
            return false;
        }
    }

    private async Task<bool> InstallExeAsync(string exePath)
    {
        try
        {
            // 新しいバージョンのEXEを実行
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Arguments = "--update-mode" // 更新モードフラグ（必要に応じて）
            };
            
            System.Diagnostics.Process.Start(startInfo);
            
            // 現在のアプリケーションを終了
            Application.Current?.Quit();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXE インストール中にエラーが発生しました");
            return false;
        }
    }

    private async Task<bool> InstallMsixAsync(string msixPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = msixPath,
                UseShellExecute = true,
                Verb = "runas" // 管理者権限で実行
            };
            
            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MSIX インストール中にエラーが発生しました");
            return false;
        }
    }

    private async Task<bool> ExtractAndInstallFromZipAsync(string zipPath)
    {
        try
        {
            var extractPath = Path.Combine(Path.GetTempPath(), "AnkiPlus_Update");
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
                
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            // MSIXファイルを検索
            var msixFiles = Directory.GetFiles(extractPath, "*.msix", SearchOption.AllDirectories);
            if (msixFiles.Length > 0)
            {
                return await InstallMsixAsync(msixFiles[0]);
            }
            
            _logger.LogWarning("ZIP内にMSIXファイルが見つかりませんでした");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZIP展開中にエラーが発生しました");
            return false;
        }
    }
}

// GitHub Releases API のレスポンス構造
public class GitHubRelease
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool Prerelease { get; set; }
    public DateTime PublishedAt { get; set; }
    public List<GitHubAsset>? Assets { get; set; }
}

public class GitHubAsset
{
    public string Name { get; set; } = string.Empty;
    public string BrowserDownloadUrl { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
} 