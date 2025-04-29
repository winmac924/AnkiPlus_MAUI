using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.IO.Compression;

namespace AnkiPlus_MAUI
{
    public partial class Confirmation : ContentPage
    {
        private Note _selectedNote;
        private string tempExtractPath; // 一時展開パス
        private string ankplsFilePath;  // .ankplsファイルのパス

        public Confirmation(Note note)
        {
            InitializeComponent();
            _selectedNote = note;

            // パス設定
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = Path.Combine(documentsPath, "AnkiPlus", $"{_selectedNote.Name}.ankpls");

            // フォルダ構造を維持した一時ディレクトリのパスを生成
            string relativePath = Path.GetRelativePath(Path.Combine(documentsPath, "AnkiPlus"), Path.GetDirectoryName(_selectedNote.FullPath));
            tempExtractPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp",
                "AnkiPlus",
                relativePath,
                $"{_selectedNote.Name}_temp"
            );

            // 一時ディレクトリが存在しない場合は作成
            if (!Directory.Exists(tempExtractPath))
            {
                Directory.CreateDirectory(tempExtractPath);
            }

            LoadNote();
        }
        // データロード
        private void LoadNote()
        {
            if (File.Exists(ankplsFilePath))
            {
                // データ取得
                int totalQuestions = GetTotalQuestions();
                NoteTitleLabel.Text = _selectedNote.Name;
                TotalQuestionsLabel.Text = $"ノート数: {totalQuestions}";
            }
            else
            {
                DisplayAlert("エラー", "データが見つかりませんでした", "OK");
            }
        }
        // ノート数取得
        private int GetTotalQuestions()
        {
            string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            if (File.Exists(cardsFilePath))
            {
                Debug.WriteLine(cardsFilePath);
                // `cards.txt` 1行目にノート数が記載されている
                var lines = File.ReadAllLines(cardsFilePath);

                if (lines.Length > 0 && int.TryParse(lines[1], out int questionCount))
                {
                    return questionCount;
                }
            }
            return 0;
        }

        // 学習開始
        private void OnStartLearningClicked(object sender, EventArgs e)
        {
            // Qa.xaml に遷移（tempファイルのパスを渡す）
            Navigation.PushAsync(new Qa(_selectedNote.Name, tempExtractPath));
        }
        // Add
        private async void AddCardClicked(object sender, EventArgs e)
        {
            if (Navigation != null)
            {
                // Add.xaml に遷移（tempファイルのパスを渡す）
                await Navigation.PushAsync(new Add(_selectedNote.Name, tempExtractPath));
            }
            else
            {
                await DisplayAlert("Error", "Navigation is not available.", "OK");
            }
        }
        // NotePage
        private async void ToNoteClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new NotePage(_selectedNote.Name, tempExtractPath));
        }
    }
}
