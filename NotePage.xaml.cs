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
using AnkiPlus_MAUI.Drawing;
using System.IO.Compression;
using System.Text.Json;
using System.Linq;
using AnkiPlus_MAUI.Models;  // SentenceDataの名前空間を追加


namespace AnkiPlus_MAUI
{
    public partial class NotePage : ContentPage, IDisposable
    {
        private BackgroundCanvas _backgroundCanvas;
        private DrawingLayer _drawingLayer;
        private TextSelectionLayer _textSelectionLayer;
        private readonly string _noteName;
        private string tempExtractPath; // 一時展開パス
        private string ankplsFilePath;  // .ankplsファイルのパス
        
        // カード追加機能用
        private bool _isAddCardVisible = false;
        private Picker _cardTypePicker;
        private Editor _frontTextEditor, _backTextEditor;
        private WebView _frontPreviewWebView, _backPreviewWebView;
        private WebView _choiceQuestionPreviewWebView, _choiceExplanationPreviewWebView;
        private VerticalStackLayout _basicCardLayout;
        private StackLayout _multipleChoiceLayout, _imageFillLayout;
        private Editor _choiceQuestion, _choiceQuestionExplanation;
        private StackLayout _choicesContainer;
        private List<string> _cards = new List<string>();
        private Editor _lastFocusedEditor = null;  // 最後にフォーカスされたエディター
        
        // プレビュー更新のデバウンス用
        private System.Timers.Timer _frontPreviewTimer;
        private System.Timers.Timer _backPreviewTimer;
        private System.Timers.Timer _choiceQuestionPreviewTimer;
        private System.Timers.Timer _choiceExplanationPreviewTimer;
        
        // フラッシュ防止用
        private bool _frontPreviewReady = false;
        private bool _backPreviewReady = false;
        private bool _choiceQuestionPreviewReady = false;
        private bool _choiceExplanationPreviewReady = false;
        
        // 選択肢カード用
        private bool _removeNumbers = false;  // 番号削除のフラグ
        
        // 画像穴埋めカード用
        private string _selectedImagePath = "";
        private List<SkiaSharp.SKRect> _selectionRects = new List<SkiaSharp.SKRect>();
        private List<string> _imagePaths = new List<string>();
        private int _imageCount = 0;
        private SkiaSharp.SKBitmap _imageBitmap;         // 画像を表示するためのビットマップ
        private SkiaSharp.SKPoint _startPoint, _endPoint;
        private bool _isDragging = false;
        private const float HANDLE_SIZE = 15;
        private SkiaSharp.Views.Maui.Controls.SKCanvasView _canvasView;
        private Label _toastLabel; // トースト表示用ラベル
        
        // ページ選択モード用
        private bool _isPageSelectionMode = false;
        private Frame _pageSelectionOverlay;
        private Label _pageSelectionLabel;
        private Button _pageConfirmButton;
        private Button _pageCancelButton;
        private int _selectedPageIndex = -1;
        
        // PDF.jsテキスト選択機能用
        private WebView _pdfTextSelectionWebView;
        private bool _isTextSelectionMode = false;
        private string _selectedText = "";
        private Button _textSelectionButton;
        private Grid _canvasGrid; // レイヤー管理用のGrid

        public NotePage(string noteName, string tempPath)
        {
            _noteName = Path.GetFileNameWithoutExtension(noteName);
            InitializeComponent();

            // ドキュメントパス設定
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = noteName;

            // 一時ディレクトリのパスを設定
            string relativePath = Path.GetRelativePath(Path.Combine(documentsPath, "AnkiPlus"), Path.GetDirectoryName(ankplsFilePath));
            tempExtractPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnkiPlus",
                relativePath,
                $"{_noteName}_temp"
            );

            Debug.WriteLine($"Temporary path: {tempExtractPath}");
            
            // レイヤーを初期化
            InitializeLayers();
            
            // テーマ変更イベントを監視
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        private void InitializeLayers()
        {
            // バックグラウンドキャンバス（PDF/画像表示用）
            _backgroundCanvas = new BackgroundCanvas();
            _backgroundCanvas.ParentScrollView = MainScrollView;
            _backgroundCanvas.InitializeCacheDirectory(_noteName, tempExtractPath);

            // 描画レイヤーを再び有効化
            _drawingLayer = new DrawingLayer();

            // テキスト選択レイヤーを初期化
            _textSelectionLayer = new TextSelectionLayer();
            _textSelectionLayer.SetBackgroundCanvas(_backgroundCanvas);
            _textSelectionLayer.SetParentScrollView(MainScrollView);
            _textSelectionLayer.TextSelected += OnTextSelected;

            // GridでBackgroundCanvas、DrawingLayer、TextSelectionLayerを重ね合わせる
            _canvasGrid = new Grid();
            
            // BackgroundCanvasを追加（最下層）
            _backgroundCanvas.SetValue(Grid.RowProperty, 0);
            _backgroundCanvas.SetValue(Grid.ColumnProperty, 0);
            _canvasGrid.Children.Add(_backgroundCanvas);
            
            // DrawingLayerを追加（中間層）
            _drawingLayer.SetValue(Grid.RowProperty, 0);
            _drawingLayer.SetValue(Grid.ColumnProperty, 0);
            _drawingLayer.HorizontalOptions = LayoutOptions.Fill;
            _drawingLayer.VerticalOptions = LayoutOptions.Fill;
            _canvasGrid.Children.Add(_drawingLayer);

            // TextSelectionLayerを追加（最上層）
            _textSelectionLayer.SetValue(Grid.RowProperty, 0);
            _textSelectionLayer.SetValue(Grid.ColumnProperty, 0);
            _textSelectionLayer.HorizontalOptions = LayoutOptions.Fill;
            _textSelectionLayer.VerticalOptions = LayoutOptions.Fill;
            _canvasGrid.Children.Add(_textSelectionLayer);

            // 初期状態ではDrawingLayerを最前面に配置（テキスト選択モードでない）
            UpdateLayerOrder();

            // GridをPageContainerに追加
            PageContainer.Children.Clear();
            PageContainer.Children.Add(_canvasGrid);
            
            Debug.WriteLine($"BackgroundCanvasとDrawingLayerを重ね合わせて初期化");
            Debug.WriteLine($"BackgroundCanvas初期化状態: HasContent={_backgroundCanvas.HasContent}, PageCount={_backgroundCanvas.PageCount}");
            Debug.WriteLine($"DrawingLayer初期サイズ: {_drawingLayer.WidthRequest}x{_drawingLayer.HeightRequest}");
            
            // スクロールイベントハンドラーを追加
            MainScrollView.Scrolled += OnMainScrollViewScrolled;
            
            // キャッシュディレクトリの初期化と保存データの復元
            InitializeCacheDirectory();
        }

        // レイヤーの順序を更新するメソッド
        private void UpdateLayerOrder()
        {
            if (_canvasGrid == null || _drawingLayer == null || _textSelectionLayer == null)
                return;

            // 現在の子要素をクリア
            _canvasGrid.Children.Clear();

            // BackgroundCanvasを最下層に追加
            _backgroundCanvas.SetValue(Grid.RowProperty, 0);
            _backgroundCanvas.SetValue(Grid.ColumnProperty, 0);
            _canvasGrid.Children.Add(_backgroundCanvas);

            if (_isTextSelectionMode)
            {
                // テキスト選択モード: DrawingLayer → TextSelectionLayer の順
                _drawingLayer.SetValue(Grid.RowProperty, 0);
                _drawingLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_drawingLayer);

                _textSelectionLayer.SetValue(Grid.RowProperty, 0);
                _textSelectionLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_textSelectionLayer);
                
                Debug.WriteLine("レイヤー順序: BackgroundCanvas → DrawingLayer → TextSelectionLayer（最前面）");
            }
            else
            {
                // 描画モード: TextSelectionLayer → DrawingLayer の順
                _textSelectionLayer.SetValue(Grid.RowProperty, 0);
                _textSelectionLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_textSelectionLayer);

                _drawingLayer.SetValue(Grid.RowProperty, 0);
                _drawingLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_drawingLayer);
                
                Debug.WriteLine("レイヤー順序: BackgroundCanvas → TextSelectionLayer → DrawingLayer（最前面）");
            }
        }

        private async void InitializeCacheDirectory()
        {
            if (!Directory.Exists(tempExtractPath))
            {
                Directory.CreateDirectory(tempExtractPath);
                Directory.CreateDirectory(Path.Combine(tempExtractPath, "PageCache"));
                Debug.WriteLine($"一時ディレクトリを作成: {tempExtractPath}");
            }
            else
            {
                Debug.WriteLine($"既存の一時ディレクトリを使用: {tempExtractPath}");
                
                // ディレクトリ内のファイルをリスト表示
                var files = Directory.GetFiles(tempExtractPath);
                Debug.WriteLine($"一時ディレクトリ内のファイル: {string.Join(", ", files.Select(Path.GetFileName))}");
            }
            
            // 保存されたコンテンツデータを確認
            var contentDataPath = Path.Combine(tempExtractPath, "content_data.json");
            bool backgroundLoaded = false;
            
            if (File.Exists(contentDataPath))
                {
                    try
                    {
                    var json = await File.ReadAllTextAsync(contentDataPath);
                    Debug.WriteLine($"content_data.jsonの内容: {json}");
                    var contentData = System.Text.Json.JsonSerializer.Deserialize<BackgroundCanvas.ContentData>(json);
                    
                    if (contentData != null)
                    {
                        Debug.WriteLine($"保存されたコンテンツデータを発見: PDF={contentData.PdfFilePath}, Image={contentData.ImageFilePath}");
                        
                        // PDFまたは画像ファイルが存在する場合は読み込み
                        if (!string.IsNullOrEmpty(contentData.PdfFilePath) && File.Exists(contentData.PdfFilePath))
                        {
                            Debug.WriteLine($"PDFファイルを自動読み込み: {contentData.PdfFilePath}");
                            await LoadPdfAsync(contentData.PdfFilePath);
                            backgroundLoaded = true;
                            
                            // PDF自動読み込み後、テキスト選択モードを有効化
                            if (_textSelectionLayer != null)
                            {
                                Debug.WriteLine("🚀 PDF自動読み込み完了 - テキスト選択モード有効化");
                                await Task.Delay(500); // レイヤー初期化を待つ
                                _textSelectionLayer.EnableTextSelection();
                                _isTextSelectionMode = true;
                                UpdateLayerOrder(); // レイヤー順序を更新
                            }
                        }
                        else if (!string.IsNullOrEmpty(contentData.ImageFilePath) && File.Exists(contentData.ImageFilePath))
                        {
                            Debug.WriteLine($"画像ファイルを自動読み込み: {contentData.ImageFilePath}");
                            await LoadImageAsync(contentData.ImageFilePath);
                            backgroundLoaded = true;
                        }
                        else
                        {
                            Debug.WriteLine("有効なPDF/画像ファイルが見つかりません");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("contentDataのデシリアライズに失敗");
                    }
                }
                catch (Exception ex)
                    {
                    Debug.WriteLine($"コンテンツデータの読み込みエラー: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("content_data.jsonが存在しません - DrawingLayerのみ初期化します");
            }
            
            // 背景が読み込まれていない場合は、DrawingLayerを手動で初期化
            if (!backgroundLoaded && _drawingLayer != null)
            {
                Debug.WriteLine("背景なしでDrawingLayerを初期化");
                // BackgroundCanvas の BASE_CANVAS_WIDTH と同じ値を基準にする
                const float defaultBaseWidth = 600f; 
                
                await _drawingLayer.InitializeAsync(_noteName, tempExtractPath);
                Debug.WriteLine($"DrawingLayer初期化完了: サイズ {_drawingLayer.WidthRequest}x{_drawingLayer.HeightRequest}");
            }
        }

        private void OnPenClicked(object sender, EventArgs e)
        {
            _drawingLayer?.SetTool(AnkiPlus_MAUI.Drawing.DrawingTool.Pen);
            Debug.WriteLine("ペンツールに切り替え");
        }

        private void OnMarkerClicked(object sender, EventArgs e)
        {
            _drawingLayer?.SetTool(AnkiPlus_MAUI.Drawing.DrawingTool.Marker);
            Debug.WriteLine("マーカーツールに切り替え");
        }

        private void OnEraserClicked(object sender, EventArgs e)
        {
            _drawingLayer?.SetTool(AnkiPlus_MAUI.Drawing.DrawingTool.Eraser);
            Debug.WriteLine("消しゴムツールに切り替え");
        }

        private void OnRulerClicked(object sender, EventArgs e)
        {
            // TODO: 定規機能の実装
            Debug.WriteLine("定規ツールクリック（未実装）");
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            _drawingLayer?.Undo();
            Debug.WriteLine("元に戻す実行");
        }

        private void OnRedoClicked(object sender, EventArgs e)
        {
            _drawingLayer?.Redo();
            Debug.WriteLine("やり直し実行");
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            _drawingLayer?.Clear();
            Debug.WriteLine("描画クリア実行");
        }

        private async void OnTextSelectionClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("テキスト選択ボタンがクリックされました");
                
                if (_textSelectionLayer != null)
                {
                    if (_isTextSelectionMode)
                    {
                        // テキスト選択モードを無効化
                        _textSelectionLayer.DisableTextSelection();
                        _isTextSelectionMode = false;
                        UpdateLayerOrder(); // レイヤー順序を更新（DrawingLayerを最前面に）
                        await ShowToast("テキスト選択モードを無効にしました - 描画モード");
                    }
                    else
                    {
                        // テキスト選択モードを有効化
                        _textSelectionLayer.EnableTextSelection();
                        _isTextSelectionMode = true;
                        UpdateLayerOrder(); // レイヤー順序を更新（TextSelectionLayerを最前面に）
                        await ShowToast("テキスト選択モードを有効にしました");
                    }
                }
                else
                {
                    await ShowToast("テキスト選択レイヤーが初期化されていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択ボタンエラー: {ex.Message}");
                await DisplayAlert("エラー", "テキスト選択機能でエラーが発生しました", "OK");
            }
        }

        private async void OnTextSelected(object sender, TextSelectedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"テキストが選択されました: '{e.SelectedText}'");
                _selectedText = e.SelectedText;
                
                // 選択されたテキストをトーストで表示
                await ShowToast($"選択: {e.SelectedText.Substring(0, Math.Min(30, e.SelectedText.Length))}...");
                
                // 必要に応じて、選択されたテキストを他の機能で使用
                // 例：カード作成時に自動入力など
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択イベントエラー: {ex.Message}");
            }
        }


        private async void OnAddCardClicked(object sender, EventArgs e)
        {
            if (_isAddCardVisible)
            {
                // カード追加パネルを閉じる
                await HideAddCardPanel();
            }
            else
            {
                // カード追加パネルを表示
                await ShowAddCardPanel();
            }
        }

        private void OnZoomSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            var scale = (float)e.NewValue;
            
            // BackgroundCanvasの拡大倍率を先に設定
            if (_backgroundCanvas != null)
            {
                _backgroundCanvas.CurrentScale = scale;
            }
            
            // DrawingLayerとBackgroundCanvasの座標系を同期
            if (_drawingLayer != null && _backgroundCanvas != null)
            {
                // BackgroundCanvasから座標系情報を取得して同期
                var totalHeight = GetBackgroundCanvasTotalHeight();
                _drawingLayer.SyncWithBackgroundCanvas(totalHeight, scale);
            }
            
            Debug.WriteLine($"ズーム倍率変更: {scale:F2} ({(int)(scale * 100)}%)");
        }

        private async Task LoadPdfAsync(string filePath)
        {
            try
            {
                if (_backgroundCanvas != null)
                {
                    await _backgroundCanvas.LoadPdfAsync(filePath);

                    // 描画レイヤーとBackgroundCanvasの座標系を同期
                    if (_drawingLayer != null)
                    {
                        // BackgroundCanvasから座標系情報を取得して同期
                        var totalHeight = GetBackgroundCanvasTotalHeight();
                        _drawingLayer.SyncWithBackgroundCanvas(totalHeight, _backgroundCanvas.CurrentScale);
                        
                        // 一時ディレクトリと描画データの初期化
                        await _drawingLayer.InitializeAsync(_noteName, tempExtractPath);
                    }

                    // コンテンツデータを保存
                    await SaveContentDataAsync(filePath, null);
                    
                    Debug.WriteLine($"PDF読み込み完了: {filePath}");
                    
                    // PDF読み込み完了後、自動的にテキスト選択モードを有効化
                    if (_textSelectionLayer != null)
                    {
                        Debug.WriteLine("🚀 PDF読み込み完了 - 自動テキスト選択モード有効化");
                        _textSelectionLayer.EnableTextSelection();
                        _isTextSelectionMode = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PDF: {ex.Message}");
                await DisplayAlert("エラー", $"PDFの読み込みに失敗しました: {ex.Message}", "OK");
            }
        }

        private async Task LoadImageAsync(string filePath)
        {
            try
            {
                if (_backgroundCanvas != null)
                {
                    await _backgroundCanvas.LoadImageAsync(filePath);

                    // 描画レイヤーのサイズを背景に合わせる
                    if (_drawingLayer != null)
                    {
                        _drawingLayer.CurrentScale = _backgroundCanvas.CurrentScale; // BackgroundCanvasのスケールに合わせる

                        // サイズを同期
                        _drawingLayer.WidthRequest = _backgroundCanvas.WidthRequest;
                        _drawingLayer.HeightRequest = _backgroundCanvas.HeightRequest;
                        
                        // 一時ディレクトリと描画データの初期化
                        await _drawingLayer.InitializeAsync(_noteName, tempExtractPath);
                    }

                    // コンテンツデータを保存
                    await SaveContentDataAsync(null, filePath);
                    
                    Debug.WriteLine($"画像読み込み完了: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image: {ex.Message}");
                await DisplayAlert("エラー", $"画像の読み込みに失敗しました: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// コンテンツデータを保存する
        /// </summary>
        private async Task SaveContentDataAsync(string pdfPath, string imagePath)
        {
            try
            {
                var contentData = new BackgroundCanvas.ContentData
                {
                    PdfFilePath = pdfPath,
                    ImageFilePath = imagePath,
                    LastScrollY = 0
                };

                var jsonData = System.Text.Json.JsonSerializer.Serialize(contentData);
                var saveFilePath = Path.Combine(tempExtractPath, "content_data.json");
                
                // ディレクトリが存在しない場合は作成
                var directory = Path.GetDirectoryName(saveFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllTextAsync(saveFilePath, jsonData);
                Debug.WriteLine($"コンテンツデータを保存: {saveFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンテンツデータの保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// BackgroundCanvasの総高さを取得（リフレクションを使用）
        /// </summary>
        private float GetBackgroundCanvasTotalHeight()
        {
            try
            {
                // BackgroundCanvasの_totalHeightフィールドにアクセス
                var field = typeof(BackgroundCanvas).GetField("_totalHeight", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    var totalHeight = (float)field.GetValue(_backgroundCanvas);
                    Debug.WriteLine($"BackgroundCanvas総高さ取得: {totalHeight}");
                    return totalHeight;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"総高さ取得エラー: {ex.Message}");
            }
            
            // フォールバック：デフォルト値
            return 600f * (4.0f / 3.0f); // 4:3のアスペクト比
        }

        private async void OnImportClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync();
                if (result != null)
                {
                    Debug.WriteLine($"ファイル選択: {result.FileName}");
                    
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
                await DisplayAlert("エラー", $"ファイルのインポートに失敗しました: {ex.Message}", "OK");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                // 描画データを保存
                if (_drawingLayer != null)
                {
                    await _drawingLayer.SaveAsync();
                    Debug.WriteLine("描画データを手動保存");
                }
                
                // 現在のコンテンツデータも保存（何かファイルが読み込まれている場合）
                // 注意: この時点では具体的なファイルパスが分からないため、
                // 実際のファイルが読み込まれた時にSaveContentDataAsyncが呼ばれることを想定
                
                await DisplayAlert("保存完了", "描画データを保存しました", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving file: {ex.Message}");
                await DisplayAlert("エラー", $"保存に失敗しました: {ex.Message}", "OK");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Debug.WriteLine("NotePage表示開始");
            // 初期化はInitializeCacheDirectoryで実行済み
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Debug.WriteLine("NotePage非表示開始");
            
            // 自動保存を同期的に実行
            try
            {
                if (_drawingLayer != null)
                {
                    // 同期的に保存処理を実行
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _drawingLayer.SaveAsync();
                            Debug.WriteLine("描画データを自動保存完了");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"描画データ保存エラー: {ex.Message}");
                        }
                    }).Wait(TimeSpan.FromSeconds(5)); // 最大5秒待機
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動保存エラー: {ex.Message}");
            }

            // リソース解放を確実に実行
            try
            {
                Debug.WriteLine("リソース解放開始");
                
                // タイマーを停止・解放
                _frontPreviewTimer?.Stop();
                _frontPreviewTimer?.Dispose();
                _frontPreviewTimer = null;
                
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                _backPreviewTimer = null;
                
                _choiceQuestionPreviewTimer?.Stop();
                _choiceQuestionPreviewTimer?.Dispose();
                _choiceQuestionPreviewTimer = null;
                
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                _choiceExplanationPreviewTimer = null;
                
                // イベントハンドラーを解除
                if (_frontPreviewWebView != null)
                {
                    _frontPreviewWebView.Navigated -= OnFrontPreviewNavigated;
                }
                
                if (_backPreviewWebView != null)
                {
                    _backPreviewWebView.Navigated -= OnBackPreviewNavigated;
                }
                
                if (_backgroundCanvas != null)
                {
                    _backgroundCanvas.Dispose();
                    _backgroundCanvas = null;
                    Debug.WriteLine("BackgroundCanvasを解放");
                }
                
                if (_drawingLayer != null)
                {
                    _drawingLayer.Dispose();
                    _drawingLayer = null;
                    Debug.WriteLine("DrawingLayerを解放");
                }
                
                // ガベージコレクションを促進
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                Debug.WriteLine("NotePage非表示完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リソース解放エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// カード追加パネルを表示
        /// </summary>
        private async Task ShowAddCardPanel()
        {
            try
            {
                // カード追加UIを初期化
                InitializeAddCardUI();
                
                // アニメーション：キャンバスを左に移動、カード追加パネルを表示
                var canvasColumn = FindByName("CanvasColumn") as ColumnDefinition;
                var addCardColumn = FindByName("AddCardColumn") as ColumnDefinition;
                var addCardScrollView = FindByName("AddCardScrollView") as ScrollView;
                
                if (canvasColumn != null && addCardColumn != null && addCardScrollView != null)
                {
                    // カード追加パネルを表示
                    addCardScrollView.IsVisible = true;
                    
                    // アニメーション：キャンバスを50%、カード追加を50%に
                    var animation = new Animation();
                    animation.Add(0, 1, new Animation(v => canvasColumn.Width = new GridLength(v, GridUnitType.Star), 1, 0.5));
                    animation.Add(0, 1, new Animation(v => addCardColumn.Width = new GridLength(v, GridUnitType.Star), 0, 0.5));
                    
                    animation.Commit(this, "ShowAddCard", 16, 300, Easing.CubicOut);
                    
                    _isAddCardVisible = true;
                    Debug.WriteLine("カード追加パネルを表示");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加パネル表示エラー: {ex.Message}");
            }
        }
        
        /// <summary>
        /// カード追加パネルを非表示
        /// </summary>
        private async Task HideAddCardPanel()
        {
            try
            {
                var canvasColumn = FindByName("CanvasColumn") as ColumnDefinition;
                var addCardColumn = FindByName("AddCardColumn") as ColumnDefinition;
                var addCardScrollView = FindByName("AddCardScrollView") as ScrollView;
                
                if (canvasColumn != null && addCardColumn != null && addCardScrollView != null)
                {
                    // アニメーション：キャンバスを100%、カード追加を0%に
                    var animation = new Animation();
                    animation.Add(0, 1, new Animation(v => canvasColumn.Width = new GridLength(v, GridUnitType.Star), 0.5, 1));
                    animation.Add(0, 1, new Animation(v => addCardColumn.Width = new GridLength(v, GridUnitType.Star), 0.5, 0));
                    
                    animation.Commit(this, "HideAddCard", 16, 300, Easing.CubicOut, (v, c) =>
                    {
                        // アニメーション完了後にパネルを非表示
                        addCardScrollView.IsVisible = false;
                    });
                    
                    _isAddCardVisible = false;
                    Debug.WriteLine("カード追加パネルを非表示");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加パネル非表示エラー: {ex.Message}");
            }
        }
        
        /// <summary>
        /// カード追加UIを初期化
        /// </summary>
        private void InitializeAddCardUI()
        {
            try
            {
                var container = FindByName("AddCardContainer") as VerticalStackLayout;
                if (container == null) return;
                
                // 既存の内容をクリア
                container.Children.Clear();
                
                // カードタイプ選択
                _cardTypePicker = new Picker
                {
                    Title = "カードタイプを選択",
                    HorizontalOptions = LayoutOptions.Fill
                };
                _cardTypePicker.Items.Add("基本・穴埋め");
                _cardTypePicker.Items.Add("選択肢");
                _cardTypePicker.Items.Add("画像穴埋め");
                _cardTypePicker.SelectedIndex = 0;
                _cardTypePicker.SelectedIndexChanged += OnCardTypeChanged;
                
                container.Children.Add(_cardTypePicker);
                
                // 基本・穴埋めカード入力
                _basicCardLayout = new VerticalStackLayout();
                
                // 表面
                var frontHeaderLayout = new HorizontalStackLayout();
                var frontLabel = new Label { Text = "表面", FontSize = 16 };
                var frontImageButton = new Button 
                { 
                    Text = "画像を追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                frontImageButton.Clicked += OnFrontAddImageClicked;
                var frontPageImageButton = new Button 
                { 
                    Text = "ページを画像として追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                frontPageImageButton.Clicked += OnFrontAddPageImageClicked;
                
                frontHeaderLayout.Children.Add(frontLabel);
                frontHeaderLayout.Children.Add(frontImageButton);
                frontHeaderLayout.Children.Add(frontPageImageButton);
                
                // 装飾ボタン群
                var decorationButtonsLayout = new HorizontalStackLayout
                {
                    Spacing = 5,
                    Margin = new Thickness(0, 5)
                };
                
                // 太字ボタン
                var boldButton = new Button
                {
                    Text = "B",
                    FontAttributes = FontAttributes.Bold,
                    WidthRequest = 40,
                    HeightRequest = 35,
                    FontSize = 14
                };
                boldButton.Clicked += OnBoldClicked;
                decorationButtonsLayout.Children.Add(boldButton);
                
                // 色ボタン群
                var redButton = new Button { Text = "赤", BackgroundColor = Colors.Red, TextColor = Colors.White, WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                redButton.Clicked += OnRedColorClicked;
                decorationButtonsLayout.Children.Add(redButton);
                
                var blueButton = new Button { Text = "青", BackgroundColor = Colors.Blue, TextColor = Colors.White, WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                blueButton.Clicked += OnBlueColorClicked;
                decorationButtonsLayout.Children.Add(blueButton);
                
                var greenButton = new Button { Text = "緑", BackgroundColor = Colors.Green, TextColor = Colors.White, WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                greenButton.Clicked += OnGreenColorClicked;
                decorationButtonsLayout.Children.Add(greenButton);
                
                var yellowButton = new Button { Text = "黄", BackgroundColor = Colors.Yellow, TextColor = Colors.Black, WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                yellowButton.Clicked += OnYellowColorClicked;
                decorationButtonsLayout.Children.Add(yellowButton);
                
                var purpleButton = new Button { Text = "紫", BackgroundColor = Colors.Purple, TextColor = Colors.White, WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                purpleButton.Clicked += OnPurpleColorClicked;
                decorationButtonsLayout.Children.Add(purpleButton);
                
                var orangeButton = new Button { Text = "橙", BackgroundColor = Colors.Orange, TextColor = Colors.Black, WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                orangeButton.Clicked += OnOrangeColorClicked;
                decorationButtonsLayout.Children.Add(orangeButton);
                
                // 上付き・下付き・穴埋めボタン
                var supButton = new Button { Text = "x²", WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                supButton.Clicked += OnSuperscriptClicked;
                decorationButtonsLayout.Children.Add(supButton);
                
                var subButton = new Button { Text = "x₂", WidthRequest = 40, HeightRequest = 35, FontSize = 12 };
                subButton.Clicked += OnSubscriptClicked;
                decorationButtonsLayout.Children.Add(subButton);
                
                var blankButton = new Button { Text = "穴埋", WidthRequest = 50, HeightRequest = 35, FontSize = 11 };
                blankButton.Clicked += OnBlankClicked;
                decorationButtonsLayout.Children.Add(blankButton);
                
                _frontTextEditor = new Editor
                {
                    HeightRequest = 80,
                    Placeholder = "表面の内容を入力"
                };
                _frontTextEditor.TextChanged += OnFrontTextChanged;
                _frontTextEditor.Focused += (s, e) => _lastFocusedEditor = _frontTextEditor;
                
                var frontPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
                _frontPreviewWebView = new WebView 
                { 
                    HeightRequest = 80,
                    Opacity = 0 // 初期状態で透明（スペースは保持）
                };
                
                // WebViewの背景色を設定（ダークモード対応）
                SetWebViewBackgroundColor(_frontPreviewWebView);
                
                // WebViewのNavigatedイベントでフラッシュ防止
                _frontPreviewWebView.Navigated += OnFrontPreviewNavigated;
                
                // 裏面
                var backHeaderLayout = new HorizontalStackLayout();
                var backLabel = new Label { Text = "裏面", FontSize = 16 };
                var backImageButton = new Button 
                { 
                    Text = "画像を追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                backImageButton.Clicked += OnBackAddImageClicked;
                var backPageImageButton = new Button 
                { 
                    Text = "ページを画像として追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                backPageImageButton.Clicked += OnBackAddPageImageClicked;
                
                backHeaderLayout.Children.Add(backLabel);
                backHeaderLayout.Children.Add(backImageButton);
                backHeaderLayout.Children.Add(backPageImageButton);
                
                _backTextEditor = new Editor
                {
                    HeightRequest = 80,
                    Placeholder = "Markdown 記法で装飾できます"
                };
                _backTextEditor.TextChanged += OnBackTextChanged;
                _backTextEditor.Focused += (s, e) => _lastFocusedEditor = _backTextEditor;
                
                var backPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
                _backPreviewWebView = new WebView 
                { 
                    HeightRequest = 80,
                    Opacity = 0 // 初期状態で透明（スペースは保持）
                };
                
                // WebViewの背景色を設定（ダークモード対応）
                SetWebViewBackgroundColor(_backPreviewWebView);
                
                // WebViewのNavigatedイベントでフラッシュ防止
                _backPreviewWebView.Navigated += OnBackPreviewNavigated;
                
                _basicCardLayout.Children.Add(frontHeaderLayout);
                _basicCardLayout.Children.Add(decorationButtonsLayout);
                _basicCardLayout.Children.Add(_frontTextEditor);
                _basicCardLayout.Children.Add(frontPreviewLabel);
                _basicCardLayout.Children.Add(_frontPreviewWebView);
                _basicCardLayout.Children.Add(backHeaderLayout);
                _basicCardLayout.Children.Add(_backTextEditor);
                _basicCardLayout.Children.Add(backPreviewLabel);
                _basicCardLayout.Children.Add(_backPreviewWebView);
                
                container.Children.Add(_basicCardLayout);
                
                // 選択肢カード入力（初期状態では非表示）
                _multipleChoiceLayout = new StackLayout { IsVisible = false };
                
                var choiceQuestionHeaderLayout = new HorizontalStackLayout();
                var choiceQuestionLabel = new Label { Text = "選択肢問題", FontSize = 16 };
                var choiceQuestionImageButton = new Button 
                { 
                    Text = "画像を追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                choiceQuestionImageButton.Clicked += OnChoiceQuestionAddImageClicked;
                var choiceQuestionPageImageButton = new Button 
                { 
                    Text = "ページを画像として追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                choiceQuestionPageImageButton.Clicked += OnChoiceQuestionAddPageImageClicked;
                
                choiceQuestionHeaderLayout.Children.Add(choiceQuestionLabel);
                choiceQuestionHeaderLayout.Children.Add(choiceQuestionImageButton);
                choiceQuestionHeaderLayout.Children.Add(choiceQuestionPageImageButton);
                
                _choiceQuestion = new Editor
                {
                    HeightRequest = 80,
                    Placeholder = "問題の内容を入力"
                };
                _choiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
                _choiceQuestion.Focused += (s, e) => _lastFocusedEditor = _choiceQuestion;
                
                var choiceQuestionPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
                _choiceQuestionPreviewWebView = new WebView 
                { 
                    HeightRequest = 80,
                    Opacity = 0 // 初期状態で透明（スペースは保持）
                };
                
                // WebViewの背景色を設定（ダークモード対応）
                SetWebViewBackgroundColor(_choiceQuestionPreviewWebView);
                
                // WebViewのNavigatedイベントでフラッシュ防止
                _choiceQuestionPreviewWebView.Navigated += OnChoiceQuestionPreviewNavigated;
                
                var choiceButtonsLayout = new HorizontalStackLayout();
                var addChoiceButton = new Button { Text = "選択肢を追加" };
                addChoiceButton.Clicked += OnAddChoice;
                
                var removeNumbersLabel = new Label 
                { 
                    Text = "番号を削除", 
                    VerticalOptions = LayoutOptions.Center, 
                    Margin = new Thickness(10, 0, 0, 0) 
                };
                var removeNumbersSwitch = new Microsoft.Maui.Controls.Switch { VerticalOptions = LayoutOptions.Center };
                removeNumbersSwitch.Toggled += OnRemoveNumbersToggled;
                
                choiceButtonsLayout.Children.Add(addChoiceButton);
                choiceButtonsLayout.Children.Add(removeNumbersLabel);
                choiceButtonsLayout.Children.Add(removeNumbersSwitch);
                
                _choicesContainer = new StackLayout();
                
                var choiceExplanationHeaderLayout = new HorizontalStackLayout();
                var choiceExplanationLabel = new Label { Text = "解説", FontSize = 16 };
                var choiceExplanationImageButton = new Button 
                { 
                    Text = "画像を追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                choiceExplanationImageButton.Clicked += OnChoiceExplanationAddImageClicked;
                var choiceExplanationPageImageButton = new Button 
                { 
                    Text = "ページを画像として追加", 
                    HorizontalOptions = LayoutOptions.End 
                };
                choiceExplanationPageImageButton.Clicked += OnChoiceExplanationAddPageImageClicked;
                
                choiceExplanationHeaderLayout.Children.Add(choiceExplanationLabel);
                choiceExplanationHeaderLayout.Children.Add(choiceExplanationImageButton);
                choiceExplanationHeaderLayout.Children.Add(choiceExplanationPageImageButton);
                
                _choiceQuestionExplanation = new Editor
                {
                    HeightRequest = 80,
                    Placeholder = "解説の内容を入力"
                };
                _choiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;
                _choiceQuestionExplanation.Focused += (s, e) => _lastFocusedEditor = _choiceQuestionExplanation;
                
                var choiceExplanationPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
                _choiceExplanationPreviewWebView = new WebView 
                { 
                    HeightRequest = 80,
                    Opacity = 0 // 初期状態で透明（スペースは保持）
                };
                
                // WebViewの背景色を設定（ダークモード対応）
                SetWebViewBackgroundColor(_choiceExplanationPreviewWebView);
                
                // WebViewのNavigatedイベントでフラッシュ防止
                _choiceExplanationPreviewWebView.Navigated += OnChoiceExplanationPreviewNavigated;
                
                _multipleChoiceLayout.Children.Add(choiceQuestionHeaderLayout);
                _multipleChoiceLayout.Children.Add(_choiceQuestion);
                _multipleChoiceLayout.Children.Add(choiceQuestionPreviewLabel);
                _multipleChoiceLayout.Children.Add(_choiceQuestionPreviewWebView);
                _multipleChoiceLayout.Children.Add(choiceButtonsLayout);
                _multipleChoiceLayout.Children.Add(_choicesContainer);
                _multipleChoiceLayout.Children.Add(choiceExplanationHeaderLayout);
                _multipleChoiceLayout.Children.Add(_choiceQuestionExplanation);
                _multipleChoiceLayout.Children.Add(choiceExplanationPreviewLabel);
                _multipleChoiceLayout.Children.Add(_choiceExplanationPreviewWebView);
                
                container.Children.Add(_multipleChoiceLayout);
                
                // 画像穴埋めカード入力（初期状態では非表示）
                _imageFillLayout = new StackLayout { IsVisible = false };
                
                var imageFillLabel = new Label { Text = "画像穴埋め", FontSize = 16 };
                
                // ボタンレイアウト
                var imageButtonsLayout = new HorizontalStackLayout { Spacing = 10 };
                
                var selectImageButton = new Button 
                { 
                    Text = "画像を選択", 
                    HorizontalOptions = LayoutOptions.FillAndExpand
                };
                selectImageButton.Clicked += OnSelectImage;
                
                var selectPageButton = new Button 
                { 
                    Text = "ページを穴埋め", 
                    HorizontalOptions = LayoutOptions.FillAndExpand
                };
                selectPageButton.Clicked += OnSelectPageForImageFill;
                
                var clearImageButton = new Button 
                { 
                    Text = "画像を消去", 
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    BackgroundColor = Colors.Red,
                    TextColor = Colors.White
                };
                clearImageButton.Clicked += OnClearImage;
                
                imageButtonsLayout.Children.Add(selectImageButton);
                imageButtonsLayout.Children.Add(selectPageButton);
                imageButtonsLayout.Children.Add(clearImageButton);
                
                _canvasView = new SkiaSharp.Views.Maui.Controls.SKCanvasView
                {
                    WidthRequest = 400,
                    HeightRequest = 300,
                    HorizontalOptions = LayoutOptions.Fill,
                    IsEnabled = true
                };
                _canvasView.PaintSurface += OnCanvasViewPaintSurface;
                _canvasView.Touch += OnCanvasTouch;
                _canvasView.EnableTouchEvents = true;
                
                _imageFillLayout.Children.Add(imageFillLabel);
                _imageFillLayout.Children.Add(imageButtonsLayout);
                _imageFillLayout.Children.Add(_canvasView);
                
                container.Children.Add(_imageFillLayout);
                
                // 保存ボタン
                var saveButton = new Button
                {
                    Text = "カードを保存",
                    HorizontalOptions = LayoutOptions.Fill,
                    Margin = new Thickness(20, 10)
                };
                saveButton.Clicked += OnSaveCardClicked;
                
                container.Children.Add(saveButton);
                
                Debug.WriteLine("カード追加UI初期化完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加UI初期化エラー: {ex.Message}");
            }
        }
        
        /// <summary>
        /// カードタイプ変更イベント
        /// </summary>
        private void OnCardTypeChanged(object sender, EventArgs e)
        {
            if (_cardTypePicker == null) return;
            
            var selectedType = _cardTypePicker.SelectedItem?.ToString();
            
            // 全てのレイアウトを非表示
            if (_basicCardLayout != null) _basicCardLayout.IsVisible = false;
            if (_multipleChoiceLayout != null) _multipleChoiceLayout.IsVisible = false;
            if (_imageFillLayout != null) _imageFillLayout.IsVisible = false;
            
            // 選択されたタイプに応じてレイアウトを表示
            switch (selectedType)
            {
                case "基本・穴埋め":
                    if (_basicCardLayout != null) _basicCardLayout.IsVisible = true;
                    break;
                case "選択肢":
                    if (_multipleChoiceLayout != null) _multipleChoiceLayout.IsVisible = true;
                    break;
                case "画像穴埋め":
                    if (_imageFillLayout != null) _imageFillLayout.IsVisible = true;
                    break;
            }
            
            Debug.WriteLine($"カードタイプ変更: {selectedType}");
        }
        
        /// <summary>
        /// 表面テキスト変更イベント
        /// </summary>
        private void OnFrontTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFrontPreviewWithDebounce();
        }
        
        /// <summary>
        /// 裏面テキスト変更イベント
        /// </summary>
        private void OnBackTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBackPreviewWithDebounce();
        }
        
        /// <summary>
        /// 選択肢問題テキスト変更イベント
        /// </summary>
        private void OnChoiceQuestionTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateChoiceQuestionPreviewWithDebounce();
        }
        
        /// <summary>
        /// 選択肢解説テキスト変更イベント
        /// </summary>
        private void OnChoiceExplanationTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateChoiceExplanationPreviewWithDebounce();
        }
        
        /// <summary>
        /// 表面プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateFrontPreviewWithDebounce()
        {
            if (_frontTextEditor == null || _frontPreviewWebView == null) return;
            
            // 既存のタイマーを停止
            _frontPreviewTimer?.Stop();
            _frontPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _frontPreviewTimer = new System.Timers.Timer(500);
            _frontPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var markdown = _frontTextEditor.Text ?? "";
                        var html = ConvertMarkdownToHtml(markdown);
                        
                        // フラッシュ防止: 更新前に透明化（スペースは保持）
                        await _frontPreviewWebView.FadeTo(0, 50); // 50ms で透明化
                        
                        // 背景色を再設定してからHTMLを更新
                        SetWebViewBackgroundColor(_frontPreviewWebView);
                        _frontPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                        _frontPreviewReady = false; // ナビゲーション完了を待つ
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"表面プレビュー更新エラー: {ex.Message}");
                    }
                });
                _frontPreviewTimer?.Stop();
                _frontPreviewTimer?.Dispose();
                _frontPreviewTimer = null;
            };
            _frontPreviewTimer.AutoReset = false;
            _frontPreviewTimer.Start();
        }

        /// <summary>
        /// 裏面プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateBackPreviewWithDebounce()
        {
            if (_backTextEditor == null || _backPreviewWebView == null) return;
            
            // 既存のタイマーを停止
            _backPreviewTimer?.Stop();
            _backPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _backPreviewTimer = new System.Timers.Timer(500);
            _backPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var markdown = _backTextEditor.Text ?? "";
                        var html = ConvertMarkdownToHtml(markdown);
                        
                        // フラッシュ防止: 更新前に透明化（スペースは保持）
                        await _backPreviewWebView.FadeTo(0, 50); // 50ms で透明化
                        
                        // 背景色を再設定してからHTMLを更新
                        SetWebViewBackgroundColor(_backPreviewWebView);
                        _backPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                        _backPreviewReady = false; // ナビゲーション完了を待つ
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"裏面プレビュー更新エラー: {ex.Message}");
                    }
                });
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                _backPreviewTimer = null;
            };
            _backPreviewTimer.AutoReset = false;
            _backPreviewTimer.Start();
        }

        /// <summary>
        /// 選択肢問題プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateChoiceQuestionPreviewWithDebounce()
        {
            if (_choiceQuestion == null || _choiceQuestionPreviewWebView == null) return;
            
            // 既存のタイマーを停止
            _choiceQuestionPreviewTimer?.Stop();
            _choiceQuestionPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _choiceQuestionPreviewTimer = new System.Timers.Timer(500);
            _choiceQuestionPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var markdown = _choiceQuestion.Text ?? "";
                        var html = ConvertMarkdownToHtml(markdown);
                        
                        // フラッシュ防止: 更新前に透明化（スペースは保持）
                        await _choiceQuestionPreviewWebView.FadeTo(0, 50); // 50ms で透明化
                        
                        // 背景色を再設定してからHTMLを更新
                        SetWebViewBackgroundColor(_choiceQuestionPreviewWebView);
                        _choiceQuestionPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                        _choiceQuestionPreviewReady = false; // ナビゲーション完了を待つ
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"選択肢問題プレビュー更新エラー: {ex.Message}");
                    }
                });
                _choiceQuestionPreviewTimer?.Stop();
                _choiceQuestionPreviewTimer?.Dispose();
                _choiceQuestionPreviewTimer = null;
            };
            _choiceQuestionPreviewTimer.AutoReset = false;
            _choiceQuestionPreviewTimer.Start();
        }

        /// <summary>
        /// 選択肢解説プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateChoiceExplanationPreviewWithDebounce()
        {
            if (_choiceQuestionExplanation == null || _choiceExplanationPreviewWebView == null) return;
            
            // 既存のタイマーを停止
            _choiceExplanationPreviewTimer?.Stop();
            _choiceExplanationPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _choiceExplanationPreviewTimer = new System.Timers.Timer(500);
            _choiceExplanationPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var markdown = _choiceQuestionExplanation.Text ?? "";
                        var html = ConvertMarkdownToHtml(markdown);
                        
                        // フラッシュ防止: 更新前に透明化（スペースは保持）
                        await _choiceExplanationPreviewWebView.FadeTo(0, 50); // 50ms で透明化
                        
                        // 背景色を再設定してからHTMLを更新
                        SetWebViewBackgroundColor(_choiceExplanationPreviewWebView);
                        _choiceExplanationPreviewWebView.Source = new HtmlWebViewSource { Html = html };
                        _choiceExplanationPreviewReady = false; // ナビゲーション完了を待つ
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"選択肢解説プレビュー更新エラー: {ex.Message}");
                    }
                });
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                _choiceExplanationPreviewTimer = null;
            };
            _choiceExplanationPreviewTimer.AutoReset = false;
            _choiceExplanationPreviewTimer.Start();
        }
        
        /// <summary>
        /// プレビューを更新（即座に実行）
        /// </summary>
        private async void UpdatePreview(Editor editor, WebView webView)
        {
            if (editor == null || webView == null) return;
            
            try
            {
                var markdown = editor.Text ?? "";
                var html = ConvertMarkdownToHtml(markdown);
                
                // フラッシュ防止: 更新前に非表示
                if (webView.IsVisible)
                {
                    await webView.FadeTo(0, 50); // 50ms で非表示
                    webView.IsVisible = false;
                }
                
                // 背景色を再設定してからHTMLを更新
                SetWebViewBackgroundColor(webView);
                webView.Source = new HtmlWebViewSource { Html = html };
                
                // ナビゲーション完了フラグをリセット
                if (webView == _frontPreviewWebView)
                    _frontPreviewReady = false;
                else if (webView == _backPreviewWebView)
                    _backPreviewReady = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プレビュー更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 表面プレビューナビゲーション完了イベント
        /// </summary>
        private async void OnFrontPreviewNavigated(object sender, WebNavigatedEventArgs e)
        {
            try
            {
                if (e.Result == WebNavigationResult.Success && !_frontPreviewReady)
                {
                    _frontPreviewReady = true;
                    
                    // 少し待ってからフェードイン（CSSの適用を確実にするため）
                    await Task.Delay(100);
                    
                    if (_frontPreviewWebView != null)
                    {
                        await _frontPreviewWebView.FadeTo(1, 150); // 150ms でフェードイン
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
                if (e.Result == WebNavigationResult.Success && !_backPreviewReady)
                {
                    _backPreviewReady = true;
                    
                    // 少し待ってからフェードイン（CSSの適用を確実にするため）
                    await Task.Delay(100);
                    
                    if (_backPreviewWebView != null)
                    {
                        await _backPreviewWebView.FadeTo(1, 150); // 150ms でフェードイン
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"裏面プレビューナビゲーション完了エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 選択肢問題プレビューナビゲーション完了イベント
        /// </summary>
        private async void OnChoiceQuestionPreviewNavigated(object sender, WebNavigatedEventArgs e)
        {
            try
            {
                if (e.Result == WebNavigationResult.Success && !_choiceQuestionPreviewReady)
                {
                    _choiceQuestionPreviewReady = true;
                    
                    // 少し待ってからフェードイン（CSSの適用を確実にするため）
                    await Task.Delay(100);
                    
                    if (_choiceQuestionPreviewWebView != null)
                    {
                        await _choiceQuestionPreviewWebView.FadeTo(1, 150); // 150ms でフェードイン
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢問題プレビューナビゲーション完了エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 選択肢解説プレビューナビゲーション完了イベント
        /// </summary>
        private async void OnChoiceExplanationPreviewNavigated(object sender, WebNavigatedEventArgs e)
        {
            try
            {
                if (e.Result == WebNavigationResult.Success && !_choiceExplanationPreviewReady)
                {
                    _choiceExplanationPreviewReady = true;
                    
                    // 少し待ってからフェードイン（CSSの適用を確実にするため）
                    await Task.Delay(100);
                    
                    if (_choiceExplanationPreviewWebView != null)
                    {
                        await _choiceExplanationPreviewWebView.FadeTo(1, 150); // 150ms でフェードイン
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢解説プレビューナビゲーション完了エラー: {ex.Message}");
            }
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
        
        /// <summary>
        /// MarkdownをHTMLに変換
        /// </summary>
        private string ConvertMarkdownToHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // 画像タグを最初に処理 - iOS版の形式に対応
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"<<img_\d{8}_\d{6}\.jpg>>");
            Debug.WriteLine($"画像タグ数: {matches.Count}");
            foreach (System.Text.RegularExpressions.Match match in matches)
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
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<<blank\|(.*?)>>", "( )");

            // HTML エスケープ
            text = System.Web.HttpUtility.HtmlEncode(text);

            // 太字変換
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");

            // 色変換
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\{red\|(.*?)\}\}", "<span style='color:red;'>$1</span>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\{blue\|(.*?)\}\}", "<span style='color:blue;'>$1</span>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\{green\|(.*?)\}\}", "<span style='color:green;'>$1</span>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\{yellow\|(.*?)\}\}", "<span style='color:yellow;'>$1</span>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\{purple\|(.*?)\}\}", "<span style='color:purple;'>$1</span>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\{orange\|(.*?)\}\}", "<span style='color:orange;'>$1</span>");

            // 上付き・下付き変換
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\^\^(.*?)\^\^", "<sup>$1</sup>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"~~(.*?)~~", "<sub>$1</sub>");

            // 必要な部分だけデコード処理
            text = System.Text.RegularExpressions.Regex.Replace(text, @"&lt;img(.*?)&gt;", "<img$1>");

            // 改行を `<br>` に変換
            text = text.Replace(Environment.NewLine, "<br>").Replace("\n", "<br>");
            
            // ダークモード対応のスタイル
            var isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;
            var backgroundColor = isDarkMode ? "#1f1f1f" : "#ffffff";
            var textColor = isDarkMode ? "#ffffff" : "#000000";
            
            return $@"<html style='background-color: {backgroundColor};'>
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
        }

        /// <summary>
        /// 画像をBase64に変換
        /// </summary>
        private string ConvertImageToBase64(string imagePath)
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

        /// <summary>
        /// トースト風の通知を表示（画面下部オーバーレイ）
        /// </summary>
        private async Task ShowToast(string message)
        {
            try
            {
                // トーストラベルが存在しない場合は作成
                if (_toastLabel == null)
                {
                    _toastLabel = new Label
                    {
                        Text = message,
                        BackgroundColor = Color.FromRgba(0, 0, 0, 0.8f), // 半透明の黒背景
                        TextColor = Colors.White,
                        FontSize = 16,
                        Padding = new Thickness(20, 12),
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.End,
                        Margin = new Thickness(20, 0, 20, 30), // 画面下部からの余白
                        IsVisible = false,
                        HorizontalTextAlignment = TextAlignment.Center
                    };

                    // メインコンテナに追加（最前面に表示）
                    if (Content is Grid mainGrid)
                    {
                        mainGrid.Children.Add(_toastLabel);
                        Grid.SetRowSpan(_toastLabel, mainGrid.RowDefinitions.Count); // 全行にスパン
                        Grid.SetColumnSpan(_toastLabel, mainGrid.ColumnDefinitions.Count); // 全列にスパン
                    }
                }
                else
                {
                    _toastLabel.Text = message;
                }

                // トーストを表示
                _toastLabel.IsVisible = true;
                _toastLabel.Opacity = 0;
                _toastLabel.TranslationY = 50; // 下から上にスライドイン

                // アニメーション：フェードイン & スライドイン
                var fadeTask = _toastLabel.FadeTo(1, 300);
                var slideTask = _toastLabel.TranslateTo(0, 0, 300, Easing.CubicOut);
                await Task.WhenAll(fadeTask, slideTask);

                // 2.5秒間表示
                await Task.Delay(2500);

                // アニメーション：フェードアウト & スライドアウト
                var fadeOutTask = _toastLabel.FadeTo(0, 300);
                var slideOutTask = _toastLabel.TranslateTo(0, 50, 300, Easing.CubicIn);
                await Task.WhenAll(fadeOutTask, slideOutTask);
                
                _toastLabel.IsVisible = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"トースト表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// カード保存イベント
        /// </summary>
        private async void OnSaveCardClicked(object sender, EventArgs e)
        {
            try
            {
                var selectedType = _cardTypePicker?.SelectedItem?.ToString();
                
                if (selectedType == "基本・穴埋め")
                {
                    var front = _frontTextEditor?.Text ?? "";
                    var back = _backTextEditor?.Text ?? "";
                    
                    if (string.IsNullOrWhiteSpace(front))
                    {
                        await DisplayAlert("エラー", "表面を入力してください", "OK");
                        return;
                    }
                    
                    // カードを保存（Add.xaml.csと同じロジック）
                    await SaveBasicCard(front, back);
                    
                    // 入力フィールドをクリア
                    _frontTextEditor.Text = "";
                    _backTextEditor.Text = "";
                    
                    // トースト表示でカード保存完了を通知
                    await ShowToast("カードを保存しました");
                }
                else if (selectedType == "選択肢")
                {
                    await SaveChoiceCard();
                }
                else if (selectedType == "画像穴埋め")
                {
                    await SaveImageFillCard();
                }
                else
                {
                    await DisplayAlert("未実装", "このカードタイプはまだ実装されていません", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード保存エラー: {ex.Message}");
                await DisplayAlert("エラー", $"カードの保存に失敗しました: {ex.Message}", "OK");
            }
        }
        
        /// <summary>
        /// 基本カードを保存（Add.xaml.cs方式）
        /// </summary>
        private async Task SaveBasicCard(string front, string back)
        {
            try
            {
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
                }
                else
                {
                    // 新規作成時は空のリストを初期化
                    lines = new List<string> { "0" };
                }

                // カードIDと日付を追加
                string newCardLine = $"{cardId},{currentDate}";
                lines.Add(newCardLine);

                // カード数を更新（1行目にカードの総数を設定）
                int cardCount = lines.Count - 1; // 1行目を除いた行数がカード数
                lines[0] = cardCount.ToString();

                // カード情報をJSONとして保存
                var cardData = new
                {
                    id = cardId,
                    type = "基本・穴埋め",
                    front = front,
                    back = back,
                    question = "",
                    explanation = "",
                    choices = new List<object>(),
                    selectionRects = new List<object>()
                };

                // JSONファイルとして保存
                string jsonPath = Path.Combine(cardsDirPath, $"{cardId}.json");
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonContent = JsonSerializer.Serialize(cardData, options);
                await File.WriteAllTextAsync(jsonPath, jsonContent, System.Text.Encoding.UTF8);

                // cards.txtを更新
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
                
                Debug.WriteLine($"基本カード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"基本カード保存エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 選択肢カードを保存（Add.xaml.cs方式）
        /// </summary>
        private async Task SaveChoiceCard()
        {
            try
            {
                var question = _choiceQuestion?.Text ?? "";
                var explanation = _choiceQuestionExplanation?.Text ?? "";
                
                if (string.IsNullOrWhiteSpace(question))
                {
                    await DisplayAlert("エラー", "問題を入力してください", "OK");
                    return;
                }

                // 選択肢を収集
                var choices = new List<object>();
                foreach (var stack in _choicesContainer.Children.OfType<StackLayout>())
                {
                    var checkBox = stack.Children.OfType<CheckBox>().FirstOrDefault();
                    var editor = stack.Children.OfType<Editor>().FirstOrDefault();
                    
                    if (editor != null && !string.IsNullOrWhiteSpace(editor.Text))
                    {
                        choices.Add(new
                        {
                            isCorrect = checkBox?.IsChecked ?? false,
                            text = editor.Text
                        });
                    }
                }

                if (choices.Count == 0)
                {
                    await DisplayAlert("エラー", "選択肢を追加してください", "OK");
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
                }
                else
                {
                    // 新規作成時は空のリストを初期化
                    lines = new List<string> { "0" };
                }

                // カードIDと日付を追加
                string newCardLine = $"{cardId},{currentDate}";
                lines.Add(newCardLine);

                // カード数を更新（1行目にカードの総数を設定）
                int cardCount = lines.Count - 1; // 1行目を除いた行数がカード数
                lines[0] = cardCount.ToString();

                // カード情報をJSONとして保存
                var cardData = new
                {
                    id = cardId,
                    type = "選択肢",
                    front = "",
                    back = "",
                    question = question,
                    explanation = explanation,
                    choices = choices,
                    selectionRects = new List<object>()
                };

                // JSONファイルとして保存
                string jsonPath = Path.Combine(cardsDirPath, $"{cardId}.json");
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonContent = JsonSerializer.Serialize(cardData, options);
                await File.WriteAllTextAsync(jsonPath, jsonContent, System.Text.Encoding.UTF8);

                // cards.txtを更新
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
                
                // 入力フィールドをクリア
                _choiceQuestion.Text = "";
                _choiceQuestionExplanation.Text = "";
                _choicesContainer.Children.Clear();
                
                // トースト表示でカード保存完了を通知
                await ShowToast("選択肢カードを保存しました");
                Debug.WriteLine($"選択肢カード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢カード保存エラー: {ex.Message}");
                await DisplayAlert("エラー", $"選択肢カードの保存に失敗しました: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// 画像穴埋めカードを保存（Add.xaml.cs方式）
        /// </summary>
        private async Task SaveImageFillCard()
        {
            try
            {
                if (_imageBitmap == null || string.IsNullOrEmpty(_selectedImagePath))
                {
                    await DisplayAlert("エラー", "画像を選択してください", "OK");
                    return;
                }

                if (_selectionRects.Count == 0)
                {
                    await DisplayAlert("エラー", "穴埋め範囲を指定してください", "OK");
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
                }
                else
                {
                    // 新規作成時は空のリストを初期化
                    lines = new List<string> { "0" };
                }

                // カードIDと日付を追加
                string newCardLine = $"{cardId},{currentDate}";
                lines.Add(newCardLine);

                // カード数を更新（1行目にカードの総数を設定）
                int cardCount = lines.Count - 1; // 1行目を除いた行数がカード数
                lines[0] = cardCount.ToString();
                
                // 選択範囲をシリアライズ用の形式に変換
                var selectionRects = _selectionRects.Select(rect => new
                {
                    x = rect.Left,
                    y = rect.Top,
                    width = rect.Width,
                    height = rect.Height
                }).ToList();
                
                // カード情報をJSONとして保存
                var cardData = new
                {
                    id = cardId,
                    type = "画像穴埋め",
                    front = "",
                    back = "",
                    question = "",
                    explanation = "",
                    choices = new List<object>(),
                    selectionRects = selectionRects
                };

                // JSONファイルとして保存
                string jsonPath = Path.Combine(cardsDirPath, $"{cardId}.json");
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonContent = JsonSerializer.Serialize(cardData, options);
                await File.WriteAllTextAsync(jsonPath, jsonContent, System.Text.Encoding.UTF8);

                // cards.txtを更新
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
                
                // 入力フィールドをクリア
                _imageBitmap = null;
                _selectedImagePath = "";
                _selectionRects.Clear();
                _canvasView.InvalidateSurface();
                
                // トースト表示でカード保存完了を通知
                await ShowToast("画像穴埋めカードを保存しました");
                Debug.WriteLine($"画像穴埋めカード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像穴埋めカード保存エラー: {ex.Message}");
                await DisplayAlert("エラー", $"画像穴埋めカードの保存に失敗しました: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// 装飾文字を挿入するヘルパーメソッド
        /// </summary>
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
                UpdatePreview(editor, editor == _frontTextEditor ? _frontPreviewWebView : _backPreviewWebView);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"装飾テキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = prefix + suffix;
                await InsertAtCursor(editor, insertText, prefix.Length);
                UpdatePreview(editor, editor == _frontTextEditor ? _frontPreviewWebView : _backPreviewWebView);
            }
        }

        /// <summary>
        /// エディターから選択されたテキストを取得
        /// </summary>
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

        /// <summary>
        /// 選択開始位置を取得
        /// </summary>
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

        /// <summary>
        /// プラットフォーム固有のハンドラーから選択開始位置を取得
        /// </summary>
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

        /// <summary>
        /// 選択範囲の長さを取得
        /// </summary>
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

        /// <summary>
        /// プラットフォーム固有のハンドラーから選択範囲を取得
        /// </summary>
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

        /// <summary>
        /// クリップボードから選択されたテキストを取得を試みる
        /// </summary>
        private async Task<string> TryGetSelectedText()
        {
            try
            {
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

        /// <summary>
        /// カーソル位置にテキストを挿入
        /// </summary>
        private async Task InsertAtCursor(Editor editor, string text, int cursorOffset = 0)
        {
            int start = editor.CursorPosition;
            string currentText = editor.Text ?? "";
            string newText = currentText.Insert(start, text);
            editor.Text = newText;
            editor.CursorPosition = start + cursorOffset;
        }

        /// <summary>
        /// 現在アクティブなエディターを取得
        /// </summary>
        private Editor GetCurrentEditor()
        {
            Debug.WriteLine($"GetCurrentEditor開始");
            
            // まず現在フォーカスされているエディターを確認
            if (_frontTextEditor != null && _frontTextEditor.IsFocused)
            {
                Debug.WriteLine("FrontTextEditorがフォーカス中");
                _lastFocusedEditor = _frontTextEditor;
                return _frontTextEditor;
            }
            if (_backTextEditor != null && _backTextEditor.IsFocused)
            {
                Debug.WriteLine("BackTextEditorがフォーカス中");
                _lastFocusedEditor = _backTextEditor;
                return _backTextEditor;
            }
            if (_choiceQuestion != null && _choiceQuestion.IsFocused)
            {
                Debug.WriteLine("ChoiceQuestionがフォーカス中");
                _lastFocusedEditor = _choiceQuestion;
                return _choiceQuestion;
            }
            if (_choiceQuestionExplanation != null && _choiceQuestionExplanation.IsFocused)
            {
                Debug.WriteLine("ChoiceQuestionExplanationがフォーカス中");
                _lastFocusedEditor = _choiceQuestionExplanation;
                return _choiceQuestionExplanation;
            }

            // 動的に作成された選択肢エディターを確認
            if (_choicesContainer != null)
            {
                foreach (var stack in _choicesContainer.Children.OfType<StackLayout>())
                {
                    var editor = stack.Children.OfType<Editor>().FirstOrDefault();
                    if (editor != null && editor.IsFocused)
                    {
                        Debug.WriteLine("選択肢のエディターがフォーカス中");
                        _lastFocusedEditor = editor;
                        return editor;
                    }
                }
            }

            // フォーカスされているエディターがない場合、最後にフォーカスされたエディターを使用
            if (_lastFocusedEditor != null)
            {
                Debug.WriteLine($"最後にフォーカスされたエディターを使用");
                return _lastFocusedEditor;
            }

            // デフォルトは表面
            Debug.WriteLine("デフォルトでFrontTextEditorを使用");
            _lastFocusedEditor = _frontTextEditor;
            return _frontTextEditor;
        }

        // 装飾文字ボタンのイベントハンドラー
        private void OnBoldClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "**", "**");
            }
        }

        private void OnRedColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{red|", "}}");
            }
        }

        private void OnBlueColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{blue|", "}}");
            }
        }

        private void OnGreenColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{green|", "}}");
            }
        }

        private void OnYellowColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{yellow|", "}}");
            }
        }

        private void OnPurpleColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{purple|", "}}");
            }
        }

        private void OnOrangeColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{orange|", "}}");
            }
        }

        private void OnSuperscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "^^", "^^");
            }
        }

        private void OnSubscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "~~", "~~");
            }
        }

        private void OnBlankClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            
            // 基本カードの表面エディターでのみ穴埋めを挿入
            if (editor != null && editor == _frontTextEditor)
            {
                Debug.WriteLine($"穴埋めを挿入");
                InsertBlankText(editor);
            }
            else
            {
                Debug.WriteLine("穴埋めは基本カードの表面でのみ使用できます");
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
                UpdatePreview(editor, editor == _frontTextEditor ? _frontPreviewWebView : _backPreviewWebView);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"穴埋めテキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = "<<blank|>>";
                await InsertAtCursor(editor, insertText, 8);
                UpdatePreview(editor, editor == _frontTextEditor ? _frontPreviewWebView : _backPreviewWebView);
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

        // 選択肢カード関連のメソッド
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
            editor.Focused += (s, e) => _lastFocusedEditor = editor;

            stack.Children.Add(checkBox);
            stack.Children.Add(editor);

            _choicesContainer.Children.Add(stack);
        }

        private void OnRemoveNumbersToggled(object sender, ToggledEventArgs e)
        {
            _removeNumbers = e.Value;

            // 選択肢コンテナが空でないことを確認
            if (_choicesContainer.Children.Count > 0)
            {
                // 最初のEditorを取得
                var editor = _choicesContainer.Children.OfType<StackLayout>()
                    .SelectMany(s => s.Children.OfType<Editor>())
                    .FirstOrDefault();

                if (editor != null)
                {
                    OnChoiceTextChanged(editor, new TextChangedEventArgs("", editor.Text));
                }
            }
        }

        private void OnChoiceTextChanged(object sender, TextChangedEventArgs e)
        {
            var editor = sender as Editor;
            if (editor == null)
            {
                Debug.WriteLine("OnChoiceTextChanged: Editor is null");
                return;
            }

            Debug.WriteLine($"OnChoiceTextChanged: New text value: '{e.NewTextValue}'");

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
                    .Select(c => _removeNumbers ? System.Text.RegularExpressions.Regex.Replace(c, @"^\d+\.\s*", "") : c)  // 番号を削除（オプション）
                    .ToList();

                Debug.WriteLine($"OnChoiceTextChanged: Split into {choices.Count} choices");

                if (choices.Count > 0)
                {
                    Debug.WriteLine("OnChoiceTextChanged: Clearing choices container");
                    // 選択肢コンテナをクリア
                    _choicesContainer.Children.Clear();

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
                        newEditor.Focused += (s, e) => _lastFocusedEditor = newEditor;

                        stack.Children.Add(checkBox);
                        stack.Children.Add(newEditor);

                        _choicesContainer.Children.Add(stack);
                        Debug.WriteLine($"Added choice: '{choice}'");
                    }
                    Debug.WriteLine($"OnChoiceTextChanged: Total choices added: {_choicesContainer.Children.Count}");
                }
            }
        }

        // 画像穴埋めカード関連のメソッド
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
                    _imageBitmap = SkiaSharp.SKBitmap.Decode(stream);
                }

                // 画像番号の読み込みと更新
                LoadImageCount();
                _imageCount++;
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
                SaveBitmapToFile(_imageBitmap, imgFilePath);
                _selectedImagePath = imgFileName;
                _imagePaths.Add(imgFilePath);
                _canvasView.InvalidateSurface();
            }
        }

        private void SaveBitmapToFile(SkiaSharp.SKBitmap bitmap, string filePath)
        {
            using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }
        }

        private void LoadImageCount()
        {
            var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            if (File.Exists(cardsFilePath))
            {
                var lines = File.ReadAllLines(cardsFilePath).ToList();
                if (lines.Count > 0 && int.TryParse(lines[0], out int count))
                {
                    _imageCount = count;
                }
            }
        }

        private void SaveImageCount()
        {
            var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            var lines = new List<string> { _imageCount.ToString() };

            if (File.Exists(cardsFilePath))
            {
                lines.AddRange(File.ReadAllLines(cardsFilePath).Skip(1));
            }

            File.WriteAllLines(cardsFilePath, lines);
        }

        private void OnCanvasTouch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
        {
            var point = e.Location;

            switch (e.ActionType)
            {
                case SkiaSharp.Views.Maui.SKTouchAction.Pressed:
                    if (e.MouseButton == SkiaSharp.Views.Maui.SKMouseButton.Right)
                    {
                        // 右クリックで削除メニュー表示
                        var clickedRect = _selectionRects.FirstOrDefault(r => r.Contains(point));
                        if (clickedRect != SkiaSharp.SKRect.Empty)
                        {
                            ShowContextMenu(point, clickedRect);
                        }
                    }
                    else
                    {
                        // 左クリックで四角形を追加
                        _isDragging = true;
                        _startPoint = point;
                        _endPoint = point;
                    }
                    break;

                case SkiaSharp.Views.Maui.SKTouchAction.Moved:
                    if (_isDragging)
                    {
                        _endPoint = point;
                    }
                    break;

                case SkiaSharp.Views.Maui.SKTouchAction.Released:
                    if (_isDragging)
                    {
                        var rect = SkiaSharp.SKRect.Create(
                            Math.Min(_startPoint.X, _endPoint.X),
                            Math.Min(_startPoint.Y, _endPoint.Y),
                            Math.Abs(_endPoint.X - _startPoint.X),
                            Math.Abs(_endPoint.Y - _startPoint.Y)
                        );

                        if (!rect.IsEmpty && rect.Width > 5 && rect.Height > 5)
                        {
                            _selectionRects.Add(rect);
                        }
                    }

                    _isDragging = false;
                    break;
            }

            // 再描画
            _canvasView.InvalidateSurface();
        }

        private async void ShowContextMenu(SkiaSharp.SKPoint point, SkiaSharp.SKRect rect)
        {
            var action = await DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                _selectionRects.Remove(rect);
                _canvasView.InvalidateSurface();
            }
        }

        private void OnCanvasViewPaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SkiaSharp.SKColors.White);

            // 画像を表示
            if (_imageBitmap != null)
            {
                var rect = new SkiaSharp.SKRect(0, 0, e.Info.Width, e.Info.Height);
                canvas.DrawBitmap(_imageBitmap, rect);
            }

            // 四角形を描画
            using (var paint = new SkiaSharp.SKPaint
            {
                Color = SkiaSharp.SKColors.Red,
                Style = SkiaSharp.SKPaintStyle.Stroke,
                StrokeWidth = 3
            })
            {
                foreach (var rect in _selectionRects)
                {
                    canvas.DrawRect(rect, paint);
                    DrawResizeHandles(canvas, rect);
                }

                if (_isDragging)
                {
                    var currentRect = SkiaSharp.SKRect.Create(
                        Math.Min(_startPoint.X, _endPoint.X),
                        Math.Min(_startPoint.Y, _endPoint.Y),
                        Math.Abs(_endPoint.X - _startPoint.X),
                        Math.Abs(_endPoint.Y - _startPoint.Y)
                    );

                    canvas.DrawRect(currentRect, paint);
                }
            }
        }

        private void DrawResizeHandles(SkiaSharp.SKCanvas canvas, SkiaSharp.SKRect rect)
        {
            using (var paint = new SkiaSharp.SKPaint
            {
                Color = SkiaSharp.SKColors.Blue,
                Style = SkiaSharp.SKPaintStyle.Fill
            })
            {
                canvas.DrawRect(new SkiaSharp.SKRect(rect.Left - HANDLE_SIZE / 2, rect.Top - HANDLE_SIZE / 2, rect.Left + HANDLE_SIZE / 2, rect.Top + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SkiaSharp.SKRect(rect.Right - HANDLE_SIZE / 2, rect.Top - HANDLE_SIZE / 2, rect.Right + HANDLE_SIZE / 2, rect.Top + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SkiaSharp.SKRect(rect.Left - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, rect.Left + HANDLE_SIZE / 2, rect.Bottom + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SkiaSharp.SKRect(rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, rect.Right + HANDLE_SIZE / 2, rect.Bottom + HANDLE_SIZE / 2), paint);
            }
        }

        /// <summary>
        /// テーマ変更イベントハンドラー
        /// </summary>
        private void OnRequestedThemeChanged(object sender, AppThemeChangedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"テーマ変更検出: {e.RequestedTheme}");
                
                // WebViewの背景色を更新
                if (_frontPreviewWebView != null)
                {
                    SetWebViewBackgroundColor(_frontPreviewWebView);
                }
                
                if (_backPreviewWebView != null)
                {
                    SetWebViewBackgroundColor(_backPreviewWebView);
                }
                
                if (_choiceQuestionPreviewWebView != null)
                {
                    SetWebViewBackgroundColor(_choiceQuestionPreviewWebView);
                }
                
                if (_choiceExplanationPreviewWebView != null)
                {
                    SetWebViewBackgroundColor(_choiceExplanationPreviewWebView);
                }
                
                // プレビューを更新
                if (_frontTextEditor != null && _frontPreviewWebView != null)
                {
                    UpdatePreview(_frontTextEditor, _frontPreviewWebView);
                }
                
                if (_backTextEditor != null && _backPreviewWebView != null)
                {
                    UpdatePreview(_backTextEditor, _backPreviewWebView);
                }
                
                if (_choiceQuestion != null && _choiceQuestionPreviewWebView != null)
                {
                    UpdatePreview(_choiceQuestion, _choiceQuestionPreviewWebView);
                }
                
                if (_choiceQuestionExplanation != null && _choiceExplanationPreviewWebView != null)
                {
                    UpdatePreview(_choiceQuestionExplanation, _choiceExplanationPreviewWebView);
                }
                
                Debug.WriteLine("テーマ変更対応完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テーマ変更対応エラー: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Debug.WriteLine("NotePage Dispose開始");
            
            try
            {
                // テーマ変更イベントの購読を解除
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeChanged -= OnRequestedThemeChanged;
                }
                
                // スクロールイベントハンドラーを解除
                if (MainScrollView != null)
                {
                    MainScrollView.Scrolled -= OnMainScrollViewScrolled;
                }
                
                // ページ選択オーバーレイのクリーンアップ
                _pageSelectionOverlay = null;
                _pageSelectionLabel = null;
                _pageConfirmButton = null;
                _pageCancelButton = null;
                _isPageSelectionMode = false;
                
                // 画像リソースの解放
                if (_imageBitmap != null)
                {
                    _imageBitmap.Dispose();
                    _imageBitmap = null;
                }
                
                if (_backgroundCanvas != null)
                {
                    _backgroundCanvas.Dispose();
                    _backgroundCanvas = null;
                }
                
                if (_drawingLayer != null)
                {
                    _drawingLayer.Dispose();
                    _drawingLayer = null;
                }
                
                // WebViewのイベントハンドラーを解除
                if (_frontPreviewWebView != null)
                {
                    _frontPreviewWebView.Navigated -= OnFrontPreviewNavigated;
                }
                
                if (_backPreviewWebView != null)
                {
                    _backPreviewWebView.Navigated -= OnBackPreviewNavigated;
                }
                
                if (_choiceQuestionPreviewWebView != null)
                {
                    _choiceQuestionPreviewWebView.Navigated -= OnChoiceQuestionPreviewNavigated;
                }
                
                if (_choiceExplanationPreviewWebView != null)
                {
                    _choiceExplanationPreviewWebView.Navigated -= OnChoiceExplanationPreviewNavigated;
                }
                
                // タイマーを停止・解放
                _frontPreviewTimer?.Stop();
                _frontPreviewTimer?.Dispose();
                
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                
                _choiceQuestionPreviewTimer?.Stop();
                _choiceQuestionPreviewTimer?.Dispose();
                
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                
                Debug.WriteLine("NotePage Dispose完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NotePage Dispose エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページ穴埋め選択
        /// </summary>
        private async void OnSelectPageForImageFill(object sender, EventArgs e)
        {
            try
            {
                // デバッグ情報を出力
                Debug.WriteLine($"ページ選択開始 - BackgroundCanvas: {_backgroundCanvas != null}");
                if (_backgroundCanvas != null)
                {
                    Debug.WriteLine($"HasContent: {_backgroundCanvas.HasContent}");
                    Debug.WriteLine($"PageCount: {_backgroundCanvas.PageCount}");
                }

                if (!(_backgroundCanvas?.HasContent == true))
                {
                    Debug.WriteLine($"コンテンツが利用できません - BackgroundCanvas: {_backgroundCanvas != null}, HasContent: {_backgroundCanvas?.HasContent}");
                    await DisplayAlert("エラー", "表示されているコンテンツがありません", "OK");
                    return;
                }

                // PDFの場合のみページ選択UI、画像の場合は直接処理
                if (_backgroundCanvas.PageCount > 0)
                {
                    // PDFページの場合：ページ選択オーバーレイを表示
                    int currentPage = GetCurrentPageIndex();
                    
                    // BackgroundCanvasでページ選択モードを有効化
                    _backgroundCanvas.EnablePageSelectionMode();
                    
                    await ShowPageSelectionOverlay(currentPage);
                }
                else
                {
                    // 単一画像の場合：直接画像として読み込み
                    await LoadCurrentImageAsImageFill();
                }


            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択エラー: {ex.Message}");
                await DisplayAlert("エラー", "ページ選択中にエラーが発生しました", "OK");
            }
        }

        /// <summary>
        /// 現在表示されているページインデックスを取得
        /// </summary>
        private int GetCurrentPageIndex()
        {
            try
            {
                if (_backgroundCanvas == null || MainScrollView == null)
                    return 0;

                // BackgroundCanvasの実装と同じロジックを使用
                return _backgroundCanvas.GetCurrentPageIndex();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// ページ選択オーバーレイを表示
        /// </summary>
        private async Task ShowPageSelectionOverlay(int pageIndex)
        {
            try
            {
                _isPageSelectionMode = true;
                _selectedPageIndex = pageIndex;

                // オーバーレイが存在しない場合は作成
                if (_pageSelectionOverlay == null)
                {
                    _pageSelectionOverlay = new Frame
                    {
                        BackgroundColor = Color.FromRgba(255, 0, 0, 0.3f), // 半透明の赤
                        BorderColor = Colors.Red,
                        CornerRadius = 8,
                        Padding = new Thickness(20),
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        IsVisible = false
                    };

                    var overlayLayout = new VerticalStackLayout { Spacing = 15 };

                    _pageSelectionLabel = new Label
                    {
                        Text = $"ページ {pageIndex + 1} を選択しますか？",
                        TextColor = Colors.White,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    };

                    var buttonsLayout = new HorizontalStackLayout 
                    { 
                        Spacing = 20,
                        HorizontalOptions = LayoutOptions.Center
                    };

                    _pageConfirmButton = new Button
                    {
                        Text = "選択",
                        BackgroundColor = Colors.Green,
                        TextColor = Colors.White,
                        WidthRequest = 100
                    };
                    _pageConfirmButton.Clicked += OnPageConfirmClicked;

                    _pageCancelButton = new Button
                    {
                        Text = "キャンセル",
                        BackgroundColor = Colors.Gray,
                        TextColor = Colors.White,
                        WidthRequest = 100
                    };
                    _pageCancelButton.Clicked += OnPageCancelClicked;

                    buttonsLayout.Children.Add(_pageConfirmButton);
                    buttonsLayout.Children.Add(_pageCancelButton);

                    overlayLayout.Children.Add(_pageSelectionLabel);
                    overlayLayout.Children.Add(buttonsLayout);

                    _pageSelectionOverlay.Content = overlayLayout;

                    // メインGridに追加
                    if (Content is Grid mainGrid)
                    {
                        mainGrid.Children.Add(_pageSelectionOverlay);
                        Grid.SetRowSpan(_pageSelectionOverlay, mainGrid.RowDefinitions.Count);
                        Grid.SetColumnSpan(_pageSelectionOverlay, mainGrid.ColumnDefinitions.Count);
                    }
                }
                else
                {
                    // ラベルテキストを更新
                    _pageSelectionLabel.Text = $"ページ {pageIndex + 1} を選択しますか？";
                }

                // オーバーレイを表示
                _pageSelectionOverlay.IsVisible = true;
                _pageSelectionOverlay.Opacity = 0;
                await _pageSelectionOverlay.FadeTo(1, 300);

                await ShowToast($"ページ {pageIndex + 1} が選択されています。他のページを選択するにはスクロールしてください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーバーレイ表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページ選択オーバーレイを非表示
        /// </summary>
        private async Task HidePageSelectionOverlay()
        {
            try
            {
                if (_pageSelectionOverlay != null && _pageSelectionOverlay.IsVisible)
                {
                    await _pageSelectionOverlay.FadeTo(0, 300);
                    _pageSelectionOverlay.IsVisible = false;
                }
                _isPageSelectionMode = false;
                
                // BackgroundCanvasでページ選択モードを無効化
                _backgroundCanvas?.DisablePageSelectionMode();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーバーレイ非表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページ選択確定
        /// </summary>
        private async void OnPageConfirmClicked(object sender, EventArgs e)
        {
            try
            {
                await HidePageSelectionOverlay();
                
                // エディターが設定されている場合は画像を追加、そうでなければ画像穴埋め用に読み込み
                if (_lastFocusedEditor != null && (_lastFocusedEditor == _frontTextEditor || 
                    _lastFocusedEditor == _backTextEditor || _lastFocusedEditor == _choiceQuestion || 
                    _lastFocusedEditor == _choiceQuestionExplanation))
                {
                    await AddCurrentPageAsImage(_lastFocusedEditor);
                    _lastFocusedEditor = null; // リセット
                }
                else
                {
                await LoadPageAsImage(_selectedPageIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ確定エラー: {ex.Message}");
                await DisplayAlert("エラー", "ページの読み込みに失敗しました", "OK");
            }
        }

        /// <summary>
        /// ページ選択キャンセル
        /// </summary>
        private async void OnPageCancelClicked(object sender, EventArgs e)
        {
            await HidePageSelectionOverlay();
        }

        /// <summary>
        /// 指定ページを画像として読み込み
        /// </summary>
        private async Task LoadPageAsImage(int pageIndex)
        {
            try
            {
                // PageCacheから画像を取得（新しいDPI設定）
                var cacheDir = Path.Combine(tempExtractPath, "PageCache");
                var highDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)150f}.png");
                var mediumDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)96f}.png");
                var oldHighDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)72f}.png");
                var oldLowDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)36f}.png");

                string imageFile = null;
                if (File.Exists(highDpiCacheFile))
                {
                    imageFile = highDpiCacheFile;
                    Debug.WriteLine($"150dpi画像を使用: {imageFile}");
                }
                else if (File.Exists(mediumDpiCacheFile))
                {
                    imageFile = mediumDpiCacheFile;
                    Debug.WriteLine($"96dpi画像を使用: {imageFile}");
                }
                else if (File.Exists(oldHighDpiCacheFile))
                {
                    imageFile = oldHighDpiCacheFile;
                    Debug.WriteLine($"旧72dpi画像を使用: {imageFile}");
                }
                else if (File.Exists(oldLowDpiCacheFile))
                {
                    imageFile = oldLowDpiCacheFile;
                    Debug.WriteLine($"旧36dpi画像を使用: {imageFile}");
                }

                if (imageFile != null)
                {
                    // 既存の画像をクリア
                    _imageBitmap?.Dispose();
                    _selectionRects.Clear();

                    // ページ画像を読み込み
                    _imageBitmap = SKBitmap.Decode(imageFile);
                    _selectedImagePath = imageFile;

                    if (_imageBitmap != null)
                    {
                        _canvasView.InvalidateSurface();
                        await ShowToast($"ページ {pageIndex + 1} が読み込まれました");
                    }
                    else
                    {
                        await DisplayAlert("エラー", "ページ画像の読み込みに失敗しました", "OK");
                    }
                }
                else
                {
                    await DisplayAlert("エラー", "ページ画像が見つかりません", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ画像読み込みエラー: {ex.Message}");
                await DisplayAlert("エラー", "ページ画像の読み込み中にエラーが発生しました", "OK");
            }
        }

        /// <summary>
        /// メインスクロールビューのスクロールイベント
        /// </summary>
        private void OnMainScrollViewScrolled(object sender, ScrolledEventArgs e)
        {
            try
            {
                // ページ選択モード中のみページ更新
                if (_isPageSelectionMode && _backgroundCanvas?.HasContent == true && _backgroundCanvas?.PageCount > 0)
                {
                    // BackgroundCanvasに直接更新させる（より高速）
                    _backgroundCanvas.UpdateSelectedPage();
                    
                    // 選択ページが変わった場合のみUI更新
                    var currentPage = _backgroundCanvas.GetCurrentPageIndex();
                    if (currentPage != _selectedPageIndex)
                    {
                        _selectedPageIndex = currentPage;
                        
                        // ラベルテキストを更新
                        if (_pageSelectionLabel != null)
                        {
                            _pageSelectionLabel.Text = $"ページ {currentPage + 1} を選択しますか？";
                        }
                        
                        Debug.WriteLine($"ページ選択更新: {currentPage + 1}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スクロールイベントエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在表示されている画像を画像穴埋め用に読み込み
        /// </summary>
        private async Task LoadCurrentImageAsImageFill()
        {
            try
            {
                var currentImagePath = _backgroundCanvas?.CurrentImagePath;
                if (string.IsNullOrEmpty(currentImagePath) || !File.Exists(currentImagePath))
                {
                    await DisplayAlert("エラー", "現在の画像が見つかりません", "OK");
                    return;
                }

                // 既存の画像をクリア
                _imageBitmap?.Dispose();
                _selectionRects.Clear();

                // 現在の画像を読み込み
                _imageBitmap = SKBitmap.Decode(currentImagePath);
                _selectedImagePath = currentImagePath;

                if (_imageBitmap != null)
                {
                    _canvasView.InvalidateSurface();
                    await ShowToast("現在の画像が読み込まれました");
                }
                else
                {
                    await DisplayAlert("エラー", "画像の読み込みに失敗しました", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"現在画像読み込みエラー: {ex.Message}");
                await DisplayAlert("エラー", "画像の読み込み中にエラーが発生しました", "OK");
            }
        }

        /// <summary>
        /// 画像を追加
        /// </summary>
        private async Task AddImageToEditor(Editor editor)
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
                    using (var bitmap = SkiaSharp.SKBitmap.Decode(sourceStream))
                    {
                        using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
                        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80)) // 品質を80%に設定
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
                if (editor == _frontTextEditor)
                {
                    UpdateFrontPreviewWithDebounce();
                }
                else if (editor == _backTextEditor)
                {
                    UpdateBackPreviewWithDebounce();
                }
                else if (editor == _choiceQuestion)
                {
                    UpdateChoiceQuestionPreviewWithDebounce();
                }
                else if (editor == _choiceQuestionExplanation)
                {
                    UpdateChoiceExplanationPreviewWithDebounce();
                }
            }
        }

        /// <summary>
        /// 現在のページを画像として追加
        /// </summary>
        private async Task AddCurrentPageAsImage(Editor editor)
        {
            try
            {
                int currentPageIndex = GetCurrentPageIndex();
                if (currentPageIndex < 0)
                {
                    await DisplayAlert("エラー", "現在のページを取得できませんでした", "OK");
                    return;
                }

                // 一時的に画像を保存するための変数
                SkiaSharp.SKBitmap tempBitmap = null;

                try
                {
                    // PageCacheから画像を取得
                    var cacheDir = Path.Combine(tempExtractPath, "PageCache");
                    var highDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)150f}.png");
                    var mediumDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)96f}.png");
                    var oldHighDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)72f}.png");
                    var oldLowDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)36f}.png");

                    string imageFile = null;
                    if (File.Exists(highDpiCacheFile))
                    {
                        imageFile = highDpiCacheFile;
                    }
                    else if (File.Exists(mediumDpiCacheFile))
                    {
                        imageFile = mediumDpiCacheFile;
                    }
                    else if (File.Exists(oldHighDpiCacheFile))
                    {
                        imageFile = oldHighDpiCacheFile;
                    }
                    else if (File.Exists(oldLowDpiCacheFile))
                    {
                        imageFile = oldLowDpiCacheFile;
                    }

                    if (imageFile != null)
                    {
                        // ページ画像を読み込み
                        tempBitmap = SkiaSharp.SKBitmap.Decode(imageFile);
                        
                        if (tempBitmap != null)
                        {
                            // 画像IDを生成
                            Random random = new Random();
                            string imageId8 = random.Next(10000000, 99999999).ToString();
                            string imageId6 = random.Next(100000, 999999).ToString();
                            string imageId = $"{imageId8}_{imageId6}";
                            
                            string imageFolder = Path.Combine(tempExtractPath, "img");
                            Directory.CreateDirectory(imageFolder);

                            string newFileName = $"img_{imageId}.jpg";
                            string newFilePath = Path.Combine(imageFolder, newFileName);

                            // ビットマップを保存
                            SaveBitmapToFile(tempBitmap, newFilePath);

                            // エディタに画像タグを挿入
                            int cursorPosition = editor.CursorPosition;
                            string text = editor.Text ?? "";
                            string newText = text.Insert(cursorPosition, $"<<img_{imageId}.jpg>>");
                            editor.Text = newText;
                            editor.CursorPosition = cursorPosition + $"<<img_{imageId}.jpg>>".Length;

                            // プレビューを更新
                            if (editor == _frontTextEditor)
                            {
                                UpdateFrontPreviewWithDebounce();
                            }
                            else if (editor == _backTextEditor)
                            {
                                UpdateBackPreviewWithDebounce();
                            }
                            else if (editor == _choiceQuestion)
                            {
                                UpdateChoiceQuestionPreviewWithDebounce();
                            }
                            else if (editor == _choiceQuestionExplanation)
                            {
                                UpdateChoiceExplanationPreviewWithDebounce();
                            }

                            await ShowToast($"ページ {currentPageIndex + 1} を画像として追加しました");
                        }
                        else
                        {
                            await DisplayAlert("エラー", "ページの画像化に失敗しました", "OK");
                        }
                    }
                    else
                    {
                        await DisplayAlert("エラー", "ページ画像が見つかりません", "OK");
                    }
                }
                finally
                {
                    // 一時的なビットマップを解放
                    tempBitmap?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ画像追加エラー: {ex.Message}");
                await DisplayAlert("エラー", "ページの画像化中にエラーが発生しました", "OK");
            }
        }

        // 基本・穴埋めカード用のイベントハンドラー
        private async void OnFrontAddImageClicked(object sender, EventArgs e)
        {
            await AddImageToEditor(_frontTextEditor);
        }

        private async void OnFrontAddPageImageClicked(object sender, EventArgs e)
        {
            _lastFocusedEditor = _frontTextEditor;
            OnSelectPageForImageFill(sender, e);
        }

        private async void OnBackAddImageClicked(object sender, EventArgs e)
        {
            await AddImageToEditor(_backTextEditor);
        }

        private async void OnBackAddPageImageClicked(object sender, EventArgs e)
        {
            _lastFocusedEditor = _backTextEditor;
            OnSelectPageForImageFill(sender, e);
        }

        // 選択肢カード用のイベントハンドラー
        private async void OnChoiceQuestionAddImageClicked(object sender, EventArgs e)
        {
            await AddImageToEditor(_choiceQuestion);
        }

        private async void OnChoiceQuestionAddPageImageClicked(object sender, EventArgs e)
        {
            _lastFocusedEditor = _choiceQuestion;
            OnSelectPageForImageFill(sender, e);
        }

        private async void OnChoiceExplanationAddImageClicked(object sender, EventArgs e)
        {
            await AddImageToEditor(_choiceQuestionExplanation);
        }

        private async void OnChoiceExplanationAddPageImageClicked(object sender, EventArgs e)
        {
            _lastFocusedEditor = _choiceQuestionExplanation;
            OnSelectPageForImageFill(sender, e);
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
                _imageBitmap?.Dispose();
                _imageBitmap = null;
                _selectedImagePath = "";
                _selectionRects.Clear();
                _isDragging = false;

                // キャンバスを再描画
                _canvasView?.InvalidateSurface();

                await ShowToast("画像を消去しました");
                
                Debug.WriteLine("画像穴埋めの画像と選択範囲を消去");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像消去エラー: {ex.Message}");
                await DisplayAlert("エラー", "画像の消去中にエラーが発生しました", "OK");
            }
        }

        /// <summary>
        /// テキスト選択モードを有効にする
        /// </summary>
        private async Task EnableTextSelectionMode()
        {
            try
            {
                Debug.WriteLine("=== テキスト選択モード開始 ===");
                
                // 現在の状態をデバッグ出力
                Debug.WriteLine($"📄 BackgroundCanvas状態: {(_backgroundCanvas != null ? "存在" : "null")}");
                if (_backgroundCanvas != null)
                {
                    Debug.WriteLine($"📄 HasContent: {_backgroundCanvas.HasContent}");
                    Debug.WriteLine($"📄 PageCount: {_backgroundCanvas.PageCount}");
                    Debug.WriteLine($"📄 CurrentPageIndex: {_backgroundCanvas.GetCurrentPageIndex()}");
                }
                
                if (_backgroundCanvas?.HasContent != true)
                {
                    Debug.WriteLine("❌ PDFが読み込まれていません");
                    await DisplayAlert("エラー", "PDFが読み込まれていません", "OK");
                    return;
                }

                _isTextSelectionMode = true;
                Debug.WriteLine("✅ テキスト選択モード有効化");

                // PDF.js WebViewを初期化（現在のページのテキストを表示）
                await InitializePdfTextSelectionWebView();
                
                // BackgroundCanvasのスクロールイベントを監視してページ変更を検出
                if (_backgroundCanvas?.ParentScrollView != null)
                {
                    _backgroundCanvas.ParentScrollView.Scrolled += OnScrollViewScrolledForTextSelection;
                    Debug.WriteLine("✅ スクロールイベントハンドラー追加完了");
                }
                else
                {
                    Debug.WriteLine("❌ ParentScrollViewが見つかりません");
                }
                
                // BackgroundCanvasはそのまま表示（テキスト選択は閉じるボタンのみ）

                // BackgroundCanvasはそのまま表示（透明度変更なし）
                // オーバーレイが透明なので、BackgroundCanvasが透けて見える

                // 現在のページのテキストを取得してWebViewに表示
                await UpdateWebViewForCurrentPage();

                await ShowToast("テキスト選択モードを有効にしました - PDFの上でテキストを選択できます");
                Debug.WriteLine("=== テキスト選択モード開始完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ テキスト選択モード有効化エラー: {ex.Message}");
                Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                await DisplayAlert("エラー", "テキスト選択モードの有効化に失敗しました", "OK");
            }
        }

        /// <summary>
        /// テキスト選択モードを無効にする
        /// </summary>
        private async Task DisableTextSelectionMode()
        {
            try
            {
                _isTextSelectionMode = false;
                Debug.WriteLine("テキスト選択モード無効化開始");

                // WebViewを非表示にして選択をクリア
                if (_pdfTextSelectionWebView != null)
                {
                    try
                    {
                        // 選択をクリア
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("clearSelection()");
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("updateStatus('テキスト選択モード終了')");
                        await Task.Delay(300);
                    }
                    catch (Exception jsEx)
                    {
                        Debug.WriteLine($"JavaScript実行エラー: {jsEx.Message}");
                    }
                    
                    // WebViewは常に表示状態を維持し、透明度のみ調整
                    _pdfTextSelectionWebView.Opacity = 0.01; // ほぼ透明に戻す
                    _pdfTextSelectionWebView.InputTransparent = false; // タッチイベントは受け取り続ける
                    Debug.WriteLine("WebView透明度調整完了");
                }

                // BackgroundCanvasの状態は変更していないので復元不要
                Debug.WriteLine("BackgroundCanvas状態維持");

                // スクロールイベントハンドラーを削除
                if (_backgroundCanvas?.ParentScrollView != null)
                {
                    _backgroundCanvas.ParentScrollView.Scrolled -= OnScrollViewScrolledForTextSelection;
                }

                // 選択されたテキストをクリア
                _selectedText = "";

                await ShowToast("テキスト選択モードを無効にしました");
                Debug.WriteLine("テキスト選択モード無効化完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択モード無効化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// PDF.jsテキスト選択WebViewを初期化
        /// </summary>
        private async Task InitializePdfTextSelectionWebView()
        {
            try
            {
                Debug.WriteLine("PDF.js WebView初期化開始");
                
                if (_pdfTextSelectionWebView == null)
                {
                    _pdfTextSelectionWebView = new WebView
                    {
                        HorizontalOptions = LayoutOptions.Fill,
                        VerticalOptions = LayoutOptions.Fill,
                        BackgroundColor = Colors.Transparent,
                        IsVisible = true, // 常に表示
                        // テキスト選択を可能にするためInputTransparentをfalseに
                        InputTransparent = false,
                        Opacity = 0.01 // ほぼ透明だが、タッチイベントは受け取る
                    };
                    
                    // プラットフォーム固有の透明化設定
#if WINDOWS
                    _pdfTextSelectionWebView.HandlerChanged += (s, e) =>
                    {
                        try
                        {
                            if (_pdfTextSelectionWebView.Handler?.PlatformView != null)
                            {
                                var platformView = _pdfTextSelectionWebView.Handler.PlatformView;
                                Debug.WriteLine($"WebView2プラットフォームビュー取得: {platformView.GetType().Name}");
                                
                                // WebView2の背景を透明に設定
                                var webView2Type = platformView.GetType();
                                var backgroundColorProperty = webView2Type.GetProperty("DefaultBackgroundColor");
                                if (backgroundColorProperty != null)
                                {
                                    // 完全透明に設定（リフレクションでColor構造体を作成）
                                    var colorType = backgroundColorProperty.PropertyType;
                                    var transparentColor = Activator.CreateInstance(colorType);
                                    
                                    // FromArgbメソッドを探して透明色を作成
                                    var fromArgbMethod = colorType.GetMethod("FromArgb", new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
                                    if (fromArgbMethod != null)
                                    {
                                        transparentColor = fromArgbMethod.Invoke(null, new object[] { (byte)0, (byte)0, (byte)0, (byte)0 });
                                    }
                                    
                                    backgroundColorProperty.SetValue(platformView, transparentColor);
                                    Debug.WriteLine("WebView2背景色を透明に設定完了");
                                }
                                else
                                {
                                    Debug.WriteLine("DefaultBackgroundColorプロパティが見つかりません");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"WebView2透明化設定エラー: {ex.Message}");
                        }
                    };
#endif
                    
                    // タップで閉じる機能は削除（常にテキスト選択可能にするため）
                    // var tapGesture = new TapGestureRecognizer();
                    // tapGesture.Tapped += async (s, e) =>
                    // {
                    //     Debug.WriteLine("WebViewタップイベント");
                    //     await DisableTextSelectionMode();
                    // };
                    // _pdfTextSelectionWebView.GestureRecognizers.Add(tapGesture);

                    // WebViewメッセージハンドラーを設定
                    _pdfTextSelectionWebView.Navigated += OnPdfTextSelectionNavigated;
                    
                    // プラットフォーム固有のメッセージハンドリング
                    SetupWebViewMessageHandling();

                    // メインGridに追加
                    if (Content is Grid mainGrid)
                    {
                        mainGrid.Children.Add(_pdfTextSelectionWebView);
                        Grid.SetRowSpan(_pdfTextSelectionWebView, mainGrid.RowDefinitions.Count);
                        Grid.SetColumnSpan(_pdfTextSelectionWebView, mainGrid.ColumnDefinitions.Count);
                        Debug.WriteLine("WebViewをGridに追加完了");
                    }

                    // HTMLファイルを読み込み
                    Debug.WriteLine("HTML読み込み開始");
                    var htmlContent = await LoadPdfJsViewerHtml();
                    Debug.WriteLine($"HTML内容長さ: {htmlContent.Length}文字");
                    
                    // WebViewに直接HTMLコンテンツを設定
                    _pdfTextSelectionWebView.Source = new HtmlWebViewSource
                    {
                        Html = htmlContent
                    };
                    Debug.WriteLine("WebViewにHTML設定完了");
                }

                _pdfTextSelectionWebView.IsVisible = true;
                // テキスト選択のためにタッチイベントを受け取る
                _pdfTextSelectionWebView.InputTransparent = false;
                _pdfTextSelectionWebView.Opacity = 0.3; // テキスト選択時は少し見えるように
                Debug.WriteLine("WebView表示設定完了");
                
                // 5秒のタイムアウトを設定
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(
                    WaitForWebViewReady(),
                    timeoutTask
                );
                
                if (completedTask == timeoutTask)
                {
                    Debug.WriteLine("WebView初期化タイムアウト - 続行します");
                    await ShowToast("テキスト選択モード初期化中...");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF.js WebView初期化エラー: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// WebViewの準備完了を待機
        /// </summary>
        private async Task WaitForWebViewReady()
        {
            var maxAttempts = 15;
            var attempt = 0;
            
            while (attempt < maxAttempts)
            {
                attempt++;
                Debug.WriteLine($"WebView準備チェック {attempt}/{maxAttempts}");
                
                try
                {
                    // Document readyStateをチェック
                    var readyState = await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("document.readyState");
                    Debug.WriteLine($"Document readyState: {readyState}");
                    
                    if (readyState?.Contains("complete") == true)
                    {
                        Debug.WriteLine("✅ WebView DOM読み込み完了");
                        
                        // 追加で関数の存在確認
                        try
                        {
                            var functionTest = await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("typeof updatePageText");
                            Debug.WriteLine($"updatePageText関数: {functionTest}");
                            
                            if (functionTest?.Contains("function") == true)
                            {
                                Debug.WriteLine("✅ WebView完全準備完了");
                                return;
                            }
                        }
                        catch (Exception funcEx)
                        {
                            Debug.WriteLine($"関数チェックエラー: {funcEx.Message}");
                        }
                        
                        // DOM準備完了なら続行
                        if (attempt >= 5)
                        {
                            Debug.WriteLine("✅ WebView基本準備完了 - 続行");
                            return;
                        }
                    }
                    else if (readyState?.Contains("interactive") == true && attempt >= 8)
                    {
                        Debug.WriteLine("✅ WebView対話可能状態 - 続行");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WebView準備チェック{attempt}エラー: {ex.Message}");
                    
                    // 後半の試行でエラーが続く場合は続行
                    if (attempt >= 10)
                    {
                        Debug.WriteLine("⚠️ WebViewエラー継続 - 強制続行");
                        return;
                    }
                }
                
                await Task.Delay(400);
            }
            
            Debug.WriteLine("⚠️ WebView初期化タイムアウト - 強制続行");
        }

        /// <summary>
        /// PDF.js ViewerのHTMLを読み込み
        /// </summary>
        private async Task<string> LoadPdfJsViewerHtml()
        {
            try
            {
                Debug.WriteLine("PDF.js ViewerHTML読み込み開始");
                
                // リソースからHTMLファイルを読み込み
                using var stream = await FileSystem.OpenAppPackageFileAsync("pdfjs-viewer.html");
                using var reader = new StreamReader(stream);
                var html = await reader.ReadToEndAsync();
                
                Debug.WriteLine($"HTML読み込み完了: {html.Length}文字");
                
                // HTMLはそのまま使用（テキストは後でJavaScriptで動的に更新）
                Debug.WriteLine("HTMLファイル読み込み完了 - テキストは動的に更新予定");
                
                return html;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF.js ViewerHTML読み込みエラー: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 現在のページ番号を取得
        /// </summary>
        private int GetCurrentPageNumber()
        {
            try
            {
                if (_backgroundCanvas != null)
                {
                    return _backgroundCanvas.GetCurrentPageIndex() + 1; // 1ベースのページ番号
                }
                return 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"現在のページ番号取得エラー: {ex.Message}");
                return 1;
            }
        }
        
        /// <summary>
        /// 現在のページのテキストを取得
        /// </summary>
        private async Task<string> GetCurrentPageTextAsync()
        {
            try
            {
                Debug.WriteLine("=== GetCurrentPageTextAsync 開始 ===");
                
                if (_backgroundCanvas == null)
                {
                    Debug.WriteLine("❌ BackgroundCanvasが初期化されていません");
                    return "";
                }
                
                Debug.WriteLine($"✅ BackgroundCanvas初期化済み: HasContent={_backgroundCanvas.HasContent}, PageCount={_backgroundCanvas.PageCount}");
                
                var currentPageIndex = _backgroundCanvas.GetCurrentPageIndex();
                Debug.WriteLine($"📄 現在のページインデックス: {currentPageIndex}");
                
                var pdfPath = GetCurrentPdfPath();
                Debug.WriteLine($"📁 PDFパス: {pdfPath}");
                
                if (string.IsNullOrEmpty(pdfPath))
                {
                    Debug.WriteLine("❌ PDFパスが空です");
                    return "";
                }
                
                if (!File.Exists(pdfPath))
                {
                    Debug.WriteLine($"❌ PDFファイルが存在しません: {pdfPath}");
                    return "";
                }
                
                Debug.WriteLine($"✅ PDFファイル存在確認OK: {new FileInfo(pdfPath).Length} bytes");
                Debug.WriteLine($"🔍 ページ{currentPageIndex + 1}のテキスト抽出開始");
                
                // PdfiumViewerを使用してテキストを抽出
                using var document = PdfiumViewer.PdfDocument.Load(pdfPath);
                Debug.WriteLine($"📖 PDF読み込み成功: {document.PageCount}ページ");
                
                if (currentPageIndex >= 0 && currentPageIndex < document.PageCount)
                {
                    var pageText = document.GetPdfText(currentPageIndex);
                    Debug.WriteLine($"✅ ページ{currentPageIndex + 1}テキスト抽出完了: {pageText?.Length ?? 0}文字");
                    
                    if (!string.IsNullOrEmpty(pageText))
                    {
                        // 最初の100文字をログに出力
                        var preview = pageText.Length > 100 ? pageText.Substring(0, 100) + "..." : pageText;
                        Debug.WriteLine($"📝 テキストプレビュー: {preview}");
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ 抽出されたテキストが空です");
                    }
                    
                    return pageText ?? "";
                }
                
                Debug.WriteLine($"❌ 無効なページインデックス: {currentPageIndex} (総ページ数: {document.PageCount})");
                return "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ページテキスト抽出エラー: {ex.Message}");
                Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                return "";
            }
        }
        
        /// <summary>
        /// HTMLのテスト用テキストを実際のページテキストに置き換え
        /// </summary>
        private string ReplaceTestTextWithPageText(string html, string pageText)
        {
            try
            {
                if (string.IsNullOrEmpty(pageText))
                {
                    return html;
                }
                
                // テキストを行に分割
                var lines = pageText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var textSpans = new List<string>();
                
                var yPosition = 100;
                var lineHeight = 25;
                
                for (int i = 0; i < Math.Min(lines.Length, 20); i++) // 最大20行まで表示
                {
                    var line = lines[i].Trim();
                    if (!string.IsNullOrEmpty(line) && line.Length > 2) // 短すぎる行は除外
                    {
                        // HTMLエスケープ
                        line = System.Net.WebUtility.HtmlEncode(line);
                        
                        var span = $@"<span style=""position: absolute; left: 50px; top: {yPosition}px; font-size: 14px; color: rgba(0,0,0,0.8); background: rgba(255,255,255,0.1); padding: 2px; max-width: 80%; word-wrap: break-word;"">{line}</span>";
                        textSpans.Add(span);
                        yPosition += lineHeight;
                    }
                }
                
                if (textSpans.Count > 0)
                {
                    var newTextContent = string.Join("\n            ", textSpans);
                    
                    // 既存のテスト用テキストを置き換え
                    var startMarker = "<!-- テスト用の選択可能なテキスト -->";
                    var endMarker = "</span>";
                    
                    var startIndex = html.IndexOf(startMarker);
                    if (startIndex >= 0)
                    {
                        var endIndex = html.IndexOf("</div>", startIndex);
                        if (endIndex >= 0)
                        {
                            var beforeText = html.Substring(0, startIndex);
                            var afterText = html.Substring(endIndex);
                            
                            html = beforeText + $"<!-- 現在のページのテキスト -->\n            {newTextContent}\n        " + afterText;
                        }
                    }
                }
                
                return html;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト置換エラー: {ex.Message}");
                return html;
            }
        }

        /// <summary>
        /// PDF.jsライブラリのパスを取得
        /// </summary>
        private string GetPdfJsPath()
        {
            try
            {
                Debug.WriteLine("PDF.jsパス解決開始");
                
                // プラットフォーム固有のパス処理
#if ANDROID
                var path = "file:///android_asset/pdfjs/pdf.js";
                Debug.WriteLine($"Android PDF.jsパス: {path}");
                return path;
#elif IOS
                var path = "pdfjs/pdf.js";
                Debug.WriteLine($"iOS PDF.jsパス: {path}");
                return path;
#elif WINDOWS
                // WindowsではMauiAssetからPDF.jsファイルを読み込み
                var path = "ms-appx:///Resources/Raw/pdfjs/pdf.js";
                Debug.WriteLine($"Windows PDF.jsパス: {path}");
                return path;
#else
                var path = "pdfjs/pdf.js";
                Debug.WriteLine($"Default PDF.jsパス: {path}");
                return path;
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF.jsパス解決エラー: {ex.Message}");
                return "pdfjs/pdf.js"; // フォールバック
            }
        }

        /// <summary>
        /// PDF.js WebViewナビゲーション完了イベント
        /// </summary>
        private async void OnPdfTextSelectionNavigated(object sender, WebNavigatedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"PDF.js WebViewナビゲーション完了: {e.Result}");
                Debug.WriteLine($"URL: {e.Url}");
                
                if (e.Result == WebNavigationResult.Success)
                {
                    Debug.WriteLine("WebViewナビゲーション成功 - テストモード開始");
                    
                    // 少し待ってからJavaScript実行をテスト
                    await Task.Delay(1000);
                    
                    try
                    {
                        // JavaScript実行テスト
                        var testResult = await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("'JavaScript実行可能'");
                        Debug.WriteLine($"JavaScript実行テスト結果: {testResult}");
                        
                        // ステータス更新
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(
                            "updateStatus('C#との通信確認完了 - テキストを選択してください')");
                        
                        // デバッグ情報更新
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(
                            "updateDebug('C#からのJavaScript実行成功')");
                        
                        await ShowToast("テキスト選択モード準備完了");
                        
                        // 5秒後に自動テストを実行
                        await Task.Delay(5000);
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("testSelection()");
                    }
                    catch (Exception jsEx)
                    {
                        Debug.WriteLine($"JavaScript実行エラー: {jsEx.Message}");
                        await ShowToast($"JavaScript実行エラー: {jsEx.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine($"WebViewナビゲーション失敗: {e.Result}");
                    await ShowToast($"WebView読み込み失敗: {e.Result}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF.js WebViewナビゲーション完了エラー: {ex.Message}");
                await ShowToast($"エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// PDFデータをPDF.js WebViewに読み込み
        /// </summary>
        private async Task LoadPdfIntoPdfJsWebView()
        {
            try
            {
                // 現在読み込まれているPDFファイルのパスを取得
                string pdfPath = GetCurrentPdfPath();
                if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
                {
                    Debug.WriteLine("PDFファイルが見つかりません");
                    return;
                }

                // PDFファイルをBase64に変換
                var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
                var base64Pdf = Convert.ToBase64String(pdfBytes);

                // 現在のスケールを取得
                var scale = _backgroundCanvas?.CurrentScale ?? 1.0f;

                // JavaScriptでPDFを読み込み
                var script = $@"
                    if (typeof loadPdf === 'function') {{
                        const pdfData = Uint8Array.from(atob('{base64Pdf}'), c => c.charCodeAt(0));
                        loadPdf(pdfData, {scale});
                    }}
                ";

                await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                Debug.WriteLine("PDFデータをPDF.js WebViewに読み込み完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDFデータ読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のPDFファイルパスを取得
        /// </summary>
        private string GetCurrentPdfPath()
        {
            try
            {
                Debug.WriteLine("=== GetCurrentPdfPath 開始 ===");
                
                if (_backgroundCanvas == null)
                {
                    Debug.WriteLine("❌ BackgroundCanvas is null");
                    return null;
                }

                Debug.WriteLine("✅ BackgroundCanvas存在確認OK");

                // BackgroundCanvasから現在のPDFパスを取得
                // リフレクションを使って_currentPdfPathフィールドにアクセス
                var type = typeof(BackgroundCanvas);
                Debug.WriteLine($"🔍 BackgroundCanvasタイプ: {type.Name}");
                
                var field = type.GetField("_currentPdfPath", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    var pdfPath = field.GetValue(_backgroundCanvas) as string;
                    Debug.WriteLine($"✅ PDF path from reflection: {pdfPath}");
                    
                    // 追加の状態確認
                    var pdfDocumentField = type.GetField("_pdfDocument", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (pdfDocumentField != null)
                    {
                        var pdfDocument = pdfDocumentField.GetValue(_backgroundCanvas);
                        Debug.WriteLine($"📖 _pdfDocument状態: {(pdfDocument != null ? "存在" : "null")}");
                    }
                    
                    var hasContentProperty = type.GetProperty("HasContent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (hasContentProperty != null)
                    {
                        var hasContent = hasContentProperty.GetValue(_backgroundCanvas);
                        Debug.WriteLine($"📄 HasContent: {hasContent}");
                    }
                    
                    var pageCountProperty = type.GetProperty("PageCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (pageCountProperty != null)
                    {
                        var pageCount = pageCountProperty.GetValue(_backgroundCanvas);
                        Debug.WriteLine($"📄 PageCount: {pageCount}");
                    }
                    
                    return pdfPath;
                }
                
                Debug.WriteLine("❌ _currentPdfPath field not found");
                
                // 利用可能なフィールドを列挙
                var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Debug.WriteLine($"🔍 利用可能なフィールド: {string.Join(", ", fields.Select(f => f.Name))}");
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ PDFパス取得エラー: {ex.Message}");
                Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// WebViewメッセージハンドリングを設定
        /// </summary>
        private void SetupWebViewMessageHandling()
        {
            try
            {
#if WINDOWS
                // Windows WebView2のメッセージハンドリング
                _pdfTextSelectionWebView.Navigated += async (sender, e) =>
                {
                    if (e.Result == WebNavigationResult.Success)
                    {
                        // C#からJavaScript関数を呼び出すためのブリッジを設定
                        var script = @"
                            window.chrome.webview.addEventListener('message', function(event) {
                                if (event.data.action === 'getSelectedText') {
                                    var selectedText = window.getSelection().toString();
                                    window.chrome.webview.postMessage({
                                        action: 'textSelected',
                                        text: selectedText
                                    });
                                }
                            });
                        ";
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                    }
                };
#elif ANDROID
                // Android WebViewのメッセージハンドリング
                _pdfTextSelectionWebView.Navigated += async (sender, e) =>
                {
                    if (e.Result == WebNavigationResult.Success)
                    {
                        // Android WebViewでのメッセージハンドリング
                        var script = @"
                            window.androidBridge = {
                                postMessage: function(message) {
                                    try {
                                        window.location = 'bridge://' + encodeURIComponent(JSON.stringify(message));
                                    } catch(e) {
                                        console.error('Message post error:', e);
                                    }
                                }
                            };
                        ";
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                    }
                };
                
                // URL変更監視でメッセージを受信
                _pdfTextSelectionWebView.Navigating += (sender, e) =>
                {
                    if (e.Url.StartsWith("bridge://"))
                    {
                        e.Cancel = true;
                        try
                        {
                            var messageData = Uri.UnescapeDataString(e.Url.Substring("bridge://".Length));
                            var message = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(messageData);
                            HandleWebViewMessage(message);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Android WebView メッセージ処理エラー: {ex.Message}");
                        }
                    }
                };
#elif IOS
                // iOS WKWebViewのメッセージハンドリング
                _pdfTextSelectionWebView.Navigated += async (sender, e) =>
                {
                    if (e.Result == WebNavigationResult.Success)
                    {
                        // iOS WebViewでのメッセージハンドリング
                        var script = @"
                            window.webkit.messageHandlers.bridge = {
                                postMessage: function(message) {
                                    try {
                                        window.location = 'bridge://' + encodeURIComponent(JSON.stringify(message));
                                    } catch(e) {
                                        console.error('Message post error:', e);
                                    }
                                }
                            };
                        ";
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                    }
                };
                
                // URL変更監視でメッセージを受信
                _pdfTextSelectionWebView.Navigating += (sender, e) =>
                {
                    if (e.Url.StartsWith("bridge://"))
                    {
                        e.Cancel = true;
                        try
                        {
                            var messageData = Uri.UnescapeDataString(e.Url.Substring("bridge://".Length));
                            var message = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(messageData);
                            HandleWebViewMessage(message);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"iOS WebView メッセージ処理エラー: {ex.Message}");
                        }
                    }
                };
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebViewメッセージハンドリング設定エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// WebViewからのメッセージを処理
        /// </summary>
        private async void HandleWebViewMessage(Dictionary<string, object> message)
        {
            try
            {
                Debug.WriteLine($"WebViewメッセージ受信: {string.Join(", ", message.Keys)}");
                
                if (message.TryGetValue("action", out var actionObj) && actionObj is string action)
                {
                    Debug.WriteLine($"WebViewメッセージアクション: {action}");
                    
                    switch (action)
                    {
                        case "textSelected":
                            if (message.TryGetValue("text", out var textObj) && textObj is string selectedText)
                            {
                                _selectedText = selectedText;
                                var displayText = selectedText.Length > 50 
                                    ? $"{selectedText.Substring(0, 50)}..." 
                                    : selectedText;
                                
                                Debug.WriteLine($"テキスト選択受信: {displayText}");
                                await ShowToast($"テキストを選択: {displayText}");
                                
                                // 現在のエディターにテキストを追加する選択肢を提供
                                var currentEditor = GetCurrentEditor();
                                if (currentEditor != null)
                                {
                                    await Task.Delay(1000);
                                    await ShowToast("選択したテキストをカードに追加しますか？");
                                }
                            }
                            break;
                        
                        case "ready":
                            Debug.WriteLine("PDF.js WebView準備完了通知受信");
                            await ShowToast("テキスト選択準備完了");
                            break;
                            
                        case "close":
                            Debug.WriteLine("WebViewから閉じる要求を受信");
                            await DisableTextSelectionMode();
                            break;
                        
                        default:
                            Debug.WriteLine($"不明なWebViewメッセージアクション: {action}");
                            break;
                    }
                }
                else
                {
                    Debug.WriteLine("WebViewメッセージにactionが含まれていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebViewメッセージ処理エラー: {ex.Message}");
                await ShowToast($"メッセージ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 選択されたテキストを取得
        /// </summary>
        private async Task<string> GetSelectedTextAsync()
        {
            try
            {
                if (_pdfTextSelectionWebView != null && _isTextSelectionMode)
                {
                    var script = @"
                        (function() {
                            var selectedText = '';
                            if (window.getSelection) {
                                selectedText = window.getSelection().toString();
                            } else if (document.selection && document.selection.createRange) {
                                selectedText = document.selection.createRange().text;
                            }
                            return selectedText;
                        })();
                    ";
                    
                    var result = await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                    return result ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択テキスト取得エラー: {ex.Message}");
            }
            
            return "";
        }

        /// <summary>
        /// テキスト選択をクリア
        /// </summary>
        private async Task ClearTextSelectionAsync()
        {
            try
            {
                if (_pdfTextSelectionWebView != null && _isTextSelectionMode)
                {
                    var script = @"
                        if (window.getSelection) {
                            window.getSelection().removeAllRanges();
                        } else if (document.selection) {
                            document.selection.clear();
                        }
                    ";
                    
                    await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                    _selectedText = "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択クリアエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// PDF.jsビューアーのズームをBackgroundCanvasと同期
        /// </summary>
        private async Task SyncPdfJsZoom()
        {
            try
            {
                if (_pdfTextSelectionWebView != null && _backgroundCanvas != null && _isTextSelectionMode)
                {
                    var scale = _backgroundCanvas.CurrentScale;
                    var script = $@"
                        if (typeof syncZoom === 'function') {{
                            syncZoom({scale});
                        }}
                    ";
                    
                    await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(script);
                    Debug.WriteLine($"PDF.jsズーム同期: {scale}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF.jsズーム同期エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// テキスト選択モード用のスクロールイベントハンドラー
        /// </summary>
        private int _lastPageIndex = -1;
        private async void OnScrollViewScrolledForTextSelection(object sender, ScrolledEventArgs e)
        {
            try
            {
                if (!_isTextSelectionMode || _backgroundCanvas == null)
                    return;

                var currentPageIndex = _backgroundCanvas.GetCurrentPageIndex();
                
                // ページが変更された場合のみWebViewを更新
                if (currentPageIndex != _lastPageIndex)
                {
                    _lastPageIndex = currentPageIndex;
                    Debug.WriteLine($"ページ変更検出: {currentPageIndex + 1}ページ目");
                    
                                    // WebViewのテキストを更新
                await UpdateWebViewForCurrentPage();
                
                // 少し待ってから再度更新を試行（確実にテキストを表示するため）
                await Task.Delay(1000);
                Debug.WriteLine("🔄 追加のテキスト更新を実行");
                await UpdateWebViewForCurrentPage();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択用スクロールイベントエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のページに合わせてWebViewを更新
        /// </summary>
        private async Task UpdateWebViewForCurrentPage()
        {
            try
            {
                if (_pdfTextSelectionWebView == null || !_isTextSelectionMode)
                    return;

                Debug.WriteLine("WebView更新開始");
                
                // 現在のページのテキストを取得
                var currentPageText = await GetCurrentPageTextAsync();
                var currentPageNumber = GetCurrentPageNumber();
                
                if (!string.IsNullOrEmpty(currentPageText))
                {
                    // JavaScriptでページテキストを更新
                    var escapedText = System.Text.Json.JsonEncodedText.Encode(currentPageText).ToString().Trim('"');
                    var updateScript = $@"
                        try {{
                            console.log('ページテキスト更新開始: ページ{currentPageNumber}');
                            
                            // updatePageText関数を使用してテキストを更新
                            if (typeof updatePageText === 'function') {{
                                updatePageText('{escapedText}', {currentPageNumber});
                                console.log('updatePageText関数でテキスト更新完了');
                            }} else {{
                                console.log('updatePageText関数が見つかりません - 直接更新');
                                
                                // 直接テキストコンテナを更新
                                var textContainer = document.getElementById('textContainer');
                                if (!textContainer) {{
                                    console.log('textContainerが見つかりません');
                                    return;
                                }}
                                
                                // 既存のテキストをクリア
                                textContainer.innerHTML = '';
                                
                                // 実際のPDFテキストを行に分割して表示
                                var pageText = '{escapedText}';
                                var lines = pageText.split(/[\\r\\n]+/).filter(line => line.trim() !== '');
                                
                                console.log('処理する行数:', lines.length);
                                
                                // 各行を配置
                                lines.forEach(function(line, index) {{
                                    if (index >= 20) return; // 最大20行まで
                                    
                                    line = line.trim();
                                    if (line.length > 0) {{
                                        var span = document.createElement('span');
                                        span.className = 'text-line';
                                        span.textContent = line;
                                        span.style.top = (50 + index * 25) + 'px';
                                        span.style.left = '50px';
                                        
                                        textContainer.appendChild(span);
                                        console.log('行追加:', line.substring(0, 30));
                                    }}
                                }});
                                
                                // ステータス更新
                                if (typeof updateStatus === 'function') {{
                                    updateStatus('ページ {currentPageNumber} - ' + lines.length + '行のテキスト表示中');
                                }}
                                
                                // デバッグ更新
                                if (typeof updateDebug === 'function') {{
                                    updateDebug('テキスト表示完了: ' + lines.length + '行');
                                }}
                            }}
                            
                            console.log('ページ{currentPageNumber}のテキスト更新完了');
                        }} catch(e) {{
                            console.log('ページ更新エラー:', e);
                            console.error('詳細エラー:', e.message, e.stack);
                        }}
                    ";
                    
                    await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(updateScript);
                    Debug.WriteLine($"✅ ページ{currentPageNumber}のテキスト更新完了");
                }
                else
                {
                    // テキストがない場合
                    var updateScript = $@"
                        try {{
                            updateStatus('ページ {currentPageNumber} - テキストなし');
                            clearPageText();
                        }} catch(e) {{
                            console.log('ページクリアエラー:', e);
                        }}
                    ";
                    
                    await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(updateScript);
                    Debug.WriteLine($"ページ{currentPageNumber}テキストなし");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView更新エラー: {ex.Message}");
            }
        }
    }
}
