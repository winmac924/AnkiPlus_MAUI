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
        private Dictionary<int, CardResult> results = new Dictionary<int, CardResult>();
        private bool showAnswer = false;  // 解答表示フラグ
        private string frontText = "";

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
            Debug.WriteLine($"Loading cards from: {cardsFilePath}");
            if (File.Exists(cardsFilePath))
            {
                var content = File.ReadAllText(cardsFilePath);
                Debug.WriteLine($"File content length: {content.Length}");
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                Debug.WriteLine($"Total lines in file: {lines.Length}");

                // メタデータ行をスキップ
                var cardLines = lines.Skip(1).ToArray();
                var card = new List<string>();

                foreach (var line in cardLines)
                {
                    if (line.Trim() == "---")
                    {
                        if (card.Count > 1)  // カードタイプと少なくとも1行の内容がある場合
                        {
                            // 空行を除去してカードを追加
                            var trimmedCard = card.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                            if (trimmedCard.Count > 1)  // カードタイプと少なくとも1行の内容がある場合
                            {
                                cards.Add(string.Join("\n", trimmedCard));
                                Debug.WriteLine($"Added card with {trimmedCard.Count} lines");
                            }
                            card.Clear();
                        }
                    }
                    else
                    {
                        card.Add(line);
                    }
                }

                // 最後のカードを追加
                if (card.Count > 1)  // カードタイプと少なくとも1行の内容がある場合
                {
                    // 空行を除去してカードを追加
                    var trimmedCard = card.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    if (trimmedCard.Count > 1)  // カードタイプと少なくとも1行の内容がある場合
                    {
                        cards.Add(string.Join("\n", trimmedCard));
                        Debug.WriteLine($"Added final card with {trimmedCard.Count} lines");
                    }
                }

                Debug.WriteLine($"Total cards loaded: {cards.Count}");

                // 結果ファイルが存在する場合、問題の出題順序を決定
                string resultsFilePath = Path.Combine(tempExtractPath, "results.txt");
                if (File.Exists(resultsFilePath))
                {
                    var resultLines = File.ReadAllLines(resultsFilePath);
                    var answeredQuestions = new HashSet<int>();
                    var incorrectQuestions = new List<int>();
                    var allCorrect = true;

                    foreach (var line in resultLines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 2)  // 基本情報を含む行を確認
                        {
                            if (int.TryParse(parts[0].Trim(), out int questionNumber))
                            {
                                // 問題番号を0ベースのインデックスに変換
                                int questionIndex = questionNumber - 1;

                                // 現在表示されている問題の結果のみを処理
                                if (questionIndex == currentIndex)
                                {
                                    answeredQuestions.Add(questionIndex);

                                    // 基本情報から正誤を取得
                                    var basicInfo = parts[1].Trim();
                                    bool wasCorrect = basicInfo.Contains("正解");

                                    // 結果オブジェクトを作成または更新
                                    if (!results.ContainsKey(questionNumber))
                                    {
                                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                                    }
                                    results[questionNumber].WasCorrect = wasCorrect;
                                    Debug.WriteLine($"問題 {questionNumber} の結果を更新: {(wasCorrect ? "正解" : "不正解")}");

                                    // 不正解の問題をリストに追加
                                    if (!wasCorrect)
                                    {
                                        incorrectQuestions.Add(questionIndex);
                                        allCorrect = false;
                                        Debug.WriteLine($"不正解の問題を追加: {questionNumber}");
                                    }
                                }
                            }
                        }
                    }

                    // 未回答の問題を探す
                    var unansweredQuestions = new List<int>();
                    for (int i = 0; i < cards.Count; i++)
                    {
                        if (!answeredQuestions.Contains(i))
                        {
                            unansweredQuestions.Add(i);
                        }
                    }

                    // 出題順序の決定
                    if (unansweredQuestions.Any())
                    {
                        // 未回答の問題がある場合、それらを先に出題
                        var random = new Random();
                        currentIndex = unansweredQuestions[random.Next(unansweredQuestions.Count)];
                        Debug.WriteLine($"未回答の問題を出題: {currentIndex + 1}");
                    }
                    else if (incorrectQuestions.Any())
                    {
                        // 不正解の問題がある場合、それらを出題
                        var random = new Random();
                        var originalCards = cards.ToList();  // 元のカードリストを保持
                        var newCards = new List<string>();

                        // 不正解の問題をシャッフルして追加
                        var shuffledIncorrect = incorrectQuestions.OrderBy(x => random.Next()).ToList();
                        foreach (var index in shuffledIncorrect)
                        {
                            if (index < originalCards.Count)
                            {
                                newCards.Add(originalCards[index]);
                                Debug.WriteLine($"不正解の問題を追加: {index + 1}");
                            }
                        }

                        // 正解の問題をシャッフルして追加
                        var correctQuestions = Enumerable.Range(0, originalCards.Count)
                            .Where(i => !incorrectQuestions.Contains(i))
                            .OrderBy(x => random.Next())
                            .ToList();

                        foreach (var index in correctQuestions)
                        {
                            if (index < originalCards.Count)
                            {
                                newCards.Add(originalCards[index]);
                                Debug.WriteLine($"正解の問題を追加: {index + 1}");
                            }
                        }

                        cards = newCards;
                        currentIndex = 0;  // シャッフル後の最初の問題から開始
                        Debug.WriteLine($"シャッフル後の問題数: {cards.Count} (不正解: {shuffledIncorrect.Count}, 正解: {correctQuestions.Count})");
                    }
                    else if (allCorrect)
                    {
                        // すべて正解の場合、全問題をシャッフル
                        var random = new Random();
                        var newCards = new List<string>();
                        var indices = Enumerable.Range(0, cards.Count).ToList();
                        
                        // Fisher-Yatesシャッフル
                        for (int i = indices.Count - 1; i > 0; i--)
                        {
                            int j = random.Next(i + 1);
                            int temp = indices[i];
                            indices[i] = indices[j];
                            indices[j] = temp;
                        }

                        // シャッフルされた順序でカードを追加
                        foreach (var index in indices)
                        {
                            // カードの先頭に問題番号を追加
                            var cardContent = cards[index];
                            var contentLines = cardContent.Split('\n').ToList();
                            contentLines.Insert(0, $"問題番号: {index + 1}");
                            newCards.Add(string.Join("\n", contentLines));
                            Debug.WriteLine($"シャッフル後の問題 {index + 1} を追加");
                        }

                        // 新しいカードリストを設定
                        cards = newCards;
                        currentIndex = 0;  // 最初の問題から開始
                        Debug.WriteLine($"全問題をシャッフルして出題（問題数: {cards.Count}）");
                    }
                    else
                    {
                        // デフォルトは最初の問題から
                        currentIndex = 0;
                        Debug.WriteLine("デフォルトで最初の問題を出題");
                    }

                    // 現在のインデックスが有効範囲内か確認
                    if (currentIndex < 0 || currentIndex >= cards.Count)
                    {
                        currentIndex = 0;
                        Debug.WriteLine("インデックスが範囲外のため、最初の問題を出題");
                    }
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
                Debug.WriteLine($"Current card content: {card}");
                var lines = card.Split('\n')
                               .Select(line => line.Trim())
                               .Where(line => !string.IsNullOrWhiteSpace(line))
                               .ToList();
                Debug.WriteLine($"Card lines count: {lines.Count}");

                if (lines.Count == 0)
                {
                    Debug.WriteLine("Empty card content, moving to next card");
                    currentIndex++;
                    DisplayCard();
                    return;
                }

                // 問題番号を取得
                int questionNumber = 0;
                if (lines[0].StartsWith("問題番号:"))
                {
                    if (int.TryParse(lines[0].Split(':')[1].Trim(), out int number))
                    {
                        questionNumber = number;
                    }
                    lines.RemoveAt(0);  // 問題番号行を削除
                }
                Debug.WriteLine($"現在の問題番号: {questionNumber}/{cards.Count}");

                // レイアウトの初期化
                BasicCardLayout.IsVisible = false;
                ChoiceCardLayout.IsVisible = false;
                ImageFillCardLayout.IsVisible = false;
                AnswerLine.IsVisible = false;

                if (lines[0].Contains("基本"))
                {
                    Debug.WriteLine("Displaying basic card");
                    DisplayBasicCard(lines);
                }
                else if (lines[0].Contains("選択肢"))
                {
                    Debug.WriteLine("Displaying choice card");
                    DisplayChoiceCard(lines);
                }
                else if (lines[0].Contains("画像穴埋め"))
                {
                    Debug.WriteLine("Displaying image fill card");
                    DisplayImageFillCard(lines);
                }
                else
                {
                    Debug.WriteLine($"Unknown card type: {lines[0]}");
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
        private void DisplayBasicCard(List<string> lines)
        {
            BasicCardLayout.IsVisible = true;

            // 複数行対応でパース
            var (frontText, backText) = ParseBasicCard(lines);
            this.frontText = frontText; // クラス変数に保存

            // 画像タグの処理
            var matches = Regex.Matches(frontText, @"<<img(\d+)>>");
            foreach (Match match in matches)
            {
                int imgNum = int.Parse(match.Groups[1].Value);
                string imgPath = Path.Combine(tempExtractPath, "img", $"img{imgNum}.png");
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
                // 現在のカードから問題番号を取得
                var card = cards[currentIndex];
                var lines = card.Split('\n')
                               .Select(line => line.Trim())
                               .Where(line => !string.IsNullOrWhiteSpace(line))
                               .ToList();

                int questionNumber = 0;
                if (lines[0].StartsWith("問題番号:"))
                {
                    if (int.TryParse(lines[0].Split(':')[1].Trim(), out int number))
                    {
                        questionNumber = number;
                    }
                }

                if (questionNumber > 0)
                {
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
                }

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
                // 現在のカードから問題番号を取得
                var card = cards[currentIndex];
                var lines = card.Split('\n')
                               .Select(line => line.Trim())
                               .Where(line => !string.IsNullOrWhiteSpace(line))
                               .ToList();

                int questionNumber = 0;
                if (lines[0].StartsWith("問題番号:"))
                {
                    if (int.TryParse(lines[0].Split(':')[1].Trim(), out int number))
                    {
                        questionNumber = number;
                    }
                }

                if (questionNumber > 0)
                {
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
                }

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

