using Microsoft.Maui.Controls;
using Firebase.Auth;
using Firebase.Auth.Providers;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using AnkiPlus_MAUI.Services;

namespace AnkiPlus_MAUI
{
    public partial class App : Application
    {
        public static FirebaseAuthClient AuthClient { get; private set; }
        public static BlobServiceClient BlobServiceClient { get; private set; }
        private static User _currentUser;
        private readonly ConfigurationService _configService;
        private readonly FileWatcherService _fileWatcherService;

        public static User CurrentUser 
        { 
            get => _currentUser;
            set => _currentUser = value;
        }

        public App(ConfigurationService configService, FileWatcherService fileWatcherService)
        {
            InitializeComponent();
            _configService = configService;
            _fileWatcherService = fileWatcherService;

            CleanupBackupFiles();
            InitializeFirebase();
            InitializeAzureBlobStorage();
            InitializeMainPage();
        }

        private void InitializeFirebase()
        {
            try
            {
            var config = new FirebaseAuthConfig
            {
                    ApiKey = _configService.GetFirebaseApiKey(),
                    AuthDomain = _configService.GetFirebaseAuthDomain(),
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                }
            };

            AuthClient = new FirebaseAuthClient(config);
                Debug.WriteLine("Firebase認証が初期化されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Firebase初期化中にエラー: {ex.Message}");
                throw;
            }
        }

        private void InitializeAzureBlobStorage()
        {
            try
            {
                string connectionString = _configService.GetAzureStorageConnectionString();
                BlobServiceClient = new BlobServiceClient(connectionString);
                Debug.WriteLine("Azure Blob Storage接続が初期化されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Azure Blob Storageの初期化中にエラー: {ex.Message}");
                throw;
            }
        }

        private void InitializeMainPage()
        {
            MainPage = new AppShell();
            _ = CheckSavedLoginAsync();
        }

        private void CleanupBackupFiles()
        {
            try
            {
                // 現在の実行ファイルのパスを取得
                var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                    return;

                var backupPath = currentExePath + ".backup";
                
                // バックアップファイルが存在する場合は削除
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    Debug.WriteLine($"バックアップファイルを削除しました: {backupPath}");
                }

                // 一時フォルダのアップデート関連ファイルもクリーンアップ
                var tempPath = Path.GetTempPath();
                var updateBatchFiles = Directory.GetFiles(tempPath, "AnkiPlus_Update*.bat");
                foreach (var batchFile in updateBatchFiles)
                {
                    try
                    {
                        File.Delete(batchFile);
                        Debug.WriteLine($"アップデートバッチファイルを削除しました: {batchFile}");
                    }
                    catch
                    {
                        // バッチファイルが実行中の場合は削除できないが、問題なし
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"バックアップファイルのクリーンアップ中にエラー: {ex.Message}");
            }
        }

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                // MainPageから呼び出されるように修正
                Debug.WriteLine("アップデートチェックをスキップしました（実装準備中）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アップデートチェック中にエラー: {ex.Message}");
            }
        }

        private async Task CheckSavedLoginAsync()
        {
            try
            {
                var email = await SecureStorage.GetAsync("user_email");
                var password = await SecureStorage.GetAsync("user_password");

                if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password))
                {
                    var userCredential = await AuthClient.SignInWithEmailAndPasswordAsync(email, password);
                    if (userCredential != null)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            CurrentUser = userCredential.User;
                            Shell.Current.GoToAsync("///MainPage");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存されたログイン情報の確認中にエラー: {ex.Message}");
            }
        }

        public static async Task SaveLoginInfo(string email, string password)
        {
            try
            {
                await SecureStorage.SetAsync("user_email", email);
                await SecureStorage.SetAsync("user_password", password);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン情報の保存中にエラー: {ex.Message}");
            }
        }

        public static async Task ClearLoginInfo()
        {
            try
            {
                SecureStorage.Default.Remove("user_email");
                SecureStorage.Default.Remove("user_password");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン情報の削除中にエラー: {ex.Message}");
            }
        }

        protected override void OnStart()
        {
            base.OnStart();
            // アプリケーション開始時にファイル監視を開始
            _fileWatcherService?.StartWatching();
        }

        protected override void OnSleep()
        {
            base.OnSleep();
            // アプリケーションがバックグラウンドに移行時にファイル監視を停止
            _fileWatcherService?.StopWatching();
        }

        protected override void OnResume()
        {
            base.OnResume();
            // アプリケーションがフォアグラウンドに戻った時にファイル監視を再開
            _fileWatcherService?.StartWatching();
        }
    }
}