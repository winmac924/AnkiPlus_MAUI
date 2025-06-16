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
using System.Text.Json;
using AnkiPlus_MAUI.Models;
using AnkiPlus_MAUI.Services;
using System.Reflection;

namespace AnkiPlus_MAUI
{
    public partial class Add : ContentPage
    {
        private string cardsFilePath;
        private string tempExtractPath;
        private List<string> cards = new List<string>();
        private string selectedImagePath = "";
        private List<SKRect> selectionRects = new List<SKRect>();
        private Dictionary<int, (int correct, int incorrect)> results = new Dictionary<int, (int, int)>();
        private string ankplsFilePath;
        private List<string> imagePaths = new List<string>();
        private int imageCount = 0;
        private SKBitmap imageBitmap;         // 画像を表示するためのビットマップ
        private SKPoint startPoint, endPoint;
        private bool isDragging = false;
        private const float HANDLE_SIZE = 15;
        private bool removeNumbers = false;  // 番号削除のフラグ
        private string editCardId = null;    // 編集対象のカードID
        private bool isDirty = false;
        private System.Timers.Timer autoSaveTimer;
        private Editor lastFocusedEditor = null;  // 最後にフォーカスされたエディター

        // プレビュー更新のデバウンス用
        private System.Timers.Timer frontPreviewTimer;
        private System.Timers.Timer backPreviewTimer;
        
        // フラッシュ防止用
        private bool frontPreviewReady = false;
        private bool backPreviewReady = false;

        public Add(string cardsPath, string tempPath, string cardId = null)
        {
            try
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタ開始");
                Debug.WriteLine($"cardsPath: {cardsPath}");
                Debug.WriteLine($"tempPath: {tempPath}");
                Debug.WriteLine($"cardId: {cardId}");

                InitializeComponent();
                Debug.WriteLine("InitializeComponent完了");

                tempExtractPath = tempPath;
                cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                Debug.WriteLine($"cardsFilePath: {cardsFilePath}");

                LoadCards();
                Debug.WriteLine("LoadCards完了");

                CardTypePicker.SelectedIndex = 0; // 初期値を「基本」に設定
                Debug.WriteLine("CardTypePicker初期化完了");

                // ノートの保存フォルダを設定
                ankplsFilePath = cardsPath;
                Debug.WriteLine($"ankplsFilePath: {ankplsFilePath}");

                // カードIDが指定されている場合は、そのカードの情報を読み込む
                if (!string.IsNullOrEmpty(cardId))
                {
                    editCardId = cardId;
                    LoadCardData(cardId);
                }

                // 自動保存タイマーを初期化
                InitializeAutoSaveTimer();
                Debug.WriteLine("自動保存タイマー初期化完了");

                // エディターのフォーカスイベントを設定
                FrontTextEditor.Focused += (s, e) => lastFocusedEditor = FrontTextEditor;
                BackTextEditor.Focused += (s, e) => lastFocusedEditor = BackTextEditor;
                
                // 選択肢カードのエディターにもフォーカスイベントを設定
                ChoiceQuestion.Focused += (s, e) => lastFocusedEditor = ChoiceQuestion;
                ChoiceQuestionExplanation.Focused += (s, e) => lastFocusedEditor = ChoiceQuestionExplanation;
                
                // 初期状態では表面エディターをデフォルトに設定
                lastFocusedEditor = FrontTextEditor;

                // WebViewの背景色を設定（ダークモード対応）
                SetWebViewBackgroundColor(FrontPreviewWebView);
                SetWebViewBackgroundColor(BackPreviewWebView);
                
                // フラッシュ防止のためWebViewを初期状態で透明化（スペースは保持）
                FrontPreviewWebView.Opacity = 0;
                BackPreviewWebView.Opacity = 0;
                
                // WebViewのNavigatedイベントでフラッシュ防止
                FrontPreviewWebView.Navigated += OnFrontPreviewNavigated;
                BackPreviewWebView.Navigated += OnBackPreviewNavigated;

                Debug.WriteLine("Add.xaml.cs コンストラクタ完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private void LoadCardData(string cardId)
        {
            try
            {
                Debug.WriteLine($"LoadCardData開始 - cardId: {cardId}");
                var jsonPath = Path.Combine(tempExtractPath, "cards", $"{cardId}.json");
                
                if (File.Exists(jsonPath))
                {
                    var jsonContent = File.ReadAllText(jsonPath);
                    var cardData = JsonSerializer.Deserialize<Models.CardData>(jsonContent);

                    // カードタイプを設定
                    int typeIndex = CardTypePicker.Items.IndexOf(cardData.type);
                    if (typeIndex >= 0)
                    {
                        CardTypePicker.SelectedIndex = typeIndex;
                    }

                    // カードの内容を設定
                    if (cardData.type == "選択肢")
                    {
                        ChoiceQuestion.Text = cardData.question;
                        ChoiceQuestionExplanation.Text = cardData.explanation;
                        
                        // 選択肢を設定
                        if (cardData.choices != null)
                        {
                            foreach (var choice in cardData.choices)
                            {
                                var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
                                var checkBox = new CheckBox { IsChecked = choice.isCorrect };
                                var editor = new Editor
                                {
                                    Text = choice.text,
                                    HeightRequest = 40,
                                    AutoSize = EditorAutoSizeOption.TextChanges
                                };

                                stack.Children.Add(checkBox);
                                stack.Children.Add(editor);
                                ChoicesContainer.Children.Add(stack);
                            }
                        }
                    }
                    else
                    {
                        FrontTextEditor.Text = cardData.front;
                        BackTextEditor.Text = cardData.back;
                    }

                    // 画像穴埋めの場合、選択範囲を設定
                    if (cardData.type == "画像穴埋め" && cardData.selectionRects != null)
                    {
                        selectionRects.Clear();
                        foreach (var rect in cardData.selectionRects)
                        {
                            selectionRects.Add(new SKRect(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height));
                        }
                    }
                }
                Debug.WriteLine("LoadCardData完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCardDataでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private class CardData
        {
            public string type { get; set; }
            public string front { get; set; }
            public string back { get; set; }
            public string question { get; set; }
            public string explanation { get; set; }
            public List<ChoiceData> choices { get; set; }
            public List<SelectionRect> selectionRects { get; set; }
        }

        private class ChoiceData
        {
            public bool isCorrect { get; set; }
            public string text { get; set; }
        }

        private class SelectionRect
        {
            public float x { get; set; }
            public float y { get; set; }
            public float width { get; set; }
            public float height { get; set; }
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
                // 選択肢コンテナをクリア
                ChoicesContainer.Children.Clear();
                // 最初の選択肢を追加
                OnAddChoice(sender, e);
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
            UpdateFrontPreviewWithDebounce();
        }
        private void BackOnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBackPreviewWithDebounce();
        }
        
        /// <summary>
        /// 表面プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateFrontPreviewWithDebounce()
        {
            if (FrontTextEditor == null || FrontPreviewWebView == null) return;
            
            // 既存のタイマーを停止
            frontPreviewTimer?.Stop();
            frontPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            frontPreviewTimer = new System.Timers.Timer(500);
            frontPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var markdown = FrontTextEditor.Text ?? "";
                        var html = ConvertMarkdownToHtml(markdown);
                        
                        // フラッシュ防止: 更新前に透明化（スペースは保持）
                        await FrontPreviewWebView.FadeTo(0, 50); // 50ms で透明化
                        
                        // 背景色を再設定してからHTMLを更新
                        SetWebViewBackgroundColor(FrontPreviewWebView);
                        FrontPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                        frontPreviewReady = false; // ナビゲーション完了を待つ
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"表面プレビュー更新エラー: {ex.Message}");
                    }
                });
                frontPreviewTimer?.Stop();
                frontPreviewTimer?.Dispose();
                frontPreviewTimer = null;
            };
            frontPreviewTimer.AutoReset = false;
            frontPreviewTimer.Start();
        }

        /// <summary>
        /// 裏面プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateBackPreviewWithDebounce()
        {
            if (BackTextEditor == null || BackPreviewWebView == null) return;
            
            // 既存のタイマーを停止
            backPreviewTimer?.Stop();
            backPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            backPreviewTimer = new System.Timers.Timer(500);
            backPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var markdown = BackTextEditor.Text ?? "";
                        var html = ConvertMarkdownToHtml(markdown);
                        
                        // フラッシュ防止: 更新前に透明化（スペースは保持）
                        await BackPreviewWebView.FadeTo(0, 50); // 50ms で透明化
                        
                        // 背景色を再設定してからHTMLを更新
                        SetWebViewBackgroundColor(BackPreviewWebView);
                        BackPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                        backPreviewReady = false; // ナビゲーション完了を待つ
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"裏面プレビュー更新エラー: {ex.Message}");
                    }
                });
                backPreviewTimer?.Stop();
                backPreviewTimer?.Dispose();
                backPreviewTimer = null;
            };
            backPreviewTimer.AutoReset = false;
            backPreviewTimer.Start();
        }

        /// <summary>
        /// WebViewの背景色を設定（ダークモード対応）
        /// </summary>
        private void SetWebViewBackgroundColor(WebView webView)
        {
            if (webView == null) return;
            
            try
            {
                var isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;
                var backgroundColor = isDarkMode ? Color.FromRgb(31, 31, 31) : Colors.White;
                webView.BackgroundColor = backgroundColor;
                
                // プラットフォーム固有の設定を追加
                try
                {
#if ANDROID
                    if (webView.Handler?.PlatformView is Android.Webkit.WebView androidWebView)
                    {
                        var bgColor = isDarkMode ? Android.Graphics.Color.Rgb(31, 31, 31) : Android.Graphics.Color.White;
                        androidWebView.SetBackgroundColor(bgColor);
                    }
#endif

#if IOS
                    if (webView.Handler?.PlatformView is WebKit.WKWebView wkWebView)
                    {
                        wkWebView.BackgroundColor = isDarkMode ? UIKit.UIColor.FromRGB(31, 31, 31) : UIKit.UIColor.White;
                        wkWebView.ScrollView.BackgroundColor = isDarkMode ? UIKit.UIColor.FromRGB(31, 31, 31) : UIKit.UIColor.White;
                    }
#endif

#if WINDOWS
                    // Windows WebView2の場合は基本的なWebView設定のみ
                    // より詳細な設定が必要な場合は将来的に追加
#endif
                }
                catch (Exception platformEx)
                {
                    Debug.WriteLine($"プラットフォーム固有設定エラー: {platformEx.Message}");
                }
                
                Debug.WriteLine($"WebView背景色設定: {(isDarkMode ? "ダーク" : "ライト")}モード");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView背景色設定エラー: {ex.Message}");
            }
        }
        private void OnAddChoice(object sender, EventArgs e)
        {
            var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
            var checkBox = new CheckBox();
            var editor = new Editor
            {
                Placeholder = "選択肢を入力（改行で区切って複数入力可能）",
                HeightRequest = 20,
                AutoSize = EditorAutoSizeOption.TextChanges
            };
            editor.TextChanged += OnChoiceTextChanged;
            editor.Focused += (s, e) => lastFocusedEditor = editor;  // フォーカスイベントを追加

            stack.Children.Add(checkBox);
            stack.Children.Add(editor);

            ChoicesContainer.Children.Add(stack);
        }

        // 番号削除トグルの処理
        private void OnRemoveNumbersToggled(object sender, ToggledEventArgs e)
        {
            removeNumbers = e.Value;

            // 選択肢コンテナが空でないことを確認
            if (ChoicesContainer.Children.Count > 0)
            {
                // 最初のEditorを取得
                var editor = ChoicesContainer.Children.OfType<StackLayout>()
                    .SelectMany(s => s.Children.OfType<Editor>())
                    .FirstOrDefault();

                if (editor != null)
                {
                    OnChoiceTextChanged(editor, new TextChangedEventArgs("", editor.Text));
                }
            }
        }

        // 選択肢のテキストが変更されたときの処理
        private void OnChoiceTextChanged(object sender, TextChangedEventArgs e)
        {
            var editor = sender as Editor;
            if (editor == null)
            {
                Debug.WriteLine("OnChoiceTextChanged: Editor is null");
                return;
            }

            Debug.WriteLine($"OnChoiceTextChanged: New text value: '{e.NewTextValue}'");
            Debug.WriteLine($"OnChoiceTextChanged: Contains \\n: {e.NewTextValue.Contains("\n")}");
            Debug.WriteLine($"OnChoiceTextChanged: Contains \\r: {e.NewTextValue.Contains("\r")}");
            Debug.WriteLine($"OnChoiceTextChanged: Contains \\r\\n: {e.NewTextValue.Contains("\r\n")}");

            // 改行が含まれている場合
            if (e.NewTextValue.Contains("\n") || e.NewTextValue.Contains("\r"))
            {
                Debug.WriteLine("OnChoiceTextChanged: Processing newlines");

                // 改行で分割し、空の行を除外
                var choices = e.NewTextValue
                    .Replace("\r\n", "\n")  // まず \r\n を \n に統一
                    .Replace("\r", "\n")    // 残りの \r を \n に変換
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)  // \n で分割
                    .Select(c => c.Trim())  // 各行の前後の空白を削除
                    .Where(c => !string.IsNullOrWhiteSpace(c))  // 空の行を除外
                    .Select(c => removeNumbers ? Regex.Replace(c, @"^\d+\.\s*", "") : c)  // 番号を削除（オプション）
                    .ToList();

                Debug.WriteLine($"OnChoiceTextChanged: Split into {choices.Count} choices");
                for (int i = 0; i < choices.Count; i++)
                {
                    Debug.WriteLine($"Choice {i + 1}: '{choices[i]}'");
                }

                if (choices.Count > 0)
                {
                    Debug.WriteLine("OnChoiceTextChanged: Clearing choices container");
                    // 選択肢コンテナをクリア
                    ChoicesContainer.Children.Clear();

                    // 各選択肢に対して新しいエントリを作成
                    foreach (var choice in choices)
                    {
                        var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
                        var checkBox = new CheckBox();
                        var newEditor = new Editor
                        {
                            Text = choice,
                            HeightRequest = 40,
                            AutoSize = EditorAutoSizeOption.TextChanges
                        };
                        newEditor.Focused += (s, e) => lastFocusedEditor = newEditor;  // フォーカスイベントを追加

                        stack.Children.Add(checkBox);
                        stack.Children.Add(newEditor);

                        ChoicesContainer.Children.Add(stack);
                        Debug.WriteLine($"Added choice: '{choice}'");
                    }
                    Debug.WriteLine($"OnChoiceTextChanged: Total choices added: {ChoicesContainer.Children.Count}");
                }
                else
                {
                    Debug.WriteLine("OnChoiceTextChanged: No valid choices found after processing");
                }
            }
            else
            {
                Debug.WriteLine("OnChoiceTextChanged: No newlines found in text");
            }
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

                // iOS版に合わせて8桁_6桁の数字形式でIDを生成
                Random random = new Random();
                string imageId8 = random.Next(10000000, 99999999).ToString(); // 8桁の数字
                string imageId6 = random.Next(100000, 999999).ToString(); // 6桁の数字
                string imageId = $"{imageId8}_{imageId6}";

                // 画像を img フォルダに保存
                var imgFolderPath = Path.Combine(tempExtractPath, "img");
                if (!Directory.Exists(imgFolderPath))
                {
                    Directory.CreateDirectory(imgFolderPath);
                }

                var imgFileName = $"img_{imageId}.jpg";
                var imgFilePath = Path.Combine(imgFolderPath, imgFileName);
                SaveBitmapToFile(imageBitmap, imgFilePath);
                selectedImagePath = imgFileName;
                imagePaths.Add(imgFilePath);
                CanvasView.InvalidateSurface();
            }
        }
        // 画像をファイルに保存
        private void SaveBitmapToFile(SKBitmap bitmap, string filePath)
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 80))
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
                ".jpg" => "image/jpg",
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

            // 画像タグを最初に処理 - iOS版の形式に対応
            var matches = Regex.Matches(text, @"<<img_\d{8}_\d{6}\.jpg>>");
            Debug.WriteLine($"画像タグ数: {matches.Count}");
            foreach (Match match in matches)
            {
                string imgFileName = match.Value.Trim('<', '>');
                string imgPath = Path.Combine(tempExtractPath, "img", imgFileName);

                if (File.Exists(imgPath))
                {
                    string base64Image = ConvertImageToBase64(imgPath);
                    if (base64Image != null)
                    {
                        text = text.Replace(match.Value, $"<img src={base64Image} style=max-height:150px; />");
                    }
                    else
                    {
                        text = text.Replace(match.Value, $"[画像が見つかりません: {imgFileName}]");
                    }
                }
                else
                {
                    text = text.Replace(match.Value, $"[画像が見つかりません: {imgFileName}]");
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


            // ダークモード対応のスタイル
            var isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;
            var backgroundColor = isDarkMode ? "#1f1f1f" : "#ffffff";
            var textColor = isDarkMode ? "#ffffff" : "#000000";

            // HTML テンプレート
            string htmlTemplate = $@"
            <html style='background-color: {backgroundColor};'>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    html {{ 
                        background-color: {backgroundColor} !important; 
                    }}
                    body {{ 
                        font-size: 18px; 
                        font-family: Arial, sans-serif; 
                        line-height: 1.5; 
                        white-space: pre-line; 
                        background-color: {backgroundColor} !important; 
                        color: {textColor}; 
                        margin: 0; 
                        padding: 10px; 
                        transition: none;
                    }}
                    sup {{ vertical-align: super; font-size: smaller; }}
                    sub {{ vertical-align: sub; font-size: smaller; }}
                    img {{ display: block; margin: 10px 0; }}
                </style>
            </head>
            <body style='background-color: {backgroundColor};'>{text}</body>
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
                // iOS版に合わせて8桁_6桁の数字形式でIDを生成
                Random random = new Random();
                string imageId8 = random.Next(10000000, 99999999).ToString(); // 8桁の数字
                string imageId6 = random.Next(100000, 999999).ToString(); // 6桁の数字
                string imageId = $"{imageId8}_{imageId6}";
                
                string imageFolder = Path.Combine(tempExtractPath, "img");
                Directory.CreateDirectory(imageFolder);

                string newFileName = $"img_{imageId}.jpg";
                string newFilePath = Path.Combine(imageFolder, newFileName);

                // 画像を読み込んで圧縮して保存
                using (var sourceStream = await result.OpenReadAsync())
                {
                    using (var bitmap = SKBitmap.Decode(sourceStream))
                    {
                        using (var image = SKImage.FromBitmap(bitmap))
                        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 80)) // 品質を80%に設定
                        using (var fileStream = File.Create(newFilePath))
                {
                            data.SaveTo(fileStream);
                        }
                    }
                }

                // エディタに `<<img_{imageId}.jpg>>` を挿入
                int cursorPosition = editor.CursorPosition;
                string text = editor.Text ?? "";
                string newText = text.Insert(cursorPosition, $"<<img_{imageId}.jpg>>");
                editor.Text = newText;
                editor.CursorPosition = cursorPosition + $"<<img_{imageId}.jpg>>".Length;

                // プレビューを更新
                if (editor == FrontTextEditor)
                {
                    FrontOnTextChanged(editor, new TextChangedEventArgs("", editor.Text));
                }
                else if (editor == BackTextEditor)
                {
                    BackOnTextChanged(editor, new TextChangedEventArgs("", editor.Text));
                }
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

        // 装飾文字を挿入するヘルパーメソッド
        private async void InsertDecorationText(Editor editor, string prefix, string suffix = "")
        {
            if (editor == null) return;

            try
            {
                // エディターから直接選択されたテキストを取得を試みる
                string selectedText = GetSelectedTextFromEditor(editor);
                
                if (!string.IsNullOrEmpty(selectedText))
                {
                    // 選択されたテキストがある場合は装飾で囲む
                    string decoratedText = prefix + selectedText + suffix;
                    
                    // 現在のテキストとカーソル位置を取得
                    int start = GetSelectionStart(editor);
                    int length = GetSelectionLength(editor);
                    string text = editor.Text ?? "";
                    
                    if (start >= 0 && length > 0 && start + length <= text.Length)
                    {
                        // 選択範囲を装飾されたテキストに置換
                        string newText = text.Remove(start, length).Insert(start, decoratedText);
                        editor.Text = newText;
                        editor.CursorPosition = start + decoratedText.Length;
                        Debug.WriteLine($"選択されたテキスト '{selectedText}' を装飾しました: {decoratedText}");
                    }
                    else
                    {
                        // 選択範囲の取得に失敗した場合はカーソル位置に挿入
                            await InsertAtCursor(editor, decoratedText);
                        }
                    }
                    else
                    {
                        // 選択されたテキストがない場合はカーソル位置に装飾タグを挿入
                        string insertText = prefix + suffix;
                        await InsertAtCursor(editor, insertText, prefix.Length);
                }

                // プレビューを更新
                UpdatePreview(editor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"装飾テキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = prefix + suffix;
                await InsertAtCursor(editor, insertText, prefix.Length);
                UpdatePreview(editor);
            }
        }

        // エディターから選択されたテキストを取得
        private string GetSelectedTextFromEditor(Editor editor)
        {
            try
            {
                // SelectionStart と SelectionLength プロパティを試す
                var selectionStart = GetSelectionStart(editor);
                var selectionLength = GetSelectionLength(editor);
                
                if (selectionStart >= 0 && selectionLength > 0)
                {
                    string text = editor.Text ?? "";
                    if (selectionStart + selectionLength <= text.Length)
                    {
                        string selectedText = text.Substring(selectionStart, selectionLength);
                        Debug.WriteLine($"エディターから選択テキストを取得: '{selectedText}' (start: {selectionStart}, length: {selectionLength})");
                        return selectedText;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択テキストの取得に失敗: {ex.Message}");
                return null;
            }
        }

        // 選択開始位置を取得（リフレクションまたはプロパティアクセス）
        private int GetSelectionStart(Editor editor)
        {
            try
            {
                // まず直接プロパティアクセスを試す
                var type = editor.GetType();
                var property = type.GetProperty("SelectionStart");
                if (property != null)
                {
                    var value = property.GetValue(editor);
                    if (value is int startPos)
                    {
                        return startPos;
                    }
                }

                // プラットフォーム固有のハンドラーを使用してみる
                return GetSelectionStartFromHandler(editor);
            }
            catch
            {
                return editor.CursorPosition;
            }
        }

        // プラットフォーム固有のハンドラーから選択開始位置を取得
        private int GetSelectionStartFromHandler(Editor editor)
        {
            try
            {
                var handler = editor.Handler;
                if (handler != null)
                {
                    // Windowsの場合
#if WINDOWS
                    if (handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
                    {
                        return textBox.SelectionStart;
                    }
#endif

                    // Androidの場合
#if ANDROID
                    if (handler.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
                    {
                        return editText.SelectionStart;
                    }
#endif

                    // iOSの場合
#if IOS
                    if (handler.PlatformView is UIKit.UITextView textView)
                    {
                        var selectedRange = textView.SelectedRange;
                        return (int)selectedRange.Location;
                    }
#endif
                }
                return editor.CursorPosition;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プラットフォーム固有の選択開始位置取得に失敗: {ex.Message}");
                return editor.CursorPosition;
            }
        }

        // 選択範囲の長さを取得（リフレクションまたはプロパティアクセス）
        private int GetSelectionLength(Editor editor)
        {
            try
            {
                // まず直接プロパティアクセスを試す
                var type = editor.GetType();
                var property = type.GetProperty("SelectionLength");
                if (property != null)
                {
                    var value = property.GetValue(editor);
                    if (value is int length)
                    {
                        return length;
                    }
                }

                // プラットフォーム固有のハンドラーを使用してみる
                return GetSelectionLengthFromHandler(editor);
            }
            catch
            {
                return 0;
            }
        }

        // プラットフォーム固有のハンドラーから選択範囲を取得
        private int GetSelectionLengthFromHandler(Editor editor)
        {
            try
            {
                // ハンドラーからプラットフォーム固有の実装にアクセス
                var handler = editor.Handler;
                if (handler != null)
                {
                    // Windowsの場合
#if WINDOWS
                    if (handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
                    {
                        return textBox.SelectionLength;
                    }
#endif

                    // Androidの場合
#if ANDROID
                    if (handler.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
                    {
                        return editText.SelectionEnd - editText.SelectionStart;
                    }
#endif

                    // iOSの場合
#if IOS
                    if (handler.PlatformView is UIKit.UITextView textView)
                    {
                        var selectedRange = textView.SelectedRange;
                        return (int)selectedRange.Length;
                    }
#endif
                }
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プラットフォーム固有の選択範囲取得に失敗: {ex.Message}");
                return 0;
            }
        }

        // クリップボードから選択されたテキストを取得を試みる
        private async Task<string> TryGetSelectedText()
        {
            try
            {
                // ユーザーに選択されたテキストをコピーしてもらう必要がある
                // より良い方法として、Ctrl+Cを自動的に送信することもできるが、
                // 現在のクリップボードの内容を確認してみる
                if (Clipboard.HasText)
                {
                    string clipboardText = await Clipboard.GetTextAsync();
                    // クリップボードのテキストが短い場合（選択されたテキストの可能性が高い）
                    if (!string.IsNullOrEmpty(clipboardText) && clipboardText.Length < 1000)
                    {
                        return clipboardText;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        // カーソル位置にテキストを挿入
        private async Task InsertAtCursor(Editor editor, string text, int cursorOffset = 0)
        {
            int start = editor.CursorPosition;
            string currentText = editor.Text ?? "";
            string newText = currentText.Insert(start, text);
            editor.Text = newText;
            editor.CursorPosition = start + cursorOffset;
        }

        // プレビューを更新（即座に実行）
        private void UpdatePreview(Editor editor)
        {
            if (editor == FrontTextEditor)
            {
                try
                {
                    var markdown = editor.Text ?? "";
                    var html = ConvertMarkdownToHtml(markdown);
                    
                    // フラッシュ防止: 更新前に非表示
                    if (FrontPreviewWebView.IsVisible)
                    {
                        FrontPreviewWebView.FadeTo(0, 50); // 50ms で非表示（awaitしない）
                        FrontPreviewWebView.IsVisible = false;
                    }
                    
                    // 背景色を再設定してからHTMLを更新
                    SetWebViewBackgroundColor(FrontPreviewWebView);
                    FrontPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                    frontPreviewReady = false; // ナビゲーション完了を待つ
                Debug.WriteLine("表面のプレビューを更新しました");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"表面プレビュー更新エラー: {ex.Message}");
                }
            }
            else if (editor == BackTextEditor)
            {
                try
                {
                    var markdown = editor.Text ?? "";
                    var html = ConvertMarkdownToHtml(markdown);
                    
                    // フラッシュ防止: 更新前に非表示
                    if (BackPreviewWebView.IsVisible)
                    {
                        BackPreviewWebView.FadeTo(0, 50); // 50ms で非表示（awaitしない）
                        BackPreviewWebView.IsVisible = false;
                    }
                    
                    // 背景色を再設定してからHTMLを更新
                    SetWebViewBackgroundColor(BackPreviewWebView);
                    BackPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                    backPreviewReady = false; // ナビゲーション完了を待つ
                Debug.WriteLine("裏面のプレビューを更新しました");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"裏面プレビュー更新エラー: {ex.Message}");
                }
            }
            else if (editor == ChoiceQuestion)
            {
                Debug.WriteLine("選択肢問題のエディターが更新されました");
                // 選択肢問題にはプレビューがないため、ログのみ
            }
            else if (editor == ChoiceQuestionExplanation)
            {
                Debug.WriteLine("選択肢解説のエディターが更新されました");
                // 選択肢解説にはプレビューがないため、ログのみ
            }
            else
            {
                // 動的に追加された選択肢のエディターの可能性
                Debug.WriteLine("その他のエディターが更新されました");
            }
        }

        // 太字ボタンのクリックイベント
        private void OnBoldClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "**", "**");
            }
        }

        // 赤色ボタンのクリックイベント
        private void OnRedColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{red|", "}}");
            }
        }

        // 青色ボタンのクリックイベント
        private void OnBlueColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{blue|", "}}");
            }
        }

        // 緑色ボタンのクリックイベント
        private void OnGreenColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{green|", "}}");
            }
        }

        // 黄色ボタンのクリックイベント
        private void OnYellowColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{yellow|", "}}");
            }
        }

        // 紫色ボタンのクリックイベント
        private void OnPurpleColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{purple|", "}}");
            }
        }

        // オレンジ色ボタンのクリックイベント
        private void OnOrangeColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{orange|", "}}");
            }
        }

        // 上付き文字ボタンのクリックイベント
        private void OnSuperscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "^^", "^^");
            }
        }

        // 下付き文字ボタンのクリックイベント
        private void OnSubscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "~~", "~~");
            }
        }

        // 穴埋めボタンのクリックイベント
        private void OnBlankClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            
            // 基本カードの表面エディターでのみ穴埋めを挿入
            if (editor != null && editor == FrontTextEditor)
            {
                Debug.WriteLine($"穴埋めを挿入: {editor.AutomationId}");
                InsertBlankText(editor);
            }
            else
            {
                Debug.WriteLine("穴埋めは基本カードの表面でのみ使用できます");
                // 必要に応じてユーザーに通知
                // await DisplayAlert("情報", "穴埋めは基本カードの表面でのみ使用できます", "OK");
            }
        }

        /// <summary>
        /// 穴埋めテキストを挿入（かっこがある場合は削除）
        /// </summary>
        private async void InsertBlankText(Editor editor)
        {
            if (editor == null) return;

            try
            {
                // エディターから直接選択されたテキストを取得を試みる
                string selectedText = GetSelectedTextFromEditor(editor);
                
                if (!string.IsNullOrEmpty(selectedText))
                {
                    // 選択されたテキストの最初と最後のかっこを削除
                    string cleanedText = RemoveSurroundingParentheses(selectedText);
                    
                    // 穴埋めタグで囲む
                    string decoratedText = "<<blank|" + cleanedText + ">>";
                    
                    // 現在のテキストとカーソル位置を取得
                    int start = GetSelectionStart(editor);
                    int length = GetSelectionLength(editor);
                    string text = editor.Text ?? "";
                    
                    if (start >= 0 && length > 0 && start + length <= text.Length)
                    {
                        // 選択範囲を装飾されたテキストに置換
                        string newText = text.Remove(start, length).Insert(start, decoratedText);
                        editor.Text = newText;
                        editor.CursorPosition = start + decoratedText.Length;
                        Debug.WriteLine($"選択されたテキスト '{selectedText}' を穴埋めに変換しました: {decoratedText}");
                    }
                    else
                    {
                        // 選択範囲の取得に失敗した場合はカーソル位置に挿入
                        await InsertAtCursor(editor, decoratedText);
                    }
                }
                else
                {
                    // 選択されたテキストがない場合はカーソル位置に穴埋めタグを挿入
                    string insertText = "<<blank|>>";
                    await InsertAtCursor(editor, insertText, 8); // "<<blank|" の後にカーソルを配置
                }

                // プレビューを更新
                UpdatePreview(editor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"穴埋めテキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = "<<blank|>>";
                await InsertAtCursor(editor, insertText, 8);
                UpdatePreview(editor);
            }
        }

        /// <summary>
        /// 文字列の最初と最後のかっこを削除
        /// </summary>
        private string RemoveSurroundingParentheses(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // 最初と最後が対応するかっこの場合は削除
            var parenthesesPairs = new (char open, char close)[]
            {
                ('(', ')'),
                ('（', '）'),
                ('[', ']'),
                ('［', '］'),
                ('{', '}'),
                ('｛', '｝')
            };
            
            foreach (var (open, close) in parenthesesPairs)
            {
                if (text.Length >= 2 && text[0] == open && text[text.Length - 1] == close)
                {
                    string result = text.Substring(1, text.Length - 2);
                    Debug.WriteLine($"かっこを削除: '{text}' → '{result}'");
                    return result;
                }
            }
            
            return text;
        }

        // 現在アクティブなエディターを取得
        private Editor GetCurrentEditor()
        {
            Debug.WriteLine($"GetCurrentEditor開始");
            Debug.WriteLine($"FrontTextEditor.IsFocused: {FrontTextEditor.IsFocused}");
            Debug.WriteLine($"BackTextEditor.IsFocused: {BackTextEditor.IsFocused}");
            Debug.WriteLine($"ChoiceQuestion.IsFocused: {ChoiceQuestion.IsFocused}");
            Debug.WriteLine($"ChoiceQuestionExplanation.IsFocused: {ChoiceQuestionExplanation.IsFocused}");
            Debug.WriteLine($"lastFocusedEditor: {lastFocusedEditor?.AutomationId}");

            // まず現在フォーカスされているエディターを確認
            if (FrontTextEditor.IsFocused)
            {
                Debug.WriteLine("FrontTextEditorがフォーカス中");
                lastFocusedEditor = FrontTextEditor;
                return FrontTextEditor;
            }
            if (BackTextEditor.IsFocused)
            {
                Debug.WriteLine("BackTextEditorがフォーカス中");
                lastFocusedEditor = BackTextEditor;
                return BackTextEditor;
            }
            if (ChoiceQuestion.IsFocused)
            {
                Debug.WriteLine("ChoiceQuestionがフォーカス中");
                lastFocusedEditor = ChoiceQuestion;
                return ChoiceQuestion;
            }
            if (ChoiceQuestionExplanation.IsFocused)
            {
                Debug.WriteLine("ChoiceQuestionExplanationがフォーカス中");
                lastFocusedEditor = ChoiceQuestionExplanation;
                return ChoiceQuestionExplanation;
            }

            // 動的に作成された選択肢エディターを確認
            foreach (var stack in ChoicesContainer.Children.OfType<StackLayout>())
            {
                var editor = stack.Children.OfType<Editor>().FirstOrDefault();
                if (editor != null && editor.IsFocused)
                {
                    Debug.WriteLine("選択肢のエディターがフォーカス中");
                    lastFocusedEditor = editor;
                    return editor;
                }
            }

            // フォーカスされているエディターがない場合、最後にフォーカスされたエディターを使用
            if (lastFocusedEditor != null)
            {
                Debug.WriteLine($"最後にフォーカスされたエディターを使用: {lastFocusedEditor.AutomationId}");
                return lastFocusedEditor;
            }

            // デフォルトは表面
            Debug.WriteLine("デフォルトでFrontTextEditorを使用");
            lastFocusedEditor = FrontTextEditor;
            return FrontTextEditor;
        }

        // カードを保存
        private async Task OnSaveCardClicked(object sender, EventArgs e)
        {
            string cardType = CardTypePicker.SelectedItem as string;

            string frontText = FrontTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
            string backText = BackTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
            string choiceQuestion = ChoiceQuestion.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
            string choiceExplanation = ChoiceQuestionExplanation.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";

            var choices = new List<object>();

            foreach (var stack in ChoicesContainer.Children.OfType<StackLayout>())
            {
                var entry = stack.Children.OfType<Editor>().FirstOrDefault();
                var checkBox = stack.Children.OfType<CheckBox>().FirstOrDefault();

                if (entry != null && !string.IsNullOrWhiteSpace(entry.Text))
                {
                    // 番号を削除（1. や 2. などの形式）
                    string cleanText = Regex.Replace(entry.Text, @"^\d+\.\s*", "").Trim()
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n");
                    bool isCorrect = checkBox?.IsChecked == true;
                    choices.Add(new
                    {
                        isCorrect = isCorrect,
                        text = cleanText
                    });
                }
            }

            if (cardType == "基本・穴埋め" && string.IsNullOrWhiteSpace(frontText))
            {
                await DisplayAlert("エラー", "表面を入力してください", "OK");
                return;
            }

            if (cardType == "選択肢" && choices.Count == 0)
            {
                await DisplayAlert("エラー", "少なくとも1つの選択肢を入力してください", "OK");
                return;
            }

            // UUIDを生成
            string cardId = Guid.NewGuid().ToString();

            // cards.txt のパス
            string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            string cardsDirPath = Path.Combine(tempExtractPath, "cards");

            // cardsディレクトリが存在しない場合は作成
            if (!Directory.Exists(cardsDirPath))
            {
                Directory.CreateDirectory(cardsDirPath);
            }

            // 現在の日時を取得
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // cards.txt の内容を読み込む
            var lines = new List<string>();
            if (File.Exists(cardsFilePath))
            {
                lines = (await File.ReadAllLinesAsync(cardsFilePath)).ToList();
                Debug.WriteLine($"既存のcards.txtを読み込み: {cardsFilePath}");
                Debug.WriteLine($"読み込んだ行数: {lines.Count}");
                foreach (var line in lines)
                {
                    Debug.WriteLine($"読み込んだ行: {line}");
                }
            }
            else
            {
                Debug.WriteLine($"cards.txtが存在しません: {cardsFilePath}");
                // 新規作成時は空のリストを初期化
                lines = new List<string> { "0" };
            }

            // カードIDと日付を追加
            string newCardLine = $"{cardId},{currentDate}";
            lines.Add(newCardLine);
            Debug.WriteLine($"新しいカードを追加: {newCardLine}");

            // カード数を更新（1行目にカードの総数を設定）
            int cardCount = lines.Count - 1; // 1行目を除いた行数がカード数
            lines[0] = cardCount.ToString();
            Debug.WriteLine($"カード数を更新: {cardCount}");

            // カード情報をJSONとして保存
            var cardData = new Models.CardData
            {
                id = cardId,
                type = cardType,
                front = frontText,
                back = backText,
                question = choiceQuestion,
                explanation = choiceExplanation,
                choices = choices.Select(c => new Models.ChoiceData
                {
                    isCorrect = ((dynamic)c).isCorrect,
                    text = ((dynamic)c).text
                }).ToList(),
                selectionRects = selectionRects.Select(r => new Models.SelectionRect
                {
                    x = r.Left,
                    y = r.Top,
                    width = r.Width,
                    height = r.Height
                }).ToList()
            };

            // JSONファイルとして保存
            string jsonPath = Path.Combine(cardsDirPath, $"{cardId}.json");
            string jsonContent = JsonSerializer.Serialize(cardData);
            await File.WriteAllTextAsync(jsonPath, jsonContent);
            Debug.WriteLine($"カード情報をJSONとして保存: {jsonPath}");

            // cards.txtを更新
            await File.WriteAllLinesAsync(cardsFilePath, lines);
            Debug.WriteLine($"cards.txtを更新: {cardsFilePath}");

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

            // エディタの内容をリセット
            FrontTextEditor.Text = "";
            BackTextEditor.Text = "";
            ChoiceQuestion.Text = "";
            ChoiceQuestionExplanation.Text = "";
            ChoicesContainer.Children.Clear();
            selectedImagePath = "";
            selectionRects.Clear();
            imageBitmap = null;
            CanvasView.InvalidateSurface();

            // プレビューの更新
            FrontPreviewWebView.Source = new HtmlWebViewSource { Html = "" };
            BackPreviewWebView.Source = new HtmlWebViewSource { Html = "" };

            await DisplayAlert("成功", "カードを保存しました", "OK");
        }

        // ボタンクリックイベントハンドラ
        private async void OnSaveCardButtonClicked(object sender, EventArgs e)
        {
            await OnSaveCardClicked(sender, e);
        }

        private void LoadCards()
        {
            try
            {
                Debug.WriteLine("LoadCards開始");
                var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                Debug.WriteLine($"読み込むcards.txtのパス: {cardsFilePath}");

                if (File.Exists(cardsFilePath))
                {
                    Debug.WriteLine("cards.txtが存在します");
                    var lines = File.ReadAllLines(cardsFilePath).ToList();
                    Debug.WriteLine($"読み込んだ行数: {lines.Count}");

                    if (lines.Count > 0 && int.TryParse(lines[0], out int count))
                    {
                        imageCount = count;
                        Debug.WriteLine($"imageCountを設定: {imageCount}");
                    }
                    else
                    {
                        Debug.WriteLine("imageCountの解析に失敗しました");
                    }
                }
                else
                {
                    Debug.WriteLine("cards.txtが存在しません");
                }
                Debug.WriteLine("LoadCards完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCardsでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeAutoSaveTimer()
        {
            autoSaveTimer = new System.Timers.Timer(10000); // 10秒ごとに保存
            autoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
            autoSaveTimer.AutoReset = true;
            autoSaveTimer.Enabled = true;
        }

        private async void AutoSaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (isDirty)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await OnSaveCardClicked(null, null);
                    });
                    isDirty = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"自動保存中にエラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 画像と選択範囲を消去
        /// </summary>
        private async void OnClearImage(object sender, EventArgs e)
        {
            try
            {
                // 確認アラート
                bool result = await DisplayAlert("確認", "現在の画像と選択範囲を消去しますか？", "はい", "いいえ");
                if (!result) return;

                // 画像とデータをクリア
                imageBitmap?.Dispose();
                imageBitmap = null;
                selectedImagePath = "";
                selectionRects.Clear();
                isDragging = false;

                // キャンバスを再描画
                CanvasView?.InvalidateSurface();

                await DisplayAlert("完了", "画像を消去しました", "OK");
                
                Debug.WriteLine("画像穴埋めの画像と選択範囲を消去");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像消去エラー: {ex.Message}");
                await DisplayAlert("エラー", "画像の消去中にエラーが発生しました", "OK");
            }
        }

    /// <summary>
    /// 表面プレビューナビゲーション完了イベント
    /// </summary>
    private async void OnFrontPreviewNavigated(object sender, WebNavigatedEventArgs e)
    {
        try
        {
            if (e.Result == WebNavigationResult.Success && !frontPreviewReady)
            {
                frontPreviewReady = true;
                
                // 少し待ってからフェードイン（CSSの適用を確実にするため）
                await Task.Delay(100);
                
                if (FrontPreviewWebView != null)
                {
                    await FrontPreviewWebView.FadeTo(1, 150); // 150ms でフェードイン
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"表面プレビューナビゲーション完了エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 裏面プレビューナビゲーション完了イベント
    /// </summary>
    private async void OnBackPreviewNavigated(object sender, WebNavigatedEventArgs e)
    {
        try
        {
            if (e.Result == WebNavigationResult.Success && !backPreviewReady)
            {
                backPreviewReady = true;
                
                // 少し待ってからフェードイン（CSSの適用を確実にするため）
                await Task.Delay(100);
                
                if (BackPreviewWebView != null)
                {
                    await BackPreviewWebView.FadeTo(1, 150); // 150ms でフェードイン
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"裏面プレビューナビゲーション完了エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    ~Add()
    {
        try
        {
            // イベントハンドラーを解除
            if (FrontPreviewWebView != null)
            {
                FrontPreviewWebView.Navigated -= OnFrontPreviewNavigated;
            }
            
            if (BackPreviewWebView != null)
            {
                BackPreviewWebView.Navigated -= OnBackPreviewNavigated;
            }
            
            // タイマーを停止・解放
            frontPreviewTimer?.Stop();
            frontPreviewTimer?.Dispose();
            
            backPreviewTimer?.Stop();
            backPreviewTimer?.Dispose();
            
            autoSaveTimer?.Stop();
            autoSaveTimer?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Addリソース解放エラー: {ex.Message}");
        }
    }
    }
}