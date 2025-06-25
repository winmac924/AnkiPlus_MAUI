using Firebase.Auth;

namespace AnkiPlus_MAUI
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingIndicator.IsVisible = LoadingIndicator.IsRunning = true;
                StatusLabel.Text = "";

                var email = EmailEntry.Text;
                var password = PasswordEntry.Text;

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    StatusLabel.Text = "メールアドレスとパスワードを入力してください。";
                    return;
                }

                var userCredential = await App.AuthClient.SignInWithEmailAndPasswordAsync(email, password);
                if (userCredential != null)
                {
                    // ログイン情報を保存
                    await App.SaveLoginInfo(email, password);
                    App.CurrentUser = userCredential.User;

                    // ログイン成功
                    await Shell.Current.GoToAsync("///MainPage");
                }
            }
            catch (FirebaseAuthException ex)
            {
                StatusLabel.Text = "ログインに失敗しました: " + ex.Message;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "エラーが発生しました: " + ex.Message;
            }
            finally
            {
                LoadingIndicator.IsVisible = LoadingIndicator.IsRunning = false;
            }
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingIndicator.IsVisible = LoadingIndicator.IsRunning = true;
                StatusLabel.Text = "";

                var email = EmailEntry.Text;
                var password = PasswordEntry.Text;

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    StatusLabel.Text = "メールアドレスとパスワードを入力してください。";
                    return;
                }

                var userCredential = await App.AuthClient.CreateUserWithEmailAndPasswordAsync(email, password);
                if (userCredential != null)
                {
                    // 登録成功
                    StatusLabel.Text = "登録が完了しました。ログインしてください。";
                    StatusLabel.TextColor = Colors.Green;
                }
            }
            catch (FirebaseAuthException ex)
            {
                StatusLabel.Text = "登録に失敗しました: " + ex.Message;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "エラーが発生しました: " + ex.Message;
            }
            finally
            {
                LoadingIndicator.IsVisible = LoadingIndicator.IsRunning = false;
            }
        }
    }
} 