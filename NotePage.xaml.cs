using System;
using System.IO;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SkiaSharp.Views.Maui.Controls;
using AnkiPlus_MAUI.Views;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AnkiPlus_MAUI
{
    public partial class NotePage : ContentPage
    {
        private List<DrawingCanvas> _drawingCanvases;
        private DrawingCanvas _activeCanvas;
        private readonly Dictionary<string, SKColor> _colors = new Dictionary<string, SKColor>
        {
            { "黒", SKColors.Black },
            { "白", SKColors.White },
            { "赤", SKColors.Red },
            { "青", SKColors.Blue },
            { "緑", SKColors.Green },
            { "黄", SKColors.Yellow },
            { "オレンジ", SKColors.Orange }
        };
        private bool _isColorBox1Selected = true;
        private bool _isPickerVisible = false;
        private readonly string _noteName;

        public NotePage(string noteName)
        {
            _noteName = noteName;
            InitializeComponent();
            _drawingCanvases = new List<DrawingCanvas>();
            
            // Initialize color picker items
            ColorPicker.ItemsSource = _colors.Keys.ToList();

            // キャッシュディレクトリの初期化
            InitializeCacheDirectory();
        }

        private void InitializeCacheDirectory()
        {
            var tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "AnkiPlus",
                $"{_noteName}_temp"
            );

            if (!Directory.Exists(tempDirectory))
            {
                Directory.CreateDirectory(tempDirectory);
                Directory.CreateDirectory(Path.Combine(tempDirectory, "PageCache"));
            }
            else
            {
                // キャッシュが存在する場合は、drawing_data.jsonを確認
                var drawingDataPath = Path.Combine(tempDirectory, "drawing_data.json");
                if (File.Exists(drawingDataPath))
                {
                    try
                    {
                        var json = File.ReadAllText(drawingDataPath);
                        var drawingData = System.Text.Json.JsonSerializer.Deserialize<DrawingCanvas.DrawingData>(json);
                        if (drawingData?.PdfFilePath != null && File.Exists(drawingData.PdfFilePath))
                        {
                            // PDFファイルが存在する場合は読み込みを開始
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                await LoadPdfAsync(drawingData.PdfFilePath);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading cached data: {ex.Message}");
                    }
                }
            }
        }

        private void OnPenClicked(object sender, EventArgs e)
        {
            foreach (var canvas in _drawingCanvases)
            {
                canvas.SetTool(DrawingCanvas.DrawingTool.Pen);
            }
        }

        private void OnMarkerClicked(object sender, EventArgs e)
        {
            foreach (var canvas in _drawingCanvases)
            {
                canvas.SetTool(DrawingCanvas.DrawingTool.Marker);
            }
        }

        private void OnEraserClicked(object sender, EventArgs e)
        {
            foreach (var canvas in _drawingCanvases)
            {
                canvas.SetTool(DrawingCanvas.DrawingTool.Eraser);
            }
        }

        private void OnRulerClicked(object sender, EventArgs e)
        {
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            _activeCanvas?.Undo();
        }

        private void OnRedoClicked(object sender, EventArgs e)
        {
            _activeCanvas?.Redo();
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            _activeCanvas?.Clear();
        }

        private void OnColorBox1Tapped(object sender, EventArgs e)
        {
            if (sender is Frame frame)
            {
                var position = frame.Bounds.Location;
                _activeCanvas?.ShowColorMenu(new SKPoint((float)position.X, (float)position.Y));
            }
        }

        private void OnColorBox2Tapped(object sender, EventArgs e)
        {
            if (sender is Frame frame)
            {
                var position = frame.Bounds.Location;
                _activeCanvas?.ShowColorMenu(new SKPoint((float)position.X, (float)position.Y));
            }
        }

        private void OnColorPickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (ColorPicker.SelectedItem == null) return;

            var selectedColorName = ColorPicker.SelectedItem.ToString();
            if (_colors.TryGetValue(selectedColorName, out var color))
            {
                var mauiColor = new Color(color.Red / 255f, color.Green / 255f, color.Blue / 255f);
                
                if (_isColorBox1Selected)
                {
                    ColorBox1.BackgroundColor = mauiColor;
                }
                else
                {
                    ColorBox2.BackgroundColor = mauiColor;
                }
                
                foreach (var canvas in _drawingCanvases)
                {
                    canvas.SetPenColor(color);
                    canvas.SetTool(DrawingCanvas.DrawingTool.Pen);
                }
            }
        }

        private void UpdateColorBoxBorders()
        {
            var selectedBorderColor = Colors.Black;
            var unselectedBorderColor = Colors.Gray;

            ColorFrame1.BorderColor = _isColorBox1Selected ? selectedBorderColor : unselectedBorderColor;
            ColorFrame2.BorderColor = !_isColorBox1Selected ? selectedBorderColor : unselectedBorderColor;
        }

        private async Task ShowColorPicker()
        {
            ColorPicker.Focus();
        }

        private async void OnImportClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync();
                if (result != null)
                {
                    if (result.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        await LoadPdfAsync(result.FullPath);
                    }
                    else
                    {
                        await LoadImageAsync(result.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error importing file: {ex.Message}");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync();
                if (result != null)
                {
                    foreach (var canvas in _drawingCanvases)
                    {
                        await canvas.SaveDrawingDataAsync(result.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving file: {ex.Message}");
            }
        }

        private async Task LoadPdfAsync(string filePath)
        {
            try
            {
                // 既存のキャンバスをクリア
                foreach (var canvas in _drawingCanvases)
                {
                    canvas.Dispose();
                }
                _drawingCanvases.Clear();
                PageContainer.Children.Clear();

                // キャッシュディレクトリのパスを取得
                var tempDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "AnkiPlus",
                    $"{_noteName}_temp"
                );

                // drawing_data.jsonを読み込む
                var drawingDataPath = Path.Combine(tempDirectory, "drawing_data.json");
                DrawingCanvas.DrawingData drawingData = null;
                if (File.Exists(drawingDataPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(drawingDataPath);
                        drawingData = System.Text.Json.JsonSerializer.Deserialize<DrawingCanvas.DrawingData>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading drawing data: {ex.Message}");
                    }
                }

                // 新しいPDFを読み込む
                var newCanvas = new DrawingCanvas();
                newCanvas.InitializeCacheDirectory(_noteName);
                newCanvas.ParentScrollView = MainScrollView;
                
                // キャンバスのタッチイベントを設定
                newCanvas.Touch += (s, e) => {
                    _activeCanvas = newCanvas;
                };

                _drawingCanvases.Add(newCanvas);
                PageContainer.Children.Add(newCanvas);

                // PDFを読み込む前に、保存された描画データを設定
                if (drawingData != null)
                {
                    newCanvas.SetDrawingData(drawingData);
                }

                await newCanvas.LoadPdfAsync(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PDF: {ex.Message}");
            }
        }

        private async Task LoadImageAsync(string filePath)
        {
            try
            {
                var canvas = new DrawingCanvas();
                canvas.InitializeCacheDirectory(_noteName);
                canvas.ParentScrollView = MainScrollView;
                
                // キャンバスのタッチイベントを設定
                canvas.Touch += (s, e) => {
                    _activeCanvas = canvas;
                };

                _drawingCanvases.Add(canvas);
                PageContainer.Children.Add(canvas);

                await canvas.LoadImageAsync(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image: {ex.Message}");
            }
        }

        private void OnTextClicked(object sender, EventArgs e)
        {
            foreach (var canvas in _drawingCanvases)
            {
                canvas.SetTool(DrawingCanvas.DrawingTool.Text);
            }
        }

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();
            if (Handler != null)
            {
                // キーボードイベントのハンドラを追加
                if (Handler.PlatformView is Microsoft.Maui.Controls.Page page)
                {
                    page.Focused += OnPageFocused;
                }
            }
        }

        private void OnPageFocused(object sender, FocusEventArgs e)
        {
            if (e.IsFocused)
            {
                // ページがフォーカスされた時の処理
                // 必要に応じて実装
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (Handler != null)
            {
                // イベントハンドラを削除
                if (Handler.PlatformView is Microsoft.Maui.Controls.Page page)
                {
                    page.Focused -= OnPageFocused;
                }
            }
            foreach (var canvas in _drawingCanvases)
            {
                canvas.Dispose();
            }
        }
    }
}

