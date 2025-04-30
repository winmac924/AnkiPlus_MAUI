using Microsoft.Maui.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Collections.ObjectModel;
using System.Diagnostics;
using SkiaSharp.Views.Maui.Controls;
using PdfiumViewer;
using System.Drawing;
using SizeF = System.Drawing.SizeF;
using Microsoft.Maui.Graphics;
using System.Runtime.InteropServices;
using AnkiPlus_MAUI.Drawing;

namespace AnkiPlus_MAUI.Views
{
    public class DrawingCanvas : SKCanvasView, IDisposable
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_CONTROL = 0x11;
        private const float BASE_CANVAS_WIDTH = 600f;  // 800fから600fに縮小
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 3.0f;
        private const float HIGH_DPI = 100f;
        private const float LOW_DPI = 50f;
        private const float SELECTION_MARGIN = 20f;
        private const float SELECTION_MARGIN_SCALE_FACTOR = 1.5f;
        private const float CONTEXT_MENU_WIDTH = 200;
        private const float CONTEXT_MENU_HEIGHT = 150;
        private const float CONTEXT_MENU_ITEM_HEIGHT = 40;
        private const float COLOR_BOX_SIZE = 30;
        private const float COLOR_BOX_MARGIN = 5;
        private const float STROKE_WIDTH_BOX_SIZE = 30;
        private const float STROKE_WIDTH_BOX_MARGIN = 5;
        private DateTime _rightClickStartTime;
        private const int LONG_PRESS_DURATION = 500;
        private bool _isRightDragging = false;
        private SKPoint _dragStartPoint;
        private const int SHAPE_RECOGNITION_DELAY = 300;
        private bool _isShapeRecognitionActive = false;
        private SKPath _previewPath;

        private SKPaint _penPaint;
        private SKPaint _markerPaint;
        private SKPaint _eraserPaint;
        private SKPaint _currentPaint;
        private SKPath _currentPath;
        private readonly ObservableCollection<DrawingStroke> _drawingElements;
        private DrawingStroke _selectedElement;
        private DrawingTool _currentTool;
        private SKPoint _lastPoint;
        private bool _isDrawing;
        private DateTime _lastMoveTime;
        private SKBitmap _backgroundImage;
        private PdfDocument _pdfDocument;
        private int _currentPage;
        private Stream _pdfStream;
        private List<SKBitmap> _pdfPages;
        private SizeF _pageSize;
        private float _totalHeight;
        private float _currentScale = 1.0f;
        private SKMatrix _transformMatrix = SKMatrix.CreateIdentity();
        private SKPoint _lastTouchPoint;
        private bool _isPanning;
        private ScrollView _parentScrollView;
        private List<PageCanvas> _pageCanvases;
        private bool _isMovingElement;
        private PageCanvas _selectedPageCanvas;
        private SKPoint _moveStartPoint;
        private Dictionary<int, SKBitmap> _highQualityPages;
        private HashSet<int> _loadingPages;
        private int _currentVisiblePage;

        // ペンとマーカーの設定を保存するプロパティ
        private SKColor _penColor = SKColors.Black;
        private float _penStrokeWidth = 2.0f;
        private SKColor _markerColor = SKColors.Yellow.WithAlpha(128);
        private float _markerStrokeWidth = 10.0f;

        // メモリ管理用の定数を調整
        private const int VISIBLE_PAGE_BUFFER = 2;  // 1から2に増加
        private const int MAX_CACHED_PAGES = 3;  // 2から3に増加
        private const int CACHE_CLEANUP_THRESHOLD = 4;  // 3から4に増加
        private const float SCALE_THRESHOLD = 1.5f;

        private const int MAX_RETRY_COUNT = 3;
        private const int RETRY_DELAY_MS = 100;

        // 保存用のデータ構造
        public class DrawingData
        {
            public string PdfFilePath { get; set; }
            public List<PageDrawingData> Pages { get; set; } = new List<PageDrawingData>();
            public Dictionary<string, float> PenSettings { get; set; } = new Dictionary<string, float>();
            public Dictionary<string, float> MarkerSettings { get; set; } = new Dictionary<string, float>();
            public double LastScrollY { get; set; }  // スクロール位置を追加
        }

        public class PageDrawingData
        {
            public int PageIndex { get; set; }
            public List<DrawingStrokeData> DrawingElements { get; set; } = new List<DrawingStrokeData>();
        }

        public class DrawingStrokeData
        {
            public List<SKPoint> Points { get; set; }
            [System.Text.Json.Serialization.JsonIgnore]
            public SKColor Color { get; set; }
            public float StrokeWidth { get; set; }
            public SKPaintStyle Style { get; set; }
            public float Transparency { get; set; }
            public DrawingTool Tool { get; set; }
            public bool IsShape { get; set; }
            public ShapeType ShapeType { get; set; }

            // 図形固有の情報
            public SKPoint Center { get; set; }  // 円の中心
            public float Radius { get; set; }    // 円の半径
            public SKPoint StartPoint { get; set; }  // 直線の始点
            public SKPoint EndPoint { get; set; }    // 直線の終点
            public List<SKPoint> Vertices { get; set; }  // 三角形や四角形の頂点

            // 色情報をJSONにシリアライズするためのプロパティ
            [System.Text.Json.Serialization.JsonPropertyName("Color")]
            public uint ColorValue
            {
                get => BitConverter.ToUInt32(new byte[] { Color.Alpha, Color.Red, Color.Green, Color.Blue }, 0);
                set
                {
                    var bytes = BitConverter.GetBytes(value);
                    Color = new SKColor(bytes[1], bytes[2], bytes[3], bytes[0]);
                }
            }
        }

        public class PageCanvas
        {
            public SKBitmap PageBitmap { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public float Y { get; set; }
            public ObservableCollection<DrawingStroke> DrawingElements { get; set; } = new ObservableCollection<DrawingStroke>();
            public SKPath CurrentPath { get; set; }
            public bool IsDrawing { get; set; }
            public SKPoint LastPoint { get; set; }
            public bool IsHighQuality { get; set; }
            public int PageIndex { get; set; }
            public bool NeedsUpdate { get; set; }

            public void Dispose()
            {
                PageBitmap?.Dispose();
                PageBitmap = null;
                CurrentPath?.Dispose();
                CurrentPath = null;
                foreach (var element in DrawingElements)
                {
                    element.Dispose();
                }
                DrawingElements.Clear();
            }
        }

        private string _currentPdfPath;
        private string _tempDirectory;
        private const string PAGE_CACHE_DIR = "PageCache";
        private const string DRAWING_DATA_FILE = "drawing_data.json";
        private DateTime _lastAutoSaveTime = DateTime.MinValue;
        private const int AUTO_SAVE_INTERVAL = 30000; // 30秒ごとに自動保存

        // 元に戻す/やり直し用のスタック
        private Stack<(int PageIndex, DrawingStroke Element)> _undoStack = new Stack<(int PageIndex, DrawingStroke Element)>();
        private Stack<(int PageIndex, DrawingStroke Element)> _redoStack = new Stack<(int PageIndex, DrawingStroke Element)>();

        private bool _lastClickWasRight = false;
        private DateTime _lastRightClickTime = DateTime.MinValue;
        private const double DOUBLE_CLICK_TIME = 800;

        private SKPoint _lastRightClickPoint;
        private bool _isShowingContextMenu;
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

        private readonly Dictionary<string, float> _strokeWidths = new Dictionary<string, float>
        {
            { "極細", 0.5f },
            { "細", 1.0f },
            { "中", 2.0f },
            { "太", 4.0f },
            { "極太", 8.0f }
        };

        private readonly Dictionary<string, float> _markerWidths = new Dictionary<string, float>
        {
            { "細", 10.0f },
            { "中", 20.0f },
            { "太", 30.0f },
            { "極太", 50.0f },
            { "特大", 80.0f }
        };

        private readonly Dictionary<string, float> _transparencies = new Dictionary<string, float>
        {
            { "不透明", 1.0f },
            { "半透明", 0.5f },
            { "透明", 0.2f }
        };

        public enum ShapeType
        {
            None,
            Line,
            Rectangle,
            Circle,
            Triangle
        }

        public class DrawingStroke : IDisposable
        {
            public SKPath DrawingPath { get; private set; }
            public SKPaint DrawingPaint { get; private set; }
            public bool IsShape { get; set; }
            public ShapeType ShapeType { get; set; }
            public bool IsMoved { get; private set; }
            public SKPoint StartPoint { get; set; }
            public SKPoint EndPoint { get; set; }
            private SKPath _originalPath;
            private SKPath _movedPath;

            public DrawingStroke(SKPath path, SKPaint paint)
            {
                DrawingPath = path;
                DrawingPaint = paint;
                IsShape = false;
                ShapeType = ShapeType.None;
                IsMoved = false;
                StartPoint = SKPoint.Empty;
                EndPoint = SKPoint.Empty;
                _originalPath = new SKPath(path);
                _movedPath = null;
            }

            public void UpdatePath(SKPath newPath)
            {
                DrawingPath?.Dispose();
                DrawingPath = newPath;
                IsMoved = true;
                _movedPath = new SKPath(newPath);
            }

            public void RestoreOriginalPosition()
            {
                if (IsMoved && _originalPath != null)
                {
                    DrawingPath?.Dispose();
                    DrawingPath = new SKPath(_originalPath);
                    IsMoved = false;
                    _movedPath = null;
                }
            }

            public void RestoreMovedPosition()
            {
                if (IsMoved && _movedPath != null)
                {
                    DrawingPath?.Dispose();
                    DrawingPath = new SKPath(_movedPath);
                }
            }

            public void Dispose()
            {
                DrawingPath?.Dispose();
                DrawingPaint?.Dispose();
                _originalPath?.Dispose();
                _movedPath?.Dispose();
            }
        }

        private ShapeType RecognizeShape(SKPath path)
        {
            if (path == null) return ShapeType.None;

            path.GetBounds(out var bounds);
            var width = bounds.Width;
            var height = bounds.Height;
            var points = GetPointsFromPath(path);

            if (points.Count < 2) return ShapeType.None;

            // 直線の判定
            if (IsLine(points))
            {
                return ShapeType.Line;
            }

            // 円の判定
            if (IsCircle(points))
            {
                return ShapeType.Circle;
            }

            // 三角形の判定
            if (IsTriangle(points))
            {
                return ShapeType.Triangle;
            }

            // 四角形の判定
            if (IsRectangle(points))
            {
                return ShapeType.Rectangle;
            }

            return ShapeType.None;
        }

        private bool IsLine(List<SKPoint> points)
        {
            if (points.Count < 2) return false;

            var firstPoint = points[0];
            var lastPoint = points[points.Count - 1];
            var lineLength = Distance(firstPoint, lastPoint);

            // 直線の長さが最小サイズ未満の場合は、直線と判定しない
            if (lineLength < 5.0f) return false;

            // 各点が直線からどれだけ離れているかをチェック
            float maxDeviation = 0;
            foreach (var point in points)
            {
                var deviation = DistanceToLine(point, firstPoint, lastPoint);
                maxDeviation = Math.Max(maxDeviation, deviation);
            }

            // 最大偏差が線の長さの20%以下なら直線と判定（10%から20%に緩和）
            return maxDeviation < lineLength * 0.2f;
        }

        private bool IsCircle(List<SKPoint> points)
        {
            if (points.Count < 4) return false;

            var bounds = new SKRect();
            foreach (var point in points)
            {
                var pointRect = new SKRect(point.X, point.Y, point.X, point.Y);
                bounds.Union(pointRect);
            }

            var center = new SKPoint(bounds.MidX, bounds.MidY);
            var radius = Math.Max(bounds.Width, bounds.Height) / 2;

            // 各点が円周からどれだけ離れているかをチェック
            float maxDeviation = 0;
            foreach (var point in points)
            {
                var distance = Distance(point, center);
                var deviation = Math.Abs(distance - radius);
                maxDeviation = Math.Max(maxDeviation, deviation);
            }

            // 最大偏差が半径の30%以下なら円と判定
            return maxDeviation < radius * 0.3f;
        }

        private bool IsTriangle(List<SKPoint> points)
        {
            if (points.Count < 3) return false;

            // 3つの頂点を探す
            var vertices = FindVertices(points, 3);
            if (vertices.Count != 3) return false;

            // 各辺を直線として判定
            int validEdges = 0;
            for (int i = 0; i < 3; i++)
            {
                var start = vertices[i];
                var end = vertices[(i + 1) % 3];
                var edgePoints = points.Where(p => IsPointOnLineSegment(p, start, end)).ToList();

                if (edgePoints.Count < 2) continue;

                // 辺の長さを計算
                var edgeLength = Distance(start, end);
                // 各点が辺からどれだけ離れているかをチェック
                float maxDeviation = 0;
                foreach (var point in edgePoints)
                {
                    var deviation = DistanceToLine(point, start, end);
                    maxDeviation = Math.Max(maxDeviation, deviation);
                }

                // 最大偏差が辺の長さの20%以下なら有効な辺と判定
                if (maxDeviation <= edgeLength * 0.2f)
                {
                    validEdges++;
                }
            }

            // 3辺のうち2辺以上が有効なら三角形と判定
            return validEdges >= 2;
        }

        private bool IsRectangle(List<SKPoint> points)
        {
            if (points.Count < 4) return false;

            // 4つの頂点を探す
            var vertices = FindVertices(points, 4);
            if (vertices.Count != 4) return false;

            // 各辺を直線として判定
            int validEdges = 0;
            for (int i = 0; i < 4; i++)
            {
                var start = vertices[i];
                var end = vertices[(i + 1) % 4];
                var edgePoints = points.Where(p => IsPointOnLineSegment(p, start, end)).ToList();

                if (edgePoints.Count < 2) continue;

                // 辺の長さを計算
                var edgeLength = Distance(start, end);
                // 各点が辺からどれだけ離れているかをチェック
                float maxDeviation = 0;
                foreach (var point in edgePoints)
                {
                    var deviation = DistanceToLine(point, start, end);
                    maxDeviation = Math.Max(maxDeviation, deviation);
                }

                // 最大偏差が辺の長さの20%以下なら有効な辺と判定
                if (maxDeviation <= edgeLength * 0.2f)
                {
                    validEdges++;
                }
            }

            // 4辺のうち3辺以上が有効なら四角形と判定
            return validEdges >= 3;
        }

        private bool IsPointOnLineSegment(SKPoint point, SKPoint start, SKPoint end)
        {
            // 点が線分のバウンディングボックス内にあるかチェック
            var minX = Math.Min(start.X, end.X);
            var maxX = Math.Max(start.X, end.X);
            var minY = Math.Min(start.Y, end.Y);
            var maxY = Math.Max(start.Y, end.Y);

            if (point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY)
            {
                return false;
            }

            // 点から線分までの距離を計算
            var distance = DistanceToLine(point, start, end);
            var lineLength = Distance(start, end);

            // 距離が線分の長さの30%以下なら線分上にあるとみなす
            return distance <= lineLength * 0.3f;
        }

        private bool IsStar(List<SKPoint> points)
        {
            if (points.Count < 5) return false;

            // 5つの頂点を探す
            var vertices = FindVertices(points, 5);
            if (vertices.Count != 5) return false;

            // 星の中心を計算
            var center = new SKPoint(
                vertices.Average(v => v.X),
                vertices.Average(v => v.Y)
            );

            // 各点が星の辺からどれだけ離れているかをチェック
            float maxDeviation = 0;
            foreach (var point in points)
            {
                var minDistance = float.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    var distance = DistanceToLine(point, vertices[i], center);
                    minDistance = Math.Min(minDistance, distance);
                }
                maxDeviation = Math.Max(maxDeviation, minDistance);
            }

            // 最大偏差が星の辺の長さの平均の25%以下なら星と判定（15%から25%に緩和）
            var avgSideLength = vertices.Average(v => Distance(v, center));
            return maxDeviation < avgSideLength * 0.25f;
        }

        private List<SKPoint> FindVertices(List<SKPoint> points, int count)
        {
            var vertices = new List<SKPoint>();
            var center = new SKPoint(
                points.Average(p => p.X),
                points.Average(p => p.Y)
            );

            // 中心からの角度でソート
            var sortedPoints = points.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToList();

            // 頂点を選択
            for (int i = 0; i < count; i++)
            {
                var index = i * sortedPoints.Count / count;
                vertices.Add(sortedPoints[index]);
            }

            return vertices;
        }

        private float Distance(SKPoint p1, SKPoint p2)
        {
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private float DistanceToLine(SKPoint point, SKPoint lineStart, SKPoint lineEnd)
        {
            var lineLength = Distance(lineStart, lineEnd);
            if (lineLength == 0) return Distance(point, lineStart);

            var t = ((point.X - lineStart.X) * (lineEnd.X - lineStart.X) +
                    (point.Y - lineStart.Y) * (lineEnd.Y - lineStart.Y)) / (lineLength * lineLength);

            t = Math.Max(0, Math.Min(1, t));

            var projection = new SKPoint(
                lineStart.X + t * (lineEnd.X - lineStart.X),
                lineStart.Y + t * (lineEnd.Y - lineStart.Y)
            );

            return Distance(point, projection);
        }

        private SKPath CreateCorrectedPath(SKPath originalPath, ShapeType shapeType)
        {
            var bounds = new SKRect();
            originalPath.GetBounds(out bounds);

            var center = new SKPoint(bounds.MidX, bounds.MidY);
            var width = bounds.Width;
            var height = bounds.Height;
            var size = Math.Max(width, height);

            // 最小サイズの制限を緩和
            var minSize = 5.0f; // 最小サイズを5ピクセルに設定
            if (size < minSize)
            {
                size = minSize;
                width = Math.Max(width, minSize);
                height = Math.Max(height, minSize);
            }

            var correctedPath = new SKPath();

            switch (shapeType)
            {
                case ShapeType.Line:
                    // 開始点と終了点を直接結ぶ
                    var points = GetPointsFromPath(originalPath);
                    if (points.Count >= 2)
                    {
                        var startPoint = points[0];
                        var endPoint = points[points.Count - 1];
                        
                        // 直線の長さが最小サイズ未満の場合は、最小サイズに拡大
                        var lineLength = Distance(startPoint, endPoint);
                        if (lineLength < minSize)
                        {
                            var direction = new SKPoint(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                            var normalizedDirection = new SKPoint(
                                direction.X / lineLength,
                                direction.Y / lineLength
                            );
                            endPoint = new SKPoint(
                                startPoint.X + normalizedDirection.X * minSize,
                                startPoint.Y + normalizedDirection.Y * minSize
                            );
                        }
                        
                        correctedPath.MoveTo(startPoint);
                        correctedPath.LineTo(endPoint);
                    }
                    break;

                case ShapeType.Circle:
                    // 円の中心と半径を計算
                    var radius = size / 2;
                    correctedPath.AddCircle(center.X, center.Y, radius);
                    break;

                case ShapeType.Triangle:
                    // 三角形の頂点を計算
                    var trianglePoints = new[]
                    {
                        new SKPoint(center.X, bounds.Top),
                        new SKPoint(bounds.Right, bounds.Bottom),
                        new SKPoint(bounds.Left, bounds.Bottom)
                    };
                    correctedPath.MoveTo(trianglePoints[0]);
                    correctedPath.LineTo(trianglePoints[1]);
                    correctedPath.LineTo(trianglePoints[2]);
                    correctedPath.Close();
                    break;

                case ShapeType.Rectangle:
                    // 四角形の頂点を計算
                    var rect = new SKRect(
                        center.X - width / 2,
                        center.Y - height / 2,
                        center.X + width / 2,
                        center.Y + height / 2
                    );
                    correctedPath.AddRect(rect);
                    break;
            }

            return correctedPath;
        }

        private async Task ShowColorPicker()
        {
            var result = await Application.Current.MainPage.DisplayActionSheet("色を選択", "キャンセル", null, _colors.Keys.ToArray());
            if (result != "キャンセル" && result != null && _colors.TryGetValue(result, out var color))
            {
                _currentPaint.Color = color;
                InvalidateSurface();
            }
        }

        public ScrollView ParentScrollView
        {
            get => _parentScrollView;
            set
            {
                if (_parentScrollView != null)
                {
                    _parentScrollView.Scrolled -= OnScrollViewScrolled;
                }

                _parentScrollView = value;

                if (_parentScrollView != null)
                {
                    _parentScrollView.Scrolled += OnScrollViewScrolled;
                    UpdateScrollViewHeight();

                    // ScrollViewが設定されたら、現在の表示範囲のページを高画質で読み込む
                    if (_pdfDocument != null)
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await UpdateVisiblePagesAsync(_parentScrollView.ScrollY);
                        });
                    }
                }
            }
        }

        private double _lastScrollY = 0;

        private async void OnScrollViewScrolled(object sender, ScrolledEventArgs e)
        {
            Debug.WriteLine($"ScrollView scrolled to: {e.ScrollY}");
            
            // スクロール位置が大きく変わった場合のみ更新
            if (Math.Abs(e.ScrollY - _lastScrollY) > 100)
            {
                _lastScrollY = e.ScrollY;
                await UpdateVisiblePagesAsync(e.ScrollY);
            }
        }

        private void UpdateScrollViewHeight()
        {
            if (_parentScrollView != null)
            {
                try
                {
                    if (_parentScrollView.Content != null)
                    {
                        _parentScrollView.Content.HeightRequest = _totalHeight;
                        Debug.WriteLine($"Setting ScrollView content height to: {_totalHeight}");
                    }
                    else
                    {
                        Debug.WriteLine("ScrollView.Content is null");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating scroll view height: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("ParentScrollView is null");
            }
        }

        public DrawingCanvas()
        {
            _drawingElements = new ObservableCollection<DrawingStroke>();
            _penPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Black,
                StrokeWidth = 2.0f,
                IsAntialias = true
            };
            _markerPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Yellow.WithAlpha(128),
                StrokeWidth = 10.0f,
                IsAntialias = true,
                BlendMode = SKBlendMode.SrcOver
            };
            _eraserPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Transparent,
                StrokeWidth = 20.0f,
                IsAntialias = true,
                BlendMode = SKBlendMode.Clear
            };
            _currentPaint = _penPaint;
            EnableTouchEvents = true;
            IgnorePixelScaling = true;
            _pdfPages = new List<SKBitmap>();
            _pageCanvases = new List<PageCanvas>();
            _totalHeight = 1000;
            _highQualityPages = new Dictionary<int, SKBitmap>();
            _loadingPages = new HashSet<int>();

            // ピンチジェスチャーを追加
            var pinchGesture = new PinchGestureRecognizer();
            pinchGesture.PinchUpdated += OnPinchUpdated;
            GestureRecognizers.Add(pinchGesture);
        }

        private void OnPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (_parentScrollView == null) return;

            switch (e.Status)
            {
                case GestureStatus.Started:
                    // ピンチ開始時の処理
                    Debug.WriteLine($"Pinch Started - Scale: {e.Scale}");
                    break;

                case GestureStatus.Running:
                    // ピンチ中の処理
                    var newScale = _currentScale * (float)e.Scale;
                    if (newScale >= MIN_SCALE && newScale <= MAX_SCALE)
                    {
                        Debug.WriteLine($"Pinch Running - Old Scale: {_currentScale}, New Scale: {newScale}, Scale: {e.Scale}");

                        // ズーム前の状態を保存
                        var oldScale = _currentScale;
                        var oldHeight = _totalHeight;
                        var viewportHeight = _parentScrollView.Height;
                        var oldScrollY = _parentScrollView.ScrollY;
                        var pinchCenterY = (float)(e.ScaleOrigin.Y * Height);

                        Debug.WriteLine($"Pinch State - Old Height: {oldHeight}, Viewport Height: {viewportHeight}, Old ScrollY: {oldScrollY}, Pinch Center Y: {pinchCenterY}");

                        // ピンチ中心のキャンバス上の相対位置を計算
                        var pointY = oldScrollY + pinchCenterY;
                        var relativeY = pointY / oldHeight;

                        Debug.WriteLine($"Pinch Center Position - PointY: {pointY}, RelativeY: {relativeY}");

                        // スケールを更新
                        _currentScale = newScale;

                        // ページキャンバスの更新を遅延させる
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await Task.Delay(50); // 50msの遅延を追加
                            UpdatePageCanvases();

                            // 新しいスクロール位置を計算
                            var newPointY = _totalHeight * relativeY;
                            var newScrollY = newPointY - (pinchCenterY * (_totalHeight / oldHeight));

                            Debug.WriteLine($"New Scroll Position - NewPointY: {newPointY}, NewScrollY: {newScrollY}");

                            // スクロール位置を更新
                            await _parentScrollView.ScrollToAsync(0, Math.Max(0, Math.Min(newScrollY, _totalHeight - viewportHeight)), false);
                            InvalidateSurface();
                        });
                    }
                    break;

                case GestureStatus.Completed:
                    // ピンチ終了時の処理
                    Debug.WriteLine($"Pinch Completed - Final Scale: {_currentScale}");
                    break;
            }
        }

        private bool IsPointNearPath(SKPath path, SKPoint point, float margin)
        {
            // パスのバウンディングボックスを取得
            path.GetBounds(out var bounds);

            // ズームレベルに応じてマージンを調整
            var scaledMargin = margin / (_currentScale * SELECTION_MARGIN_SCALE_FACTOR);

            // マージンを含めた拡大バウンディングボックス
            var expandedBounds = new SKRect(
                bounds.Left - scaledMargin,
                bounds.Top - scaledMargin,
                bounds.Right + scaledMargin,
                bounds.Bottom + scaledMargin
            );

            // まず拡大バウンディングボックスでの判定
            if (!expandedBounds.Contains(point))
            {
                return false;
            }

            // パスに沿って点との距離を計算
            using (var measure = new SKPathMeasure(path))
            {
                float length = measure.Length;
                float step = 1.0f; // 1ピクセルごとにチェック

                for (float distance = 0; distance < length; distance += step)
                {
                    measure.GetPosition(distance, out var pathPoint);
                    float dx = point.X - pathPoint.X;
                    float dy = point.Y - pathPoint.Y;
                    float pointDistance = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (pointDistance <= scaledMargin)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private SKPoint _lastTouchPoint1;
        private SKPoint _lastTouchPoint2;
        private float _lastDistance;

        protected override void OnTouch(SKTouchEventArgs e)
        {
            bool isCtrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isRightClick = e.MouseButton == SKMouseButton.Right;

            Debug.WriteLine($"Touch Event - Action: {e.ActionType}, Location: ({e.Location.X}, {e.Location.Y}), CtrlPressed: {isCtrlPressed}");

            // 2本指のタッチイベントを処理
            if (e.ActionType == SKTouchAction.Pressed || e.ActionType == SKTouchAction.Moved)
            {
                if (e.DeviceType == SKTouchDeviceType.Touch)
                {
                    if (e.Id == 0)
                    {
                        _lastTouchPoint1 = e.Location;
                    }
                    else if (e.Id == 1)
                    {
                        _lastTouchPoint2 = e.Location;
                    }

                    // 両方のタッチポイントが設定されている場合
                    if (_lastTouchPoint1 != SKPoint.Empty && _lastTouchPoint2 != SKPoint.Empty)
                    {
                        var currentDistance = (float)Math.Sqrt(
                            Math.Pow(_lastTouchPoint2.X - _lastTouchPoint1.X, 2) +
                            Math.Pow(_lastTouchPoint2.Y - _lastTouchPoint1.Y, 2));

                        if (_lastDistance > 0)
                        {
                            var zoomFactor = currentDistance / _lastDistance;
                            var newScale = _currentScale * zoomFactor;
                            if (newScale >= MIN_SCALE && newScale <= MAX_SCALE)
                            {
                                Debug.WriteLine($"Pinch Zoom - Old Scale: {_currentScale}, New Scale: {newScale}, Zoom Factor: {zoomFactor}");

                                // ズーム前の状態を保存
                                var oldScale = _currentScale;
                                var oldHeight = _totalHeight;
                                var viewportHeight = _parentScrollView.Height;
                                var oldScrollY = _parentScrollView.ScrollY;
                                var pinchCenterY = (_lastTouchPoint1.Y + _lastTouchPoint2.Y) / 2;

                                Debug.WriteLine($"Pinch State - Old Height: {oldHeight}, Viewport Height: {viewportHeight}, Old ScrollY: {oldScrollY}, Pinch Center Y: {pinchCenterY}");

                                // ピンチ中心のキャンバス上の相対位置を計算
                                var pointY = oldScrollY + pinchCenterY;
                                var relativeY = pointY / oldHeight;

                                Debug.WriteLine($"Pinch Center Position - PointY: {pointY}, RelativeY: {relativeY}");

                                // スケールを更新
                                _currentScale = newScale;

                                // ページキャンバスの更新を遅延させる
                                MainThread.BeginInvokeOnMainThread(async () =>
                                {
                                    await Task.Delay(50); // 50msの遅延を追加
                                    UpdatePageCanvases();

                                    // 新しいスクロール位置を計算
                                    var newPointY = _totalHeight * relativeY;
                                    var newScrollY = newPointY - (pinchCenterY * (_totalHeight / oldHeight));

                                    Debug.WriteLine($"New Scroll Position - NewPointY: {newPointY}, NewScrollY: {newScrollY}");

                                    // スクロール位置を更新
                                    await _parentScrollView.ScrollToAsync(0, Math.Max(0, Math.Min(newScrollY, _totalHeight - viewportHeight)), false);
                                    InvalidateSurface();
                                });
                            }
                        }
                        _lastDistance = currentDistance;
                    }
                }
            }
            else if (e.ActionType == SKTouchAction.Released)
            {
                if (e.Id == 0)
                {
                    _lastTouchPoint1 = SKPoint.Empty;
                }
                else if (e.Id == 1)
                {
                    _lastTouchPoint2 = SKPoint.Empty;
                }
                _lastDistance = 0;
            }

            // Ctrlキーが押されていない場合、ホイールイベントは処理しない
            if (e.ActionType == SKTouchAction.WheelChanged && !isCtrlPressed)
            {
                e.Handled = false;
                return;
            }

            // タッチ位置をキャンバス座標系に変換
            var info = CanvasSize;
            var scale = Math.Min(info.Width / BASE_CANVAS_WIDTH, info.Height / (_totalHeight / _currentScale));
            var centerX = (info.Width - (BASE_CANVAS_WIDTH * scale)) / 2;
            var centerY = (info.Height - (_totalHeight * scale / _currentScale)) / 2;
            var canvasX = (e.Location.X - centerX) / scale;
            var canvasY = (e.Location.Y - centerY) / scale;
            var transformedPoint = new SKPoint(canvasX, canvasY);

            Debug.WriteLine($"Touch Transform - Scale: {scale}, CenterX: {centerX}, CenterY: {centerY}");
            Debug.WriteLine($"Transformed Point - X: {transformedPoint.X}, Y: {transformedPoint.Y}");

            // タッチされたページを特定
            var pageCanvas = _pageCanvases.FirstOrDefault(p =>
                canvasY >= p.Y / _currentScale &&
                canvasY <= (p.Y + p.Height) / _currentScale);

            if (pageCanvas == null)
            {
                Debug.WriteLine("No page canvas found for touch position");
                e.Handled = false;
                return;
            }

            Debug.WriteLine($"Found Page Canvas - PageIndex: {pageCanvas.PageIndex}, Y: {pageCanvas.Y}, Height: {pageCanvas.Height}");

            // ページ内の相対座標を計算
            var pageRelativePoint = new SKPoint(
                transformedPoint.X,
                transformedPoint.Y - pageCanvas.Y / _currentScale
            );

            Debug.WriteLine($"Page Relative Point - X: {pageRelativePoint.X}, Y: {pageRelativePoint.Y}");

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (isRightClick)
                    {
                        _rightClickStartTime = DateTime.Now;
                        _dragStartPoint = pageRelativePoint;
                        _isRightDragging = true;

                        // 右クリックで要素を選択
                        _selectedElement = null;
                        _selectedPageCanvas = null;
                        foreach (var canvas in _pageCanvases)
                        {
                            foreach (var element in canvas.DrawingElements)
                            {
                                if (IsPointNearPath(element.DrawingPath, pageRelativePoint, SELECTION_MARGIN))
                                {
                                    _selectedElement = element;
                                    _selectedPageCanvas = canvas;
                                    break;
                                }
                            }
                            if (_selectedElement != null) break;
                        }

                        // 要素が見つからなかった場合のみメニューを表示
                        if (_selectedElement == null)
                        {
                            _lastRightClickPoint = e.Location;
                            _isShowingContextMenu = true;
                            InvalidateSurface();
                        }
                        e.Handled = true;
                    }
                    else if (_isShowingContextMenu)
                    {
                        // 太さ選択ボタンのチェック
                        for (int i = 0; i < _strokeWidths.Count; i++)
                        {
                            if (IsPointInStrokeWidthBox(e.Location, i))
                            {
                                var width = _strokeWidths.Values.ElementAt(i);
                                _currentPaint.StrokeWidth = width;
                                _isShowingContextMenu = false;
                                InvalidateSurface();
                                e.Handled = true;
                                return;
                            }
                        }

                        // 透明度選択ボタンのチェック（マーカーツール時のみ）
                        if (_currentTool == DrawingTool.Marker)
                        {
                            for (int i = 0; i < _transparencies.Count; i++)
                            {
                                if (IsPointInTransparencyBox(e.Location, i))
                                {
                                    var transparency = _transparencies.Values.ElementAt(i);
                                    _currentPaint.Color = _currentPaint.Color.WithAlpha((byte)(transparency * 255));
                                    _isShowingContextMenu = false;
                                    InvalidateSurface();
                                    e.Handled = true;
                                    return;
                                }
                            }
                        }

                        // 色選択ボタンのチェック
                        for (int i = 0; i < _colors.Count; i++)
                        {
                            if (IsPointInColorBox(e.Location, i))
                            {
                                var color = _colors.Values.ElementAt(i);
                                if (_currentTool == DrawingTool.Marker)
                                {
                                    // マーカーの場合は現在の透明度を保持
                                    _currentPaint.Color = color.WithAlpha(_currentPaint.Color.Alpha);
                                }
                                else
                                {
                                    _currentPaint.Color = color;
                                }
                                _isShowingContextMenu = false;
                                InvalidateSurface();
                                e.Handled = true;
                                return;
                            }
                        }

                        // メニュー外をクリックした場合はメニューを閉じる
                        _isShowingContextMenu = false;
                        InvalidateSurface();
                        e.Handled = true;
                    }
                    else if (_currentTool == DrawingTool.Eraser)
                    {
                        // 消しゴムツールの処理
                        pageCanvas.IsDrawing = true;
                        pageCanvas.CurrentPath = new SKPath();
                        pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
                        pageCanvas.LastPoint = pageRelativePoint;
                        e.Handled = true;
                    }
                    else
                    {
                        // 通常の描画処理
                        pageCanvas.IsDrawing = true;
                        pageCanvas.CurrentPath = new SKPath();
                        pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
                        pageCanvas.LastPoint = pageRelativePoint;
                        _lastMoveTime = DateTime.Now;
                        _isShapeRecognitionActive = false;
                        _previewPath = null;
                        // 高画質ページを維持
                        if (!_highQualityPages.ContainsKey(pageCanvas.PageIndex))
                        {
                            _ = LoadPdfPageAsync(pageCanvas.PageIndex, HIGH_DPI);
                        }
                        e.Handled = true;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (_isRightDragging && _selectedElement != null && _selectedPageCanvas != null)
                    {
                        // 要素の移動
                        var deltaX = pageRelativePoint.X - _dragStartPoint.X;
                        var deltaY = pageRelativePoint.Y - _dragStartPoint.Y;

                        var newPath = new SKPath();
                        _selectedElement.DrawingPath.GetBounds(out var bounds);
                        newPath.AddPath(_selectedElement.DrawingPath);
                        newPath.Transform(SKMatrix.CreateTranslation(deltaX, deltaY));

                        _selectedElement.UpdatePath(newPath);
                        _dragStartPoint = pageRelativePoint;
                        InvalidateSurface();
                        e.Handled = true;
                    }
                    else if (pageCanvas.IsDrawing)
                    {
                        // 通常の描画処理
                        pageCanvas.CurrentPath.LineTo(pageRelativePoint);
                        pageCanvas.LastPoint = pageRelativePoint;

                        // 消しゴムツールの場合は即座に描画を更新
                        if (_currentTool == DrawingTool.Eraser)
                        {
                            // 消しゴムのパスと交差する要素を削除
                            var elementsToRemove = pageCanvas.DrawingElements
                                .Where(element => PathsIntersect(pageCanvas.CurrentPath, element.DrawingPath))
                                .ToList();

                            foreach (var element in elementsToRemove)
                            {
                                pageCanvas.DrawingElements.Remove(element);
                                _undoStack.Push((pageCanvas.PageIndex, element));
                            }

                            // 新しいパスを開始
                            pageCanvas.CurrentPath = new SKPath();
                            pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
                            InvalidateSurface();
                        }
                        else
                        {
                            // 一定時間動きが止まったら図形認識を開始
                            var now = DateTime.Now;
                            if ((now - _lastMoveTime).TotalMilliseconds > SHAPE_RECOGNITION_DELAY)
                            {
                                if (!_isShapeRecognitionActive)
                                {
                                    _isShapeRecognitionActive = true;
                                    var shapeType = RecognizeShape(pageCanvas.CurrentPath);
                                    if (shapeType != ShapeType.None)
                                    {
                                        _previewPath = CreateCorrectedPath(pageCanvas.CurrentPath, shapeType);
                                    }
                                }
                            }
                            else
                            {
                                _isShapeRecognitionActive = false;
                                _previewPath = null;
                            }
                        }

                        _lastMoveTime = DateTime.Now;
                        InvalidateSurface();
                        e.Handled = true;
                    }
                    break;

                case SKTouchAction.Released:
                    if (isRightClick)
                    {
                        var pressDuration = (DateTime.Now - _rightClickStartTime).TotalMilliseconds;
                        if (pressDuration < LONG_PRESS_DURATION)
                        {
                            // 短い右クリック（通常の右クリック）
                            var now = DateTime.Now;
                            if (_lastClickWasRight && (now - _lastRightClickTime).TotalMilliseconds < DOUBLE_CLICK_TIME)
                            {
                                // 右ダブルクリックの処理
                                if (_selectedElement != null && _selectedPageCanvas != null)
                                {
                                    _selectedPageCanvas.DrawingElements.Remove(_selectedElement);
                                    _undoStack.Push((_selectedPageCanvas.PageIndex, _selectedElement));
                                    _selectedElement = null;
                                    _selectedPageCanvas = null;
                                    InvalidateSurface();
                                }
                            }
                            _lastClickWasRight = true;
                            _lastRightClickTime = now;
                        }
                        _isRightDragging = false;
                        e.Handled = true;
                    }
                    else if (pageCanvas.IsDrawing)
                    {
                        if (_currentTool == DrawingTool.Eraser)
                        {
                            pageCanvas.IsDrawing = false;
                            pageCanvas.CurrentPath = null;
                            InvalidateSurface();
                        }
                        else
                        {
                            if (_currentTool != DrawingTool.Eraser)
                            {
                                var finalPath = _previewPath ?? pageCanvas.CurrentPath;
                                var element = new DrawingStroke(finalPath, _currentPaint.Clone());
                                if (_previewPath != null)
                                {
                                    var shapeType = RecognizeShape(pageCanvas.CurrentPath);
                                    element.IsShape = true;
                                    element.ShapeType = shapeType;
                                }
                                // StartPointとEndPointを設定
                                element.StartPoint = pageCanvas.LastPoint;
                                element.EndPoint = pageRelativePoint;
                                pageCanvas.DrawingElements.Add(element);
                                _undoStack.Push((pageCanvas.PageIndex, element));
                                _redoStack.Clear();
                                // 描画データを保存
                                _ = SaveDrawingDataAsync();
                            }
                            pageCanvas.IsDrawing = false;
                            _isShapeRecognitionActive = false;
                            _previewPath = null;
                            InvalidateSurface();
                            e.Handled = true;
                        }
                    }

                    // タッチ操作後に自動保存をチェック
                    _ = AutoSaveIfNeeded();
                    break;

                case SKTouchAction.WheelChanged:
                    if (isCtrlPressed && _parentScrollView != null)
                    {
                        var zoomFactor = e.WheelDelta > 0 ? 1.1f : 0.9f;
                        var newScale = _currentScale * zoomFactor;
                        if (newScale >= MIN_SCALE && newScale <= MAX_SCALE)
                        {
                            Debug.WriteLine($"Zoom Change - Old Scale: {_currentScale}, New Scale: {newScale}, Zoom Factor: {zoomFactor}");

                            // ズーム前の状態を保存
                            var oldScale = _currentScale;
                            var oldHeight = _totalHeight;
                            var viewportHeight = _parentScrollView.Height;
                            var oldScrollY = _parentScrollView.ScrollY;
                            var mouseY = e.Location.Y;

                            Debug.WriteLine($"Zoom State - Old Height: {oldHeight}, Viewport Height: {viewportHeight}, Old ScrollY: {oldScrollY}, MouseY: {mouseY}");

                            // マウス位置のキャンバス上の相対位置を計算
                            var pointY = oldScrollY + mouseY;
                            var relativeY = pointY / oldHeight;

                            Debug.WriteLine($"Mouse Position - PointY: {pointY}, RelativeY: {relativeY}");

                            // スケールを更新
                            _currentScale = newScale;

                            // ページキャンバスの更新を遅延させる
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                await Task.Delay(50); // 50msの遅延を追加
                                UpdatePageCanvases();

                                // 新しいスクロール位置を計算
                                var newPointY = _totalHeight * relativeY;
                                var newScrollY = newPointY - (mouseY * (_totalHeight / oldHeight));

                                Debug.WriteLine($"New Scroll Position - NewPointY: {newPointY}, NewScrollY: {newScrollY}");

                                // スクロール位置を更新
                                await _parentScrollView.ScrollToAsync(0, Math.Max(0, Math.Min(newScrollY, _totalHeight - viewportHeight)), false);
                                InvalidateSurface();
                            });
                        }
                        e.Handled = true;
                    }
                    break;
            }
        }

        private bool _isUpdating;
        private bool _needsUpdate;
        private readonly object _updateLock = new object();

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            // 高品質な描画を有効化
            var paint = new SKPaint
            {
                IsAntialias = _currentScale <= 2.0f,
                FilterQuality = SKFilterQuality.Low
            };

            var info = e.Info;
            Debug.WriteLine($"Canvas Info - Width: {info.Width}, Height: {info.Height}, DPI: {info.Width / BASE_CANVAS_WIDTH}");

            // スケーリング計算
            var scale = Math.Min(info.Width / BASE_CANVAS_WIDTH, info.Height / (_totalHeight / _currentScale));
            var centerX = (info.Width - (BASE_CANVAS_WIDTH * scale)) / 2;
            var centerY = (info.Height - (_totalHeight * scale / _currentScale)) / 2;

            Debug.WriteLine($"Scaling Info - Scale: {scale}, CenterX: {centerX}, CenterY: {centerY}, CurrentScale: {_currentScale}");

            canvas.Translate(centerX, centerY);

            var scrollY = _parentScrollView?.ScrollY ?? 0;
            var viewportHeight = _parentScrollView?.Height ?? Height;

            // 表示範囲内のページのみを描画（バッファを追加）
            var visiblePages = _pageCanvases
                .Where(p => !(p.Y + p.Height < scrollY - viewportHeight * 1.5 ||
                             p.Y > scrollY + viewportHeight * 2.5))
                .ToList();

            foreach (var pageCanvas in visiblePages)
            {
                if (pageCanvas.PageBitmap == null) continue;

                // ページの描画
                var dest = new SKRect(
                    0,
                    pageCanvas.Y * scale / _currentScale,
                    BASE_CANVAS_WIDTH * scale,
                    (pageCanvas.Y + pageCanvas.Height) * scale / _currentScale
                );

                // ビットマップの描画を最適化
                using (var bitmapPaint = new SKPaint { FilterQuality = SKFilterQuality.Low })
                {
                    canvas.DrawBitmap(pageCanvas.PageBitmap, dest, bitmapPaint);
                }

                // 描画要素の描画
                foreach (var element in pageCanvas.DrawingElements.ToList())
                {
                    var path = new SKPath();
                    var elementPaint = element.DrawingPaint.Clone();
                    elementPaint.IsAntialias = _currentScale <= 2.0f;

                    if (element.IsShape)
                    {
                        switch (element.ShapeType)
                        {
                            case ShapeType.Line:
                                var points = GetPointsFromPath(element.DrawingPath);
                                if (points.Count >= 2)
                                {
                                    path.MoveTo(points[0]);
                                    path.LineTo(points[points.Count - 1]);
                                }
                                break;
                            case ShapeType.Circle:
                                var bounds = element.DrawingPath.Bounds;
                                var center = new SKPoint(bounds.MidX, bounds.MidY);
                                var radius = Math.Max(bounds.Width, bounds.Height) / 2;
                                path.AddCircle(center.X, center.Y, radius);
                                break;
                            case ShapeType.Triangle:
                            case ShapeType.Rectangle:
                                path.AddPath(element.DrawingPath);
                                break;
                        }
                    }
                    else
                    {
                        path.AddPath(element.DrawingPath);
                    }

                    // スケーリングと位置調整を適用
                    var matrix = SKMatrix.CreateIdentity();
                    matrix = matrix.PostConcat(SKMatrix.CreateScale(scale, scale));
                    matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, pageCanvas.Y * scale / _currentScale));
                    path.Transform(matrix);

                    // 図形の場合は線の太さを調整
                    if (element.IsShape)
                    {
                        elementPaint.StrokeWidth = Math.Max(element.DrawingPaint.StrokeWidth * scale, 1.0f);
                    }
                    else
                    {
                        elementPaint.StrokeWidth = element.DrawingPaint.StrokeWidth * scale;
                    }

                    canvas.DrawPath(path, elementPaint);
                }

                // 現在の描画パスの描画
                if (pageCanvas.IsDrawing && _currentTool != DrawingTool.Eraser)
                {
                    var currentPath = new SKPath();
                    if (_previewPath != null)
                    {
                        currentPath.AddPath(_previewPath);
                    }
                    else
                    {
                        currentPath.AddPath(pageCanvas.CurrentPath);
                    }

                    var currentPaint = _currentPaint.Clone();
                    currentPaint.IsAntialias = _currentScale <= 2.0f;
                    currentPaint.StrokeWidth = _currentPaint.StrokeWidth * scale;

                    var matrix = SKMatrix.CreateIdentity();
                    matrix = matrix.PostConcat(SKMatrix.CreateScale(scale, scale));
                    matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, pageCanvas.Y * scale / _currentScale));
                    currentPath.Transform(matrix);

                    canvas.DrawPath(currentPath, currentPaint);
                }
            }

            // コンテキストメニューの描画
            if (_isShowingContextMenu)
            {
                DrawContextMenu(canvas);
            }
        }

        private void UpdatePageCanvases()
        {
            if (_isUpdating)
            {
                _needsUpdate = true;
                return;
            }

            lock (_updateLock)
            {
                _isUpdating = true;
                _needsUpdate = false;

                try
                {
                    Debug.WriteLine($"Updating Page Canvases - Current Scale: {_currentScale}");

                    var oldPageCanvases = _pageCanvases.ToList();
                    _pageCanvases.Clear();

                    float currentY = 0;
                    for (int i = 0; i < _pdfPages.Count; i++)
                    {
                        var bitmap = _pdfPages[i];
                        if (bitmap == null)
                        {
                            Debug.WriteLine($"Warning: Page bitmap is null for page {i}");
                            continue;
                        }

                        var pageScale = (BASE_CANVAS_WIDTH * _currentScale) / bitmap.Width;
                        var pageCanvas = new PageCanvas
                        {
                            PageIndex = i,
                            PageBitmap = bitmap,
                            Width = BASE_CANVAS_WIDTH * _currentScale,
                            Height = bitmap.Height * pageScale,
                            Y = currentY,
                            DrawingElements = new ObservableCollection<DrawingStroke>(),
                            IsHighQuality = _highQualityPages.ContainsKey(i)
                        };

                        // 既存のページキャンバスから描画要素をコピー
                        if (i < oldPageCanvases.Count && oldPageCanvases[i]?.DrawingElements != null)
                        {
                            foreach (var element in oldPageCanvases[i].DrawingElements)
                            {
                                pageCanvas.DrawingElements.Add(element);
                            }
                        }

                        _pageCanvases.Add(pageCanvas);
                        currentY += pageCanvas.Height + 20;
                    }

                    _totalHeight = currentY;
                    WidthRequest = BASE_CANVAS_WIDTH * _currentScale;
                    HeightRequest = _totalHeight;

                    // ScrollViewの更新を確実に行う
                    if (_parentScrollView != null)
                    {
                        _parentScrollView.Content.HeightRequest = _totalHeight;
                        Debug.WriteLine($"Setting ScrollView content height to: {_totalHeight}");
                    }

                    InvalidateSurface();
                }
                finally
                {
                    _isUpdating = false;
                    if (_needsUpdate)
                    {
                        UpdatePageCanvases();
                    }
                }
            }
        }

        private void DrawContextMenu(SKCanvas canvas)
        {
            if (!_isShowingContextMenu) return;

            Debug.WriteLine($"Drawing context menu at: ({_lastRightClickPoint.X}, {_lastRightClickPoint.Y})");

            var menuPaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill
            };
            var borderPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1
            };

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            canvas.DrawRect(menuRect, menuPaint);
            canvas.DrawRect(menuRect, borderPaint);

            // 色選択ボタンの描画（一番上）
            float x = menuRect.Left + COLOR_BOX_MARGIN;
            float y = menuRect.Top + (CONTEXT_MENU_ITEM_HEIGHT - COLOR_BOX_SIZE) / 2;

            foreach (var color in _colors.Values)
            {
                var colorBoxRect = new SKRect(x, y, x + COLOR_BOX_SIZE, y + COLOR_BOX_SIZE);
                var colorBoxPaint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill
                };
                var colorBoxBorderPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1
                };

                canvas.DrawRect(colorBoxRect, colorBoxPaint);
                canvas.DrawRect(colorBoxRect, colorBoxBorderPaint);

                x += COLOR_BOX_SIZE + COLOR_BOX_MARGIN;
                if (x + COLOR_BOX_SIZE > menuRect.Right)
                {
                    break;
                }
            }

            // 透明度選択ボタンの描画（マーカーツール時のみ、色選択の下）
            if (_currentTool == DrawingTool.Marker)
            {
                x = menuRect.Left + STROKE_WIDTH_BOX_MARGIN;
                y = menuRect.Top + CONTEXT_MENU_ITEM_HEIGHT + (CONTEXT_MENU_ITEM_HEIGHT - STROKE_WIDTH_BOX_SIZE) / 2;

                foreach (var transparency in _transparencies)
                {
                    var transparencyBoxRect = new SKRect(x, y, x + STROKE_WIDTH_BOX_SIZE, y + STROKE_WIDTH_BOX_SIZE);
                    var transparencyBoxPaint = new SKPaint
                    {
                        Color = SKColors.White,
                        Style = SKPaintStyle.Fill
                    };
                    var transparencyBoxBorderPaint = new SKPaint
                    {
                        Color = SKColors.Black,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 1
                    };

                    canvas.DrawRect(transparencyBoxRect, transparencyBoxPaint);
                    canvas.DrawRect(transparencyBoxRect, transparencyBoxBorderPaint);

                    // 透明度の表示
                    var transparencyPaint = new SKPaint
                    {
                        Color = _markerPaint.Color.WithAlpha((byte)(transparency.Value * 255)),
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    canvas.DrawRect(transparencyBoxRect, transparencyPaint);

                    x += STROKE_WIDTH_BOX_SIZE + STROKE_WIDTH_BOX_MARGIN;
                    if (x + STROKE_WIDTH_BOX_SIZE > menuRect.Right)
                    {
                        break;
                    }
                }
            }

            // 太さ選択ボタンの描画（一番下）
            x = menuRect.Left + STROKE_WIDTH_BOX_MARGIN;
            y = menuRect.Top + CONTEXT_MENU_ITEM_HEIGHT * (_currentTool == DrawingTool.Marker ? 2 : 1) + (CONTEXT_MENU_ITEM_HEIGHT - STROKE_WIDTH_BOX_SIZE) / 2;

            var widths = _currentTool == DrawingTool.Marker ? _markerWidths : _strokeWidths;
            foreach (var width in widths)
            {
                var widthBoxRect = new SKRect(x, y, x + STROKE_WIDTH_BOX_SIZE, y + STROKE_WIDTH_BOX_SIZE);
                var widthBoxPaint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill
                };
                var widthBoxBorderPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1
                };

                canvas.DrawRect(widthBoxRect, widthBoxPaint);
                canvas.DrawRect(widthBoxRect, widthBoxBorderPaint);

                // 太さの表示
                var linePaint = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = width.Value,
                    IsAntialias = true
                };
                canvas.DrawLine(
                    widthBoxRect.Left + STROKE_WIDTH_BOX_MARGIN,
                    widthBoxRect.MidY,
                    widthBoxRect.Right - STROKE_WIDTH_BOX_MARGIN,
                    widthBoxRect.MidY,
                    linePaint);

                x += STROKE_WIDTH_BOX_SIZE + STROKE_WIDTH_BOX_MARGIN;
                if (x + STROKE_WIDTH_BOX_SIZE > menuRect.Right)
                {
                    break;
                }
            }
        }

        private bool IsPointInColorBox(SKPoint point, int colorIndex)
        {
            if (!_isShowingContextMenu) return false;

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            float x = menuRect.Left + COLOR_BOX_MARGIN + (COLOR_BOX_SIZE + COLOR_BOX_MARGIN) * colorIndex;
            float y = menuRect.Top + (CONTEXT_MENU_ITEM_HEIGHT - COLOR_BOX_SIZE) / 2;

            var colorBoxRect = new SKRect(x, y, x + COLOR_BOX_SIZE, y + COLOR_BOX_SIZE);
            return colorBoxRect.Contains(point);
        }

        private bool IsPointInStrokeWidthBox(SKPoint point, int widthIndex)
        {
            if (!_isShowingContextMenu) return false;

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            float x = menuRect.Left + STROKE_WIDTH_BOX_MARGIN + (STROKE_WIDTH_BOX_SIZE + STROKE_WIDTH_BOX_MARGIN) * widthIndex;
            float y = menuRect.Top + CONTEXT_MENU_ITEM_HEIGHT * (_currentTool == DrawingTool.Marker ? 2 : 1) + (CONTEXT_MENU_ITEM_HEIGHT - STROKE_WIDTH_BOX_SIZE) / 2;

            var widthBoxRect = new SKRect(x, y, x + STROKE_WIDTH_BOX_SIZE, y + STROKE_WIDTH_BOX_SIZE);
            return widthBoxRect.Contains(point);
        }

        private bool IsPointInTransparencyBox(SKPoint point, int transparencyIndex)
        {
            if (!_isShowingContextMenu || _currentTool != DrawingTool.Marker) return false;

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            float x = menuRect.Left + STROKE_WIDTH_BOX_MARGIN + (STROKE_WIDTH_BOX_SIZE + STROKE_WIDTH_BOX_MARGIN) * transparencyIndex;
            float y = menuRect.Top + CONTEXT_MENU_ITEM_HEIGHT + (CONTEXT_MENU_ITEM_HEIGHT - STROKE_WIDTH_BOX_SIZE) / 2;

            var transparencyBoxRect = new SKRect(x, y, x + STROKE_WIDTH_BOX_SIZE, y + STROKE_WIDTH_BOX_SIZE);
            return transparencyBoxRect.Contains(point);
        }

        public void SetTool(DrawingTool tool)
        {
            _currentTool = tool;
            switch (tool)
            {
                case DrawingTool.Pen:
                    _currentPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = _penColor,
                        StrokeWidth = _penStrokeWidth,
                        IsAntialias = true
                    };
                    break;
                case DrawingTool.Marker:
                    _currentPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = _markerColor,
                        StrokeWidth = _markerStrokeWidth,
                        IsAntialias = true,
                        BlendMode = SKBlendMode.SrcOver
                    };
                    break;
                case DrawingTool.Eraser:
                    _currentPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = SKColors.Transparent,
                        StrokeWidth = 20.0f,
                        IsAntialias = true,
                        BlendMode = SKBlendMode.Clear
                    };
                    break;
            }
        }

        public void SetPenColor(SKColor color)
        {
            if (_currentTool == DrawingTool.Pen)
            {
                _currentPaint.Color = color;
            }
            _penColor = color;
        }

        public void SetPenStrokeWidth(float width)
        {
            if (_currentTool == DrawingTool.Pen)
            {
                _currentPaint.StrokeWidth = width;
            }
            _penStrokeWidth = width;
        }

        public void SetMarkerColor(SKColor color)
        {
            if (_currentTool == DrawingTool.Marker)
            {
                _currentPaint.Color = color;
            }
            _markerColor = color;
        }

        public void SetMarkerStrokeWidth(float width)
        {
            if (_currentTool == DrawingTool.Marker)
            {
                _currentPaint.StrokeWidth = width;
            }
            _markerStrokeWidth = width;
        }

        public void Clear()
        {
            _drawingElements.Clear();
            _backgroundImage?.Dispose();
            _backgroundImage = null;
            ClearPdfPages();
            _pdfDocument?.Dispose();
            _pdfDocument = null;
            _pdfStream?.Dispose();
            _pdfStream = null;
            _undoStack.Clear();
            _redoStack.Clear();
            InvalidateSurface();
        }

        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var (pageIndex, element) = _undoStack.Pop();
                var pageCanvas = _pageCanvases.FirstOrDefault(pc => pc.PageIndex == pageIndex);
                if (pageCanvas != null)
                {
                    // 要素が移動された場合の処理
                    if (element.IsMoved)
                    {
                        // 元の位置に戻す
                        element.RestoreOriginalPosition();
                        InvalidateSurface();
                    }
                    else
                    {
                        // 通常の削除処理
                        pageCanvas.DrawingElements.Remove(element);
                        _redoStack.Push((pageIndex, element));
                        InvalidateSurface();
                    }
                }
            }
        }

        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var (pageIndex, element) = _redoStack.Pop();
                var pageCanvas = _pageCanvases.FirstOrDefault(pc => pc.PageIndex == pageIndex);
                if (pageCanvas != null)
                {
                    // 要素が移動された場合の処理
                    if (element.IsMoved)
                    {
                        // 移動後の位置に戻す
                        element.RestoreMovedPosition();
                        InvalidateSurface();
                    }
                    else
                    {
                        // 通常の追加処理
                        pageCanvas.DrawingElements.Add(element);
                        _undoStack.Push((pageIndex, element));
                        InvalidateSurface();
                    }
                }
            }
        }

        public async Task LoadImageAsync(string filePath)
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    _backgroundImage?.Dispose();
                    _backgroundImage = SKBitmap.Decode(stream);
                    InvalidateSurface();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image: {ex.Message}");
            }
        }

        private async Task LoadPdfPageAsync(int pageIndex, float dpi)
        {
            if (_pdfDocument == null || pageIndex < 0 || pageIndex >= _pdfDocument.PageCount)
                return;

            try
            {
                // キャッシュファイルのパスを生成
                var pageCacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
                Directory.CreateDirectory(pageCacheDir);
                var cacheFileName = Path.Combine(pageCacheDir, $"page_{pageIndex}_{dpi}.png");

                // キャッシュファイルが存在する場合はそれを使用
                if (File.Exists(cacheFileName))
                {
                    int retryCount = 0;
                    while (retryCount < MAX_RETRY_COUNT)
                    {
                        try
                        {
                            using (var stream = File.OpenRead(cacheFileName))
                            {
                                var bitmap = SKBitmap.Decode(stream);
                                if (bitmap != null)
                                {
                                    await SetPageBitmap(pageIndex, bitmap, dpi);
                                    return;
                                }
                            }
                            break;
                        }
                        catch (IOException ex)
                        {
                            retryCount++;
                            if (retryCount >= MAX_RETRY_COUNT)
                            {
                                Debug.WriteLine($"Error loading cached page {pageIndex} after {MAX_RETRY_COUNT} retries: {ex.Message}");
                                // キャッシュファイルが破損している場合は削除
                                try { File.Delete(cacheFileName); } catch { }
                                break;
                            }
                            await Task.Delay(RETRY_DELAY_MS);
                        }
                    }
                }

                // キャッシュファイルが存在しないか破損している場合は新規作成
                Debug.WriteLine($"Loading page {pageIndex} at {dpi} DPI");
                var pageSize = _pdfDocument.PageSizes[pageIndex];
                var widthInPixels = (int)Math.Round(pageSize.Width * dpi / 72f);
                var heightInPixels = (int)Math.Round(pageSize.Height * dpi / 72f);

                using (var page = _pdfDocument.Render(pageIndex, widthInPixels, heightInPixels, dpi, dpi, true))
                using (var memoryStream = new MemoryStream())
                {
                    page.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    memoryStream.Position = 0;
                    var bitmap = SKBitmap.Decode(memoryStream);

                    if (bitmap != null)
                    {
                        // キャッシュファイルとして保存（リトライ付き）
                        int retryCount = 0;
                        while (retryCount < MAX_RETRY_COUNT)
                        {
                            try
                            {
                                using (var stream = File.Create(cacheFileName))
                                {
                                    var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                                    data.SaveTo(stream);
                                }
                                break;
                            }
                            catch (IOException ex)
                            {
                                retryCount++;
                                if (retryCount >= MAX_RETRY_COUNT)
                                {
                                    Debug.WriteLine($"Error saving cache file for page {pageIndex} after {MAX_RETRY_COUNT} retries: {ex.Message}");
                                    break;
                                }
                                await Task.Delay(RETRY_DELAY_MS);
                            }
                        }

                        await SetPageBitmap(pageIndex, bitmap, dpi);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PDF page {pageIndex}: {ex.Message}");
            }
            finally
            {
                _loadingPages.Remove(pageIndex);
            }
        }

        private async Task SetPageBitmap(int pageIndex, SKBitmap bitmap, float dpi)
        {
            if (bitmap == null) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    if (dpi == HIGH_DPI)
                    {
                        Debug.WriteLine($"Setting high quality page {pageIndex}");

                        // 既存の高画質ページを破棄
                        if (_highQualityPages.TryGetValue(pageIndex, out var oldHighQualityBitmap))
                        {
                            oldHighQualityBitmap?.Dispose();
                        }

                        _highQualityPages[pageIndex] = bitmap;

                        // 高画質ページを表示用のページとしても設定
                        var oldBitmap = _pdfPages[pageIndex];
                        _pdfPages[pageIndex] = bitmap;
                        if (oldBitmap != null && oldBitmap != bitmap)
                        {
                            oldBitmap.Dispose();
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Setting low quality page {pageIndex}");
                        if (!_highQualityPages.ContainsKey(pageIndex))
                        {
                            var oldBitmap = _pdfPages[pageIndex];
                            _pdfPages[pageIndex] = bitmap;
                            if (oldBitmap != null)
                            {
                                oldBitmap.Dispose();
                            }
                        }
                        else
                        {
                            bitmap.Dispose();
                            return;
                        }
                    }

                    // ページキャンバスの更新
                    var pageCanvas = _pageCanvases.FirstOrDefault(pc => pc.PageIndex == pageIndex);
                    if (pageCanvas == null)
                    {
                        // ページキャンバスが存在しない場合は新規作成
                        pageCanvas = new PageCanvas
                        {
                            PageIndex = pageIndex,
                            DrawingElements = new ObservableCollection<DrawingStroke>()
                        };
                        _pageCanvases.Add(pageCanvas);
                    }

                    pageCanvas.PageBitmap = _pdfPages[pageIndex];
                    pageCanvas.IsHighQuality = dpi == HIGH_DPI;
                    pageCanvas.NeedsUpdate = true;
                    Debug.WriteLine($"Page {pageIndex} updated, invalidating surface");
                    InvalidateSurface();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting page bitmap for page {pageIndex}: {ex.Message}");
                    bitmap?.Dispose();
                }
            });
        }

        private async Task UpdateVisiblePagesAsync(double scrollY)
        {
            if (_pdfDocument == null || _pageCanvases.Count == 0)
                return;

            var viewportHeight = _parentScrollView?.Height ?? Height;

            // 表示範囲の計算を最適化
            var visiblePages = _pageCanvases
                .Select((page, index) => new { Page = page, Index = index })
                .Where(p => !(p.Page.Y + p.Page.Height < scrollY - viewportHeight * 2 ||
                             p.Page.Y > scrollY + viewportHeight * 3))  // バッファを拡大
                .ToList();

            if (!visiblePages.Any())
                return;

            _currentVisiblePage = visiblePages.First().Index;

            // 読み込む範囲を拡大（前後2ページずつ）
            var startPage = Math.Max(0, _currentVisiblePage - VISIBLE_PAGE_BUFFER);
            var endPage = Math.Min(_pdfDocument.PageCount - 1, _currentVisiblePage + VISIBLE_PAGE_BUFFER);

            // メモリ使用量のチェックを緩和
            if (_highQualityPages.Count > MAX_CACHED_PAGES)
            {
                await CleanupMemoryAsync();
            }

            var needsUpdate = false;

            // 表示範囲外のページを低画質に戻す処理を最適化
            var pagesToDowngrade = _highQualityPages.Keys
                .Where(i => i < startPage || i > endPage)
                .ToList();

            foreach (var pageIndex in pagesToDowngrade)
            {
                if (_highQualityPages.TryGetValue(pageIndex, out var highQualityBitmap))
                {
                    _highQualityPages.Remove(pageIndex);
                    needsUpdate = true;

                    // 低画質ページの読み込みを非同期で実行
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using (var page = _pdfDocument.Render(pageIndex,
                                (int)Math.Round(_pdfDocument.PageSizes[pageIndex].Width * LOW_DPI / 72f),
                                (int)Math.Round(_pdfDocument.PageSizes[pageIndex].Height * LOW_DPI / 72f),
                                LOW_DPI, LOW_DPI, true))
                            using (var memoryStream = new MemoryStream())
                            {
                                page.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                                memoryStream.Position = 0;
                                var lowQualityBitmap = SKBitmap.Decode(memoryStream);

                                if (lowQualityBitmap != null)
                                {
                                    await MainThread.InvokeOnMainThreadAsync(() =>
                                    {
                                        try
                                        {
                                            _pdfPages[pageIndex] = lowQualityBitmap;
                                            var pageCanvas = _pageCanvases.FirstOrDefault(pc => pc.PageIndex == pageIndex);
                                            if (pageCanvas != null)
                                            {
                                                pageCanvas.PageBitmap = lowQualityBitmap;
                                                pageCanvas.IsHighQuality = false;
                                                pageCanvas.NeedsUpdate = true;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Error updating page canvas for page {pageIndex}: {ex.Message}");
                                            lowQualityBitmap?.Dispose();
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading low quality page {pageIndex}: {ex.Message}");
                        }
                    });
                }
            }

            // 表示範囲内のページを高画質で読み込む（非同期で実行）
            for (int i = startPage; i <= endPage; i++)
            {
                if (!_highQualityPages.ContainsKey(i) && !_loadingPages.Contains(i))
                {
                    _loadingPages.Add(i);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await LoadPdfPageAsync(i, HIGH_DPI);
                            needsUpdate = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading high quality page {i}: {ex.Message}");
                        }
                        finally
                        {
                            _loadingPages.Remove(i);
                        }
                    });
                }
            }

            if (needsUpdate)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        UpdatePageCanvases();
                        InvalidateSurface();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating page canvases: {ex.Message}");
                    }
                });
            }
        }

        private async Task CleanupMemoryAsync()
        {
            if (_highQualityPages.Count > CACHE_CLEANUP_THRESHOLD)
            {
                var visibleRange = Enumerable.Range(
                    Math.Max(0, _currentVisiblePage - 2),  // 1から2に増加
                    Math.Min(_pdfDocument.PageCount - 1, _currentVisiblePage + 2) - Math.Max(0, _currentVisiblePage - 2) + 1
                );

                var pagesToRemove = _highQualityPages.Keys
                    .Where(k => !visibleRange.Contains(k))
                    .OrderByDescending(k => Math.Abs(k - _currentVisiblePage))
                    .Skip(MAX_CACHED_PAGES)
                    .ToList();

                foreach (var pageIndex in pagesToRemove)
                {
                    if (_highQualityPages.TryGetValue(pageIndex, out var bitmap))
                    {
                        bitmap.Dispose();
                        _highQualityPages.Remove(pageIndex);
                        await LoadPdfPageAsync(pageIndex, LOW_DPI);
                    }
                }
            }
        }

        public async Task SaveDrawingDataAsync(string customPath = null)
        {
            try
            {
                var drawingData = new DrawingData
                {
                    PdfFilePath = _currentPdfPath,
                    Pages = _pageCanvases
                        .Where(pc => pc.PageIndex < _pdfPages.Count)
                        .Select(pc => new PageDrawingData
                        {
                            PageIndex = pc.PageIndex,
                            DrawingElements = pc.DrawingElements.Select(de => new DrawingStrokeData
                            {
                                Points = GetPointsFromPath(de.DrawingPath).Select(p => new SKPoint(p.X / _currentScale, p.Y / _currentScale)).ToList(),
                                Color = de.DrawingPaint.Color,
                                StrokeWidth = de.DrawingPaint.StrokeWidth / _currentScale,
                                Style = de.DrawingPaint.Style,
                                Transparency = de.DrawingPaint.Color.Alpha / 255.0f,
                                Tool = de.DrawingPaint.BlendMode == SKBlendMode.SrcOver ? DrawingTool.Marker : DrawingTool.Pen,
                                IsShape = de.IsShape,
                                ShapeType = de.ShapeType,
                                Center = de.IsShape ? new SKPoint(de.DrawingPath.Bounds.MidX / _currentScale, de.DrawingPath.Bounds.MidY / _currentScale) : SKPoint.Empty,
                                Radius = de.IsShape && de.ShapeType == ShapeType.Circle ? Math.Max(de.DrawingPath.Bounds.Width, de.DrawingPath.Bounds.Height) / (2 * _currentScale) : 0,
                                StartPoint = de.IsShape && de.ShapeType == ShapeType.Line ? new SKPoint(de.StartPoint.X / _currentScale, de.StartPoint.Y / _currentScale) : SKPoint.Empty,
                                EndPoint = de.IsShape && de.ShapeType == ShapeType.Line ? new SKPoint(de.EndPoint.X / _currentScale, de.EndPoint.Y / _currentScale) : SKPoint.Empty,
                                Vertices = de.IsShape && (de.ShapeType == ShapeType.Triangle || de.ShapeType == ShapeType.Rectangle) ? GetPointsFromPath(de.DrawingPath).Select(p => new SKPoint(p.X / _currentScale, p.Y / _currentScale)).ToList() : new List<SKPoint>()
                            }).ToList()
                        }).ToList(),
                    PenSettings = new Dictionary<string, float>
                    {
                        { "StrokeWidth", _penPaint.StrokeWidth },
                        { "Color", BitConverter.ToUInt32(new byte[] { _penPaint.Color.Blue, _penPaint.Color.Green, _penPaint.Color.Red, _penPaint.Color.Alpha }, 0) }
                    },
                    MarkerSettings = new Dictionary<string, float>
                    {
                        { "StrokeWidth", _markerPaint.StrokeWidth },
                        { "Color", BitConverter.ToUInt32(new byte[] { _markerPaint.Color.Blue, _markerPaint.Color.Green, _markerPaint.Color.Red, _markerPaint.Color.Alpha }, 0) }
                    }
                };

                // 直線の開始点と終了点の処理
                foreach (var page in drawingData.Pages)
                {
                    foreach (var element in page.DrawingElements)
                    {
                        if (element.IsShape && element.ShapeType == ShapeType.Line)
                        {
                            if (element.Points.Count >= 2)
                            {
                                // 始点と終点が同じ場合は、終点を少しずらす
                                if (element.StartPoint.Equals(element.EndPoint))
                                {
                                    element.EndPoint = new SKPoint(
                                        element.StartPoint.X + 1,
                                        element.StartPoint.Y + 1
                                    );
                                }
                                // 始点と終点が逆になっている場合は、入れ替える
                                else if (element.StartPoint.X > element.EndPoint.X || 
                                        (element.StartPoint.X == element.EndPoint.X && element.StartPoint.Y > element.EndPoint.Y))
                                {
                                    var temp = element.StartPoint;
                                    element.StartPoint = element.EndPoint;
                                    element.EndPoint = temp;
                                }
                            }
                        }
                    }
                }

                var json = System.Text.Json.JsonSerializer.Serialize(drawingData);
                var savePath = Path.Combine(_tempDirectory, "drawing_data.json");
                await File.WriteAllTextAsync(savePath, json);
                Debug.WriteLine($"Drawing data saved to: {savePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving drawing data: {ex.Message}");
            }
        }

        public async Task LoadDrawingDataAsync()
        {
            try
            {
                var drawingDataPath = Path.Combine(_tempDirectory, DRAWING_DATA_FILE);
                if (File.Exists(drawingDataPath))
                {
                    var json = await File.ReadAllTextAsync(drawingDataPath);
                    var drawingData = System.Text.Json.JsonSerializer.Deserialize<DrawingData>(json);

                    // ツールの設定を復元
                    if (drawingData.PenSettings != null)
                    {
                        _penPaint.StrokeWidth = drawingData.PenSettings.GetValueOrDefault("StrokeWidth", 2.0f);
                        var alpha = (byte)drawingData.PenSettings.GetValueOrDefault("ColorAlpha", 255);
                        var red = (byte)drawingData.PenSettings.GetValueOrDefault("ColorRed", 0);
                        var green = (byte)drawingData.PenSettings.GetValueOrDefault("ColorGreen", 0);
                        var blue = (byte)drawingData.PenSettings.GetValueOrDefault("ColorBlue", 0);
                        _penPaint.Color = new SKColor(red, green, blue, alpha);
                        _penPaint.BlendMode = SKBlendMode.Src;
                        _penColor = _penPaint.Color;
                        Debug.WriteLine($"Pen color loaded: A={_penPaint.Color.Alpha}, R={_penPaint.Color.Red}, G={_penPaint.Color.Green}, B={_penPaint.Color.Blue}");
                    }

                    if (drawingData.MarkerSettings != null)
                    {
                        _markerPaint.StrokeWidth = drawingData.MarkerSettings.GetValueOrDefault("StrokeWidth", 10.0f);
                        var alpha = (byte)drawingData.MarkerSettings.GetValueOrDefault("ColorAlpha", 128);
                        var red = (byte)drawingData.MarkerSettings.GetValueOrDefault("ColorRed", 255);
                        var green = (byte)drawingData.MarkerSettings.GetValueOrDefault("ColorGreen", 255);
                        var blue = (byte)drawingData.MarkerSettings.GetValueOrDefault("ColorBlue", 0);
                        _markerPaint.Color = new SKColor(red, green, blue, alpha);
                        _markerPaint.BlendMode = SKBlendMode.SrcOver;
                        _markerColor = _markerPaint.Color;
                        Debug.WriteLine($"Marker color loaded: A={_markerPaint.Color.Alpha}, R={_markerPaint.Color.Red}, G={_markerPaint.Color.Green}, B={_markerPaint.Color.Blue}");
                    }

                    // PageCacheが存在する場合は、PageCacheから読み込む
                    var pageCacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
                    if (Directory.Exists(pageCacheDir))
                    {
                        var cacheFiles = Directory.GetFiles(pageCacheDir, "page_*.png");
                        if (cacheFiles.Length > 0)
                        {
                            // 既存のページをクリア
                            foreach (var page in _pdfPages)
                            {
                                page?.Dispose();
                            }
                            foreach (var page in _highQualityPages.Values)
                            {
                                page?.Dispose();
                            }
                            _pdfPages.Clear();
                            _highQualityPages.Clear();
                            _pageCanvases.Clear();

                            // 低画質ページを読み込む
                            foreach (var cacheFile in cacheFiles.OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_')[1])))
                            {
                                using (var stream = File.OpenRead(cacheFile))
                                {
                                    var bitmap = SKBitmap.Decode(stream);
                                    if (bitmap != null)
                                    {
                                        _pdfPages.Add(bitmap);
                                        var pageCanvas = new PageCanvas
                                        {
                                            PageIndex = _pdfPages.Count - 1,
                                            PageBitmap = bitmap,
                                            DrawingElements = new ObservableCollection<DrawingStroke>()
                                        };
                                        _pageCanvases.Add(pageCanvas);
                                    }
                                }
                            }

                            // 高画質ページを読み込む
                            var highQualityFiles = Directory.GetFiles(pageCacheDir, "page_*_100.png");
                            foreach (var cacheFile in highQualityFiles.OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_')[1])))
                            {
                                using (var stream = File.OpenRead(cacheFile))
                                {
                                    var bitmap = SKBitmap.Decode(stream);
                                    if (bitmap != null)
                                    {
                                        var pageIndex = int.Parse(Path.GetFileNameWithoutExtension(cacheFile).Split('_')[1]);
                                        if (pageIndex < _pdfPages.Count)
                                        {
                                            _highQualityPages[pageIndex] = bitmap;
                                            _pdfPages[pageIndex] = bitmap;
                                            var pageCanvas = _pageCanvases[pageIndex];
                                            pageCanvas.PageBitmap = bitmap;
                                            pageCanvas.IsHighQuality = true;
                                        }
                                    }
                                }
                            }

                            // 描画データの復元
                            foreach (var pageData in drawingData.Pages)
                            {
                                if (pageData.PageIndex < _pageCanvases.Count)
                                {
                                    var pageCanvas = _pageCanvases[pageData.PageIndex];
                                    pageCanvas.DrawingElements.Clear();

                                    foreach (var elementData in pageData.DrawingElements)
                                    {
                                        var path = new SKPath();
                                        var paint = new SKPaint
                                        {
                                            Style = elementData.Style,
                                            Color = elementData.Color,
                                            StrokeWidth = elementData.StrokeWidth * _currentScale,
                                            IsAntialias = true,
                                            BlendMode = elementData.Tool == DrawingTool.Marker ? SKBlendMode.SrcOver : SKBlendMode.Src
                                        };

                                        if (elementData.Tool == DrawingTool.Marker)
                                        {
                                            paint.Color = paint.Color.WithAlpha((byte)(elementData.Transparency * 255));
                                        }

                                        Debug.WriteLine($"Loading element color: A={paint.Color.Alpha}, R={paint.Color.Red}, G={paint.Color.Green}, B={paint.Color.Blue}, BlendMode={paint.BlendMode}");

                                        // 図形の種類に応じてパスを作成
                                        if (elementData.IsShape)
                                        {
                                            switch (elementData.ShapeType)
                                            {
                                                case ShapeType.Circle:
                                                    path.AddCircle(elementData.Center.X * _currentScale, elementData.Center.Y * _currentScale, elementData.Radius * _currentScale);
                                                    break;
                                                case ShapeType.Line:
                                                    path.MoveTo(elementData.StartPoint.X * _currentScale, elementData.StartPoint.Y * _currentScale);
                                                    path.LineTo(elementData.EndPoint.X * _currentScale, elementData.EndPoint.Y * _currentScale);
                                                    break;
                                                case ShapeType.Triangle:
                                                case ShapeType.Rectangle:
                                                    if (elementData.Vertices.Count > 0)
                                                    {
                                                        path.MoveTo(elementData.Vertices[0].X * _currentScale, elementData.Vertices[0].Y * _currentScale);
                                                        for (int i = 1; i < elementData.Vertices.Count; i++)
                                                        {
                                                            path.LineTo(elementData.Vertices[i].X * _currentScale, elementData.Vertices[i].Y * _currentScale);
                                                        }
                                                        path.Close();
                                                    }
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            // 通常の描画
                                            if (elementData.Points.Count > 0)
                                            {
                                                path.MoveTo(elementData.Points[0].X * _currentScale, elementData.Points[0].Y * _currentScale);
                                                for (int i = 1; i < elementData.Points.Count; i++)
                                                {
                                                    path.LineTo(elementData.Points[i].X * _currentScale, elementData.Points[i].Y * _currentScale);
                                                }
                                            }
                                        }

                                        var stroke = new DrawingStroke(path, paint);
                                        if (elementData.IsShape)
                                        {
                                            stroke.IsShape = true;
                                            stroke.ShapeType = elementData.ShapeType;
                                            stroke.StartPoint = new SKPoint(elementData.StartPoint.X * _currentScale, elementData.StartPoint.Y * _currentScale);
                                            stroke.EndPoint = new SKPoint(elementData.EndPoint.X * _currentScale, elementData.EndPoint.Y * _currentScale);
                                        }

                                        pageCanvas.DrawingElements.Add(stroke);
                                    }
                                }
                            }

                            // ページキャンバスの更新
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                try
                                {
                                    UpdatePageCanvases();
                                    InvalidateSurface();
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error updating page canvases: {ex.Message}");
                                }
                            });

                            // スクロール位置の復元
                            if (drawingData.LastScrollY > 0 && _parentScrollView != null)
                            {
                                await MainThread.InvokeOnMainThreadAsync(async () =>
                                {
                                    try
                                    {
                                        // ページキャンバスの更新を待機
                                        await Task.Delay(300); // 待機時間を延長

                                        // ScrollViewの高さを更新
                                        UpdateScrollViewHeight();

                                        // スクロール位置を復元
                                        var targetScrollY = Math.Min(drawingData.LastScrollY, _totalHeight - _parentScrollView.Height);
                                        await _parentScrollView.ScrollToAsync(0, targetScrollY, false);

                                        Debug.WriteLine($"Restoring scroll position: {targetScrollY} (original: {drawingData.LastScrollY}, total height: {_totalHeight}, viewport height: {_parentScrollView.Height})");

                                        // 表示を更新
                                        InvalidateSurface();
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error restoring scroll position: {ex.Message}");
                                    }
                                });
                            }

                            Debug.WriteLine("Drawing data loaded successfully from PageCache");
                            return;
                        }
                    }

                    // PageCacheが存在しない場合は、PDFファイルから読み込む
                    if (drawingData.PdfFilePath != null && File.Exists(drawingData.PdfFilePath))
                    {
                        await LoadPdfAsync(drawingData.PdfFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading drawing data: {ex.Message}");
            }
        }

        private List<SKPoint> GetPointsFromPath(SKPath path)
        {
            var points = new List<SKPoint>();
            using (var iterator = path.CreateIterator(false))
            {
                var pathVerb = SKPathVerb.Move;
                var pathPoints = new SKPoint[4];

                while ((pathVerb = iterator.Next(pathPoints)) != SKPathVerb.Done)
                {
                    switch (pathVerb)
                    {
                        case SKPathVerb.Move:
                            points.Add(pathPoints[0]);
                            break;
                        case SKPathVerb.Line:
                            points.Add(pathPoints[1]);
                            break;
                        case SKPathVerb.Quad:
                            points.Add(pathPoints[1]);
                            points.Add(pathPoints[2]);
                            break;
                        case SKPathVerb.Cubic:
                            points.Add(pathPoints[1]);
                            points.Add(pathPoints[2]);
                            points.Add(pathPoints[3]);
                            break;
                        case SKPathVerb.Close:
                            if (points.Count > 0)
                            {
                                points.Add(points[0]);
                            }
                            break;
                    }
                }
            }
            return points;
        }

        private DrawingData _savedDrawingData;

        public void SetDrawingData(DrawingData drawingData)
        {
            if (drawingData == null) return;

            // 描画データを一時的に保存
            _savedDrawingData = drawingData;
        }

        public async Task LoadPdfAsync(string filePath)
        {
            try
            {
                // 既存の描画データを保存
                if (_currentPdfPath != null)
                {
                    await SaveDrawingDataAsync();
                }

                // 既存のリソースを解放
                ClearPdfPages();
                _pdfDocument?.Dispose();
                _pdfStream?.Dispose();

                // 新しいPDFを読み込む
                _currentPdfPath = filePath;
                _pdfPages.Clear();
                _pageCanvases.Clear();

                // キャッシュディレクトリの初期化
                var pageCacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
                if (!Directory.Exists(pageCacheDir))
                {
                    Directory.CreateDirectory(pageCacheDir);
                }

                // PDFの読み込み
                _pdfStream = File.OpenRead(filePath);
                _pdfDocument = PdfDocument.Load(_pdfStream);
                if (_pdfDocument != null)
                {
                    _pdfPages = new List<SKBitmap>(new SKBitmap[_pdfDocument.PageCount]);

                    // ページの読み込み（低画質のみ）
                    for (int pageIndex = 0; pageIndex < _pdfDocument.PageCount; pageIndex++)
                    {
                        try
                        {
                            await LoadPdfPageAsync(pageIndex, LOW_DPI);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading page {pageIndex}: {ex.Message}");
                        }
                    }

                    // ページキャンバスの初期化
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            UpdatePageCanvases();
                            InvalidateSurface();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error initializing page canvases: {ex.Message}");
                        }
                    });

                    // 保存された描画データがある場合は、それを優先して使用
                    if (_savedDrawingData != null)
                    {
                        // 描画データの復元
                        foreach (var pageData in _savedDrawingData.Pages)
                        {
                            if (pageData.PageIndex < _pageCanvases.Count)
                            {
                                var pageCanvas = _pageCanvases[pageData.PageIndex];
                                pageCanvas.DrawingElements.Clear();

                                foreach (var elementData in pageData.DrawingElements)
                                {
                                    var path = new SKPath();
                                    var paint = new SKPaint
                                    {
                                        Style = elementData.Style,
                                        Color = elementData.Color,
                                        StrokeWidth = elementData.StrokeWidth * _currentScale,
                                        IsAntialias = true
                                    };

                                    if (elementData.Tool == DrawingTool.Marker)
                                    {
                                        paint.BlendMode = SKBlendMode.SrcOver;
                                        paint.Color = paint.Color.WithAlpha((byte)(elementData.Transparency * 255));
                                    }

                                    // 図形の種類に応じてパスを作成
                                    if (elementData.IsShape)
                                    {
                                        switch (elementData.ShapeType)
                                        {
                                            case ShapeType.Circle:
                                                path.AddCircle(elementData.Center.X * _currentScale, elementData.Center.Y * _currentScale, elementData.Radius * _currentScale);
                                                break;
                                            case ShapeType.Line:
                                                path.MoveTo(elementData.StartPoint.X * _currentScale, elementData.StartPoint.Y * _currentScale);
                                                path.LineTo(elementData.EndPoint.X * _currentScale, elementData.EndPoint.Y * _currentScale);
                                                break;
                                            case ShapeType.Triangle:
                                            case ShapeType.Rectangle:
                                                if (elementData.Vertices.Count > 0)
                                                {
                                                    path.MoveTo(elementData.Vertices[0].X * _currentScale, elementData.Vertices[0].Y * _currentScale);
                                                    for (int i = 1; i < elementData.Vertices.Count; i++)
                                                    {
                                                        path.LineTo(elementData.Vertices[i].X * _currentScale, elementData.Vertices[i].Y * _currentScale);
                                                    }
                                                    path.Close();
                                                }
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        // 通常の描画
                                        if (elementData.Points.Count > 0)
                                        {
                                            path.MoveTo(elementData.Points[0].X * _currentScale, elementData.Points[0].Y * _currentScale);
                                            for (int i = 1; i < elementData.Points.Count; i++)
                                            {
                                                path.LineTo(elementData.Points[i].X * _currentScale, elementData.Points[i].Y * _currentScale);
                                            }
                                        }
                                    }

                                    var stroke = new DrawingStroke(path, paint);
                                    if (elementData.IsShape)
                                    {
                                        stroke.IsShape = true;
                                        stroke.ShapeType = elementData.ShapeType;
                                        stroke.StartPoint = new SKPoint(elementData.StartPoint.X * _currentScale, elementData.StartPoint.Y * _currentScale);
                                        stroke.EndPoint = new SKPoint(elementData.EndPoint.X * _currentScale, elementData.EndPoint.Y * _currentScale);
                                    }

                                    pageCanvas.DrawingElements.Add(stroke);
                                }
                            }
                        }

                        _savedDrawingData = null;
                    }

                    // スクロール位置の復元
                    if (_parentScrollView != null)
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            // ページキャンバスの更新を待機
                            await Task.Delay(200);
                            UpdateScrollViewHeight();
                            InvalidateSurface();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error updating scroll view: {ex.Message}");
                        }
                    });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PDF: {ex.Message}");
                // エラー発生時はリソースを解放
                ClearPdfPages();
                _pdfDocument?.Dispose();
                _pdfStream?.Dispose();
                _pdfDocument = null;
                _pdfStream = null;
            }
        }

        private void ClearPdfPages()
        {
            // 描画要素を保持したまま、ビットマップのみを解放
            foreach (var page in _pdfPages)
            {
                try
                {
                    page?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing page bitmap: {ex.Message}");
                }
            }
            foreach (var page in _highQualityPages.Values)
            {
                try
                {
                    page?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing high quality page bitmap: {ex.Message}");
                }
            }

            // ビットマップ関連のコレクションのみクリア
            _pdfPages.Clear();
            _highQualityPages.Clear();
            _loadingPages.Clear();

            // GCを明示的に呼び出してメモリを解放
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            Debug.WriteLine($"OnSizeAllocated called with width: {width}, height: {height}");

            if (_pdfPages.Count > 0)
            {
                float totalHeight = 0;
                float totalWidth = BASE_CANVAS_WIDTH * _currentScale;
                foreach (var pageBitmap in _pdfPages)
                {
                    if (pageBitmap != null)
                    {
                        var pageScale = totalWidth / pageBitmap.Width;
                        var destHeight = pageBitmap.Height * pageScale;
                        totalHeight += destHeight + 20; // ページ間の余白を含める
                    }
                }
                WidthRequest = totalWidth;
                HeightRequest = totalHeight;
                _totalHeight = totalHeight;
                Debug.WriteLine($"Setting WidthRequest to: {totalWidth}, HeightRequest to: {totalHeight}");

                // ScrollViewのContentの高さを更新
                UpdateScrollViewHeight();
            }
        }

        public void Dispose()
        {
            try
            {
                // アプリ終了時に強制的に保存
                _ = SaveDrawingDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during final save: {ex.Message}");
            }

            ClearPdfPages();
            foreach (var pageCanvas in _pageCanvases)
            {
                pageCanvas.Dispose();
            }
            _pageCanvases.Clear();
            _currentPaint?.Dispose();
            _currentPath?.Dispose();
            _backgroundImage?.Dispose();
            _pdfDocument?.Dispose();
            _pdfStream?.Dispose();
        }

        private bool PathsIntersect(SKPath path1, SKPath path2)
        {
            using (var path1Copy = new SKPath(path1))
            using (var path2Copy = new SKPath(path2))
            {
                // パスの太さを考慮してバウンディングボックスを拡大
                path1Copy.GetBounds(out var bounds1);
                path2Copy.GetBounds(out var bounds2);

                float margin = Math.Max(_currentPaint.StrokeWidth, 10);
                bounds1.Inflate(margin, margin);
                bounds2.Inflate(margin, margin);

                // まずバウンディングボックスで判定
                if (!bounds1.IntersectsWith(bounds2))
                {
                    return false;
                }

                // より詳細な交差判定
                using (var measure1 = new SKPathMeasure(path1Copy))
                using (var measure2 = new SKPathMeasure(path2Copy))
                {
                    float length1 = measure1.Length;
                    float length2 = measure2.Length;
                    float step = 5.0f; // 5ピクセルごとにチェック

                    for (float distance1 = 0; distance1 < length1; distance1 += step)
                    {
                        if (!measure1.GetPosition(distance1, out var point1))
                            continue;

                        for (float distance2 = 0; distance2 < length2; distance2 += step)
                        {
                            if (!measure2.GetPosition(distance2, out var point2))
                                continue;

                            float dx = point1.X - point2.X;
                            float dy = point1.Y - point2.Y;
                            float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                            if (distance <= margin)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
        }

        public void ShowColorMenu(SKPoint position)
        {
            _lastRightClickPoint = position;
            _isShowingContextMenu = true;
            Debug.WriteLine($"Showing context menu at: ({position.X}, {position.Y})");
            InvalidateSurface();
        }

        public void InitializeCacheDirectory(string noteName, string tempDir)
        {
            _tempDirectory = tempDir;
            Debug.WriteLine($"Cache directory initialized: {_tempDirectory}");
            // キャッシュディレクトリの存在確認
            if (!Directory.Exists(_tempDirectory))
            {
                Directory.CreateDirectory(_tempDirectory);
                Directory.CreateDirectory(Path.Combine(_tempDirectory, PAGE_CACHE_DIR));
            }
            else
            {
                // キャッシュが存在する場合は、drawing_data.jsonを確認
                var drawingDataPath = Path.Combine(_tempDirectory, DRAWING_DATA_FILE);
                if (File.Exists(drawingDataPath))
                {
                    try
                    {
                        var json = File.ReadAllText(drawingDataPath);
                        var drawingData = System.Text.Json.JsonSerializer.Deserialize<DrawingData>(json);
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

        private async Task AutoSaveIfNeeded()
        {
            if ((DateTime.Now - _lastAutoSaveTime).TotalMilliseconds >= AUTO_SAVE_INTERVAL)
            {
                try
                {
                    await SaveDrawingDataAsync();
                    _lastAutoSaveTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during auto-save: {ex.Message}");
                }
            }
        }

        public async Task OnBackButtonPressed()
        {
            try
            {
                // 戻るボタンが押された時に保存
                await SaveDrawingDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during back button save: {ex.Message}");
            }
        }
    }
}