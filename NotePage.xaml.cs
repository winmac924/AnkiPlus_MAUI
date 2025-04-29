using System;
using System.IO;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using AnkiPlus_MAUI.Views;

namespace AnkiPlus_MAUI
{
    public partial class NotePage : ContentPage
    {
        private List<DrawingCanvas> _drawingCanvases;
        private DrawingCanvas _activeCanvas;
        private readonly string _noteName;
        private string tempExtractPath; // 一時展開パス
        private string ankplsFilePath;  // .ankplsファイルのパス

        public NotePage(string noteName, string tempPath)
        {
            _noteName = noteName;
            InitializeComponent();
            _drawingCanvases = new List<DrawingCanvas>();

            // パス設定
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = Path.Combine(documentsPath, "AnkiPlus", $"{_noteName}.ankpls");

            // 一時ディレクトリのパスを設定
            string relativePath = Path.GetRelativePath(Path.Combine(documentsPath, "AnkiPlus"), Path.GetDirectoryName(ankplsFilePath));
            tempExtractPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp",
                "AnkiPlus",
                relativePath,
                $"{_noteName}_temp"
            );

            Debug.WriteLine($"Temporary path: {tempExtractPath}");
            // キャッシュディレクトリの初期化
            InitializeCacheDirectory();
        }

        private void InitializeCacheDirectory()
        {
            if (!Directory.Exists(tempExtractPath))
            {
                Directory.CreateDirectory(tempExtractPath);
                Directory.CreateDirectory(Path.Combine(tempExtractPath, "PageCache"));
            }
            else
            {
                // キャッシュが存在する場合は、drawing_data.jsonを確認
                var drawingDataPath = Path.Combine(tempExtractPath, "drawing_data.json");
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
                canvas.SetTool(DrawingTool.Pen);
            }
        }

        private void OnMarkerClicked(object sender, EventArgs e)
        {
            foreach (var canvas in _drawingCanvases)
            {
                canvas.SetTool(DrawingTool.Marker);
            }
        }

        private void OnEraserClicked(object sender, EventArgs e)
        {
            foreach (var canvas in _drawingCanvases)
            {
                canvas.SetTool(DrawingTool.Eraser);
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

                // 新しいPDFを読み込む
                var newCanvas = new DrawingCanvas();
                newCanvas.InitializeCacheDirectory(_noteName);
                newCanvas.ParentScrollView = MainScrollView;

                // キャンバスのタッチイベントを設定
                newCanvas.Touch += (s, e) =>
                {
                    if (e.ActionType == SKTouchAction.Pressed && e.MouseButton == SKMouseButton.Right)
                    {
                        // 右クリックでコンテキストメニューを表示
                        ShowContextMenu(e.Location);
                    }
                    _activeCanvas = newCanvas;
                };

                _drawingCanvases.Add(newCanvas);
                PageContainer.Children.Add(newCanvas);

                await newCanvas.LoadPdfAsync(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PDF: {ex.Message}");
            }
        }

        private async void ShowContextMenu(SKPoint location)
        {
            var action = await DisplayActionSheet(
                "操作を選択",
                "キャンセル",
                null,
                "元に戻す",
                "やり直す",
                "クリア"
            );

            switch (action)
            {
                case "元に戻す":
                    _activeCanvas?.Undo();
                    break;
                case "やり直す":
                    _activeCanvas?.Redo();
                    break;
                case "クリア":
                    _activeCanvas?.Clear();
                    break;
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
                canvas.Touch += (s, e) =>
                {
                    if (e.ActionType == SKTouchAction.Pressed && e.MouseButton == SKMouseButton.Right)
                    {
                        // 右クリックでコンテキストメニューを表示
                        ShowContextMenu(e.Location);
                    }
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

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            foreach (var canvas in _drawingCanvases)
            {
                canvas.Dispose();
            }
        }
    }
}

