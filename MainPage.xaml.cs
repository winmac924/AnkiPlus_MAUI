using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;

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
                        await Navigation.PushAsync(new Confirmation(note.FullPath));
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

        private void CollectCardsFromFolder(string folderPath, List<string> cards)
        {
            Debug.WriteLine($"Searching in folder: {folderPath}");

            // .ankplsファイルを探す
            var ankplsFiles = Directory.GetFiles(folderPath, "*.ankpls");
            Debug.WriteLine($"Found {ankplsFiles.Length} .ankpls files");

            foreach (var ankplsFile in ankplsFiles)
            {
                Debug.WriteLine($"Processing .ankpls file: {ankplsFile}");
                try
                {
                    // 一時フォルダのパスを作成
                    var tempFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Temp",
                        "AnkiPlus",
                        Path.GetFileNameWithoutExtension(ankplsFile) + "_temp");

                    // 一時フォルダが存在しない場合は作成
                    if (!Directory.Exists(tempFolder))
                    {
                        Directory.CreateDirectory(tempFolder);
                    }

                    using (var archive = ZipFile.OpenRead(ankplsFile))
                    {
                        // アーカイブ内のファイル一覧を表示
                        Debug.WriteLine($"Files in {ankplsFile}:");
                        foreach (var entry in archive.Entries)
                        {
                            Debug.WriteLine($"  - {entry.FullName}");
                        }

                        // cards.txtを探す
                        var cardsEntry = archive.Entries.FirstOrDefault(e => e.Name == "cards.txt");
                        if (cardsEntry != null)
                        {
                            using (var stream = cardsEntry.Open())
                            using (var reader = new StreamReader(stream))
                            {
                                var content = reader.ReadToEnd();
                                Debug.WriteLine($"Found cards.txt with {content.Length} characters in {ankplsFile}");
                                cards.Add(content);

                                // cards.txtを一時フォルダに保存
                                File.WriteAllText(Path.Combine(tempFolder, "cards.txt"), content);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"No cards.txt found in {ankplsFile}");
                        }

                        // results.txtを探す
                        var resultsEntry = archive.Entries.FirstOrDefault(e => e.Name == "results.txt");
                        if (resultsEntry != null)
                        {
                            using (var stream = resultsEntry.Open())
                            using (var reader = new StreamReader(stream))
                            {
                                var content = reader.ReadToEnd();
                                Debug.WriteLine($"Found results.txt with {content.Length} characters in {ankplsFile}");
                                
                                // results.txtを一時フォルダに保存
                                File.WriteAllText(Path.Combine(tempFolder, "results.txt"), content);
                            }
                        }

                        // imgフォルダを探す
                        var imgEntries = archive.Entries.Where(e => e.FullName.StartsWith("img/")).ToList();
                        if (imgEntries.Any())
                        {
                            // 一時フォルダ内にimgフォルダを作成
                            var tempImgFolder = Path.Combine(tempFolder, "img");
                            if (!Directory.Exists(tempImgFolder))
                            {
                                Directory.CreateDirectory(tempImgFolder);
                            }

                            // 画像ファイルを展開
                            foreach (var entry in imgEntries)
                            {
                                var fileName = Path.GetFileName(entry.FullName);
                                var targetPath = Path.Combine(tempImgFolder, fileName);
                                
                                using (var stream = entry.Open())
                                using (var fileStream = File.Create(targetPath))
                                {
                                    stream.CopyTo(fileStream);
                                }
                                Debug.WriteLine($"Extracted image: {fileName}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing {ankplsFile}: {ex.Message}");
                    // 破損したファイルの場合は、一時フォルダから読み込みを試みる
                    try
                    {
                        var tempFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Temp",
                            "AnkiPlus",
                            Path.GetFileNameWithoutExtension(ankplsFile) + "_temp");

                        Debug.WriteLine($"Checking temp folder: {tempFolder}");
                        if (Directory.Exists(tempFolder))
                        {
                            var files = Directory.GetFiles(tempFolder);
                            Debug.WriteLine($"Files in temp folder:");
                            foreach (var file in files)
                            {
                                Debug.WriteLine($"  - {Path.GetFileName(file)}");
                            }

                            var tempCardsFile = Path.Combine(tempFolder, "cards.txt");
                            if (File.Exists(tempCardsFile))
                            {
                                var content = File.ReadAllText(tempCardsFile);
                                Debug.WriteLine($"Found cards.txt in temp folder with {content.Length} characters");
                                cards.Add(content);
                            }
                        }
                    }
                    catch (Exception tempEx)
                    {
                        Debug.WriteLine($"Error processing temp folder: {tempEx.Message}");
                    }
                }
            }

            // サブフォルダを再帰的に探索
            foreach (var dir in Directory.GetDirectories(folderPath))
            {
                Debug.WriteLine($"Recursively searching in: {dir}");
                CollectCardsFromFolder(dir, cards);
            }
        }

        private async void OnAnkiModeClicked(object sender, EventArgs e)
        {
            var currentFolder = _currentPath.Peek();
            Debug.WriteLine($"Starting card collection from: {currentFolder}");
            var allCards = new List<string>();

            // 現在のフォルダ内のすべてのcards.txtを収集
            CollectCardsFromFolder(currentFolder, allCards);
            Debug.WriteLine($"Total cards collected: {allCards.Count}");

            if (allCards.Count == 0)
            {
                await DisplayAlert("確認", "カードが見つかりませんでした。", "OK");
                return;
            }

            // Qaページに遷移し、カードを渡す
            await Navigation.PushAsync(new Qa(allCards));
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
