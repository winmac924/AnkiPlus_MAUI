using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO.Compression;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Web;
using System.Reflection;

namespace AnkiPlus_MAUI
{
    public partial class Edit : ContentPage
    {
        private string tempExtractPath;
        private string ankplsFilePath;
        private List<CardInfo> cards = new List<CardInfo>();
        private List<CardInfo> filteredCards = new List<CardInfo>(); // 検索結果用
        private string editCardId = null;    // 編集対象のカードID
        private bool isDirty = false;        // 変更があるかどうかのフラグ
        private System.Timers.Timer autoSaveTimer;  // 自動保存用タイマー
        private List<SKRect> selectionRects = new List<SKRect>();  // 画像穴埋め用の選択範囲
        private SKBitmap imageBitmap;         // 画像を表示するためのビットマップ
        private SKPoint startPoint, endPoint;
        private bool isDragging = false;
        private const float HANDLE_SIZE = 15;

        // WebView初期化フラグ
        private bool _frontPreviewInitialized = false;
        private bool _backPreviewInitialized = false;
        private bool _choicePreviewInitialized = false;
        private bool _choiceExplanationPreviewInitialized = false;

        public class CardInfo
        {
            public string Id { get; set; }
            public string FrontText { get; set; }
            public string ImageInfo { get; set; }
            public bool HasImage { get; set; }
            public string LastModified { get; set; }
        }

        public Edit(string notePath, string tempPath)
        {
            try
            {
                Debug.WriteLine("Edit.xaml.cs コンストラクタ開始");
                InitializeComponent();

                tempExtractPath = tempPath;
                ankplsFilePath = notePath;

                // ノート名を設定
                NoteTitleLabel.Text = Path.GetFileNameWithoutExtension(ankplsFilePath);

                // カード情報を読み込む
                LoadCards();

                // 自動保存タイマーの設定
                autoSaveTimer = new System.Timers.Timer(5000); // 5秒ごとに自動保存
                autoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
                autoSaveTimer.AutoReset = false; // 一度だけ実行

                // テキスト変更イベントの設定
                FrontTextEditor.TextChanged += OnTextChanged;
                BackTextEditor.TextChanged += OnTextChanged;
                ChoiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
                ChoiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;

                // カードタイプの初期設定
                CardTypePicker.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Edit.xaml.cs コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private void LoadCards()
        {
            try
            {
                Debug.WriteLine("LoadCards開始");
                var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                var cardsDirPath = Path.Combine(tempExtractPath, "cards");

                if (File.Exists(cardsFilePath))
                {
                    var lines = File.ReadAllLines(cardsFilePath);
                    if (lines.Length > 1) // 1行目はカード数
                    {
                        cards.Clear();
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var parts = lines[i].Split(',');
                            if (parts.Length >= 2)
                            {
                                var cardId = parts[0];
                                var lastModified = DateTime.ParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", null);
                                var jsonPath = Path.Combine(cardsDirPath, $"{cardId}.json");

                                if (File.Exists(jsonPath))
                                {
                                    var jsonContent = File.ReadAllText(jsonPath);
                                    var cardData = JsonSerializer.Deserialize<CardData>(jsonContent);

                                    var cardInfo = new CardInfo
                                    {
                                        Id = cardId,
                                        FrontText = cardData.type == "選択肢" ? cardData.question : cardData.front,
                                        LastModified = lastModified.ToString("yyyy-MM-dd HH:mm:ss")
                                    };

                                    // 画像情報を取得
                                    var imageMatches = System.Text.RegularExpressions.Regex.Matches(cardInfo.FrontText, @"<<img_\d{8}_\d{6}\.jpg>>");
                                    if (imageMatches.Count > 0)
                                    {
                                        cardInfo.HasImage = true;
                                        cardInfo.ImageInfo = $"画像: {string.Join(", ", imageMatches.Select(m => m.Value))}";
                                    }

                                    cards.Add(cardInfo);
                                }
                            }
                        }

                        // カードを最終更新日時の降順でソート
                        cards = cards.OrderByDescending(c => DateTime.Parse(c.LastModified)).ToList();
                        filteredCards = new List<CardInfo>(cards);
                        CardsCollectionView.ItemsSource = filteredCards;
                        TotalCardsLabel.Text = $"カード枚数: {cards.Count}";
                        UpdateSearchResult();
                    }
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

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            // タイマーをリセット
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private void FrontOnTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateFrontPreviewWithDebounce(e.NewTextValue ?? "");
        }

        private void BackOnTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateBackPreviewWithDebounce(e.NewTextValue ?? "");
        }

        /// <summary>
        /// 表面プレビューを更新（デバウンス付き）
        /// </summary>
        private System.Timers.Timer _frontPreviewTimer;
        private void UpdateFrontPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _frontPreviewTimer?.Stop();
            _frontPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _frontPreviewTimer = new System.Timers.Timer(500);
            _frontPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await UpdateFrontPreviewAsync(text);
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
        private System.Timers.Timer _backPreviewTimer;
        private void UpdateBackPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _backPreviewTimer?.Stop();
            _backPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _backPreviewTimer = new System.Timers.Timer(500);
            _backPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await UpdateBackPreviewAsync(text);
                });
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                _backPreviewTimer = null;
            };
            _backPreviewTimer.AutoReset = false;
            _backPreviewTimer.Start();
        }

        /// <summary>
        /// 表面プレビューを更新（JavaScript手法）
        /// </summary>
        private async Task UpdateFrontPreviewAsync(string text)
        {
            try
            {
                if (!_frontPreviewInitialized)
                {
                    var baseHtml = CreateBaseHtmlTemplate();
                    FrontPreviewWebView.Source = new HtmlWebViewSource { Html = baseHtml };
                    _frontPreviewInitialized = true;
                    
                    // WebViewの読み込み完了を待つ
                    var tcs = new TaskCompletionSource<bool>();
                    
                    void OnNavigated(object sender, WebNavigatedEventArgs e)
                    {
                        FrontPreviewWebView.Navigated -= OnNavigated;
                        tcs.SetResult(true);
                    }
                    
                    FrontPreviewWebView.Navigated += OnNavigated;
                    
                    // タイムアウト設定
                    Device.StartTimer(TimeSpan.FromSeconds(3), () =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            FrontPreviewWebView.Navigated -= OnNavigated;
                            tcs.SetResult(false);
                        }
                        return false;
                    });
                    
                    await tcs.Task;
                    
                    // WebViewの完全初期化を待つ
                    await Task.Delay(200);
                    await UpdateWebViewContent(FrontPreviewWebView, text ?? "", false);
                }
                else
                {
                    // 2回目以降はコンテンツ部分のみ更新
                    await UpdateWebViewContent(FrontPreviewWebView, text ?? "", false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"表面プレビュー更新エラー: {ex.Message}");
                // フォールバック：従来の方法で更新
                try
                {
                    var fallbackHtml = ConvertMarkdownToHtml(text ?? "");
                    FrontPreviewWebView.Source = new HtmlWebViewSource { Html = fallbackHtml };
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"フォールバック更新エラー: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// 裏面プレビューを更新（JavaScript手法）
        /// </summary>
        private async Task UpdateBackPreviewAsync(string text)
        {
            try
            {
                if (!_backPreviewInitialized)
                {
                    var baseHtml = CreateBaseHtmlTemplate();
                    BackPreviewWebView.Source = new HtmlWebViewSource { Html = baseHtml };
                    _backPreviewInitialized = true;
                    
                    // WebViewの読み込み完了を待つ
                    var tcs = new TaskCompletionSource<bool>();
                    
                    void OnNavigated(object sender, WebNavigatedEventArgs e)
                    {
                        BackPreviewWebView.Navigated -= OnNavigated;
                        tcs.SetResult(true);
                    }
                    
                    BackPreviewWebView.Navigated += OnNavigated;
                    
                    // タイムアウト設定
                    Device.StartTimer(TimeSpan.FromSeconds(3), () =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            BackPreviewWebView.Navigated -= OnNavigated;
                            tcs.SetResult(false);
                        }
                        return false;
                    });
                    
                    await tcs.Task;
                    
                    // WebViewの完全初期化を待つ
                    await Task.Delay(200);
                    await UpdateWebViewContent(BackPreviewWebView, text ?? "", false);
                }
                else
                {
                    // 2回目以降はコンテンツ部分のみ更新
                    await UpdateWebViewContent(BackPreviewWebView, text ?? "", false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"裏面プレビュー更新エラー: {ex.Message}");
                // フォールバック：従来の方法で更新
                try
                {
                    var fallbackHtml = ConvertMarkdownToHtml(text ?? "");
                    BackPreviewWebView.Source = new HtmlWebViewSource { Html = fallbackHtml };
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"フォールバック更新エラー: {fallbackEx.Message}");
                }
            }
        }

        private string ConvertMarkdownToHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // ダークモード対応
            var isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;
            var backgroundColor = isDarkMode ? "#1E1E1E" : "#FFFFFF";
            var textColor = isDarkMode ? "#FFFFFF" : "#000000";
            var codeBackground = isDarkMode ? "#2D2D30" : "#F5F5F5";

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
                        text = text.Replace(match.Value, $"<img src={base64Image} style='max-height:150px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.2);' />");
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

            // 穴埋め変換 `<<blank|文字>>` → `( )`
            var redColor = isDarkMode ? "#FF6B6B" : "red";
            text = Regex.Replace(text, @"<<blank\|(.*?)>>", "( )");

            // 改行の正規化と段落処理
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // imgタグを一時的に保護
            var protectedElements = new List<string>();
            int elementIndex = 0;
            
            text = Regex.Replace(text, @"<img[^>]*>", match =>
            {
                var placeholder = $"__ELEMENT_PLACEHOLDER_{elementIndex}__";
                protectedElements.Add(match.Value);
                elementIndex++;
                return placeholder;
            });

            // HTML エスケープ
            text = HttpUtility.HtmlEncode(text);

            // 保護された要素を復元
            for (int i = 0; i < protectedElements.Count; i++)
            {
                text = text.Replace($"__ELEMENT_PLACEHOLDER_{i}__", protectedElements[i]);
            }

            // 太字変換
            text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");

            // ダークモード対応の色変換
            var blueColor = isDarkMode ? "#6BB6FF" : "blue";
            var greenColor = isDarkMode ? "#90EE90" : "green";
            var yellowColor = isDarkMode ? "#FFD700" : "yellow";
            var purpleColor = isDarkMode ? "#DA70D6" : "purple";
            var orangeColor = isDarkMode ? "#FFA500" : "orange";
            
            text = Regex.Replace(text, @"\{\{red\|(.*?)\}\}", $"<span style='color:{redColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{blue\|(.*?)\}\}", $"<span style='color:{blueColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{green\|(.*?)\}\}", $"<span style='color:{greenColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{yellow\|(.*?)\}\}", $"<span style='color:{yellowColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{purple\|(.*?)\}\}", $"<span style='color:{purpleColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{orange\|(.*?)\}\}", $"<span style='color:{orangeColor};'>$1</span>");

            // 上付き・下付き変換
            text = Regex.Replace(text, @"\^\^(.*?)\^\^", "<sup>$1</sup>");
            text = Regex.Replace(text, @"~~(.*?)~~", "<sub>$1</sub>");

            // 段落処理
            var lines = text.Split('\n');
            var processedLines = new List<string>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                if (string.IsNullOrWhiteSpace(line))
                {
                    // 空行の場合
                    if (processedLines.Count > 0 && !processedLines.Last().EndsWith("</p>"))
                    {
                        processedLines.Add("</p>");
                    }
                    if (i < lines.Length - 1 && !string.IsNullOrWhiteSpace(lines[i + 1]))
                    {
                        processedLines.Add("<p>");
                    }
                }
                else
                {
                    // 最初の行または前の行が空行の場合、段落開始
                    if (processedLines.Count == 0 || processedLines.Last().EndsWith("</p>") || processedLines.Last() == "<p>")
                    {
                        if (processedLines.Count == 0 || processedLines.Last() != "<p>")
                        {
                            processedLines.Add("<p>");
                        }
                        processedLines.Add(line);
                    }
                    else
                    {
                        // 同じ段落内の改行
                        processedLines.Add("<br>" + line);
                    }
                }
            }
            
            // 最後の段落を閉じる
            if (processedLines.Count > 0 && !processedLines.Last().EndsWith("</p>"))
            {
                processedLines.Add("</p>");
            }
            
            text = string.Join("", processedLines);

            // HTML テンプレート
            string htmlTemplate = $@"
            <html>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body {{ 
                        font-size: 18px; 
                        font-family: Arial, sans-serif; 
                        line-height: 1.5; 
                        background-color: {backgroundColor};
                        color: {textColor};
                        margin: 10px;
                        padding: 10px;
                        min-height: 100vh;
                    }}
                    sup {{ vertical-align: super; font-size: smaller; }}
                    sub {{ vertical-align: sub; font-size: smaller; }}
                    img {{ 
                        display: block; 
                        margin: 10px 0; 
                        border-radius: 8px;
                        box-shadow: 0 2px 8px rgba(0,0,0,0.2);
                        max-height: 150px;
                    }}
                    code {{
                        background-color: {codeBackground};
                        padding: 2px 4px;
                        border-radius: 4px;
                        font-family: 'Courier New', monospace;
                    }}
                    pre {{
                        background-color: {codeBackground};
                        padding: 10px;
                        border-radius: 8px;
                        overflow-x: auto;
                    }}
                    p {{
                        margin: 0 0 10px 0;
                        padding: 0;
                    }}
                    p:last-child {{
                        margin-bottom: 0;
                    }}
                </style>
            </head>
            <body>{text}</body>
            </html>";

            return htmlTemplate;
        }

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
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };

            return $"data:{mimeType};base64,{base64String}";
        }

        /// <summary>
        /// ベースHTMLテンプレートを作成（JavaScript機能付き）
        /// </summary>
        private string CreateBaseHtmlTemplate()
        {
            var isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;
            var backgroundColor = isDarkMode ? "#1E1E1E" : "#FFFFFF";
            var textColor = isDarkMode ? "#FFFFFF" : "#000000";
            var codeBackground = isDarkMode ? "#2D2D30" : "#F5F5F5";
            var redColor = isDarkMode ? "#FF6B6B" : "red";
            
            return $@"
            <html>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body {{ 
                        font-size: 18px; 
                        font-family: Arial, sans-serif; 
                        line-height: 1.5; 
                        background-color: {backgroundColor};
                        color: {textColor};
                        margin: 10px;
                        padding: 10px;
                        min-height: 100vh;
                    }}
                    sup {{ vertical-align: super; font-size: smaller; }}
                    sub {{ vertical-align: sub; font-size: smaller; }}
                    img {{ 
                        display: block; 
                        margin: 10px 0; 
                        border-radius: 8px;
                        box-shadow: 0 2px 8px rgba(0,0,0,0.2);
                        max-height: 150px;
                    }}
                    code {{
                        background-color: {codeBackground};
                        padding: 2px 4px;
                        border-radius: 4px;
                        font-family: 'Courier New', monospace;
                    }}
                    pre {{
                        background-color: {codeBackground};
                        padding: 10px;
                        border-radius: 8px;
                        overflow-x: auto;
                    }}
                    .blank-placeholder {{
                        min-width: 20px;
                        display: inline-block;
                    }}
                    #main-content {{
                        opacity: 1;
                        transition: opacity 0.3s ease;
                    }}
                    .loading {{
                        opacity: 0.5;
                    }}
                    p {{
                        margin: 0 0 10px 0;
                        padding: 0;
                    }}
                    p:last-child {{
                        margin-bottom: 0;
                    }}
                </style>
                <script>
                    console.log('JavaScript loaded');
                    
                    function updateContent(content) {{
                        console.log('updateContent called with:', content);
                        var mainContent = document.getElementById('main-content');
                        if (mainContent) {{
                            console.log('Updating main-content');
                            mainContent.innerHTML = content;
                            console.log('Content updated successfully');
                            console.log('New innerHTML:', mainContent.innerHTML);
                        }} else {{
                            console.error('main-content element not found');
                        }}
                    }}
                    
                    function updateContentBase64(base64Content) {{
                        console.log('updateContentBase64 called');
                        try {{
                            // 方法1: TextDecoderを使用（最も確実）
                            if (typeof TextDecoder !== 'undefined') {{
                                var binaryString = atob(base64Content);
                                var bytes = new Uint8Array(binaryString.length);
                                for (var i = 0; i < binaryString.length; i++) {{
                                    bytes[i] = binaryString.charCodeAt(i);
                                }}
                                var decoder = new TextDecoder('utf-8');
                                var decodedContent = decoder.decode(bytes);
                                console.log('TextDecoder decoded content:', decodedContent);
                            }} else {{
                                // 方法2: decodeURIComponent + escapeを使用
                                var decodedContent = decodeURIComponent(escape(atob(base64Content)));
                                console.log('decodeURIComponent decoded content:', decodedContent);
                            }}
                            
                            var mainContent = document.getElementById('main-content');
                            if (mainContent) {{
                                console.log('Updating main-content with base64 content');
                                mainContent.innerHTML = decodedContent;
                                console.log('Content updated successfully');
                            }} else {{
                                console.error('main-content element not found');
                            }}
                        }} catch (e) {{
                            console.error('Base64 decode error:', e);
                            // フォールバック: 標準のatobを試行
                            try {{
                                var fallbackContent = atob(base64Content);
                                var mainContent = document.getElementById('main-content');
                                if (mainContent) {{
                                    mainContent.innerHTML = fallbackContent;
                                    console.log('Fallback decode successful');
                                }}
                            }} catch (fallbackError) {{
                                console.error('Fallback decode also failed:', fallbackError);
                            }}
                        }}
                    }}
                    
                    function showAllAnswers() {{
                        var blanks = document.querySelectorAll('.blank-placeholder');
                        blanks.forEach(function(blank) {{
                            var answer = blank.getAttribute('data-answer');
                            if (answer) {{
                                blank.textContent = answer;
                                blank.style.color = '{redColor}';
                            }}
                        }});
                    }}
                    
                    function insertText(elementId, text) {{
                        var element = document.getElementById(elementId);
                        if (element) {{
                            element.innerHTML = text;
                        }}
                    }}
                    
                    // DOM読み込み完了後の確認
                    document.addEventListener('DOMContentLoaded', function() {{
                        console.log('DOM loaded, main-content exists:', !!document.getElementById('main-content'));
                    }});
                </script>
            </head>
            <body>
                <div id='main-content'>読み込み中...</div>
            </body>
            </html>";
        }

        /// <summary>
        /// コンテンツ部分のみのHTMLを生成
        /// </summary>
        private string ConvertToContentHtml(string text, bool showAnswer = false)
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

            // 穴埋め表示処理（JavaScript操作用のIDを付与）
            var isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;
            var redColor = isDarkMode ? "#FF6B6B" : "red";
            
            int blankCounter = 0;
            if (showAnswer)
            {
                // 解答表示時は `<<blank|文字>>` → `(文字)`
                text = Regex.Replace(text, @"<<blank\|(.*?)>>", match =>
                {
                    blankCounter++;
                    var answer = match.Groups[1].Value;
                    return $"(<span id='blank_{blankCounter}' style='color:{redColor};'>{answer}</span>)";
                });
            }
            else
            {
                // 問題表示時は `<<blank|文字>>` → `( )` （後でJavaScriptで操作可能）
                text = Regex.Replace(text, @"<<blank\|(.*?)>>", match =>
                {
                    blankCounter++;
                    var answer = match.Groups[1].Value;
                    return $"(<span id='blank_{blankCounter}' data-answer='{HttpUtility.HtmlEncode(answer)}' class='blank-placeholder'> </span>)";
                });
            }

            // 改行の正規化と段落処理
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // 穴埋めのspanタグとimgタグを一時的に保護
            var protectedElements = new List<string>();
            int elementIndex = 0;
            
            // spanタグを保護
            text = Regex.Replace(text, @"<span[^>]*>.*?</span>", match =>
            {
                var placeholder = $"__ELEMENT_PLACEHOLDER_{elementIndex}__";
                protectedElements.Add(match.Value);
                elementIndex++;
                return placeholder;
            });
            
            // imgタグを保護
            text = Regex.Replace(text, @"<img[^>]*>", match =>
            {
                var placeholder = $"__ELEMENT_PLACEHOLDER_{elementIndex}__";
                protectedElements.Add(match.Value);
                elementIndex++;
                return placeholder;
            });

            // HTML エスケープ
            text = HttpUtility.HtmlEncode(text);

            // 保護された要素を復元
            for (int i = 0; i < protectedElements.Count; i++)
            {
                text = text.Replace($"__ELEMENT_PLACEHOLDER_{i}__", protectedElements[i]);
            }

            // 太字変換
            text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");

            // ダークモード対応の色変換
            var blueColor = isDarkMode ? "#6BB6FF" : "blue";
            var greenColor = isDarkMode ? "#90EE90" : "green";
            var yellowColor = isDarkMode ? "#FFD700" : "yellow";
            var purpleColor = isDarkMode ? "#DA70D6" : "purple";
            var orangeColor = isDarkMode ? "#FFA500" : "orange";
            
            text = Regex.Replace(text, @"\{\{red\|(.*?)\}\}", $"<span style='color:{redColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{blue\|(.*?)\}\}", $"<span style='color:{blueColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{green\|(.*?)\}\}", $"<span style='color:{greenColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{yellow\|(.*?)\}\}", $"<span style='color:{yellowColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{purple\|(.*?)\}\}", $"<span style='color:{purpleColor};'>$1</span>");
            text = Regex.Replace(text, @"\{\{orange\|(.*?)\}\}", $"<span style='color:{orangeColor};'>$1</span>");

            // 上付き・下付き変換
            text = Regex.Replace(text, @"\^\^(.*?)\^\^", "<sup>$1</sup>");
            text = Regex.Replace(text, @"~~(.*?)~~", "<sub>$1</sub>");

            // 必要な部分だけデコード処理
            text = Regex.Replace(text, @"&lt;img(.*?)&gt;", "<img$1>");
            text = Regex.Replace(text, @"&lt;span(.*?)&gt;", "<span$1>");
            text = Regex.Replace(text, @"&lt;/span&gt;", "</span>");

            // 改行処理：連続する空行を段落として処理
            var lines = text.Split('\n');
            var processedLines = new List<string>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                if (string.IsNullOrEmpty(line))
                {
                    // 空行の場合、段落区切りとして処理
                    if (processedLines.Count > 0 && !processedLines.Last().EndsWith("</p>"))
                    {
                        processedLines.Add("</p><p>");
                    }
                }
                else
                {
                    // 最初の行の場合はpタグで開始
                    if (processedLines.Count == 0)
                    {
                        processedLines.Add("<p>" + line);
                    }
                    else if (processedLines.Last().EndsWith("</p><p>"))
                    {
                        // 新しい段落の最初の行
                        processedLines[processedLines.Count - 1] = processedLines.Last() + line;
                    }
                    else
                    {
                        // 同じ段落内での改行
                        processedLines.Add("<br>" + line);
                    }
                }
            }
            
            // 最後にpタグを閉じる
            if (processedLines.Count > 0 && !processedLines.Last().EndsWith("</p>"))
            {
                processedLines.Add("</p>");
            }
            
            text = string.Join("", processedLines);

            return text;
        }

        /// <summary>
        /// WebView初期化を確実に行う
        /// </summary>
        private async Task EnsureWebViewInitialized(WebView webView)
        {
            try
            {
                // WebViewハンドラーの初期化確認
                if (webView?.Handler == null)
                {
                    Debug.WriteLine("WebViewハンドラーが未初期化");
                    return;
                }

                // プラットフォーム固有の初期化
#if WINDOWS
                var platformView = webView.Handler.PlatformView;
                if (platformView != null)
                {
                    // リフレクションを使用してWebView2の初期化を確認
                    try
                    {
                        var coreWebView2Property = platformView.GetType().GetProperty("CoreWebView2");
                        if (coreWebView2Property != null)
                        {
                            var coreWebView2 = coreWebView2Property.GetValue(platformView);
                            if (coreWebView2 == null)
                            {
                                Debug.WriteLine("CoreWebView2を初期化中...");
                                var ensureMethod = platformView.GetType().GetMethod("EnsureCoreWebView2Async");
                                if (ensureMethod != null)
                                {
                                    var task = ensureMethod.Invoke(platformView, null) as Task;
                                    if (task != null)
                                    {
                                        await task;
                                        Debug.WriteLine("CoreWebView2初期化完了");
                                        // 初期化後に少し待機
                                        await Task.Delay(100);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception reflectionEx)
                    {
                        Debug.WriteLine($"リフレクション処理エラー: {reflectionEx.Message}");
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView初期化エラー: {ex.Message}");
                // 初期化エラーは致命的ではないため、続行
            }
        }

        /// <summary>
        /// WebViewのコンテンツ部分のみを更新
        /// </summary>
        private async Task UpdateWebViewContent(WebView webView, string text, bool showAnswer = false)
        {
            try
            {
                // WebViewが初期化されているかチェック
                if (webView?.Handler == null)
                {
                    Debug.WriteLine("WebView未初期化のため、フォールバックを使用");
                    throw new InvalidOperationException("WebView not initialized");
                }

                // WebView2の初期化を確実に行う
                await EnsureWebViewInitialized(webView);

                var contentHtml = ConvertToContentHtml(text, showAnswer);
                
                // JavaScript実行前に少し待機
                await Task.Delay(50);
                
                Debug.WriteLine($"元のコンテンツ: {contentHtml}");
                
                // 方法1: Base64エンコードを使用（より安全）
                try
                {
                    var contentBytes = System.Text.Encoding.UTF8.GetBytes(contentHtml);
                    var base64Content = Convert.ToBase64String(contentBytes);
                    
                    Debug.WriteLine($"Base64エンコード実行: updateContentBase64('{base64Content.Substring(0, Math.Min(base64Content.Length, 50))}...');");
                    await webView.EvaluateJavaScriptAsync($"updateContentBase64('{base64Content}');");
                    
                    // JavaScript実行後に確認
                    await Task.Delay(100);
                    var consoleCheck = await webView.EvaluateJavaScriptAsync("document.getElementById('main-content').innerHTML;");
                    Debug.WriteLine($"Base64更新後のコンテンツ確認: {consoleCheck}");
                }
                catch (Exception base64Ex)
                {
                    Debug.WriteLine($"Base64方式エラー: {base64Ex.Message}");
                    
                    // 方法2: 従来のエスケープ処理（フォールバック）
                    try
                    {
                        var escapedContent = contentHtml
                            .Replace("\\", "\\\\")    // バックスラッシュを最初に処理
                            .Replace("'", "\\'")      // シングルクォート
                            .Replace("\"", "\\\"")    // ダブルクォート
                            .Replace("\r\n", "\\n")   // 改行（CRLF）
                            .Replace("\n", "\\n")     // 改行（LF）
                            .Replace("\r", "\\n")     // 改行（CR）
                            .Replace("\t", "\\t");    // タブ
                        
                        Debug.WriteLine($"エスケープ方式実行: updateContent('{escapedContent.Substring(0, Math.Min(escapedContent.Length, 50))}...');");
                        await webView.EvaluateJavaScriptAsync($"updateContent('{escapedContent}');");
                        
                        await Task.Delay(100);
                        var consoleCheck2 = await webView.EvaluateJavaScriptAsync("document.getElementById('main-content').innerHTML;");
                        Debug.WriteLine($"エスケープ更新後のコンテンツ確認: {consoleCheck2}");
                    }
                    catch (Exception escapeEx)
                    {
                        Debug.WriteLine($"エスケープ方式エラー: {escapeEx.Message}");
                        throw;
                    }
                }
                Debug.WriteLine("WebViewコンテンツ更新完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebViewコンテンツ更新エラー: {ex.Message}");
                Debug.WriteLine($"エラー詳細: {ex}");
                
                // フォールバック：HTML全体を再読み込み
                try
                {
                    Debug.WriteLine("フォールバック実行中...");
                    var fullHtml = ConvertMarkdownToHtml(text ?? "");
                    webView.Source = new HtmlWebViewSource { Html = fullHtml };
                    Debug.WriteLine("フォールバック完了");
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"フォールバックエラー: {fallbackEx.Message}");
                }
            }
        }

        private async void AutoSaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (isDirty && !string.IsNullOrEmpty(editCardId))
            {
                await SaveCurrentCard();
            }
        }

        private async Task SaveCurrentCard()
        {
            try
            {
                if (string.IsNullOrEmpty(editCardId)) return;

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

                // カード情報をJSONとして保存
                var cardData = new
                {
                    id = editCardId,
                    type = cardType,
                    front = frontText,
                    back = backText,
                    question = choiceQuestion,
                    explanation = choiceExplanation,
                    choices = choices,
                    selectionRects = selectionRects.Select(r => new
                    {
                        x = r.Left,
                        y = r.Top,
                        width = r.Width,
                        height = r.Height
                    }).ToList()
                };

                string jsonPath = Path.Combine(tempExtractPath, "cards", $"{editCardId}.json");
                string jsonContent = JsonSerializer.Serialize(cardData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await File.WriteAllTextAsync(jsonPath, jsonContent);

                // 変更があった場合のみcards.txtの更新日時を更新
                if (isDirty)
                {
                    string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                    if (File.Exists(cardsFilePath))
                    {
                        var lines = await File.ReadAllLinesAsync(cardsFilePath);
                        var newLines = new List<string>();
                        bool cardUpdated = false;

                        // 1行目は画像番号なのでそのまま保持
                        if (lines.Length > 0)
                        {
                            newLines.Add(lines[0]);
                        }

                        // カード情報の更新
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var parts = lines[i].Split(',');
                            if (parts.Length >= 2 && parts[0] == editCardId)
                            {
                                // 更新日時を現在時刻に更新
                                newLines.Add($"{editCardId},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                cardUpdated = true;
                            }
                            else
                            {
                                newLines.Add(lines[i]);
                            }
                        }

                        // カードが存在しない場合は新規追加
                        if (!cardUpdated)
                        {
                            newLines.Add($"{editCardId},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        }

                        await File.WriteAllLinesAsync(cardsFilePath, newLines);
                    }
                }

                // .ankpls を更新
                if (File.Exists(ankplsFilePath))
                {
                    File.Delete(ankplsFilePath);
                }
                ZipFile.CreateFromDirectory(tempExtractPath, ankplsFilePath);

                isDirty = false;
                Debug.WriteLine("カードを自動保存しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動保存でエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        private async void OnCardSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is CardInfo selectedCard)
            {
                // 現在のカードを保存
                if (isDirty && !string.IsNullOrEmpty(editCardId))
                {
                    await SaveCurrentCard();
                }

                // 新しいカードを読み込む
                editCardId = selectedCard.Id;
                LoadCardData(selectedCard.Id);
                
                // プレビューを強制更新
                await Task.Delay(200); // WebView初期化を待つ
                
                var cardType = CardTypePicker.SelectedItem?.ToString();
                Debug.WriteLine($"カード選択後のプレビュー更新: {cardType}");
                
                switch (cardType)
                {
                    case "基本・穴埋め":
                        Debug.WriteLine("基本カードのプレビューを更新");
                        await UpdateFrontPreviewAsync(FrontTextEditor.Text ?? "");
                        await UpdateBackPreviewAsync(BackTextEditor.Text ?? "");
                        break;
                    case "選択肢":
                        Debug.WriteLine("選択肢カードのプレビューを更新");
                        await UpdateChoiceQuestionPreviewAsync(ChoiceQuestion.Text ?? "");
                        await UpdateChoiceExplanationPreviewAsync(ChoiceQuestionExplanation.Text ?? "");
                        break;
                }
            }
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            // 画面を離れる時に保存
            if (isDirty && !string.IsNullOrEmpty(editCardId))
            {
                await SaveCurrentCard();
            }
            
            // リソース解放
            autoSaveTimer?.Stop();
            autoSaveTimer?.Dispose();
            
            _frontPreviewTimer?.Stop();
            _frontPreviewTimer?.Dispose();
            
            _backPreviewTimer?.Stop();
            _backPreviewTimer?.Dispose();
            
            _choiceQuestionPreviewTimer?.Stop();
            _choiceQuestionPreviewTimer?.Dispose();
            
            _choiceExplanationPreviewTimer?.Stop();
            _choiceExplanationPreviewTimer?.Dispose();
        }

        private void OnEditCardClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is CardInfo card)
            {
                // カード編集画面に遷移
                Navigation.PushAsync(new Add(ankplsFilePath, tempExtractPath, card.Id));
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
                    var cardData = JsonSerializer.Deserialize<CardData>(jsonContent);

                    // カードタイプを設定
                    int typeIndex = CardTypePicker.Items.IndexOf(cardData.type);
                    if (typeIndex >= 0)
                    {
                        CardTypePicker.SelectedIndex = typeIndex;
                    }
                    
                    // レイアウトの表示制御を直接実行
                    BasicCardLayout.IsVisible = cardData.type == "基本・穴埋め";
                    MultipleChoiceLayout.IsVisible = cardData.type == "選択肢";
                    ImageFillLayout.IsVisible = cardData.type == "画像穴埋め";
                    
                    Debug.WriteLine($"レイアウト制御: {cardData.type} - Basic:{BasicCardLayout.IsVisible}, Choice:{MultipleChoiceLayout.IsVisible}, Image:{ImageFillLayout.IsVisible}");

                    // カードの内容を設定
                    if (cardData.type == "選択肢")
                    {
                        ChoiceQuestion.Text = cardData.question ?? "";
                        ChoiceQuestionExplanation.Text = cardData.explanation ?? "";
                        
                        // TextChangedイベントハンドラーを設定
                        ChoiceQuestion.TextChanged -= OnChoiceQuestionTextChanged; // 重複を防ぐため一度削除
                        ChoiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
                        ChoiceQuestionExplanation.TextChanged -= OnChoiceExplanationTextChanged;
                        ChoiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;
                        
                        // プレビューをJavaScript手法で初期化（初期化フラグをリセット）
                        _choicePreviewInitialized = false;
                        _choiceExplanationPreviewInitialized = false;
                        
                        Debug.WriteLine($"選択肢カードのプレビュー初期化: question='{cardData.question}', explanation='{cardData.explanation}'");
                        
                        // 選択肢を設定
                        if (cardData.choices != null)
                        {
                            ChoicesContainer.Children.Clear();
                            foreach (var choice in cardData.choices)
                            {
                                var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
                                var checkBox = new CheckBox { IsChecked = choice.isCorrect };
                                var editor = new Editor
                                {
                                    Text = choice.text ?? "",
                                    HeightRequest = 40,
                                    AutoSize = EditorAutoSizeOption.TextChanges
                                };
                                
                                // TextChangedイベントを追加
                                editor.TextChanged += OnChoiceTextChanged;

                                // ダークモード対応
                                editor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
                                editor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);

                                stack.Children.Add(checkBox);
                                stack.Children.Add(editor);
                                ChoicesContainer.Children.Add(stack);
                            }
                        }
                    }
                    else
                    {
                        FrontTextEditor.Text = cardData.front ?? "";
                        BackTextEditor.Text = cardData.back ?? "";
                        
                        // プレビューをJavaScript手法で初期化（初期化フラグをリセット）
                        _frontPreviewInitialized = false;
                        _backPreviewInitialized = false;
                        
                        Debug.WriteLine($"基本カードのプレビュー初期化: front='{cardData.front}', back='{cardData.back}'");
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

        private void OnCardTypeChanged(object sender, EventArgs e)
        {
            string selectedType = CardTypePicker.SelectedItem as string;

            BasicCardLayout.IsVisible = selectedType == "基本・穴埋め";
            MultipleChoiceLayout.IsVisible = selectedType == "選択肢";
            ImageFillLayout.IsVisible = selectedType == "画像穴埋め";
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            if (imageBitmap != null)
            {
                var rect = new SKRect(0, 0, e.Info.Width, e.Info.Height);
                canvas.DrawBitmap(imageBitmap, rect);
            }

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

        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        var clickedRect = selectionRects.FirstOrDefault(r => r.Contains(point));
                        if (clickedRect != SKRect.Empty)
                        {
                            ShowContextMenu(point, clickedRect);
                        }
                    }
                    else
                    {
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
                            isDirty = true;
                        }
                    }
                    isDragging = false;
                    break;
            }

            CanvasView.InvalidateSurface();
        }

        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                selectionRects.Remove(rect);
                isDirty = true;
                CanvasView.InvalidateSurface();
            }
        }

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

        private async void ChoiceQuestionOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(ChoiceQuestion);
            OnChoiceQuestionTextChanged(ChoiceQuestion, new TextChangedEventArgs("", ChoiceQuestion.Text));
        }

        private async void ChoiceExplanationOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(ChoiceQuestionExplanation);
            OnChoiceExplanationTextChanged(ChoiceQuestionExplanation, new TextChangedEventArgs("", ChoiceQuestionExplanation.Text));
        }

        private void OnAddChoice(object sender, EventArgs e)
        {
            var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
            var checkBox = new CheckBox();
            var editor = new Editor
            {
                Placeholder = "選択肢を入力（改行で区切って複数入力可能）",
                HeightRequest = 40,
                AutoSize = EditorAutoSizeOption.TextChanges
            };
            
            // ダークモード対応
            editor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
            editor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
            
            editor.TextChanged += OnChoiceTextChanged;

            stack.Children.Add(checkBox);
            stack.Children.Add(editor);

            ChoicesContainer.Children.Add(stack);
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private void OnChoiceTextChanged(object sender, TextChangedEventArgs e)
        {
            var editor = sender as Editor;
            if (editor == null) return;

            // 改行が含まれている場合
            if (e.NewTextValue.Contains("\n") || e.NewTextValue.Contains("\r"))
            {
                // 改行で分割し、空の行を除外
                var choices = e.NewTextValue
                    .Replace("\r\n", "\n")  // まず \r\n を \n に統一
                    .Replace("\r", "\n")    // 残りの \r を \n に変換
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)  // \n で分割
                    .Select(c => c.Trim())  // 各行の前後の空白を削除
                    .Where(c => !string.IsNullOrWhiteSpace(c))  // 空の行を除外
                    .ToList();

                if (choices.Count > 0)
                {
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
                        
                        // ダークモード対応
                        newEditor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
                        newEditor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
                        
                        newEditor.TextChanged += OnChoiceTextChanged;

                        stack.Children.Add(checkBox);
                        stack.Children.Add(newEditor);

                        ChoicesContainer.Children.Add(stack);
                    }
                }
            }

            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private async void OnSelectImage(object sender, EventArgs e)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "画像を選択",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                try
                {
                    // 画像を読み込む
                    using (var stream = File.OpenRead(result.FullPath))
                    {
                        imageBitmap = SKBitmap.Decode(stream);
                    }

                    // 画像のサイズを調整
                    if (imageBitmap != null)
                    {
                        // キャンバスのサイズに合わせて画像をリサイズ
                        var info = CanvasView.CanvasSize;
                        var scale = Math.Min(info.Width / imageBitmap.Width, info.Height / imageBitmap.Height);
                        var scaledWidth = imageBitmap.Width * scale;
                        var scaledHeight = imageBitmap.Height * scale;

                        var resizedBitmap = imageBitmap.Resize(
                            new SKImageInfo((int)scaledWidth, (int)scaledHeight),
                            SKFilterQuality.High
                        );

                        imageBitmap.Dispose();
                        imageBitmap = resizedBitmap;

                        // 選択範囲をクリア
                        selectionRects.Clear();
                        isDirty = true;
                        CanvasView.InvalidateSurface();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"画像の読み込みでエラー: {ex.Message}");
                    await DisplayAlert("エラー", "画像の読み込みに失敗しました。", "OK");
                }
            }
        }

        private void OnChoiceQuestionTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateChoiceQuestionPreviewWithDebounce(e.NewTextValue ?? "");
        }

        private void OnChoiceExplanationTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateChoiceExplanationPreviewWithDebounce(e.NewTextValue ?? "");
        }

        /// <summary>
        /// 選択肢問題プレビューを更新（デバウンス付き）
        /// </summary>
        private System.Timers.Timer _choiceQuestionPreviewTimer;
        private void UpdateChoiceQuestionPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _choiceQuestionPreviewTimer?.Stop();
            _choiceQuestionPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _choiceQuestionPreviewTimer = new System.Timers.Timer(500);
            _choiceQuestionPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await UpdateChoiceQuestionPreviewAsync(text);
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
        private System.Timers.Timer _choiceExplanationPreviewTimer;
        private void UpdateChoiceExplanationPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _choiceExplanationPreviewTimer?.Stop();
            _choiceExplanationPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（500ms後に実行）
            _choiceExplanationPreviewTimer = new System.Timers.Timer(500);
            _choiceExplanationPreviewTimer.Elapsed += (s, e) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await UpdateChoiceExplanationPreviewAsync(text);
                });
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                _choiceExplanationPreviewTimer = null;
            };
            _choiceExplanationPreviewTimer.AutoReset = false;
            _choiceExplanationPreviewTimer.Start();
        }

        /// <summary>
        /// 選択肢問題プレビューを更新（JavaScript手法）
        /// </summary>
        private async Task UpdateChoiceQuestionPreviewAsync(string text)
        {
            try
            {
                if (!_choicePreviewInitialized)
                {
                    var baseHtml = CreateBaseHtmlTemplate();
                    ChoicePreviewWebView.Source = new HtmlWebViewSource { Html = baseHtml };
                    _choicePreviewInitialized = true;
                    
                    // WebViewの読み込み完了を待つ
                    var tcs = new TaskCompletionSource<bool>();
                    
                    void OnNavigated(object sender, WebNavigatedEventArgs e)
                    {
                        ChoicePreviewWebView.Navigated -= OnNavigated;
                        tcs.SetResult(true);
                    }
                    
                    ChoicePreviewWebView.Navigated += OnNavigated;
                    
                    // タイムアウト設定
                    Device.StartTimer(TimeSpan.FromSeconds(3), () =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            ChoicePreviewWebView.Navigated -= OnNavigated;
                            tcs.SetResult(false);
                        }
                        return false;
                    });
                    
                    await tcs.Task;
                    
                    // WebViewの完全初期化を待つ
                    await Task.Delay(200);
                    await UpdateWebViewContent(ChoicePreviewWebView, text ?? "", false);
                }
                else
                {
                    // 2回目以降はコンテンツ部分のみ更新
                    await UpdateWebViewContent(ChoicePreviewWebView, text ?? "", false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢問題プレビュー更新エラー: {ex.Message}");
                // フォールバック：従来の方法で更新
                try
                {
                    var fallbackHtml = ConvertMarkdownToHtml(text ?? "");
                    ChoicePreviewWebView.Source = new HtmlWebViewSource { Html = fallbackHtml };
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"フォールバック更新エラー: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// 選択肢解説プレビューを更新（JavaScript手法）
        /// </summary>
        private async Task UpdateChoiceExplanationPreviewAsync(string text)
        {
            try
            {
                if (!_choiceExplanationPreviewInitialized)
                {
                    var baseHtml = CreateBaseHtmlTemplate();
                    ChoiceExplanationPreviewWebView.Source = new HtmlWebViewSource { Html = baseHtml };
                    _choiceExplanationPreviewInitialized = true;
                    
                    // WebViewの読み込み完了を待つ
                    var tcs = new TaskCompletionSource<bool>();
                    
                    void OnNavigated(object sender, WebNavigatedEventArgs e)
                    {
                        ChoiceExplanationPreviewWebView.Navigated -= OnNavigated;
                        tcs.SetResult(true);
                    }
                    
                    ChoiceExplanationPreviewWebView.Navigated += OnNavigated;
                    
                    // タイムアウト設定
                    Device.StartTimer(TimeSpan.FromSeconds(3), () =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            ChoiceExplanationPreviewWebView.Navigated -= OnNavigated;
                            tcs.SetResult(false);
                        }
                        return false;
                    });
                    
                    await tcs.Task;
                    
                    // WebViewの完全初期化を待つ
                    await Task.Delay(200);
                    await UpdateWebViewContent(ChoiceExplanationPreviewWebView, text ?? "", false);
                }
                else
                {
                    // 2回目以降はコンテンツ部分のみ更新
                    await UpdateWebViewContent(ChoiceExplanationPreviewWebView, text ?? "", false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢解説プレビュー更新エラー: {ex.Message}");
                // フォールバック：従来の方法で更新
                try
                {
                    var fallbackHtml = ConvertMarkdownToHtml(text ?? "");
                    ChoiceExplanationPreviewWebView.Source = new HtmlWebViewSource { Html = fallbackHtml };
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"フォールバック更新エラー: {fallbackEx.Message}");
                }
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            PerformSearch(e.NewTextValue);
        }

        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            CardSearchBar.Text = "";
            PerformSearch("");
        }

        private void PerformSearch(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                // 検索テキストが空の場合、全てのカードを表示
                filteredCards = new List<CardInfo>(cards);
            }
            else
            {
                // 大文字小文字を区別しない検索
                string lowerSearchText = searchText.ToLower();
                filteredCards = cards.Where(card =>
                    card.FrontText.ToLower().Contains(lowerSearchText) ||
                    (card.ImageInfo != null && card.ImageInfo.ToLower().Contains(lowerSearchText))
                ).ToList();
            }

            CardsCollectionView.ItemsSource = filteredCards;
            UpdateSearchResult();
        }

        private void UpdateSearchResult()
        {
            if (string.IsNullOrWhiteSpace(CardSearchBar.Text))
            {
                SearchResultLabel.Text = $"全{cards.Count}件";
            }
            else
            {
                SearchResultLabel.Text = $"{filteredCards.Count}/{cards.Count}件";
            }
        }
    }
} 