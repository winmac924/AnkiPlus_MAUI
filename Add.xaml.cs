using Microsoft.Maui.Controls;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using SkiaSharp.Views.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace AnkiPlus_MAUI
{
    public partial class Add : ContentPage
    {
        private string ankplsFilePath; // .ankpls のパス
        private string tempExtractPath; // 一時展開フォルダ
        private Note _selectedNote;
        private List<string> imagePaths = new List<string>(); // 画像パスを保存
        private int imageCount = 0; // 画像番号管理
        private string selectedImagePath = "";         // 選択した画像のパス
        private SKBitmap imageBitmap;         // 画像を表示するためのビットマップ
        private List<SKRect> selectionRects = new List<SKRect>();
        private SKPoint startPoint, endPoint;
        private bool isDragging = false;
        private const float HANDLE_SIZE = 15;
        public Add(Note selectedNote)
        {
            InitializeComponent();
            this._selectedNote = selectedNote;
            CardTypePicker.SelectedIndex = 0; // 初期値を「基本」に設定

            // ノートの保存フォルダを設定
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = Path.Combine(documentsPath, "AnkiPlus", $"{_selectedNote.Name}.ankpls");

            // 一時フォルダを作成
            tempExtractPath = Path.Combine(Path.GetTempPath(), "AnkiPlus", $"{selectedNote.Name}_temp");

            LoadAnkplsFile();
        }

        private void LoadAnkplsFile()
        {
            try
            {
                // .ankpls がない場合、新規作成
                if (!File.Exists(ankplsFilePath))
                {
                    Directory.CreateDirectory(tempExtractPath);
                    Directory.CreateDirectory(Path.Combine(tempExtractPath, "img"));

                    // 空の cards.txt を作成
                    File.WriteAllText(Path.Combine(tempExtractPath, "cards.txt"), "");

                    // .ankpls を作成（ZIP圧縮）
                    using (FileStream zipToCreate = new FileStream(ankplsFilePath, FileMode.Create))
                    using (ZipArchive archive = new ZipArchive(zipToCreate, ZipArchiveMode.Create, true))
                    {
                        archive.CreateEntryFromFile(Path.Combine(tempExtractPath, "cards.txt"), "cards.txt");
                    }
                }
                else
                {
                    // 既存の .ankpls を展開
                    Directory.CreateDirectory(tempExtractPath);
                    ZipFile.ExtractToDirectory(ankplsFilePath, tempExtractPath, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling .ankpls file: {ex.Message}");
            }
        }
        // 問題数を読み込む
        private async Task<int> GetCardCountAsync(string cardsFilePath)
        {
            if (!File.Exists(cardsFilePath))
            {
                return 0;
            }

            var lines = await File.ReadAllLinesAsync(cardsFilePath);

            // 問題数は2行目に保存
            if (lines.Length >= 2 && int.TryParse(lines[1], out int count))
            {
                return count;
            }

            return 0;
        }

        // カードタイプが変更されたときの処理
        private void OnCardTypeChanged(object sender, EventArgs e)
        {
            string selectedType = CardTypePicker.SelectedItem as string;

            if (selectedType == "基本・穴埋め")
            {
                BasicCardLayout.IsVisible = true;
            }
            else
            {
                BasicCardLayout.IsVisible = false;
            }
            if (selectedType == "選択肢")
            {
                MultipleChoiceLayout.IsVisible = true;
            }
            else
            {
                MultipleChoiceLayout.IsVisible = false;
            }
            if (selectedType == "画像穴埋め")
            {
                ImageFillLayout.IsVisible = true;
            }
            else
            {
                ImageFillLayout.IsVisible = false;
            }

        }
        // テキスト変更時にプレビュー更新
        private void FrontOnTextChanged(object sender, TextChangedEventArgs e)
        {
            string htmlContent = ConvertMarkdownToHtml(e.NewTextValue);
            FrontPreviewWebView.Source = new HtmlWebViewSource { Html = htmlContent };
        }
        private void BackOnTextChanged(object sender, TextChangedEventArgs e)
        {
            string htmlContent = ConvertMarkdownToHtml(e.NewTextValue);
            BackPreviewWebView.Source = new HtmlWebViewSource { Html = htmlContent };
        }
        private void OnAddChoice(object sender, EventArgs e)
        {
            var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
            var checkBox = new CheckBox();
            var entry = new Entry { Placeholder = "選択肢を入力" };

            stack.Children.Add(checkBox);
            stack.Children.Add(entry);

            ChoicesContainer.Children.Add(stack);
        }
        // 画像選択と保存処理
        private async void OnSelectImage(object sender, EventArgs e)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "画像を選択",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                using (var stream = await result.OpenReadAsync())
                {
                    imageBitmap = SKBitmap.Decode(stream);
                }

                // 画像番号の読み込みと更新
                LoadImageCount();
                imageCount++;
                SaveImageCount();

                // 画像を img フォルダに保存
                var imgFolderPath = Path.Combine(tempExtractPath, "img");
                if (!Directory.Exists(imgFolderPath))
                {
                    Directory.CreateDirectory(imgFolderPath);
                }

                var imgFileName = $"img{imageCount}.png";
                var imgFilePath = Path.Combine(imgFolderPath, imgFileName);
                SaveBitmapToFile(imageBitmap, imgFilePath);

                imagePaths.Add(imgFilePath);
                CanvasView.InvalidateSurface();
            }
        }
        // 画像をファイルに保存
        private void SaveBitmapToFile(SKBitmap bitmap, string filePath)
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }
        }
        // 画像番号を読み込む
        private void LoadImageCount()
        {
            var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            if (File.Exists(cardsFilePath))
            {
                var lines = File.ReadAllLines(cardsFilePath).ToList();
                if (lines.Count > 0 && int.TryParse(lines[0], out int count))
                {
                    imageCount = count;
                }
            }
        }
        // 画像番号を保存する
        private void SaveImageCount()
        {
            var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            var lines = new List<string> { imageCount.ToString() };

            if (File.Exists(cardsFilePath))
            {
                lines.AddRange(File.ReadAllLines(cardsFilePath).Skip(1));
            }

            File.WriteAllLines(cardsFilePath, lines);
        }
        // タッチイベント処理（左クリックで四角形を追加、右クリックで削除）
        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        // 右クリックで削除メニュー表示
                        var clickedRect = selectionRects.FirstOrDefault(r => r.Contains(point));
                        if (clickedRect != SKRect.Empty)
                        {
                            ShowContextMenu(point, clickedRect);
                        }
                    }
                    else
                    {
                        // 左クリックで四角形を追加
                        isDragging = true;
                        startPoint = point;
                        endPoint = point;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (isDragging)
                    {
                        endPoint = point;
                    }
                    break;

                case SKTouchAction.Released:
                    if (isDragging)
                    {
                        var rect = SKRect.Create(
                            Math.Min(startPoint.X, endPoint.X),
                            Math.Min(startPoint.Y, endPoint.Y),
                            Math.Abs(endPoint.X - startPoint.X),
                            Math.Abs(endPoint.Y - startPoint.Y)
                        );

                        if (!rect.IsEmpty && rect.Width > 5 && rect.Height > 5)
                        {
                            selectionRects.Add(rect);
                        }
                    }

                    isDragging = false;
                    break;
            }

            // 再描画
            CanvasView.InvalidateSurface();
        }
        // 削除コンテキストメニューの表示
        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                selectionRects.Remove(rect);
                CanvasView.InvalidateSurface();
            }
        }
        // 描画処理
        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            // 画像を表示
            if (imageBitmap != null)
            {
                var rect = new SKRect(0, 0, e.Info.Width, e.Info.Height);
                canvas.DrawBitmap(imageBitmap, rect);
            }

            // 四角形を描画
            using (var paint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3
            })
            {
                foreach (var rect in selectionRects)
                {
                    canvas.DrawRect(rect, paint);
                    DrawResizeHandles(canvas, rect);
                }

                if (isDragging)
                {
                    var currentRect = SKRect.Create(
                        Math.Min(startPoint.X, endPoint.X),
                        Math.Min(startPoint.Y, endPoint.Y),
                        Math.Abs(endPoint.X - startPoint.X),
                        Math.Abs(endPoint.Y - startPoint.Y)
                    );

                    canvas.DrawRect(currentRect, paint);
                }
            }
        }
        private void DrawResizeHandles(SKCanvas canvas, SKRect rect)
        {
            using (var paint = new SKPaint
            {
                Color = SKColors.Blue,
                Style = SKPaintStyle.Fill
            })
            {
                canvas.DrawRect(new SKRect(rect.Left - HANDLE_SIZE / 2, rect.Top - HANDLE_SIZE / 2, rect.Left + HANDLE_SIZE / 2, rect.Top + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SKRect(rect.Right - HANDLE_SIZE / 2, rect.Top - HANDLE_SIZE / 2, rect.Right + HANDLE_SIZE / 2, rect.Top + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SKRect(rect.Left - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, rect.Left + HANDLE_SIZE / 2, rect.Bottom + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SKRect(rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, rect.Right + HANDLE_SIZE / 2, rect.Bottom + HANDLE_SIZE / 2), paint);
            }
        }
        // Markdown を HTML に変換
        string ConvertImageToBase64(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                return null;
            }

            byte[] imageBytes = File.ReadAllBytes(imagePath);
            string base64String = Convert.ToBase64String(imageBytes);
            string mimeType = Path.GetExtension(imagePath).ToLower() switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };

            return $"data:{mimeType};base64,{base64String}";
        }

        // Markdown を HTML に変換
        private string ConvertMarkdownToHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // 画像タグを最初に処理
            var matches = Regex.Matches(text, @"<<img(\d+)>>");
            Debug.WriteLine($"画像タグ数: {matches.Count}");
            foreach (Match match in matches)
            {
                int imgNum = int.Parse(match.Groups[1].Value);
                string imgPath = Path.Combine(tempExtractPath, "img", $"img{imgNum}.png");

                if (File.Exists(imgPath))
                {
                    string base64Image = ConvertImageToBase64(imgPath);
                    if (base64Image != null)
                    {
                        text = text.Replace(match.Value, $"<img src={base64Image} style=max-height:150px; />");
                    }
                    else
                    {
                        text = text.Replace(match.Value, $"[画像が見つかりません: img{imgNum}.png]");
                    }
                }
                else
                {
                    text = text.Replace(match.Value, $"[画像が見つかりません: img{imgNum}.png]");
                }
            }

            // 穴埋め変換 `<<blank|文字>>` → `(文字)`
            text = Regex.Replace(text, @"<<blank\|(.*?)>>", "( )");

            // HTML エスケープ
            text = HttpUtility.HtmlEncode(text);

            // 太字変換
            text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");

            // 色変換
            text = Regex.Replace(text, @"\{\{red\|(.*?)\}\}", "<span style='color:red;'>$1</span>");
            text = Regex.Replace(text, @"\{\{blue\|(.*?)\}\}", "<span style='color:blue;'>$1</span>");
            text = Regex.Replace(text, @"\{\{green\|(.*?)\}\}", "<span style='color:green;'>$1</span>");
            text = Regex.Replace(text, @"\{\{yellow\|(.*?)\}\}", "<span style='color:yellow;'>$1</span>");
            text = Regex.Replace(text, @"\{\{purple\|(.*?)\}\}", "<span style='color:purple;'>$1</span>");
            text = Regex.Replace(text, @"\{\{orange\|(.*?)\}\}", "<span style='color:orange;'>$1</span>");

            // 上付き・下付き変換
            text = Regex.Replace(text, @"\^\^(.*?)\^\^", "<sup>$1</sup>");
            text = Regex.Replace(text, @"~~(.*?)~~", "<sub>$1</sub>");

            // 必要な部分だけデコード処理
            text = Regex.Replace(text, @"&lt;img(.*?)&gt;", "<img$1>");

            // 改行を `<br>` に変換
            text = text.Replace(Environment.NewLine, "<br>").Replace("\n", "<br>");


            // HTML テンプレート
            string htmlTemplate = $@"
            <html>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body {{ font-size: 18px; font-family: Arial, sans-serif; line-height: 1.5; white-space: pre-line; }}
                    sup {{ vertical-align: super; font-size: smaller; }}
                    sub {{ vertical-align: sub; font-size: smaller; }}
                    img {{ display: block; margin: 10px 0; }}
                </style>
            </head>
            <body>{text}</body>
            </html>";

            return htmlTemplate;
        }
        // 画像を追加
        private async Task AddImage(Editor editor)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "画像を選択",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                // 画像番号を取得
                string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");

                // 画像番号を読み取る
                int currentImageIndex = 1;
                if (File.Exists(cardsFilePath))
                {
                    var lines = await File.ReadAllLinesAsync(cardsFilePath);
                    if (lines.Length > 0 && int.TryParse(lines[0], out int savedIndex))
                    {
                        currentImageIndex = savedIndex + 1;  // 画像番号をインクリメント
                    }
                }

                // 画像の保存先とファイル名
                string imageFolder = Path.Combine(tempExtractPath, "img");
                Directory.CreateDirectory(imageFolder);

                string newFileName = $"img{currentImageIndex}.png";
                string newFilePath = Path.Combine(imageFolder, newFileName);

                // 画像を保存
                using (var sourceStream = File.OpenRead(result.FullPath))
                using (var destinationStream = File.Create(newFilePath))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                imagePaths.Add(newFilePath);

                // `cards.txt` の先頭に画像番号を更新
                var newLines = new List<string> { currentImageIndex.ToString() };
                if (File.Exists(cardsFilePath))
                {
                    newLines.AddRange(File.ReadAllLines(cardsFilePath).Skip(1));
                }
                await File.WriteAllLinesAsync(cardsFilePath, newLines);

                // エディタに `<<img{n}>>` を挿入
                int cursorPosition = editor.CursorPosition;
                string text = editor.Text ?? "";
                string newText = text.Insert(cursorPosition, $"<<img{currentImageIndex}>>");
                editor.Text = newText;
                editor.CursorPosition = cursorPosition + $"<<img{currentImageIndex}>>".Length;
            }
        }

        private async void FrontOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(FrontTextEditor);
            FrontOnTextChanged(FrontTextEditor, new TextChangedEventArgs("", FrontTextEditor.Text));
        }

        private async void BackOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(BackTextEditor);
            BackOnTextChanged(BackTextEditor, new TextChangedEventArgs("", BackTextEditor.Text));
        }

        // カードを保存
        private async void OnSaveCardClicked(object sender, EventArgs e)
        {
            string cardType = CardTypePicker.SelectedItem as string;

            string frontText = FrontTextEditor.Text;
            string backText = BackTextEditor.Text;
            string choiceQuestion = ChoiceQuestion.Text;
            string choiceExplanation = ChoiceQuestionExplanation.Text;

            var choices = new List<string>();

            foreach (var stack in ChoicesContainer.Children.OfType<StackLayout>())
            {
                var entry = stack.Children.OfType<Entry>().FirstOrDefault();
                var checkBox = stack.Children.OfType<CheckBox>().FirstOrDefault();

                if (entry != null && !string.IsNullOrWhiteSpace(entry.Text))
                {
                    string isCorrect = checkBox?.IsChecked == true ? "正解" : "不正解";
                    choices.Add($"{entry.Text} ({isCorrect})");
                }
            }

            if (cardType == "基本・穴埋め" && string.IsNullOrWhiteSpace(frontText))
            {
                await DisplayAlert("エラー", "表面を入力してください", "OK");
                return;
            }

            // cards.txt のパス
            string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");

            // 問題数を取得
            int cardCount = await GetCardCountAsync(cardsFilePath);
            cardCount++;  // カウントをインクリメント

            // ファイルの内容を読み込む
            var lines = new List<string>();

            if (File.Exists(cardsFilePath))
            {
                lines = (await File.ReadAllLinesAsync(cardsFilePath)).ToList();

                // 問題数を2行目に更新
                if (lines.Count >= 2)
                {
                    lines[1] = cardCount.ToString();
                }
                else
                {
                    // 画像番号がない場合はデフォルト値で作成
                    lines.Insert(0, "0");  // 画像番号
                    lines.Insert(1, cardCount.ToString());  // 問題数
                }
            }
            else
            {
                // 初回作成
                lines.Add("0");               // 画像番号
                lines.Add("1");               // 問題数
            }

            // カード情報を追加
            lines.Add("---");
            lines.Add($"カードタイプ: {cardType}");

            if (cardType == "基本・穴埋め")
            {
                lines.Add($"表面: {frontText}");
                lines.Add($"裏面: {backText}");
            }
            else if (cardType == "選択肢")
            {
                lines.Add($"問題: {choiceQuestion}");
                lines.Add($"解説: {choiceExplanation}");

                foreach (var choice in choices)
                {
                    lines.Add($"選択肢: {choice}");
                }
            }
            else if (cardType == "画像穴埋め")
            {
                lines.Add($"画像: {selectedImagePath}");

                foreach (var rect in selectionRects)
                {
                    lines.Add($"範囲: {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
                }
            }

            // ファイルに保存
            await File.WriteAllLinesAsync(cardsFilePath, lines);

            // .ankpls を更新
            try
            {
                if (File.Exists(ankplsFilePath))
                {
                    File.Delete(ankplsFilePath);
                }

                ZipFile.CreateFromDirectory(tempExtractPath, ankplsFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating .ankpls file: {ex.Message}");
            }

            await DisplayAlert("成功", "カードを保存しました", "OK");
            await Navigation.PopAsync();
        }
    }
}
