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
            // 最終確認
            var confirmResult = await Application.Current.MainPage.DisplayAlert(
                "🔄 アップデート実行",
                "アップデートを実行します。\n\n処理内容：\n1. 新しいバージョンをダウンロード\n2. アプリケーションを終了\n3. ファイルを自動更新\n4. 新しいバージョンを起動\n\n実行しますか？",
                "実行する",
                "キャンセル"
            );

            if (!confirmResult)
            {
                _logger.LogInformation("ユーザーがアップデートをキャンセルしました");
                return;
            }

            // ダウンロード開始の通知
            await Application.Current.MainPage.DisplayAlert(
                "📥 ダウンロード中",
                "新しいバージョンをダウンロード中です...\nしばらくお待ちください。",
                "OK"
            );

            _logger.LogInformation("アップデートのダウンロードを開始: {Url}", updateInfo.DownloadUrl);
            
            bool success = false;
            try
            {
                success = await _updateService.DownloadAndInstallUpdateAsync(updateInfo.DownloadUrl);
            }
            catch (Exception downloadEx)
            {
                _logger.LogError(downloadEx, "アップデートのダウンロード中に例外が発生しました");
                success = false;
            }

            if (success)
            {
                // 成功の場合は、アプリが自動終了するので通知は不要
                _logger.LogInformation("アップデート処理が正常に開始されました - アプリを終了します");
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert(
                    "❌ アップデート失敗",
                    "アップデートに失敗しました。\n\n手動でアップデートしてください：\n1. GitHubリリースページにアクセス\n2. 最新の .exe ファイルをダウンロード\n3. 現在のファイルを置き換え",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデートのダウンロード中にエラーが発生しました");
            
            await Application.Current.MainPage.DisplayAlert(
                "❌ アップデートエラー",
                $"アップデート中にエラーが発生しました。\n\n手動でアップデートしてください：\n1. https://github.com/winmac924/AnkiPlus_MAUI/releases\n2. 最新の .exe ファイルをダウンロード\n3. 現在のファイルを置き換え\n\nエラー詳細: {ex.Message}",
                "OK"
            );
        }
    }

    /// <summary>
    /// 開発中のテスト用：アップデートチェックを無効化
    /// </summary>
    public static bool IsUpdateCheckEnabled => 
#if DEBUG
        true; // デバッグモードでは無効
#else
        true;  // リリースモードでは有効
#endif
} 