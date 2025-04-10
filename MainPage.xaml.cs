using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using System;
namespace AnkiPlus_MAUI
{
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<Note> Notes { get; set; }
        private const int NoteWidth = 150; // ノート1つの幅
        private const int NoteMargin = 10;  // 各ノート間の固定間隔
        private const int PaddingSize = 20; // コレクションの左右余白
        private Note _selectedNote;
        // `C:\Users\ユーザー名\Documents\AnkiPlus` に保存
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AnkiPlus");

        public MainPage()
        {
            InitializeComponent();
            Notes = new ObservableCollection<Note>();
            BindingContext = this;

            LoadNotes();
        }

        // ノートを読み込む
        private void LoadNotes()
        {
            var fullPath = Path.Combine(FileSystem.AppDataDirectory, FolderPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var files = Directory.GetFiles(fullPath, "*.ankpls");
            Notes.Clear();
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                Notes.Add(new Note { Name = fileName, Icon = "note1.png" });
            }
        }
        // タップ時の処理（スタイラス or 指/マウス）
        private void OnTapped(object sender, TappedEventArgs e)
        {
            var frame = sender as Frame;
            var note = frame?.BindingContext as Note;
            if (note == null) return;

            // スタイラスペンかどうかを判定
            if (e.GetType().ToString().Contains("Pen"))
            {
                // スタイラスなら NotePage へ
                Navigation.PushAsync(new NotePage(note.Name));
            }
            else
            {
                // 指 or マウスなら Confirmation へ
                Navigation.PushAsync(new Confirmation(note));
            }
        }
        // 新規ノート作成ボタンのクリックイベント
        private async void OnCreateNewNoteClicked(object sender, EventArgs e)
        {
            string newNoteName = await DisplayPromptAsync("新規ノート作成", "ノートの名前を入力してください");
            if (!string.IsNullOrWhiteSpace(newNoteName))
            {
                SaveNewNote(newNoteName);
            }
        }
        // ノートを保存する
        private void SaveNewNote(string noteName)
        {
            var fullPath = Path.Combine(FileSystem.AppDataDirectory, FolderPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var filePath = Path.Combine(fullPath, $"{noteName}.ankpls");

            // ファイルを作成（中身は空でもOK）
            File.WriteAllText(filePath, "");

            // ノートをリストに追加（上部に追加）
            Notes.Insert(0, new Note { Name = noteName, Icon = "note1.png" });
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            UpdateSpan(width);
        }

        private void UpdateSpan(double width)
        {
        }
    }
    public class Note
    {
        public string Name { get; set; }
        public string Icon { get; set; }
    }

}
