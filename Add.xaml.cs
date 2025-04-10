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
        private string ankplsFilePath; // .ankpls �̃p�X
        private string tempExtractPath; // �ꎞ�W�J�t�H���_
        private Note _selectedNote;
        private List<string> imagePaths = new List<string>(); // �摜�p�X��ۑ�
        private int imageCount = 0; // �摜�ԍ��Ǘ�
        private string selectedImagePath = "";         // �I�������摜�̃p�X
        private SKBitmap imageBitmap;         // �摜��\�����邽�߂̃r�b�g�}�b�v
        private List<SKRect> selectionRects = new List<SKRect>();
        private SKPoint startPoint, endPoint;
        private bool isDragging = false;
        private const float HANDLE_SIZE = 15;
        public Add(Note selectedNote)
        {
            InitializeComponent();
            this._selectedNote = selectedNote;
            CardTypePicker.SelectedIndex = 0; // �����l���u��{�v�ɐݒ�

            // �m�[�g�̕ۑ��t�H���_��ݒ�
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = Path.Combine(documentsPath, "AnkiPlus", $"{_selectedNote.Name}.ankpls");

            // �ꎞ�t�H���_���쐬
            tempExtractPath = Path.Combine(Path.GetTempPath(), "AnkiPlus", $"{selectedNote.Name}_temp");

            LoadAnkplsFile();
        }

        private void LoadAnkplsFile()
        {
            try
            {
                // .ankpls ���Ȃ��ꍇ�A�V�K�쐬
                if (!File.Exists(ankplsFilePath))
                {
                    Directory.CreateDirectory(tempExtractPath);
                    Directory.CreateDirectory(Path.Combine(tempExtractPath, "img"));

                    // ��� cards.txt ���쐬
                    File.WriteAllText(Path.Combine(tempExtractPath, "cards.txt"), "");

                    // .ankpls ���쐬�iZIP���k�j
                    using (FileStream zipToCreate = new FileStream(ankplsFilePath, FileMode.Create))
                    using (ZipArchive archive = new ZipArchive(zipToCreate, ZipArchiveMode.Create, true))
                    {
                        archive.CreateEntryFromFile(Path.Combine(tempExtractPath, "cards.txt"), "cards.txt");
                    }
                }
                else
                {
                    // ������ .ankpls ��W�J
                    Directory.CreateDirectory(tempExtractPath);
                    ZipFile.ExtractToDirectory(ankplsFilePath, tempExtractPath, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling .ankpls file: {ex.Message}");
            }
        }
        // ��萔��ǂݍ���
        private async Task<int> GetCardCountAsync(string cardsFilePath)
        {
            if (!File.Exists(cardsFilePath))
            {
                return 0;
            }

            var lines = await File.ReadAllLinesAsync(cardsFilePath);

            // ��萔��2�s�ڂɕۑ�
            if (lines.Length >= 2 && int.TryParse(lines[1], out int count))
            {
                return count;
            }

            return 0;
        }

        // �J�[�h�^�C�v���ύX���ꂽ�Ƃ��̏���
        private void OnCardTypeChanged(object sender, EventArgs e)
        {
            string selectedType = CardTypePicker.SelectedItem as string;

            if (selectedType == "��{�E������")
            {
                BasicCardLayout.IsVisible = true;
            }
            else
            {
                BasicCardLayout.IsVisible = false;
            }
            if (selectedType == "�I����")
            {
                MultipleChoiceLayout.IsVisible = true;
            }
            else
            {
                MultipleChoiceLayout.IsVisible = false;
            }
            if (selectedType == "�摜������")
            {
                ImageFillLayout.IsVisible = true;
            }
            else
            {
                ImageFillLayout.IsVisible = false;
            }

        }
        // �e�L�X�g�ύX���Ƀv���r���[�X�V
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
            var entry = new Entry { Placeholder = "�I���������" };

            stack.Children.Add(checkBox);
            stack.Children.Add(entry);

            ChoicesContainer.Children.Add(stack);
        }
        // �摜�I���ƕۑ�����
        private async void OnSelectImage(object sender, EventArgs e)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "�摜��I��",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                using (var stream = await result.OpenReadAsync())
                {
                    imageBitmap = SKBitmap.Decode(stream);
                }

                // �摜�ԍ��̓ǂݍ��݂ƍX�V
                LoadImageCount();
                imageCount++;
                SaveImageCount();

                // �摜�� img �t�H���_�ɕۑ�
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
        // �摜���t�@�C���ɕۑ�
        private void SaveBitmapToFile(SKBitmap bitmap, string filePath)
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }
        }
        // �摜�ԍ���ǂݍ���
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
        // �摜�ԍ���ۑ�����
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
        // �^�b�`�C�x���g�����i���N���b�N�Ŏl�p�`��ǉ��A�E�N���b�N�ō폜�j
        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        // �E�N���b�N�ō폜���j���[�\��
                        var clickedRect = selectionRects.FirstOrDefault(r => r.Contains(point));
                        if (clickedRect != SKRect.Empty)
                        {
                            ShowContextMenu(point, clickedRect);
                        }
                    }
                    else
                    {
                        // ���N���b�N�Ŏl�p�`��ǉ�
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

            // �ĕ`��
            CanvasView.InvalidateSurface();
        }
        // �폜�R���e�L�X�g���j���[�̕\��
        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await DisplayActionSheet("�폜���܂����H", "�L�����Z��", "�폜");

            if (action == "�폜")
            {
                selectionRects.Remove(rect);
                CanvasView.InvalidateSurface();
            }
        }
        // �`�揈��
        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            // �摜��\��
            if (imageBitmap != null)
            {
                var rect = new SKRect(0, 0, e.Info.Width, e.Info.Height);
                canvas.DrawBitmap(imageBitmap, rect);
            }

            // �l�p�`��`��
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
        // Markdown �� HTML �ɕϊ�
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
        private string ConvertMarkdownToHtml(string text)
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

            // �����ߕϊ� `<<blank|����>>` �� `(����)`
            text = Regex.Replace(text, @"<<blank\|(.*?)>>", "( )");

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
        // �摜��ǉ�
        private async Task AddImage(Editor editor)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "�摜��I��",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                // �摜�ԍ����擾
                string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");

                // �摜�ԍ���ǂݎ��
                int currentImageIndex = 1;
                if (File.Exists(cardsFilePath))
                {
                    var lines = await File.ReadAllLinesAsync(cardsFilePath);
                    if (lines.Length > 0 && int.TryParse(lines[0], out int savedIndex))
                    {
                        currentImageIndex = savedIndex + 1;  // �摜�ԍ����C���N�������g
                    }
                }

                // �摜�̕ۑ���ƃt�@�C����
                string imageFolder = Path.Combine(tempExtractPath, "img");
                Directory.CreateDirectory(imageFolder);

                string newFileName = $"img{currentImageIndex}.png";
                string newFilePath = Path.Combine(imageFolder, newFileName);

                // �摜��ۑ�
                using (var sourceStream = File.OpenRead(result.FullPath))
                using (var destinationStream = File.Create(newFilePath))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                imagePaths.Add(newFilePath);

                // `cards.txt` �̐擪�ɉ摜�ԍ����X�V
                var newLines = new List<string> { currentImageIndex.ToString() };
                if (File.Exists(cardsFilePath))
                {
                    newLines.AddRange(File.ReadAllLines(cardsFilePath).Skip(1));
                }
                await File.WriteAllLinesAsync(cardsFilePath, newLines);

                // �G�f�B�^�� `<<img{n}>>` ��}��
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

        // �J�[�h��ۑ�
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
                    string isCorrect = checkBox?.IsChecked == true ? "����" : "�s����";
                    choices.Add($"{entry.Text} ({isCorrect})");
                }
            }

            if (cardType == "��{�E������" && string.IsNullOrWhiteSpace(frontText))
            {
                await DisplayAlert("�G���[", "�\�ʂ���͂��Ă�������", "OK");
                return;
            }

            // cards.txt �̃p�X
            string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");

            // ��萔���擾
            int cardCount = await GetCardCountAsync(cardsFilePath);
            cardCount++;  // �J�E���g���C���N�������g

            // �t�@�C���̓��e��ǂݍ���
            var lines = new List<string>();

            if (File.Exists(cardsFilePath))
            {
                lines = (await File.ReadAllLinesAsync(cardsFilePath)).ToList();

                // ��萔��2�s�ڂɍX�V
                if (lines.Count >= 2)
                {
                    lines[1] = cardCount.ToString();
                }
                else
                {
                    // �摜�ԍ����Ȃ��ꍇ�̓f�t�H���g�l�ō쐬
                    lines.Insert(0, "0");  // �摜�ԍ�
                    lines.Insert(1, cardCount.ToString());  // ��萔
                }
            }
            else
            {
                // ����쐬
                lines.Add("0");               // �摜�ԍ�
                lines.Add("1");               // ��萔
            }

            // �J�[�h����ǉ�
            lines.Add("---");
            lines.Add($"�J�[�h�^�C�v: {cardType}");

            if (cardType == "��{�E������")
            {
                lines.Add($"�\��: {frontText}");
                lines.Add($"����: {backText}");
            }
            else if (cardType == "�I����")
            {
                lines.Add($"���: {choiceQuestion}");
                lines.Add($"���: {choiceExplanation}");

                foreach (var choice in choices)
                {
                    lines.Add($"�I����: {choice}");
                }
            }
            else if (cardType == "�摜������")
            {
                lines.Add($"�摜: {selectedImagePath}");

                foreach (var rect in selectionRects)
                {
                    lines.Add($"�͈�: {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
                }
            }

            // �t�@�C���ɕۑ�
            await File.WriteAllLinesAsync(cardsFilePath, lines);

            // .ankpls ���X�V
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

            await DisplayAlert("����", "�J�[�h��ۑ����܂���", "OK");
            await Navigation.PopAsync();
        }
    }
}
