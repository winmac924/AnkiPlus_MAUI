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
        public static User CurrentUser 
        { 
            get => _currentUser;
            set => _currentUser = value;
        }

        public App()
        {
            InitializeComponent();

            InitializeFirebase();
            InitializeAzureBlobStorage();
            InitializeMainPage();
        }

        private void InitializeFirebase()
        {
            var config = new FirebaseAuthConfig
            {
                ApiKey = "AIzaSyAJa6TbK4Kl--oBBPSF-QPbGwWhYhh13kg",
                AuthDomain = "flashnote-75f1e.firebaseapp.com",
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                }
            };

            AuthClient = new FirebaseAuthClient(config);
        }

        private void InitializeAzureBlobStorage()
        {
            try
            {
                string connectionString = "DefaultEndpointsProtocol=https;AccountName=flashnote;AccountKey=iNQjjaa/klcND+6ACeug3V9APg49JJJW/1lK2UMqidcrFprG+hqJE2v55c1npFPdTwzE3I7ckyxz+AStVc7IWQ==;EndpointSuffix=core.windows.net";
                BlobServiceClient = new BlobServiceClient(connectionString);
                Debug.WriteLine("Azure Blob Storage接続が初期化されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Azure Blob Storageの初期化中にエラー: {ex.Message}");
            }
        }

        private void InitializeMainPage()
        {
            MainPage = new AppShell();
            _ = CheckSavedLoginAsync();
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
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}