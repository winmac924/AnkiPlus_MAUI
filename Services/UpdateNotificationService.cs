using Microsoft.Extensions.Logging;

namespace AnkiPlus_MAUI.Services;

public class UpdateNotificationService
{
    private readonly GitHubUpdateService _updateService;
    private readonly ILogger<UpdateNotificationService> _logger;

    public UpdateNotificationService(GitHubUpdateService updateService, ILogger<UpdateNotificationService> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    public async Task CheckAndNotifyUpdatesAsync()
    {
        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            
            if (updateInfo?.IsUpdateAvailable == true)
            {
                await ShowUpdateNotificationAsync(updateInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート通知処理中にエラーが発生しました");
        }
    }

    private async Task ShowUpdateNotificationAsync(UpdateInfo updateInfo)
    {
        var releaseDate = updateInfo.ReleaseDate?.ToString("yyyy/MM/dd") ?? "";
        var message = $"新しいバージョン {updateInfo.LatestVersion} が利用可能です。";
        
        if (!string.IsNullOrEmpty(releaseDate))
        {
            message += $"\nリリース日: {releaseDate}";
        }
        
        if (!string.IsNullOrEmpty(updateInfo.ReleaseNotes))
        {
            // リリースノートを適度な長さに制限
            var notes = updateInfo.ReleaseNotes.Length > 200 
                ? updateInfo.ReleaseNotes.Substring(0, 200) + "..."
                : updateInfo.ReleaseNotes;
            message += $"\n\n変更内容:\n{notes}";
        }
        
        message += "\n\n今すぐアップデートしますか？";

        var userResponse = await Application.Current?.MainPage?.DisplayAlert(
            "🚀 GitHub からアップデート利用可能",
            message,
            "はい",
            "後で"
        );

        if (userResponse == true && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            await PerformUpdateAsync(updateInfo.DownloadUrl);
        }
    }

    private async Task PerformUpdateAsync(string downloadUrl)
    {
        try
        {
            // プログレスバーやローディング表示を追加することも可能
            await Application.Current?.MainPage?.DisplayAlert(
                "アップデート中",
                "アップデートをダウンロードしています...",
                "OK"
            );

            var success = await _updateService.DownloadAndInstallUpdateAsync(downloadUrl);
            
            if (!success)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "アップデートエラー",
                    "アップデートのインストールに失敗しました。後でもう一度お試しください。",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート実行中にエラーが発生しました");
        }
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        // 起動時のバックグラウンドアップデートチェック
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000); // アプリ起動から5秒後にチェック
            await CheckAndNotifyUpdatesAsync();
        });
    }
} 