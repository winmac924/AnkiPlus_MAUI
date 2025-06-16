using SkiaSharp.Views.Maui;
using SkiaSharp;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

namespace AnkiPlus_MAUI
{
    public partial class Qa : ContentPage
    {
        private string cardsFilePath;
        private string tempExtractPath;
        private List<CardData> cards = new List<CardData>();
        private int currentIndex = 0;
        private int correctCount = 0;
        private int incorrectCount = 0;
        // クラスの先頭で変数を宣言
        private string selectedImagePath = "";
        private List<SKRect> selectionRects = new List<SKRect>();
        // 各問題ごとの正解・不正解回数を管理
        private Dictionary<int, CardResult> results = new Dictionary<int, CardResult>();
        private bool showAnswer = false;  // 解答表示フラグ
        private string frontText = "";

        // 新形式用のカードデータクラス
        private class CardData
        {
            public string id { get; set; }
            public string type { get; set; }
            public string front { get; set; }
            public string back { get; set; }
            public string question { get; set; }
            public string explanation { get; set; }
            public List<ChoiceItem> choices { get; set; }
            public List<SelectionRect> selectionRects { get; set; }
            public string imageFileName { get; set; } // 画像穴埋めカード用の画像ファイル名
        }
        private class ChoiceItem
        {
            public string text { get; set; }
            public bool isCorrect { get; set; }
        }
        private class SelectionRect
        {
            public float x { get; set; }
            public float y { get; set; }
            public float width { get; set; }
            public float height { get; set; }
        }

        private class CardResult
        {
            public bool WasCorrect { get; set; }  // 直前の正誤のみを保持
            public DateTime? NextReviewTime { get; set; }
            public int OriginalQuestionNumber { get; set; }  // 元の問題番号を保持
        }

        public Qa(string cardsPath, string tempPath)
        {
            InitializeComponent();
            // 一時フォルダ
            tempExtractPath = tempPath;
            cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            LoadCards();
            DisplayCard();
        }

        public Qa(List<string> cardsList)
        {
            InitializeComponent();
            // 一時フォルダを作成（結果保存用）
            tempExtractPath = Path.Combine(Path.GetTempPath(), "AnkiPlus_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempExtractPath);

            // 新形式ではこのコンストラクタは使わない想定ですが、空リストで初期化
            cards = new List<CardData>();
            Debug.WriteLine($"Loaded {cards.Count} cards");
            DisplayCard();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CanvasView.InvalidateSurface();
            LoadResultsFromFile();
        }
        // カードを読み込む
        private void LoadCards()
        {
            cards.Clear();
            string cardsDir = Path.Combine(tempExtractPath, "cards");
            if (!File.Exists(cardsFilePath) || !Directory.Exists(cardsDir)) return;

            var lines = File.ReadAllLines(cardsFilePath);
            foreach (var line in lines.Skip(1)) // 1行目はカード数
                        {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 1) continue;
                string uuid = parts[0];
                string jsonPath = Path.Combine(cardsDir, $"{uuid}.json");
                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var card = JsonSerializer.Deserialize<CardData>(json);
                    if (card != null) cards.Add(card);
                }
            }
        }
        // 結果ファイルを読み込む
        private void LoadResultsFromFile()
        {
            try
            {
                string resultsFilePath = Path.Combine(tempExtractPath, "results.txt");

                if (File.Exists(resultsFilePath))
                {
                    var lines = File.ReadAllLines(resultsFilePath);
                    // results.Clear();  // 既存の結果をクリアしない

                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            if (int.TryParse(parts[0].Trim(), out int questionNumber))
                            {
                                // 問題番号を0ベースのインデックスに変換
                                int questionIndex = questionNumber - 1;

                                // 表示されている問題のみ結果を読み込む
                                if (questionIndex < cards.Count)
                                {
                                    // 既存の結果を保持
                                    if (!results.ContainsKey(questionNumber))
                                    {
                                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                                    }

                                    // 基本情報の解析
                                    var basicInfo = parts[1].Trim();
                                    results[questionNumber].WasCorrect = basicInfo.Contains("正解");

                                    // 次回表示時間の解析
                                    if (parts.Length > 2 && DateTime.TryParse(parts[2], out DateTime nextReview))
                                    {
                                        results[questionNumber].NextReviewTime = nextReview;
                                    }

                                    Debug.WriteLine($"問題 {questionNumber} の結果を読み込み: {(results[questionNumber].WasCorrect ? "正解" : "不正解")}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"結果読み込み中にエラー: {ex.Message}");
            }
        }
        // 問題を表示
        private void DisplayCard()
        {
            try
            {
                if (cards == null || !cards.Any())
                {
                    Debug.WriteLine("No cards available");
                    return;
                }

                if (currentIndex >= cards.Count)
                {
                    Debug.WriteLine("すべての問題が出題されました");
                    // 完了メッセージを表示
                    DisplayAlert("完了", "すべての問題が出題されました。", "OK");
                    // 前のページに戻る
                    Navigation.PopAsync();
                    return;
                }

                var card = cards[currentIndex];
                Debug.WriteLine($"Current card id: {card.id}, type: {card.type}");

                // レイアウトの初期化
                BasicCardLayout.IsVisible = false;
                ChoiceCardLayout.IsVisible = false;
                ImageFillCardLayout.IsVisible = false;

                if (card.type.Contains("基本"))
                {
                    Debug.WriteLine("Displaying basic card");
                    DisplayBasicCard(card);
                }
                else if (card.type.Contains("選択肢"))
                {
                    Debug.WriteLine("Displaying choice card");
                    DisplayChoiceCard(card);
                }
                else if (card.type.Contains("画像穴埋め"))
                {
                    Debug.WriteLine("Displaying image fill card");
                    DisplayImageFillCard(card);
                }
                else
                {
                    Debug.WriteLine($"Unknown card type: {card.type}");
                    // 不明なカードタイプの場合は次のカードへ
                    currentIndex++;
                    DisplayCard();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DisplayCard: {ex}");
                DisplayAlert("Error", "Failed to display card", "OK");
            }
        }
        // 基本カードを解析
        private (string FrontText, string BackText) ParseBasicCard(List<string> lines)
        {
            var frontText = new StringBuilder();
            var backText = new StringBuilder();
            bool isFront = false;
            bool isBack = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("表面:"))
                {
                    isFront = true;
                    isBack = false;
                    frontText.AppendLine(line.Substring(3));
                }
                else if (line.StartsWith("裏面:"))
                {
                    isBack = true;
                    isFront = false;
                    backText.AppendLine(line.Substring(3));
                }
                else
                {
                    if (isFront)
                    {
                        frontText.AppendLine(line);
                    }
                    else if (isBack)
                    {
                        backText.AppendLine(line);
                    }
                }
            }

            return (frontText.ToString().Trim(), backText.ToString().Trim());
        }
        // 基本・穴埋めカード表示
        private void DisplayBasicCard(CardData card)
        {
            BasicCardLayout.IsVisible = true;
            this.frontText = card.front ?? "";
            string frontText = card.front ?? "";
            string backText = card.back ?? "";

            // 画像タグの処理
            var matches = Regex.Matches(frontText, @"<<img_.*?\.jpg>>");
            foreach (Match match in matches)
            {
                string imgFileName = match.Value.Trim('<', '>');
                string imgPath = Path.Combine(tempExtractPath, "img", imgFileName);
                if (File.Exists(imgPath))
                {
                    string base64Image = ConvertImageToBase64(imgPath);
                    if (base64Image != null)
                    {
                        frontText = frontText.Replace(match.Value, $"<img src={base64Image} style=max-height:150px; />");
                    }
                }
            }

            // 表面と裏面のプレビュー表示
            FrontPreviewWebView.Source = new HtmlWebViewSource
            {
                Html = ConvertMarkdownToHtml(frontText, showAnswer: false)
            };

            BackPreviewWebView.Source = new HtmlWebViewSource
            {
                Html = ConvertMarkdownToHtml(backText, showAnswer: false)
            };

            BackPreviewWebView.IsVisible = false;
        }
        // 選択肢カード表示
        private List<CheckBox> checkBoxes = new List<CheckBox>();  // チェックボックスを保持
        private List<bool> currentCorrectFlags = new List<bool>();  // 現在のカードの正誤情報

        private void DisplayChoiceCard(CardData card)
        {
            ChoiceCardLayout.IsVisible = true;

            var (question, explanation, choices, isCorrectFlags) = ParseChoiceCard(card);

            ChoiceQuestionWebView.Source = new HtmlWebViewSource
            {
                Html = ConvertMarkdownToHtml(question)
            };

            ChoiceContainer.Children.Clear();
            checkBoxes.Clear();

            // 選択肢をシャッフル
            var random = new Random();
            var shuffledIndices = Enumerable.Range(0, choices.Count).OrderBy(x => random.Next()).ToList();
            var shuffledChoices = shuffledIndices.Select(i => choices[i]).ToList();
            currentCorrectFlags = shuffledIndices.Select(i => isCorrectFlags[i]).ToList();  // シャッフルされた正誤フラグを保存

            for (int i = 0; i < shuffledChoices.Count; i++)
            {
                var choiceText = shuffledChoices[i];

                var choiceLayout = new HorizontalStackLayout
                {
                    Spacing = 10
                };

                // チェックボックス
                var checkBox = new CheckBox
                {
                    IsChecked = false
                };

                checkBoxes.Add(checkBox);

                // 選択肢のラベル（クリック可能なボタンとして実装）
                var choiceButton = new Button
                {
                    Text = choiceText,
                    VerticalOptions = LayoutOptions.Center,
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.Black,
                    FontSize = 16,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0)
                };

                // ボタンのクリックイベント
                int index = i; // クロージャのためにインデックスを保存
                choiceButton.Clicked += (s, e) =>
                {
                    checkBox.IsChecked = !checkBox.IsChecked;
                };

                // 正誤マークを表示するための Label
                var resultLabel = new Label
                {
                    Text = "",
                    VerticalOptions = LayoutOptions.Center,
                    IsVisible = false
                };

                // チェックボックスとボタンを追加
                choiceLayout.Children.Add(checkBox);
                choiceLayout.Children.Add(choiceButton);
                choiceLayout.Children.Add(resultLabel);

                ChoiceContainer.Children.Add(choiceLayout);
            }

            // 解説を表示
            ChoiceExplanationWebView.Source = new HtmlWebViewSource
            {
                Html = ConvertMarkdownToHtml(explanation)
            };
            ChoiceExplanationWebView.IsVisible = false;
        }

        private async void DisplayImageFillCard(CardData card)
        {
            ImageFillCardLayout.IsVisible = true;
            selectionRects.Clear();

            // imageFileNameフィールドから画像ファイル名を取得
            string imageFileName = GetImageFileNameFromCard(card);

            if (!string.IsNullOrWhiteSpace(imageFileName))
            {
                // フルパスを作成
                string imageFolder = Path.Combine(tempExtractPath, "img");
                selectedImagePath = Path.Combine(imageFolder, imageFileName);

                if (File.Exists(selectedImagePath))
                {
                    Debug.WriteLine($"画像読み込み成功: {selectedImagePath}");
                    CanvasView.InvalidateSurface();  // 再描画
                }
                else
                {
                    Debug.WriteLine($"画像が存在しません: {selectedImagePath}");
                    Debug.WriteLine($"探索パス: {selectedImagePath}");
                    Debug.WriteLine($"imgフォルダ内容: {string.Join(", ", Directory.GetFiles(imageFolder))}");
                    await DisplayAlert("エラー", "画像が存在しません。", "OK");
                    return;
                }
            }
            else
            {
                Debug.WriteLine($"画像ファイル名が取得できません。card.front: '{card.front}', JSONデータ確認が必要");
                await DisplayAlert("エラー", "画像パスが無効です。", "OK");
                return;
            }

            // 範囲の追加
            foreach (var line in card.selectionRects)
                {
                selectionRects.Add(new SKRect(line.x, line.y, line.x + line.width, line.y + line.height));
            }

            CanvasView.InvalidateSurface();
        }

        /// <summary>
        /// カードデータから画像ファイル名を取得
        /// </summary>
        private string GetImageFileNameFromCard(CardData card)
        {
            // 新形式: imageFileNameフィールドから取得
            if (!string.IsNullOrWhiteSpace(card.imageFileName))
            {
                Debug.WriteLine($"新形式の画像ファイル名を使用: {card.imageFileName}");
                return card.imageFileName;
            }

            // 旧形式: frontフィールドから取得（後方互換性のため）
            if (!string.IsNullOrWhiteSpace(card.front))
            {
                Debug.WriteLine($"旧形式のfrontフィールドを使用: {card.front}");
                return card.front;
            }

            Debug.WriteLine("画像ファイル名が見つかりません");
            return null;
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var surface = e.Surface;
            var canvas = surface.Canvas;
            var info = e.Info;

            canvas.Clear(SKColors.White);

            if (string.IsNullOrWhiteSpace(selectedImagePath) || !File.Exists(selectedImagePath))
            {
                Debug.WriteLine("画像が選択されていないか、存在しません。");
                return;
            }

            try
            {
                // ファイルパスから直接画像を読み込む
                using (var stream = File.OpenRead(selectedImagePath))
                {
                    var bitmap = SKBitmap.Decode(stream);
                    Debug.WriteLine($"画像サイズ: {bitmap.Width} x {bitmap.Height}");
                    Debug.WriteLine($"キャンバスサイズ: {info.Width} x {info.Height}");
                    
                    if (bitmap == null)
                    {
                        Debug.WriteLine("画像のデコードに失敗しました。");
                        return;
                    }

                    // アスペクト比を維持して画像を描画
                    float imageAspect = (float)bitmap.Width / bitmap.Height;
                    float canvasAspect = (float)info.Width / info.Height;
                    
                    SKRect imageRect;
                    float scale;
                    
                    if (imageAspect > canvasAspect)
                    {
                        // 画像の方が横長：幅をキャンバスに合わせ、高さを調整
                        scale = (float)info.Width / bitmap.Width;
                        float scaledHeight = bitmap.Height * scale;
                        float offsetY = (info.Height - scaledHeight) / 2;
                        imageRect = new SKRect(0, offsetY, info.Width, offsetY + scaledHeight);
                    }
                    else
                    {
                        // 画像の方が縦長：高さをキャンバスに合わせ、幅を調整
                        scale = (float)info.Height / bitmap.Height;
                        float scaledWidth = bitmap.Width * scale;
                        float offsetX = (info.Width - scaledWidth) / 2;
                        imageRect = new SKRect(offsetX, 0, offsetX + scaledWidth, info.Height);
                    }
                    
                    canvas.DrawBitmap(bitmap, imageRect);
                    Debug.WriteLine($"描画領域: {imageRect.Left}, {imageRect.Top}, {imageRect.Right}, {imageRect.Bottom}");
                    Debug.WriteLine($"スケール: {scale}");

                    // 正規化座標を実際の座標に変換して範囲を表示
                    foreach (var normalizedRect in selectionRects)
                    {
                        // 正規化座標を実際の画像座標に変換
                        float actualX = normalizedRect.Left * bitmap.Width;
                        float actualY = normalizedRect.Top * bitmap.Height;
                        float actualWidth = normalizedRect.Width * bitmap.Width;
                        float actualHeight = normalizedRect.Height * bitmap.Height;
                        
                        // 画像座標をキャンバス座標に変換
                        float canvasX = imageRect.Left + (actualX * scale);
                        float canvasY = imageRect.Top + (actualY * scale);
                        float canvasWidth = actualWidth * scale;
                        float canvasHeight = actualHeight * scale;
                        
                        var displayRect = new SKRect(canvasX, canvasY, canvasX + canvasWidth, canvasY + canvasHeight);
                        
                        // 塗りつぶし用のペイント
                        using (var fillPaint = new SKPaint
                        {
                            Color = SKColors.Red,
                            Style = SKPaintStyle.Fill
                        })
                        {
                            canvas.DrawRect(displayRect, fillPaint);
                        }

                        // 枠線表示
                        using (var borderPaint = new SKPaint
                        {
                            Color = SKColors.Black,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3
                        })
                        {
                            canvas.DrawRect(displayRect, borderPaint);
                        }

                        Debug.WriteLine($"正規化座標: {normalizedRect.Left:F3}, {normalizedRect.Top:F3}, {normalizedRect.Width:F3}, {normalizedRect.Height:F3}");
                        Debug.WriteLine($"表示座標: {displayRect.Left:F1}, {displayRect.Top:F1}, {displayRect.Right:F1}, {displayRect.Bottom:F1}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"描画中にエラーが発生しました: {ex.Message}");
            }
        }

        // 選択肢カードを解析
        private (string Question, string Explanation, List<string> Choices, List<bool> IsCorrect) ParseChoiceCard(CardData card)
        {
            string question = card.question ?? "";
            string explanation = card.explanation ?? "";
            var choices = new List<string>();
            var isCorrectFlags = new List<bool>();
            if (card.choices != null)
            {
                foreach (var c in card.choices)
                {
                    choices.Add(c.text);
                    isCorrectFlags.Add(c.isCorrect);
                }
            }
            return (question, explanation, choices, isCorrectFlags);
        }

        private void OnShowAnswerClicked(object sender, EventArgs e)
        {
            if (BasicCardLayout.IsVisible)
            {
                showAnswer = true;  // 解答表示フラグを有効に
                // 解答を表示時に穴埋めを `({文字})` に変換
                Debug.WriteLine(frontText);
                var answerFrontHtml = ConvertMarkdownToHtml(frontText, showAnswer: true);
                FrontPreviewWebView.Source = new HtmlWebViewSource
                {
                    Html = answerFrontHtml
                };
                BackPreviewWebView.IsVisible = true;
                Correct.IsVisible = true;
                Incorrect.IsVisible = true;
                AnswerLine.IsVisible = true;
                ShowAnswerButton.IsVisible = false;
            }
            else if (ChoiceCardLayout.IsVisible)
            {
                // 選択肢の正誤を判定
                bool isCorrect = true;
                for (int i = 0; i < checkBoxes.Count; i++)
                {
                    var checkBox = checkBoxes[i];
                    var parentLayout = (HorizontalStackLayout)checkBox.Parent;
                    var resultLabel = (Label)parentLayout.Children[2];

                    if (checkBox.IsChecked != currentCorrectFlags[i])
                    {
                        isCorrect = false;
                    }

                    if (currentCorrectFlags[i])
                    {
                        resultLabel.Text = "正";
                        resultLabel.TextColor = Colors.Green;
                        resultLabel.IsVisible = true;
                    }
                    else
                    {
                        resultLabel.Text = "誤";
                        resultLabel.TextColor = Colors.Red;
                        resultLabel.IsVisible = true;
                    }
                }

                // 結果を保存
                if (!results.ContainsKey(currentIndex + 1))
                {
                    results[currentIndex + 1] = new CardResult();
                }

                var result = results[currentIndex + 1];
                result.WasCorrect = isCorrect;

                // 次回表示時間を設定
                result.NextReviewTime = DateTime.Now.AddMinutes(isCorrect ? 10 : 1);  // 正解なら10分、不正解なら1分後に再表示

                SaveResultsToFile();

                // 解説を表示
                ChoiceExplanationWebView.IsVisible = true;

                // 「次へ」ボタンを表示
                ShowAnswerButton.IsVisible = false;
                NextButton.IsVisible = true;
            }
            else if (ImageFillCardLayout.IsVisible)
            {
                selectionRects.Clear();
                CanvasView.InvalidateSurface();
                Correct.IsVisible = true;
                Incorrect.IsVisible = true;
                AnswerLine.IsVisible = true;
                ShowAnswerButton.IsVisible = false;

                // 画像穴埋め問題の結果を保存
                if (!results.ContainsKey(currentIndex + 1))
                {
                    results[currentIndex + 1] = new CardResult();
                }
            }
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            try
            {
                currentIndex++;
                ShowAnswerButton.IsVisible = true;
                NextButton.IsVisible = false;
                Debug.WriteLine($"次の問題へ移動: {currentIndex + 1}/{cards.Count}");
                DisplayCard();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnNextClicked: {ex}");
                DisplayAlert("Error", "Failed to move to next card", "OK");
            }
        }

        // 正解ボタン
        private void OnCorrectClicked(object sender, EventArgs e)
        {
            try
            {
                // 現在のカードのインデックスをそのまま利用
                int questionNumber = currentIndex + 1;
                    if (!results.ContainsKey(questionNumber))
                    {
                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                    }
                    var result = results[questionNumber];
                    result.WasCorrect = true;  // 正解として記録
                    result.OriginalQuestionNumber = questionNumber;  // 元の問題番号を保持
                    // 次回表示時間を設定
                    result.NextReviewTime = DateTime.Now.AddMinutes(10);  // 10分後に再表示
                    SaveResultsToFile();

                currentIndex++;
                Correct.IsVisible = false;
                Incorrect.IsVisible = false;
                AnswerLine.IsVisible = false;
                ShowAnswerButton.IsVisible = true;
                DisplayCard();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnCorrectClicked: {ex}");
                DisplayAlert("Error", "Failed to process correct answer", "OK");
            }
        }

        // 不正解ボタン
        private void OnIncorrectClicked(object sender, EventArgs e)
        {
            try
            {
                int questionNumber = currentIndex + 1;
                    if (!results.ContainsKey(questionNumber))
                    {
                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                    }
                    var result = results[questionNumber];
                    result.WasCorrect = false;  // 不正解として記録
                    result.OriginalQuestionNumber = questionNumber;  // 元の問題番号を保持
                    // 次回表示時間を設定
                    result.NextReviewTime = DateTime.Now.AddMinutes(1);  // 1分後に再表示
                    SaveResultsToFile();

                currentIndex++;
                Correct.IsVisible = false;
                Incorrect.IsVisible = false;
                AnswerLine.IsVisible = false;
                ShowAnswerButton.IsVisible = true;
                DisplayCard();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnIncorrectClicked: {ex}");
                DisplayAlert("Error", "Failed to process incorrect answer", "OK");
            }
        }

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
        private string ConvertMarkdownToHtml(string text, bool showAnswer = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // 画像タグを最初に処理
            var matches = Regex.Matches(text, @"<<img_.*?\.jpg>>");
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

            // 穴埋め表示処理
            if (showAnswer)
            {
                Debug.WriteLine(frontText);
                // 解答表示時は `<<blank|文字>>` → `(文字)`
                text = Regex.Replace(text, @"<<blank\|(.*?)>>", "($1)");
            }
            else
            {
                // 問題表示時は `<<blank|文字>>` → `( )`
                text = Regex.Replace(text, @"<<blank\|(.*?)>>", "( )");
            }

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

            // 穴埋めの解答を赤字に変換（エスケープ後）
            if (showAnswer)
            {
                text = Regex.Replace(text, @"\((.*?)\)", "(<span style='color:red;'>$1</span>)");
            }

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

        // 結果を即時保存
        private void SaveResultsToFile()
        {
            try
            {
                string resultsFilePath = Path.Combine(tempExtractPath, "results.txt");

                var resultLines = new List<string>();

                foreach (var entry in results)
                {
                    int questionNumber = entry.Key;  // 元の問題番号
                    var result = entry.Value;

                    // 基本情報（直前の正誤のみ）
                    var basicInfo = $"正誤: {(result.WasCorrect ? "正解" : "不正解")}";

                    // 次回表示時間
                    var nextReviewString = result.NextReviewTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

                    resultLines.Add($"{questionNumber} | {basicInfo} | {nextReviewString}");
                }

                // 問題番号でソート
                resultLines.Sort((a, b) =>
                {
                    int numA = int.Parse(a.Split('|')[0].Trim());
                    int numB = int.Parse(b.Split('|')[0].Trim());
                    return numA.CompareTo(numB);
                });

                File.WriteAllLines(resultsFilePath, resultLines);
                Debug.WriteLine($"結果を保存しました: {resultsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"結果の保存中にエラーが発生: {ex.Message}");
            }
        }

    }
}

