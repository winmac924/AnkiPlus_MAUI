using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.IO.Compression;

namespace AnkiPlus_MAUI
{
    public partial class Confirmation : ContentPage
    {
        private Note _selectedNote;
        private string tempExtractPath; // �ꎞ�W�J�t�H���_
        private string ankplsFilePath;  // .ankpls�t�@�C���̃p�X

        public Confirmation(Note note)
        {
            InitializeComponent();
            _selectedNote = note;

            // �p�X�ݒ�
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = Path.Combine(documentsPath, "AnkiPlus", $"{_selectedNote.Name}.ankpls");
            tempExtractPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "AnkiPlus", $"{_selectedNote.Name}_temp");

            LoadNote();
        }
        // �m�[�g��ǂݍ��݁A��萔��\��
        private void LoadNote()
        {
            if (File.Exists(ankplsFilePath))
            {
                // ��萔���擾
                int totalQuestions = GetTotalQuestions();
                NoteTitleLabel.Text = _selectedNote.Name;
                TotalQuestionsLabel.Text = $"��萔: {totalQuestions}";
            }
            else
            {
                DisplayAlert("�G���[", "�m�[�g�t�@�C����������܂���ł���", "OK");
            }
        }
        // ��萔���擾�i���j
        private int GetTotalQuestions()
        {
            string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            if (File.Exists(cardsFilePath))
            {
                Debug.WriteLine(cardsFilePath);
                // `cards.txt` ��1�s�ڂɖ�萔���L�^����Ă���
                var lines = File.ReadAllLines(cardsFilePath);

                if (lines.Length > 0 && int.TryParse(lines[1], out int questionCount))
                {
                    return questionCount;
                }
            }
            return 0;
        }

        // �w�K���J�n
        private void OnStartLearningClicked(object sender, EventArgs e)
        {
            // Qa.xaml �ɑJ�ځi���j
            Navigation.PushAsync(new Qa(_selectedNote.Name));
        }
        // Add��
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
        // NotePage��
        private async void ToNoteClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new NotePage(_selectedNote.Name));
        }
    }
}
