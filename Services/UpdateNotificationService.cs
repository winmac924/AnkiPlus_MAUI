using Microsoft.Extensions.Logging;

namespace AnkiPlus_MAUI.Services;

public class UpdateNotificationService
{
    private readonly GitHubUpdateService _updateService;
    private readonly ILogger<UpdateNotificationService> _logger;
    private bool _isCheckingForUpdates = false;

    public UpdateNotificationService(GitHubUpdateService updateService, ILogger<UpdateNotificationService> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    public async Task CheckForUpdatesAsync()
    {
        if (_isCheckingForUpdates)
        {
            _logger.LogInformation("アップデート確認が既に実行中です");
            return;
        }

        try
        {
            _isCheckingForUpdates = true;
            _logger.LogInformation("アップデート確認を開始します");

            var updateInfo = await _updateService.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                _logger.LogInformation("アップデート確認が完了しました（リリース情報なし）");
                return;
            }

            if (updateInfo.IsUpdateAvailable)
            {
                _logger.LogInformation("新しいアップデートが利用可能です: {Version}", updateInfo.LatestVersion);
                await ShowUpdateNotificationAsync(updateInfo);
            }
            else
            {
                _logger.LogInformation("アプリは最新バージョンです");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート確認中にエラーが発生しました");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private async Task ShowUpdateNotificationAsync(UpdateInfo updateInfo)
    {
        try
        {
            var title = "🚀 新しいバージョンが利用可能です";
            var message = $"AnkiPlus MAUI {updateInfo.LatestVersion} がリリースされました。\n\n" +
                         $"📋 更新内容:\n{updateInfo.ReleaseNotes}\n\n" +
                         $"今すぐダウンロードしますか？";

            var result = await Application.Current.MainPage.DisplayAlert(
                title,
                message,
                "ダウンロード",
                "後で"
            );

            if (result && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                _logger.LogInformation("ユーザーがアップデートのダウンロードを選択しました");
                await StartUpdateDownloadAsync(updateInfo);
            }
            else
            {
                _logger.LogInformation("ユーザーがアップデートを後回しにしました");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート通知の表示中にエラーが発生しました");
        }
    }

    private async Task StartUpdateDownloadAsync(UpdateInfo updateInfo)
    {
        try
        {
            // ダウンロード開始の通知
            await Application.Current.MainPage.DisplayAlert(
                "📥 ダウンロード開始",
                "アップデートのダウンロードを開始します。\n完了まで少々お待ちください...",
                "OK"
            );

            _logger.LogInformation("アップデートのダウンロードを開始: {Url}", updateInfo.DownloadUrl);
            
            var success = await _updateService.DownloadAndInstallUpdateAsync(updateInfo.DownloadUrl);

            if (success)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "✅ ダウンロード完了",
                    "アップデートのダウンロードが完了しました。\n新しいバージョンが起動します。",
                    "OK"
                );
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert(
                    "❌ ダウンロード失敗",
                    "アップデートのダウンロードに失敗しました。\n手動でGitHubからダウンロードしてください。",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデートのダウンロード中にエラーが発生しました");
            
            await Application.Current.MainPage.DisplayAlert(
                "❌ エラー",
                "アップデートのダウンロード中にエラーが発生しました。\n手動でGitHubからダウンロードしてください。",
                "OK"
            );
        }
    }

    /// <summary>
    /// 開発中のテスト用：アップデートチェックを無効化
    /// </summary>
    public static bool IsUpdateCheckEnabled => 
#if DEBUG
        false; // デバッグモードでは無効
#else
        true;  // リリースモードでは有効
#endif
} 