using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace AnkiPlus_MAUI
{
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<Note> Notes { get; set; }
        private const int NoteWidth = 150; // ノート1つの幅
        private const int NoteMargin = 10;  // 各ノート間の固定間隔
        private const int PaddingSize = 10; // コレクションの左右余白
        private Note _selectedNote;
        private Stack<string> _currentPath = new Stack<string>();
        // `C:\Users\ユーザー名\Documents\AnkiPlus` に保存
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AnkiPlus");

        public MainPage()
        {
            InitializeComponent();
            Notes = new ObservableCollection<Note>();
            BindingContext = this;
            _currentPath.Push(FolderPath);
            LoadNotes();

            // 初期レイアウトの設定
            if (NotesCollectionView?.ItemsLayout is GridItemsLayout gridLayout)
            {
                gridLayout.HorizontalItemSpacing = NoteMargin;
                gridLayout.VerticalItemSpacing = NoteMargin;
            }
        }

        // ノートを読み込む
        private void LoadNotes()
        {
            var currentFolder = _currentPath.Peek();
            if (!Directory.Exists(currentFolder))
            {
                Directory.CreateDirectory(currentFolder);
            }

            Notes.Clear();

            // 親フォルダへ戻るボタンを追加（ルートフォルダでない場合）
            if (_currentPath.Count > 1)
            {
                var parentDir = Path.GetDirectoryName(currentFolder);
                Notes.Add(new Note
                {
                    Name = "..",
                    Icon = "folder.png",
                    IsFolder = true,
                    FullPath = parentDir,
                    LastModified = Directory.GetLastWriteTime(parentDir)
                });
            }

            // フォルダとファイルを一緒に取得して最終更新日でソート
            var items = new List<Note>();

            // フォルダを追加
            var directories = Directory.GetDirectories(currentFolder);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                items.Add(new Note
                {
                    Name = dirName,
                    Icon = "folder.png",
                    IsFolder = true,
                    FullPath = dir,
                    LastModified = Directory.GetLastWriteTime(dir)
                });
            }

            // ノートファイルを追加
            var files = Directory.GetFiles(currentFolder, "*.ankpls");
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                items.Add(new Note
                {
                    Name = fileName,
                    Icon = "note1.png",
                    IsFolder = false,
                    FullPath = file,
                    LastModified = File.GetLastWriteTime(file)
                });
            }

            // 最終更新日の新しい順にソート
            var sortedItems = items.OrderByDescending(item => item.LastModified);
            foreach (var item in sortedItems)
            {
                Notes.Add(item);
            }
        }
        // タップ時の処理（スタイラス or 指/マウス）
        private async void OnTapped(object sender, TappedEventArgs e)
        {
            var frame = sender as Frame;
            if (frame != null)
            {
                // タップ時のアニメーション
                await frame.ScaleTo(0.95, 50);
                await frame.ScaleTo(1, 50);

                var note = frame.BindingContext as Note;
                if (note == null) return;

                if (note.IsFolder)
                {
                    if (note.Name == "..")
                    {
                        if (_currentPath.Count > 1)
                        {
                            _currentPath.Pop();
                            LoadNotes();
                        }
                    }
                    else
                    {
                        _currentPath.Push(note.FullPath);
                        LoadNotes();
                    }
                }
                else
                {
                    if (e.GetType().ToString().Contains("Pen"))
                    {
                        // スタイラスペンの場合は直接NotePageへ
                    }
                    else
                    {
                        // 指/マウスの場合は確認画面へ
                        await Navigation.PushAsync(new Confirmation(note));
                        Debug.WriteLine($"Selected Note: {note.FullPath}");
                    }
                }
            }
        }
        // 新規ノート作成ボタンのクリックイベント
        private async void OnCreateNewNoteClicked(object sender, EventArgs e)
        {
            string action = await DisplayActionSheet("新規作成", "キャンセル", null, "ノート", "フォルダ");

            if (action == "ノート")
            {
                string newNoteName = await DisplayPromptAsync("新規ノート作成", "ノートの名前を入力してください");
                if (!string.IsNullOrWhiteSpace(newNoteName))
                {
                    SaveNewNote(newNoteName);
                }
            }
            else if (action == "フォルダ")
            {
                string newFolderName = await DisplayPromptAsync("新規フォルダ作成", "フォルダの名前を入力してください");
                if (!string.IsNullOrWhiteSpace(newFolderName))
                {
                    CreateNewFolder(newFolderName);
                }
            }
        }
        // ノートを保存する
        private void SaveNewNote(string noteName)
        {
            var currentFolder = _currentPath.Peek();
            var filePath = Path.Combine(currentFolder, $"{noteName}.ankpls");
            File.WriteAllText(filePath, "");
            Notes.Insert(0, new Note { Name = noteName, Icon = "note1.png", IsFolder = false, FullPath = filePath });
        }

        private void CreateNewFolder(string folderName)
        {
            var currentFolder = _currentPath.Peek();
            var newFolderPath = Path.Combine(currentFolder, folderName);
            Directory.CreateDirectory(newFolderPath);
            Notes.Insert(0, new Note { Name = folderName, Icon = "folder.png", IsFolder = true, FullPath = newFolderPath });
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            // 最小幅を設定（1つのアイテムが表示できる最小幅）
            if (width < NoteWidth + (PaddingSize * 2))
            {
                width = NoteWidth + (PaddingSize * 2);
            }

            UpdateSpan(width);
        }

        private void UpdateSpan(double width)
        {
            if (NotesCollectionView?.ItemsLayout is GridItemsLayout gridLayout)
            {
                // パディングとマージンを考慮した実効幅を計算
                double effectiveWidth = width - (PaddingSize * 2);

                // 1列に表示できるアイテム数を計算（マージンを考慮）
                int newSpan = Math.Max(1, (int)((effectiveWidth + NoteMargin) / (NoteWidth + NoteMargin)));

                // 現在のアイテム数で必要な列数を計算
                int itemCount = Notes?.Count ?? 0;
                int minRequiredSpan = Math.Max(1, Math.Min(newSpan, itemCount));

                // 実際に使用する幅を計算（中央寄せのため）
                double usedWidth = (minRequiredSpan * (NoteWidth + NoteMargin)) - NoteMargin;

                // 新しい左右のパディングを計算（中央寄せ）
                double newPadding = Math.Max(PaddingSize, (width - usedWidth) / 2);

                // CollectionViewのマージンを更新
                NotesCollectionView.Margin = new Thickness(newPadding, NotesCollectionView.Margin.Top,
                                                         newPadding, NotesCollectionView.Margin.Bottom);

                // Spanを更新
                if (gridLayout.Span != minRequiredSpan)
                {
                    gridLayout.Span = minRequiredSpan;
                }
            }
        }
    }
    public class Note
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public bool IsFolder { get; set; }
        public string FullPath { get; set; }
        public DateTime LastModified { get; set; }
    }

}
