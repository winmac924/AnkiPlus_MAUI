using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.IO.Compression;

namespace AnkiPlus_MAUI
{
    public partial class Confirmation : ContentPage
    {
        private Note _selectedNote;
        private string tempExtractPath; // 一時展開フォルダ
        private string ankplsFilePath;  // .ankplsファイルのパス

        public Confirmation(Note note)
        {
            InitializeComponent();
            _selectedNote = note;

            // パス設定
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = Path.Combine(documentsPath, "AnkiPlus", $"{_selectedNote.Name}.ankpls");
            tempExtractPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "AnkiPlus", $"{_selectedNote.Name}_temp");

            LoadNote();
        }
        // ノートを読み込み、問題数を表示
        private void LoadNote()
        {
            if (File.Exists(ankplsFilePath))
            {
                // 問題数を取得
                int totalQuestions = GetTotalQuestions();
                NoteTitleLabel.Text = _selectedNote.Name;
                TotalQuestionsLabel.Text = $"問題数: {totalQuestions}";
            }
            else
            {
                DisplayAlert("エラー", "ノートファイルが見つかりませんでした", "OK");
            }
        }
        // 問題数を取得（仮）
        private int GetTotalQuestions()
        {
            string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            if (File.Exists(cardsFilePath))
            {
                Debug.WriteLine(cardsFilePath);
                // `cards.txt` の1行目に問題数が記録されている
                var lines = File.ReadAllLines(cardsFilePath);

                if (lines.Length > 0 && int.TryParse(lines[1], out int questionCount))
                {
                    return questionCount;
                }
            }
            return 0;
        }

        // 学習を開始
        private void OnStartLearningClicked(object sender, EventArgs e)
        {
            // Qa.xaml に遷移（仮）
            Navigation.PushAsync(new Qa(_selectedNote.Name));
        }
        // Addへ
        private async void AddCardClicked(object sender, EventArgs e)
        {
            if (Navigation != null)
            {
                await Navigation.PushAsync(new Add(_selectedNote));
            }
            else
            {
                await DisplayAlert("Error", "Navigation is not available.", "OK");
            }
        }
        // NotePageへ
        private async void ToNoteClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new NotePage(_selectedNote.Name));
        }
    }
}
