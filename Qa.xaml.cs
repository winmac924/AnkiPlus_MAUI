using SkiaSharp.Views.Maui;
using SkiaSharp;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

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
        // �N���X�̐擪�ŕϐ���錾
        private string selectedImagePath = "";
        private List<SKRect> selectionRects = new List<SKRect>();
        // �e��育�Ƃ̐����E�s�����񐔂��Ǘ�
        private Dictionary<int, (int correct, int incorrect)> results = new Dictionary<int, (int, int)>();
        private bool showAnswer = false;  // �𓚕\���t���O
        private string frontText = "";

        public Qa(string cardsPath)
        {
            InitializeComponent();
            // �ꎞ�t�H���_
            cardsFilePath = Path.Combine(Path.GetTempPath(), "AnkiPlus", $"{cardsPath}_temp", "cards.txt");
            tempExtractPath = Path.Combine(Path.GetTempPath(), "AnkiPlus", $"{cardsPath}_temp");
            LoadCards();
            DisplayCard();
        }
        protected override void OnAppearing()
        {
            base.OnAppearing();
            CanvasView.InvalidateSurface();
            LoadResultsFromFile();
        }
        // �J�[�h��ǂݍ���
        private void LoadCards()
        {
            if (File.Exists(cardsFilePath))
            {
                var lines = File.ReadAllLines(cardsFilePath);
                var card = new List<string>();
                var isFirstCardSkipped = false; // �ŏ��̃J�[�h���X�L�b�v���邽�߂̃t���O

                foreach (var line in lines)
                {
                    if (line == "---")
                    {
                        if (card.Count > 0)
                        {
                            if (isFirstCardSkipped)
                            {
                                cards.Add(string.Join("\n", card)); // �ŏ��̃J�[�h�ȊO��ǉ�
                            }
                            else
                            {
                                isFirstCardSkipped = true; // �ŏ��̃J�[�h���X�L�b�v
                            }
                            card.Clear();
                        }
                    }
                    else
                    {
                        card.Add(line);
                    }
                }

                if (card.Count > 0 && isFirstCardSkipped)
                {
                    cards.Add(string.Join("\n", card));
                }

                Debug.WriteLine($"Loaded {cards.Count} cards");
            }
        }
        // ���ʃt�@�C����ǂݍ��ށi�t�H�[�}�b�g�Ή��j
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
                            // �J�[�h�ԍ����擾
                            if (int.TryParse(parts[0].Trim(), out int questionNumber))
                            {
                                // "����: 1��, �s����: 0��" �`�������
                                var correctMatch = Regex.Match(line, @"����:\s*(\d+)��").Groups[1].Value;
                                var incorrectMatch = Regex.Match(line, @"�s����:\s*(\d+)��").Groups[1].Value;

                                int correct = int.TryParse(correctMatch, out int c) ? c : 0;
                                int incorrect = int.TryParse(incorrectMatch, out int ic) ? ic : 0;

                                results[questionNumber] = (correct, incorrect);

                                Debug.WriteLine($"Loaded result: {questionNumber}: {correct} correct, {incorrect} incorrect");
                            }
                        }
                    }
                }

                Debug.WriteLine($"���ʂ�ǂݍ��݂܂���: {resultsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"���ʓǂݍ��ݒ��ɃG���[: {ex.Message}");
            }
        }
        // ����\��
        private async void DisplayCard()
        {
            if (currentIndex >= cards.Count)
            {
                // ���ʂ��t�@�C���ɕۑ�
                SaveResultsToFile();
                await DisplayAlert("�I��", "���ׂĂ̖�肪�I�����܂���", "OK");
                await Navigation.PopAsync();
                return;
            }

            var card = cards[currentIndex];
            var lines = card.Split('\n').Select(line => line.Trim()).ToList();

            // ���C�A�E�g�̏�����
            BasicCardLayout.IsVisible = false;
            ChoiceCardLayout.IsVisible = false;
            ImageFillCardLayout.IsVisible = false;
            AnswerLine.IsVisible = false;

            if (lines[0].Contains("��{"))
            {
                DisplayBasicCard(lines);
            }
            else if (lines[0].Contains("�I����"))
            {
                DisplayChoiceCard(lines);
            }
            else if (lines[0].Contains("�摜������"))
            {
                DisplayImageFillCard(lines);
            }
        }
        // ��{�E�����߃J�[�h�\��
        private void DisplayBasicCard(List<string> lines)
        {
            BasicCardLayout.IsVisible = true;

            frontText = lines.FirstOrDefault(l => l.StartsWith("�\��:"))?.Substring(3) ?? "";
            string backText = lines.FirstOrDefault(l => l.StartsWith("����:"))?.Substring(3) ?? "";
            // ����\�����͉𓚔�\��
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
        // �I�����J�[�h�\��
        private List<CheckBox> checkBoxes = new List<CheckBox>();  // �`�F�b�N�{�b�N�X��ێ�
        private List<bool> currentCorrectFlags = new List<bool>();  // ���݂̃J�[�h�̐�����

        private void DisplayChoiceCard(List<string> lines)
        {
            ChoiceCardLayout.IsVisible = true;

            var (question, explanation, choices, isCorrectFlags) = ParseChoiceCard(lines);
            currentCorrectFlags = isCorrectFlags;  // ���݂̐������ێ�

            ChoiceQuestionWebView.Source = new HtmlWebViewSource
            {
                Html = ConvertMarkdownToHtml(question)
            };

            ChoiceContainer.Children.Clear();
            checkBoxes.Clear();

            for (int i = 0; i < choices.Count; i++)
            {
                var choiceText = choices[i];

                var choiceLayout = new HorizontalStackLayout
                {
                    Spacing = 10
                };

                // �`�F�b�N�{�b�N�X
                var checkBox = new CheckBox
                {
                    IsChecked = false
                };

                checkBoxes.Add(checkBox);

                // �I�����̃��x��
                var choiceLabel = new Label
                {
                    Text = choiceText,
                    VerticalOptions = LayoutOptions.Center
                };

                // ����}�[�N��\�����邽�߂� Label
                var resultLabel = new Label
                {
                    Text = "",
                    VerticalOptions = LayoutOptions.Center,
                    IsVisible = false
                };

                // �`�F�b�N�{�b�N�X�ƃ��x����ǉ�
                choiceLayout.Children.Add(checkBox);
                choiceLayout.Children.Add(choiceLabel);
                choiceLayout.Children.Add(resultLabel);

                ChoiceContainer.Children.Add(choiceLayout);
            }

            // �����\��
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

            string imageFileName = lines.FirstOrDefault(l => l.StartsWith("�摜:"))?.Split(": ")[1];

            if (!string.IsNullOrWhiteSpace(imageFileName))
            {
                // �t���p�X���쐬
                string imageFolder = Path.Combine(tempExtractPath, "img");
                selectedImagePath = Path.Combine(imageFolder, imageFileName);

                if (File.Exists(selectedImagePath))
                {
                    Debug.WriteLine($"�摜�ǂݍ��ݐ���: {selectedImagePath}");
                    CanvasView.InvalidateSurface();  // �ĕ`��
                }
                else
                {
                    Debug.WriteLine($"�摜�����݂��܂���: {selectedImagePath}");
                    await DisplayAlert("�G���[", "�摜�����݂��܂���B", "OK");
                    return;
                }
            }
            else
            {
                await DisplayAlert("�G���[", "�摜�p�X�������ł��B", "OK");
                return;
            }

            // �͈͂̒ǉ�
            foreach (var line in lines.Where(l => l.StartsWith("�͈�:")))
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
                Debug.WriteLine("�摜���I������Ă��Ȃ����A���݂��܂���B");
                return;
            }

            try
            {
                // �t�@�C���p�X���璼�ډ摜��ǂݍ���
                using (var stream = File.OpenRead(selectedImagePath))
                {
                    var bitmap = SKBitmap.Decode(stream);
                    Debug.WriteLine($"�摜�T�C�Y: {bitmap.Width} x {bitmap.Height}");
                    if (bitmap == null)
                    {
                        Debug.WriteLine("�摜�̃f�R�[�h�Ɏ��s���܂����B");
                        return;
                    }

                    // �摜��`��i�T�C�Y�����Ȃ��j
                    var imageRect = new SKRect(0, 0, info.Width, info.Height);
                    canvas.DrawBitmap(bitmap, imageRect);

                    // �͈͂����̂܂ܕ\��
                    foreach (var rect in selectionRects)
                    {
                        // �h��Ԃ��p�̃y�C���g
                        using (var fillPaint = new SKPaint
                        {
                            Color = SKColors.Red,
                            Style = SKPaintStyle.Fill
                        })
                        {
                            canvas.DrawRect(rect, fillPaint);
                        }

                        // �g���\��
                        using (var borderPaint = new SKPaint
                        {
                            Color = SKColors.Black,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3
                        })
                        {
                            canvas.DrawRect(rect, borderPaint);
                        }

                        Debug.WriteLine($"�\���͈�: {rect.Left}, {rect.Top}, {rect.Right}, {rect.Bottom}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"�`�撆�ɃG���[���������܂���: {ex.Message}");
            }
        }

        // �I�����J�[�h�����
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
                if (line.StartsWith("���:"))
                {
                    isQuestion = true;
                    isExplanation = false;
                    question.AppendLine(line.Substring(3));
                }
                else if (line.StartsWith("���:"))
                {
                    isExplanation = true;
                    isQuestion = false;
                    explanation.AppendLine(line.Substring(3));
                }
                else if (line.StartsWith("�I����:"))
                {
                    var choiceText = line.Substring(4);

                    // ����t���O�𔻒�
                    bool isCorrect = choiceText.Contains("(����)");
                    isCorrectFlags.Add(isCorrect);

                    // �\���p�̑I�����e�L�X�g�� `(����)` �� `(�s����)` ������
                    var cleanChoice = Regex.Replace(choiceText, @"\s*\(����\)|\s*\(�s����\)", "").Trim();
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
            // �𓚕\������
            if (BasicCardLayout.IsVisible)
            {
                showAnswer = true;  // �𓚕\���t���O��L����
                // �𓚂�\�����Ɍ����߂� `({����})` �ɕϊ�
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
                        // ���� �� �ΐF
                        resultLabel.Text = "��";
                        resultLabel.TextColor = Colors.Green;
                        resultLabel.IsVisible = true;
                    }
                    else
                    {
                        // �s���� �� �ԐF
                        resultLabel.Text = "��";
                        resultLabel.TextColor = Colors.Red;
                        resultLabel.IsVisible = true;
                    }
                }

                // �����\��
                ChoiceExplanationWebView.IsVisible = true;
            }
            else if (ImageFillCardLayout.IsVisible)
            {
                selectionRects.Clear();
                CanvasView.InvalidateSurface();
            }
        }

        // �����{�^��
        private void OnCorrectClicked(object sender, EventArgs e)
        {
            if (!results.ContainsKey(currentIndex + 1))
            {
                results[currentIndex + 1] = (0, 0);
            }

            // �J�E���g�ƕۑ�
            var (correct, incorrect) = results[currentIndex + 1];
            results[currentIndex + 1] = (correct + 1, incorrect);

            SaveResultsToFile();  // �����ɕۑ�
            currentIndex++;
            Correct.IsVisible = false;
            Incorrect.IsVisible = false;
            AnswerLine.IsVisible = false;
            ShowAnswerButton.IsVisible = true;
            DisplayCard();
        }

        // �s�����{�^��
        private void OnIncorrectClicked(object sender, EventArgs e)
        {
            if (!results.ContainsKey(currentIndex + 1))
            {
                results[currentIndex + 1] = (0, 0);
            }

            // �J�E���g�ƕۑ�
            var (correct, incorrect) = results[currentIndex + 1];
            results[currentIndex + 1] = (correct, incorrect + 1);

            SaveResultsToFile();  // �����ɕۑ�
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

        // Markdown �� HTML �ɕϊ�
        private string ConvertMarkdownToHtml(string text, bool showAnswer = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // �摜�^�O���ŏ��ɏ���
            var matches = Regex.Matches(text, @"<<img(\d+)>>");
            Debug.WriteLine($"�摜�^�O��: {matches.Count}");
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
                        text = text.Replace(match.Value, $"[�摜��������܂���: img{imgNum}.png]");
                    }
                }
                else
                {
                    text = text.Replace(match.Value, $"[�摜��������܂���: img{imgNum}.png]");
                }
            }

            // �����ߕ\������
            if (showAnswer)
            {
                Debug.WriteLine(frontText);
                // �𓚕\������ `<<blank|����>>` �� `({����})`
                text = Regex.Replace(text, @"<<blank\|(.*?)>>", "($1)");
            }
            else
            {
                // ���\������ `<<blank|����>>` �� `( )`
                text = Regex.Replace(text, @"<<blank\|(.*?)>>", "( )");
            }

            // HTML �G�X�P�[�v
            text = HttpUtility.HtmlEncode(text);

            // �����ϊ�
            text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");

            // �F�ϊ�
            text = Regex.Replace(text, @"\{\{red\|(.*?)\}\}", "<span style='color:red;'>$1</span>");
            text = Regex.Replace(text, @"\{\{blue\|(.*?)\}\}", "<span style='color:blue;'>$1</span>");
            text = Regex.Replace(text, @"\{\{green\|(.*?)\}\}", "<span style='color:green;'>$1</span>");
            text = Regex.Replace(text, @"\{\{yellow\|(.*?)\}\}", "<span style='color:yellow;'>$1</span>");
            text = Regex.Replace(text, @"\{\{purple\|(.*?)\}\}", "<span style='color:purple;'>$1</span>");
            text = Regex.Replace(text, @"\{\{orange\|(.*?)\}\}", "<span style='color:orange;'>$1</span>");

            // ��t���E���t���ϊ�
            text = Regex.Replace(text, @"\^\^(.*?)\^\^", "<sup>$1</sup>");
            text = Regex.Replace(text, @"~~(.*?)~~", "<sub>$1</sub>");

            // �K�v�ȕ��������f�R�[�h����
            text = Regex.Replace(text, @"&lt;img(.*?)&gt;", "<img$1>");

            // ���s�� `<br>` �ɕϊ�
            text = text.Replace(Environment.NewLine, "<br>").Replace("\n", "<br>");


            // HTML �e���v���[�g
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

        // ���ʂ𑦎��ۑ�
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

                    resultLines.Add($"{questionNumber}: ����: {correct}��, �s����: {incorrect}��");
                }

                File.WriteAllLines(resultsFilePath, resultLines);
                Debug.WriteLine($"���ʂ�ۑ����܂���: {resultsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"���ʂ̕ۑ����ɃG���[������: {ex.Message}");
            }
        }

    }
}
