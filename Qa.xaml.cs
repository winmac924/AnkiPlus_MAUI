using SkiaSharp.Views.Maui;
using SkiaSharp;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Linq;
using System.Collections.Generic;

namespace AnkiPlus_MAUI
{
    public partial class Qa : ContentPage
    {
        private string cardsFilePath;
        private string tempExtractPath;
        private List<string> cards = new List<string>();
        private int currentIndex = 0;
        private int correctCount = 0;
        private int incorrectCount = 0;
        // クラスの先頭で変数を宣言
        private string selectedImagePath = "";
        private List<SKRect> selectionRects = new List<SKRect>();
        // 各問題ごとの正解・不正解回数を管理
        private Dictionary<int, (int correct, int incorrect)> results = new Dictionary<int, (int, int)>();
        private bool showAnswer = false;  // 解答表示フラグ
        private string frontText = "";

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

            // カードデータを直接メモリ上で処理
            cards = new List<string>();
            foreach (var content in cardsList)
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                var card = new List<string>();

                foreach (var line in lines)
                {
                    if (line.Trim() == "---")
                    {
                        if (card.Count > 0)
                        {
                            cards.Add(string.Join("\n", card));
                            card.Clear();
                        }
                    }
                    else
                    {
                        card.Add(line);
                    }
                }

                // 最後のカードを追加
                if (card.Count > 0)
                {
                    cards.Add(string.Join("\n", card));
                }
            }

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
            if (File.Exists(cardsFilePath))
            {
                var content = File.ReadAllText(cardsFilePath);
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                var card = new List<string>();

                foreach (var line in lines)
                {
                    if (line.Trim() == "---")
                    {
                        if (card.Count > 0)
                        {
                            cards.Add(string.Join("\n", card));
                            card.Clear();
                        }
                    }
                    else
                    {
                        card.Add(line);
                    }
                }

                // 最後のカードを追加
                if (card.Count > 0)
                {
                    cards.Add(string.Join("\n", card));
                }

                Debug.WriteLine($"Loaded {cards.Count} cards");

                // 結果ファイルが存在する場合、未解答の問題から始める
                string resultsFilePath = Path.Combine(tempExtractPath, "results.txt");
                if (File.Exists(resultsFilePath))
                {
                    var resultLines = File.ReadAllLines(resultsFilePath);
                    var answeredQuestions = new HashSet<int>();

                    foreach (var line in resultLines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 3)
                        {
                            if (int.TryParse(parts[0].Trim(), out int questionNumber))
                            {
                                answeredQuestions.Add(questionNumber);
                            }
                        }
                    }

                    // 未解答の問題を探す
                    for (int i = 0; i < cards.Count; i++)
                    {
                        if (!answeredQuestions.Contains(i + 1))
                        {
                            currentIndex = i;
                            break;
                        }
                    }
                }
            }
        }
        // 結果ファイルを読み込む（フォーマット対応）
        private void LoadResultsFromFile()
        {
            try
            {
                string resultsFilePath = Path.Combine(tempExtractPath, "results.txt");

                if (File.Exists(resultsFilePath))
                {
                    var lines = File.ReadAllLines(resultsFilePath);

                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');

                        if (parts.Length >= 3)
                        {
                            // カード番号を取得
                            if (int.TryParse(parts[0].Trim(), out int questionNumber))
                            {
                                // "正解: 1回, 不正解: 0回" 形式を解析
                                var correctMatch = Regex.Match(line, @"正解:\s*(\d+)回").Groups[1].Value;
                                var incorrectMatch = Regex.Match(line, @"不正解:\s*(\d+)回").Groups[1].Value;

                                int correct = int.TryParse(correctMatch, out int c) ? c : 0;
                                int incorrect = int.TryParse(incorrectMatch, out int ic) ? ic : 0;

                                results[questionNumber] = (correct, incorrect);

                                Debug.WriteLine($"Loaded result: {questionNumber}: {correct} correct, {incorrect} incorrect");
                            }
                        }
                    }
                }

                Debug.WriteLine($"結果を読み込みました: {resultsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"結果読み込み中にエラー: {ex.Message}");
            }
        }
        // 問題を表示
        private async void DisplayCard()
        {
            if (currentIndex >= cards.Count)
            {
                // 結果をファイルに保存
                SaveResultsToFile();
                await DisplayAlert("終了", "すべての問題が終了しました", "OK");
                await Navigation.PopAsync();
                return;
            }

            var card = cards[currentIndex];
            var lines = card.Split('\n').Select(line => line.Trim()).ToList();

            // レイアウトの初期化
            BasicCardLayout.IsVisible = false;
            ChoiceCardLayout.IsVisible = false;
            ImageFillCardLayout.IsVisible = false;
            AnswerLine.IsVisible = false;

            if (lines[0].Contains("基本"))
            {
                DisplayBasicCard(lines);
            }
            else if (lines[0].Contains("選択肢"))
            {
                DisplayChoiceCard(lines);
            }
            else if (lines[0].Contains("画像穴埋め"))
            {
                DisplayImageFillCard(lines);
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
        private void DisplayBasicCard(List<string> lines)
        {
            BasicCardLayout.IsVisible = true;

            // 複数行対応でパース
            var (frontText, backText) = ParseBasicCard(lines);
            this.frontText = frontText; // クラス変数に保存

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

        private void DisplayChoiceCard(List<string> lines)
        {
            ChoiceCardLayout.IsVisible = true;

            var (question, explanation, choices, isCorrectFlags) = ParseChoiceCard(lines);

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

                // 選択肢のラベル
                var choiceLabel = new Label
                {
                    Text = choiceText,
                    VerticalOptions = LayoutOptions.Center
                };

                // 正誤マークを表示するための Label
                var resultLabel = new Label
                {
                    Text = "",
                    VerticalOptions = LayoutOptions.Center,
                    IsVisible = false
                };

                // チェックボックスとラベルを追加
                choiceLayout.Children.Add(checkBox);
                choiceLayout.Children.Add(choiceLabel);
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

        private async void DisplayImageFillCard(List<string> lines)
        {
            ImageFillCardLayout.IsVisible = true;
            selectionRects.Clear();

            string imageFileName = lines.FirstOrDefault(l => l.StartsWith("画像:"))?.Split(": ")[1];

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
                    await DisplayAlert("エラー", "画像が存在しません。", "OK");
                    return;
                }
            }
            else
            {
                await DisplayAlert("エラー", "画像パスが無効です。", "OK");
                return;
            }

            // 範囲の追加
            foreach (var line in lines.Where(l => l.StartsWith("範囲:")))
            {
                var values = line.Split(": ")[1].Split(',').Select(float.Parse).ToArray();
                if (values.Length == 4)
                {
                    selectionRects.Add(new SKRect(values[0], values[1], values[2], values[3]));
                }
            }

            CanvasView.InvalidateSurface();
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
                    if (bitmap == null)
                    {
                        Debug.WriteLine("画像のデコードに失敗しました。");
                        return;
                    }

                    // 画像を描画（サイズ調整なし）
                    var imageRect = new SKRect(0, 0, info.Width, info.Height);
                    canvas.DrawBitmap(bitmap, imageRect);

                    // 範囲をそのまま表示
                    foreach (var rect in selectionRects)
                    {
                        // 塗りつぶし用のペイント
                        using (var fillPaint = new SKPaint
                        {
                            Color = SKColors.Red,
                            Style = SKPaintStyle.Fill
                        })
                        {
                            canvas.DrawRect(rect, fillPaint);
                        }

                        // 枠線表示
                        using (var borderPaint = new SKPaint
                        {
                            Color = SKColors.Black,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3
                        })
                        {
                            canvas.DrawRect(rect, borderPaint);
                        }

                        Debug.WriteLine($"表示範囲: {rect.Left}, {rect.Top}, {rect.Right}, {rect.Bottom}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"描画中にエラーが発生しました: {ex.Message}");
            }
        }

        // 選択肢カードを解析
        private (string Question, string Explanation, List<string> Choices, List<bool> IsCorrect) ParseChoiceCard(List<string> lines)
        {
            var question = new StringBuilder();
            var explanation = new StringBuilder();
            var choices = new List<string>();
            var isCorrectFlags = new List<bool>();

            bool isQuestion = false;
            bool isExplanation = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("問題:"))
                {
                    isQuestion = true;
                    isExplanation = false;
                    question.AppendLine(line.Substring(3));
                }
                else if (line.StartsWith("解説:"))
                {
                    isExplanation = true;
                    isQuestion = false;
                    explanation.AppendLine(line.Substring(3));
                }
                else if (line.StartsWith("選択肢:"))
                {
                    var choiceText = line.Substring(4);

                    // 正誤フラグを判定
                    bool isCorrect = choiceText.Contains("(正解)");
                    isCorrectFlags.Add(isCorrect);

                    // 表示用の選択肢テキストは `(正解)` や `(不正解)` を除去
                    var cleanChoice = Regex.Replace(choiceText, @"\s*\(正解\)|\s*\(不正解\)", "").Trim();
                    choices.Add(cleanChoice);
                }
                else
                {
                    if (isQuestion)
                    {
                        question.AppendLine(line);
                    }
                    else if (isExplanation)
                    {
                        explanation.AppendLine(line);
                    }
                }
            }

            return (question.ToString().Trim(), explanation.ToString().Trim(), choices, isCorrectFlags);
        }

        private void OnShowAnswerClicked(object sender, EventArgs e)
        {

            Correct.IsVisible = true;
            Incorrect.IsVisible = true;
            AnswerLine.IsVisible = true;
            ShowAnswerButton.IsVisible = false;
            // 解答表示処理
            if (BasicCardLayout.IsVisible)
            {
                showAnswer = true;  // 解答表示フラグを有効に
                // 解答を表示時に穴埋めを `({文字})` に変換
                Debug.WriteLine(frontText);
                var answerFrontHtml = ConvertMarkdownToHtml(frontText, showAnswer: true);
                FrontPreviewWebView.Source = new HtmlWebViewSource
                {
                    Html = answerFrontHtml
                }
                ;
                BackPreviewWebView.IsVisible = true;
            }
            else if (ChoiceCardLayout.IsVisible)
            {
                for (int i = 0; i < checkBoxes.Count; i++)
                {
                    var checkBox = checkBoxes[i];
                    var parentLayout = (HorizontalStackLayout)checkBox.Parent;
                    var resultLabel = (Label)parentLayout.Children[2];

                    if (currentCorrectFlags[i])
                    {
                        // 正解 → 緑色
                        resultLabel.Text = "正";
                        resultLabel.TextColor = Colors.Green;
                        resultLabel.IsVisible = true;
                    }
                    else
                    {
                        // 不正解 → 赤色
                        resultLabel.Text = "誤";
                        resultLabel.TextColor = Colors.Red;
                        resultLabel.IsVisible = true;
                    }
                }

                // 解説を表示
                ChoiceExplanationWebView.IsVisible = true;
            }
            else if (ImageFillCardLayout.IsVisible)
            {
                selectionRects.Clear();
                CanvasView.InvalidateSurface();
            }
        }

        // 正解ボタン
        private void OnCorrectClicked(object sender, EventArgs e)
        {
            if (!results.ContainsKey(currentIndex + 1))
            {
                results[currentIndex + 1] = (0, 0);
            }

            // カウントと保存
            var (correct, incorrect) = results[currentIndex + 1];
            results[currentIndex + 1] = (correct + 1, incorrect);

            SaveResultsToFile();  // 即座に保存
            currentIndex++;
            Correct.IsVisible = false;
            Incorrect.IsVisible = false;
            AnswerLine.IsVisible = false;
            ShowAnswerButton.IsVisible = true;
            DisplayCard();
        }

        // 不正解ボタン
        private void OnIncorrectClicked(object sender, EventArgs e)
        {
            if (!results.ContainsKey(currentIndex + 1))
            {
                results[currentIndex + 1] = (0, 0);
            }

            // カウントと保存
            var (correct, incorrect) = results[currentIndex + 1];
            results[currentIndex + 1] = (correct, incorrect + 1);

            SaveResultsToFile();  // 即座に保存
            currentIndex++;
            Correct.IsVisible = false;
            Incorrect.IsVisible = false;
            AnswerLine.IsVisible = false;
            ShowAnswerButton.IsVisible = true;
            DisplayCard();
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
                    int questionNumber = entry.Key;
                    var (correct, incorrect) = entry.Value;

                    resultLines.Add($"{questionNumber}: 正解: {correct}回, 不正解: {incorrect}回");
                }

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
