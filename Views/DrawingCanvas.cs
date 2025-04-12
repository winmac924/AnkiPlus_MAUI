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
        private const float BASE_CANVAS_WIDTH = 800f;
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 2.0f;
        private const float HIGH_DPI = 100f;
        private const float LOW_DPI = 50f;
        private const float SELECTION_MARGIN = 20f;
        private const float SELECTION_MARGIN_SCALE_FACTOR = 1.5f;
        private const float CONTEXT_MENU_WIDTH = 260;
        private const float CONTEXT_MENU_HEIGHT = 130;
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

        // メモリ管理用の定数
        private const int VISIBLE_PAGE_BUFFER = 1;
        private const int MAX_CACHED_PAGES = 3;
        private const int CACHE_CLEANUP_THRESHOLD = 5;
        private const float SCALE_THRESHOLD = 1.5f;

        private const float PARTIAL_ERASER_WIDTH_SMALL = 10.0f;
        private const float PARTIAL_ERASER_WIDTH_MEDIUM = 20.0f;
        private const float PARTIAL_ERASER_WIDTH_LARGE = 30.0f;

        // 保存用のデータ構造
        public class DrawingData
        {
            public string PdfFilePath { get; set; }
            public List<PageDrawingData> Pages { get; set; } = new List<PageDrawingData>();
            public Dictionary<string, float> PenSettings { get; set; } = new Dictionary<string, float>();
            public Dictionary<string, float> MarkerSettings { get; set; } = new Dictionary<string, float>();
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
            { "細", 5.0f },
            { "中", 10.0f },
            { "太", 20.0f },
            { "極太", 30.0f },
            { "特大", 40.0f }
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

        public enum DrawingTool
        {
            Pen,
            Marker,
            Eraser,
            Text
        }

        public class DrawingStroke : IDisposable
        {
            public SKPath DrawingPath { get; private set; }
            public SKPaint DrawingPaint { get; private set; }
            public bool IsShape { get; set; }
            public ShapeType ShapeType { get; set; }
            public bool IsMoved { get; private set; }
            private SKPath _originalPath;
            private SKPath _movedPath;

            public DrawingStroke(SKPath path, SKPaint paint)
            {
                DrawingPath = path;
                DrawingPaint = paint;
                IsShape = false;
                ShapeType = ShapeType.None;
                IsMoved = false;
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

            // 各点が直線からどれだけ離れているかをチェック
            float maxDeviation = 0;
            foreach (var point in points)
            {
                var deviation = DistanceToLine(point, firstPoint, lastPoint);
                maxDeviation = Math.Max(maxDeviation, deviation);
            }

            // 最大偏差が線の長さの10%以下なら直線と判定
            return maxDeviation < lineLength * 0.1f;
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
            var minSize = 2.0f; // 最小サイズを2ピクセルに設定
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
                        
                        // 直線の長さを計算
                        var lineLength = (float)Math.Sqrt(
                            Math.Pow(endPoint.X - startPoint.X, 2) + 
                            Math.Pow(endPoint.Y - startPoint.Y, 2)
                        );
                        
                        // 最小長さを設定（スケーリングを考慮）
                        var scaledMinSize = minSize / _currentScale;
                        if (lineLength < scaledMinSize)
                        {
                            var angle = Math.Atan2(endPoint.Y - startPoint.Y, endPoint.X - startPoint.X);
                            endPoint = new SKPoint(
                                startPoint.X + (float)(scaledMinSize * Math.Cos(angle)),
                                startPoint.Y + (float)(scaledMinSize * Math.Sin(angle))
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

        private async void OnScrollViewScrolled(object sender, ScrolledEventArgs e)
        {
            Debug.WriteLine($"ScrollView scrolled to: {e.ScrollY}");
            await UpdateVisiblePagesAsync(e.ScrollY);
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
        }

        private bool IsPointNearPath(SKPath path, SKPoint point, float margin)
        {
            // パスのバウンディングボックスを取得
            path.GetBounds(out var bounds);
            
            // マージンを含めた拡大バウンディングボックス
            var expandedBounds = new SKRect(
                bounds.Left - margin,
                bounds.Top - margin,
                bounds.Right + margin,
                bounds.Bottom + margin
            );

            // まず拡大バウンディングボックスでの判定
            if (!expandedBounds.Contains(point))
            {
                return false;
            }

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
                    
                    if (pointDistance <= margin)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        protected override void OnTouch(SKTouchEventArgs e)
        {
            bool isCtrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isRightClick = e.MouseButton == SKMouseButton.Right;

            // Ctrlキーが押されていない場合、ホイールイベントは処理しない
            if (e.ActionType == SKTouchAction.WheelChanged && !isCtrlPressed)
            {
                e.Handled = false;
                return;
            }

            // タッチ位置をキャンバス座標系に変換
            var info = CanvasSize;
            var centerX = (info.Width - (BASE_CANVAS_WIDTH * _currentScale)) / 2;
            var canvasX = (e.Location.X - centerX) / _currentScale;
            var canvasY = e.Location.Y / _currentScale;
            var transformedPoint = new SKPoint(canvasX, canvasY);

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

            // ページ内の相対座標を計算
            var pageRelativePoint = new SKPoint(
                transformedPoint.X,
                transformedPoint.Y - pageCanvas.Y / _currentScale
            );

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (_currentTool == DrawingTool.Eraser && _currentEraserMode == EraserMode.Partial && _isShowingContextMenu)
                    {
                        // 部分削除の太さボタンのクリック判定
                        for (int i = 0; i < 3; i++)
                        {
                            if (IsPointInPartialEraserWidthButton(e.Location, i))
                            {
                                switch (i)
                                {
                                    case 0:
                                        _partialEraserWidth = PARTIAL_ERASER_WIDTH_SMALL;
                                        break;
                                    case 1:
                                        _partialEraserWidth = PARTIAL_ERASER_WIDTH_MEDIUM;
                                        break;
                                    case 2:
                                        _partialEraserWidth = PARTIAL_ERASER_WIDTH_LARGE;
                                        break;
                                }
                                _isShowingContextMenu = false;
                                InvalidateSurface();
                                e.Handled = true;
                                return;
                            }
                        }
                    }

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
                        if (_currentTool == DrawingTool.Eraser)
                        {
                            // 消しゴムモードの選択
                            if (IsPointInEraserModeBox(e.Location, 0)) // 一括削除
                            {
                                _currentEraserMode = EraserMode.Full;
                                _isShowingContextMenu = false;
                                InvalidateSurface();
                                e.Handled = true;
                                return;
                            }
                            else if (IsPointInEraserModeBox(e.Location, 1)) // 部分削除
                            {
                                _currentEraserMode = EraserMode.Partial;
                                _isShowingContextMenu = false;
                                InvalidateSurface();
                                e.Handled = true;
                                return;
                            }
                        }
                        else
                        {
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
                        }

                        // メニュー外をクリックした場合はメニューを閉じる
                        _isShowingContextMenu = false;
                        InvalidateSurface();
                        e.Handled = true;
                    }
                    else if (_currentTool == DrawingTool.Eraser)
                    {
                        if (_currentEraserMode == EraserMode.Partial)
                        {
                            // 部分削除モードの処理
                            pageCanvas.IsDrawing = true;
                            pageCanvas.CurrentPath = new SKPath();
                            pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
                            pageCanvas.LastPoint = pageRelativePoint;
                            e.Handled = true;
                        }
                        else
                        {
                            // 一括削除モードの処理
                            pageCanvas.IsDrawing = true;
                            pageCanvas.CurrentPath = new SKPath();
                            pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
                            pageCanvas.LastPoint = pageRelativePoint;
                            e.Handled = true;
                        }
                    }
                    else if (_currentTool == DrawingTool.Text)
                    {
                        AddTextBox(pageRelativePoint);
                        e.Handled = true;
                        return;
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
                        if (_currentTool == DrawingTool.Eraser)
                        {
                            if (_currentEraserMode == EraserMode.Partial)
                            {
                                // 部分削除の処理
                                pageCanvas.CurrentPath.LineTo(pageRelativePoint);
                                pageCanvas.LastPoint = pageRelativePoint;

                                // 消しゴムのパスと交差する要素を部分的に削除
                                foreach (var element in pageCanvas.DrawingElements.ToList())
                                {
                                    if (PathsIntersect(pageCanvas.CurrentPath, element.DrawingPath))
                                    {
                                        // 交差部分を検出して線を分割
                                        var points = GetPointsFromPath(element.DrawingPath);
                                        if (points.Count >= 2)
                                        {
                                            var newPaths = SplitPathAtIntersection(points, pageCanvas.CurrentPath, element);
                                            if (newPaths.Count > 0)
                                            {
                                                // 元の要素を削除
                                                pageCanvas.DrawingElements.Remove(element);
                                                _undoStack.Push((pageCanvas.PageIndex, element));

                                                // 分割されたパスを新しい要素として追加
                                                foreach (var newPath in newPaths)
                                                {
                                                    var newElement = new DrawingStroke(newPath, element.DrawingPaint.Clone())
                                                    {
                                                        IsShape = element.IsShape,
                                                        ShapeType = element.ShapeType
                                                    };
                                                    pageCanvas.DrawingElements.Add(newElement);
                                                    _undoStack.Push((pageCanvas.PageIndex, newElement));
                                                }
                                            }
                                            else
                                            {
                                                // 完全に消去される場合
                                                pageCanvas.DrawingElements.Remove(element);
                                                _undoStack.Push((pageCanvas.PageIndex, element));
                                            }
                                        }
                                    }
                                }

                                // 新しいパスを開始
                                pageCanvas.CurrentPath = new SKPath();
                                pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
                            }
                            else
                            {
                                // 一括削除の処理
                                pageCanvas.CurrentPath.LineTo(pageRelativePoint);
                                pageCanvas.LastPoint = pageRelativePoint;

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
                            }
                            InvalidateSurface();
                        }
                        else
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
                            else if (_currentTool == DrawingTool.Eraser && _currentEraserMode == EraserMode.Partial)
                            {
                                // 部分削除の処理
                                pageCanvas.CurrentPath.LineTo(pageRelativePoint);
                                pageCanvas.LastPoint = pageRelativePoint;

                                // 消しゴムのパスと交差する要素を部分的に削除
                                foreach (var element in pageCanvas.DrawingElements.ToList())
                                {
                                    if (PathsIntersect(pageCanvas.CurrentPath, element.DrawingPath))
                                    {
                                        // 交差部分を検出して線を分割
                                        var points = GetPointsFromPath(element.DrawingPath);
                                        if (points.Count >= 2)
                                        {
                                            var newPaths = SplitPathAtIntersection(points, pageCanvas.CurrentPath, element);
                                            if (newPaths.Count > 0)
                                            {
                                                // 元の要素を削除
                                                pageCanvas.DrawingElements.Remove(element);
                                                _undoStack.Push((pageCanvas.PageIndex, element));

                                                // 分割されたパスを新しい要素として追加
                                                foreach (var newPath in newPaths)
                                                {
                                                    var newElement = new DrawingStroke(newPath, element.DrawingPaint.Clone())
                                                    {
                                                        IsShape = element.IsShape,
                                                        ShapeType = element.ShapeType
                                                    };
                                                    pageCanvas.DrawingElements.Add(newElement);
                                                    _undoStack.Push((pageCanvas.PageIndex, newElement));
                                                }
                                            }
                                            else
                                            {
                                                // 完全に消去される場合
                                                pageCanvas.DrawingElements.Remove(element);
                                                _undoStack.Push((pageCanvas.PageIndex, element));
                                            }
                                        }
                                    }
                                }

                                // 新しいパスを開始
                                pageCanvas.CurrentPath = new SKPath();
                                pageCanvas.CurrentPath.MoveTo(pageRelativePoint);
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
                            // 消しゴムの場合は何もしない（既に削除済み）
                            pageCanvas.IsDrawing = false;
                            pageCanvas.CurrentPath = null;
                            InvalidateSurface();
                        }
                        else
                        {
                            if (_currentTool != DrawingTool.Eraser)
                            {
                                // 図形認識が有効な場合は補正されたパスを使用
                                var finalPath = _previewPath ?? pageCanvas.CurrentPath;
                                var element = new DrawingStroke(finalPath, _currentPaint.Clone());
                                
                                // 図形認識が有効な場合は図形としてマーク
                                if (_previewPath != null)
                                {
                                    var shapeType = RecognizeShape(pageCanvas.CurrentPath);
                                    element.IsShape = true;
                                    element.ShapeType = shapeType;
                                }
                                
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
                        var scale = e.WheelDelta > 0 ? 1.1f : 0.9f;
                        var newScale = _currentScale * scale;
                        if (newScale >= MIN_SCALE && newScale <= MAX_SCALE)
                        {
                            // ズーム前の状態を保存
                            var oldScale = _currentScale;
                            var oldHeight = _totalHeight;
                            var viewportHeight = _parentScrollView.Height;
                            var oldScrollY = _parentScrollView.ScrollY;
                            var mouseY = e.Location.Y;

                            // マウス位置のキャンバス上の相対位置を計算
                            var pointY = oldScrollY + mouseY;
                            var relativeY = pointY / oldHeight;

                            // スケールを更新
                            _currentScale = newScale;
                            UpdatePageCanvases();

                            // 新しいスクロール位置を計算
                            var newPointY = _totalHeight * relativeY;
                            var newScrollY = newPointY - (mouseY * (_totalHeight / oldHeight));

                            // スクロール位置を更新
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                _parentScrollView.ScrollToAsync(0, Math.Max(0, Math.Min(newScrollY, _totalHeight - viewportHeight)), false);
                            });

                            InvalidateSurface();
                        }
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void UpdatePageCanvases()
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
                currentY += pageCanvas.Height + 20; // ページ間の余白を追加
            }

            // スクロールビューの高さを更新
            _totalHeight = currentY;
            WidthRequest = BASE_CANVAS_WIDTH * _currentScale;
            HeightRequest = _totalHeight;
            UpdateScrollViewHeight();

            Debug.WriteLine($"Total Height: {_totalHeight}, Width Request: {WidthRequest}, Height Request: {HeightRequest}");

            InvalidateSurface();
        }

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);
            var canvas = e.Surface.Canvas;
            
            // 高品質な描画を有効化
            canvas.Clear(SKColors.White);
            canvas.SetMatrix(SKMatrix.CreateIdentity());
            
            // アンチエイリアスとフィルタリングを設定
            var paint = new SKPaint
            {
                IsAntialias = _currentScale <= 2.0f,
                FilterQuality = SKFilterQuality.Low
            };

            if (_pageCanvases.Count == 0)
            {
                return;
            }

            var info = e.Info;
            var centerX = (info.Width - (BASE_CANVAS_WIDTH * _currentScale)) / 2;
            canvas.Translate(centerX, 0);

            var scrollY = _parentScrollView?.ScrollY ?? 0;
            var viewportHeight = _parentScrollView?.Height ?? Height;

            Debug.WriteLine($"Canvas Info - Width: {info.Width}, Height: {info.Height}, Scale: {_currentScale}");
            Debug.WriteLine($"Scroll Info - Y: {scrollY}, Viewport Height: {viewportHeight}");

            // 表示範囲内のページのみを描画
            var visiblePages = _pageCanvases
                .Where(p => !(p.Y + p.Height < scrollY - viewportHeight || 
                             p.Y > scrollY + viewportHeight * 2))
                .ToList();

            Debug.WriteLine($"Visible Pages Count: {visiblePages.Count}");

            foreach (var pageCanvas in visiblePages)
            {
                if (pageCanvas.PageBitmap == null) continue;

                Debug.WriteLine($"Drawing Page {pageCanvas.PageIndex} - Y: {pageCanvas.Y}, Height: {pageCanvas.Height}");

                // ページの描画
                var dest = new SKRect(0, pageCanvas.Y, BASE_CANVAS_WIDTH * _currentScale, pageCanvas.Y + pageCanvas.Height);
                canvas.DrawBitmap(pageCanvas.PageBitmap, dest, paint);

                // 描画要素の描画
                foreach (var element in pageCanvas.DrawingElements.ToList())
                {
                    var path = new SKPath();
                    var elementPaint = element.DrawingPaint.Clone();
                    elementPaint.IsAntialias = _currentScale <= 2.0f;
                    
                    // 図形の場合、適切なパスを作成
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
                        // 通常の描画の場合、元のパスをそのまま使用
                        path.AddPath(element.DrawingPath);
                    }
                    
                    // スケーリングと位置調整を適用
                    var matrix = SKMatrix.CreateIdentity();
                    matrix = matrix.PostConcat(SKMatrix.CreateScale(_currentScale, _currentScale));
                    matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, pageCanvas.Y));
                    path.Transform(matrix);
                    
                    // 線の太さを調整（最小値を設定）
                    var minStrokeWidth = 1.0f;
                    var originalStrokeWidth = element.DrawingPaint.StrokeWidth;
                    var scaledStrokeWidth = Math.Max(originalStrokeWidth * _currentScale, minStrokeWidth);
                    elementPaint.StrokeWidth = scaledStrokeWidth;
                    
                    // 色と透明度を正しく適用
                    elementPaint.Color = element.DrawingPaint.Color;
                    elementPaint.BlendMode = element.DrawingPaint.BlendMode;
                    
                    // マーカーの場合は透明度を適用
                    if (element.DrawingPaint.BlendMode == SKBlendMode.SrcOver)
                    {
                        elementPaint.Color = element.DrawingPaint.Color;
                    }
                    
                    // パスのバウンディングボックスを取得してデバッグ出力
                    path.GetBounds(out var pathBounds);
                    Debug.WriteLine($"Drawing element - Type: {(element.IsShape ? element.ShapeType.ToString() : "Freehand")}, " +
                                  $"Bounds: ({pathBounds.Left}, {pathBounds.Top}) to ({pathBounds.Right}, {pathBounds.Bottom}), " +
                                  $"StrokeWidth: {scaledStrokeWidth}, Scale: {_currentScale}");
                    
                    canvas.DrawPath(path, elementPaint);
                }

                // 現在の描画パスの描画（消しゴムツール以外の場合のみ）
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
                    
                    // スケーリングと位置調整を適用
                    var matrix = SKMatrix.CreateIdentity();
                    matrix = matrix.PostConcat(SKMatrix.CreateScale(_currentScale, _currentScale));
                    matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, pageCanvas.Y));
                    currentPath.Transform(matrix);
                    
                    var currentPaint = _currentPaint.Clone();
                    currentPaint.IsAntialias = _currentScale <= 2.0f;
                    var currentStrokeWidth = Math.Max(_currentPaint.StrokeWidth * _currentScale, 1.0f);
                    currentPaint.StrokeWidth = currentStrokeWidth;
                    currentPaint.BlendMode = _currentPaint.BlendMode;
                    
                    canvas.DrawPath(currentPath, currentPaint);
                }

                // 部分削除中の消しゴムの範囲を表示
                if (_currentTool == DrawingTool.Eraser && _currentEraserMode == EraserMode.Partial && pageCanvas.IsDrawing)
                {
                    var eraserPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = SKColors.Red.WithAlpha(128),
                        StrokeWidth = _partialEraserWidth * 2,
                        IsAntialias = true,
                        PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0)
                    };

                    var eraserPath = new SKPath();
                    eraserPath.AddPath(pageCanvas.CurrentPath);
                    
                    // スケーリングと位置調整を適用
                    var matrix = SKMatrix.CreateIdentity();
                    matrix = matrix.PostConcat(SKMatrix.CreateScale(_currentScale, _currentScale));
                    matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, pageCanvas.Y));
                    eraserPath.Transform(matrix);
                    
                    canvas.DrawPath(eraserPath, eraserPaint);
                }
            }

            // 選択された要素のハイライト表示
            if (_selectedElement != null && _selectedPageCanvas != null)
            {
                var highlightPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Blue,
                    StrokeWidth = Math.Max(2 * _currentScale, 1.0f),
                    IsAntialias = _currentScale <= 2.0f,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 5 * _currentScale, 5 * _currentScale }, 0)
                };

                var path = new SKPath();
                path.AddPath(_selectedElement.DrawingPath);
                
                // スケーリングと位置調整を適用
                var matrix = SKMatrix.CreateIdentity();
                matrix = matrix.PostConcat(SKMatrix.CreateScale(_currentScale, _currentScale));
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, _selectedPageCanvas.Y));
                path.Transform(matrix);
                
                canvas.DrawPath(path, highlightPaint);
            }

            // コンテキストメニューの表示
            DrawContextMenu(canvas);

            // テキストボックスの描画
            foreach (var textBox in _textBoxes)
            {
                // テキストボックスの背景
                var backgroundPaint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(textBox.Bounds, backgroundPaint);

                // テキストボックスの枠
                var borderPaint = new SKPaint
                {
                    Color = textBox.IsEditing ? SKColors.Blue : SKColors.Gray,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1
                };
                canvas.DrawRect(textBox.Bounds, borderPaint);

                // テキスト
                var textPaint = new SKPaint
                {
                    Color = textBox.Color,
                    TextSize = textBox.FontSize,
                    IsAntialias = true
                };

                // テキストの描画位置を計算
                var textBounds = new SKRect();
                textPaint.MeasureText(textBox.Text, ref textBounds);
                var x = textBox.Bounds.Left + TEXT_BOX_PADDING;
                var y = textBox.Bounds.Top + textBox.Bounds.Height / 2 + textBounds.Height / 2;

                canvas.DrawText(textBox.Text, x, y, textPaint);
            }
        }

        private void DrawContextMenu(SKCanvas canvas)
        {
            if (!_isShowingContextMenu) return;

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
            var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = 14,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("MS Gothic")
            };

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            canvas.DrawRect(menuRect, menuPaint);
            canvas.DrawRect(menuRect, borderPaint);

            float y = menuRect.Top + 10;

            if (_currentTool == DrawingTool.Eraser)
            {
                // 消しゴムモードの選択
                // 一括削除ボタン
                var fullEraserRect = new SKRect(menuRect.Left + 10, y, menuRect.Right - 10, y + 30);
                var fullEraserPaint = new SKPaint
                {
                    Color = _currentEraserMode == EraserMode.Full ? SKColors.LightBlue : SKColors.White,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(fullEraserRect, fullEraserPaint);
                canvas.DrawRect(fullEraserRect, borderPaint);
                canvas.DrawText("一括削除", fullEraserRect.Left + 5, y + 20, textPaint);

                // 部分削除ボタン
                y += 40;
                var partialEraserRect = new SKRect(menuRect.Left + 10, y, menuRect.Right - 10, y + 30);
                var partialEraserPaint = new SKPaint
                {
                    Color = _currentEraserMode == EraserMode.Partial ? SKColors.LightBlue : SKColors.White,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(partialEraserRect, partialEraserPaint);
                canvas.DrawRect(partialEraserRect, borderPaint);
                canvas.DrawText("部分削除", partialEraserRect.Left + 5, y + 20, textPaint);

                // 部分削除時の太さ選択（部分削除モード時のみ表示）
                if (_currentEraserMode == EraserMode.Partial)
                {
                    y += 40;
                    var buttonWidth = (menuRect.Width - 40) / 3;
                    
                    // 小ボタン
                    var smallRect = new SKRect(menuRect.Left + 10, y, menuRect.Left + 10 + buttonWidth, y + 30);
                    var smallPaint = new SKPaint
                    {
                        Color = _partialEraserWidth == PARTIAL_ERASER_WIDTH_SMALL ? SKColors.LightBlue : SKColors.White,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawRect(smallRect, smallPaint);
                    canvas.DrawRect(smallRect, borderPaint);
                    canvas.DrawText("小", smallRect.Left + (buttonWidth - textPaint.MeasureText("小")) / 2, y + 20, textPaint);

                    // 中ボタン
                    var mediumRect = new SKRect(smallRect.Right + 5, y, smallRect.Right + 5 + buttonWidth, y + 30);
                    var mediumPaint = new SKPaint
                    {
                        Color = _partialEraserWidth == PARTIAL_ERASER_WIDTH_MEDIUM ? SKColors.LightBlue : SKColors.White,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawRect(mediumRect, mediumPaint);
                    canvas.DrawRect(mediumRect, borderPaint);
                    canvas.DrawText("中", mediumRect.Left + (buttonWidth - textPaint.MeasureText("中")) / 2, y + 20, textPaint);

                    // 大ボタン
                    var largeRect = new SKRect(mediumRect.Right + 5, y, mediumRect.Right + 5 + buttonWidth, y + 30);
                    var largePaint = new SKPaint
                    {
                        Color = _partialEraserWidth == PARTIAL_ERASER_WIDTH_LARGE ? SKColors.LightBlue : SKColors.White,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawRect(largeRect, largePaint);
                    canvas.DrawRect(largeRect, borderPaint);
                    canvas.DrawText("大", largeRect.Left + (buttonWidth - textPaint.MeasureText("大")) / 2, y + 20, textPaint);
                }
            }
            else
            {
                // 色選択ボタン
                for (int i = 0; i < _colors.Count; i++)
                {
                    var color = _colors.Values.ElementAt(i);
                    var colorRect = new SKRect(
                        menuRect.Left + 10 + (i * (COLOR_BOX_SIZE + 5)),
                        y,
                        menuRect.Left + 10 + (i * (COLOR_BOX_SIZE + 5)) + COLOR_BOX_SIZE,
                        y + COLOR_BOX_SIZE
                    );
                    var colorPaint = new SKPaint
                    {
                        Color = color,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawRect(colorRect, colorPaint);
                    canvas.DrawRect(colorRect, borderPaint);
                }

                // 太さ選択ボタン
                y += COLOR_BOX_SIZE + 10;
                for (int i = 0; i < _strokeWidths.Count; i++)
                {
                    var width = _strokeWidths.Values.ElementAt(i);
                    var widthRect = new SKRect(
                        menuRect.Left + 10 + (i * (STROKE_WIDTH_BOX_SIZE + 5)),
                        y,
                        menuRect.Left + 10 + (i * (STROKE_WIDTH_BOX_SIZE + 5)) + STROKE_WIDTH_BOX_SIZE,
                        y + STROKE_WIDTH_BOX_SIZE
                    );
                    canvas.DrawRect(widthRect, menuPaint);
                    canvas.DrawRect(widthRect, borderPaint);

                    // 太さを表す線を描画
                    var linePaint = new SKPaint
                    {
                        Color = SKColors.Black,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = width,
                        IsAntialias = true
                    };
                    var lineY = y + STROKE_WIDTH_BOX_SIZE / 2;
                    canvas.DrawLine(
                        widthRect.Left + 5,
                        lineY,
                        widthRect.Right - 5,
                        lineY,
                        linePaint
                    );
                }

                // マーカーの場合は透明度選択も表示
                if (_currentTool == DrawingTool.Marker)
                {
                    y += STROKE_WIDTH_BOX_SIZE + 10;
                    for (int i = 0; i < _transparencies.Count; i++)
                    {
                        var transparency = _transparencies.Values.ElementAt(i);
                        var transparencyRect = new SKRect(
                            menuRect.Left + 10 + (i * (STROKE_WIDTH_BOX_SIZE + 5)),
                            y,
                            menuRect.Left + 10 + (i * (STROKE_WIDTH_BOX_SIZE + 5)) + STROKE_WIDTH_BOX_SIZE,
                            y + STROKE_WIDTH_BOX_SIZE
                        );
                        canvas.DrawRect(transparencyRect, menuPaint);
                        canvas.DrawRect(transparencyRect, borderPaint);

                        // 透明度を表すグラデーションを描画
                        var gradientPaint = new SKPaint
                        {
                            Style = SKPaintStyle.Fill,
                            Shader = SKShader.CreateLinearGradient(
                                new SKPoint(transparencyRect.Left, transparencyRect.Top),
                                new SKPoint(transparencyRect.Right, transparencyRect.Top),
                                new[] { SKColors.Black.WithAlpha((byte)(transparency * 255)), SKColors.Transparent },
                                new[] { 0f, 1f },
                                SKShaderTileMode.Clamp
                            )
                        };
                        canvas.DrawRect(transparencyRect, gradientPaint);
                    }
                }
            }
        }

        private string GetPartialEraserWidthText()
        {
            if (_partialEraserWidth == PARTIAL_ERASER_WIDTH_SMALL) return "小";
            if (_partialEraserWidth == PARTIAL_ERASER_WIDTH_MEDIUM) return "中";
            if (_partialEraserWidth == PARTIAL_ERASER_WIDTH_LARGE) return "大";
            return "中";
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
            _currentPaint = tool switch
            {
                DrawingTool.Pen => _penPaint.Clone(),
                DrawingTool.Marker => _markerPaint.Clone(),
                DrawingTool.Eraser => _eraserPaint.Clone(),
                _ => _currentPaint
            };
            if (tool != DrawingTool.Text)
            {
                FinishTextBoxEditing();
            }
        }

        private void UpdateCurrentPaint(SKColor color, float strokeWidth, float? transparency = null)
        {
            switch (_currentTool)
            {
                case DrawingTool.Pen:
                    _currentPaint = _penPaint.Clone();
                    _currentPaint.Color = color;
                    _currentPaint.StrokeWidth = strokeWidth;
                    if (transparency.HasValue)
                    {
                        _currentPaint.Color = new SKColor(color.Red, color.Green, color.Blue, (byte)(255 * transparency.Value));
                    }
                    break;

                case DrawingTool.Marker:
                    _currentPaint = _markerPaint.Clone();
                    _currentPaint.Color = color;
                    // マーカーの太さを_markerWidthsから取得
                    if (_currentTool == DrawingTool.Marker)
                    {
                        var currentWidth = GetMarkerWidthText();
                        if (_markerWidths.TryGetValue(currentWidth, out float markerWidth))
                        {
                            _currentPaint.StrokeWidth = markerWidth;
                        }
                    }
                    else
                    {
                        _currentPaint.StrokeWidth = strokeWidth;
                    }
                    if (transparency.HasValue)
                    {
                        _currentPaint.Color = new SKColor(color.Red, color.Green, color.Blue, (byte)(255 * transparency.Value));
                    }
                    break;

                case DrawingTool.Eraser:
                    _currentPaint = _eraserPaint.Clone();
                    _currentPaint.StrokeWidth = strokeWidth;
                    break;

                default:
                    _currentPaint = _penPaint.Clone();
                    break;
            }
        }

        private string GetMarkerWidthText()
        {
            // 現在のマーカーの太さに最も近い値を探す
            var currentWidth = _currentPaint.StrokeWidth;
            var closestWidth = _markerWidths.OrderBy(x => Math.Abs(x.Value - currentWidth)).First();
            return closestWidth.Key;
        }

        public void SetMarkerWidth(string width)
        {
            if (_markerWidths.TryGetValue(width, out float strokeWidth))
            {
                _markerPaint.StrokeWidth = strokeWidth;
                UpdateCurrentPaint(_currentPaint.Color, strokeWidth);
                InvalidateSurface();
            }
        }

        public void CycleMarkerWidth()
        {
            var currentWidth = GetMarkerWidthText();
            var widths = _markerWidths.Keys.ToList();
            var currentIndex = widths.IndexOf(currentWidth);
            var nextIndex = (currentIndex + 1) % widths.Count;
            SetMarkerWidth(widths[nextIndex]);
        }

        public void SetPenColor(SKColor color)
        {
            if (_currentPaint != null)
            {
                _currentPaint.Color = color;
                _currentPaint.Style = SKPaintStyle.Stroke;
                _currentPaint.StrokeWidth = 2;
                _currentPaint.StrokeCap = SKStrokeCap.Round;
                _currentPaint.StrokeJoin = SKStrokeJoin.Round;
                _currentPaint.IsAntialias = true;
            }
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading cached page {pageIndex}: {ex.Message}");
                        // キャッシュファイルが破損している場合は削除
                        try { File.Delete(cacheFileName); } catch { }
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
                        // キャッシュファイルとして保存
                        using (var stream = File.Create(cacheFileName))
                        {
                            var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                            data.SaveTo(stream);
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
            
            // バッファを1ページに減らす
            var visiblePages = _pageCanvases
                .Select((page, index) => new { Page = page, Index = index })
                .Where(p => !(p.Page.Y + p.Page.Height < scrollY - viewportHeight || 
                             p.Page.Y > scrollY + viewportHeight * 1.5))
                .ToList();

            if (!visiblePages.Any())
                return;

            _currentVisiblePage = visiblePages.First().Index;

            // 読み込む範囲を縮小（前後1ページずつ）
            var startPage = Math.Max(0, _currentVisiblePage - VISIBLE_PAGE_BUFFER);
            var endPage = Math.Min(_pdfDocument.PageCount - 1, _currentVisiblePage + VISIBLE_PAGE_BUFFER);

            // メモリ使用量が多い場合は強制的にクリーンアップを実行
            if (_highQualityPages.Count > MAX_CACHED_PAGES)
            {
                await CleanupMemoryAsync();
            }

            var needsUpdate = false;

            // 表示範囲外のページを低画質に戻す処理を同期的に実行
            var pagesToDowngrade = _highQualityPages.Keys
                .Where(i => i < startPage || i > endPage)
                .ToList();

            foreach (var pageIndex in pagesToDowngrade)
            {
                Debug.WriteLine($"Downgrading page {pageIndex} to low quality");
                SKBitmap highQualityBitmap = null;
                
                // 高画質ビットマップを取得
                if (_highQualityPages.TryGetValue(pageIndex, out highQualityBitmap))
                {
                    _highQualityPages.Remove(pageIndex);
                    needsUpdate = true;

                    // 低画質ページを読み込む
                    try
                    {
                        SKBitmap lowQualityBitmap = null;
                        using (var page = _pdfDocument.Render(pageIndex, 
                            (int)Math.Round(_pdfDocument.PageSizes[pageIndex].Width * LOW_DPI / 72f),
                            (int)Math.Round(_pdfDocument.PageSizes[pageIndex].Height * LOW_DPI / 72f),
                            LOW_DPI, LOW_DPI, true))
                        using (var memoryStream = new MemoryStream())
                        {
                            page.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                            memoryStream.Position = 0;
                            lowQualityBitmap = SKBitmap.Decode(memoryStream);

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

                    // 高画質ビットマップを解放
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            highQualityBitmap?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error disposing high quality bitmap for page {pageIndex}: {ex.Message}");
                        }
                    });
                }
            }

            // 表示範囲内のページを高画質で読み込む（一度に1ページずつ）
            for (int i = startPage; i <= endPage; i++)
            {
                if (!_highQualityPages.ContainsKey(i) && !_loadingPages.Contains(i))
                {
                    Debug.WriteLine($"Loading high quality page {i}");
                    _loadingPages.Add(i);
                    try
                    {
                        await LoadPdfPageAsync(i, HIGH_DPI);
                        needsUpdate = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading high quality page {i}: {ex.Message}");
                        _loadingPages.Remove(i);
                    }
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
            // キャッシュ制限を超えた場合のみクリーンアップを実行
            if (_highQualityPages.Count > CACHE_CLEANUP_THRESHOLD)
            {
                var visibleRange = Enumerable.Range(
                    Math.Max(0, _currentVisiblePage - 2),
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
                    Pages = _pageCanvases.Select(pc => new PageDrawingData
                    {
                        PageIndex = pc.PageIndex,
                        DrawingElements = pc.DrawingElements.Select(de => new DrawingStrokeData
                        {
                            Points = GetPointsFromPath(de.DrawingPath),
                            Color = de.DrawingPaint.Color,
                            StrokeWidth = de.DrawingPaint.StrokeWidth,
                            Style = de.DrawingPaint.Style,
                            Transparency = de.DrawingPaint.Color.Alpha / 255.0f,
                            Tool = de.DrawingPaint.BlendMode == SKBlendMode.SrcOver ? DrawingTool.Marker : DrawingTool.Pen,
                            IsShape = de.IsShape,
                            ShapeType = de.ShapeType,
                            
                            // 図形固有の情報を保存
                            Center = de.IsShape ? new SKPoint(de.DrawingPath.Bounds.MidX, de.DrawingPath.Bounds.MidY) : SKPoint.Empty,
                            Radius = de.IsShape && de.ShapeType == ShapeType.Circle ? 
                                Math.Max(de.DrawingPath.Bounds.Width, de.DrawingPath.Bounds.Height) / 2 : 0,
                            StartPoint = de.IsShape && de.ShapeType == ShapeType.Line ? 
                                GetPointsFromPath(de.DrawingPath).FirstOrDefault() : SKPoint.Empty,
                            EndPoint = de.IsShape && de.ShapeType == ShapeType.Line ? 
                                GetPointsFromPath(de.DrawingPath).LastOrDefault() : SKPoint.Empty,
                            Vertices = de.IsShape && (de.ShapeType == ShapeType.Triangle || de.ShapeType == ShapeType.Rectangle) ? 
                                GetPointsFromPath(de.DrawingPath) : new List<SKPoint>()
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

                // 直線の開始点と終了点が同じ場合は、終了点を少しずらす
                foreach (var page in drawingData.Pages)
                {
                    foreach (var element in page.DrawingElements)
                    {
                        if (element.IsShape && element.ShapeType == ShapeType.Line)
                        {
                            if (element.StartPoint.Equals(element.EndPoint))
                            {
                                // 終了点を開始点から少しずらす
                                element.EndPoint = new SKPoint(
                                    element.StartPoint.X + 1,
                                    element.StartPoint.Y + 1
                                );
                            }
                        }
                    }
                }

                var json = System.Text.Json.JsonSerializer.Serialize(drawingData);
                var savePath = customPath ?? Path.Combine(_tempDirectory, DRAWING_DATA_FILE);
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

                    if (drawingData.PdfFilePath != null && drawingData.PdfFilePath != _currentPdfPath)
                    {
                        await LoadPdfAsync(drawingData.PdfFilePath);
                    }

                    // ツールの設定を復元
                    if (drawingData.PenSettings != null)
                    {
                        _penPaint.StrokeWidth = drawingData.PenSettings.GetValueOrDefault("StrokeWidth", 2.0f);
                        var colorValue = drawingData.PenSettings.GetValueOrDefault("Color", 0xFF000000);
                        var colorBytes = BitConverter.GetBytes((uint)colorValue);
                        _penPaint.Color = new SKColor(colorBytes[1], colorBytes[2], colorBytes[3], colorBytes[0]);
                        _penPaint.BlendMode = SKBlendMode.Src;
                        Debug.WriteLine($"Pen color loaded: A={_penPaint.Color.Alpha}, R={_penPaint.Color.Red}, G={_penPaint.Color.Green}, B={_penPaint.Color.Blue}");
                    }

                    if (drawingData.MarkerSettings != null)
                    {
                        _markerPaint.StrokeWidth = drawingData.MarkerSettings.GetValueOrDefault("StrokeWidth", 10.0f);
                        var colorValue = drawingData.MarkerSettings.GetValueOrDefault("Color", 0x80FFFF00);
                        var colorBytes = BitConverter.GetBytes((uint)colorValue);
                        _markerPaint.Color = new SKColor(colorBytes[1], colorBytes[2], colorBytes[3], colorBytes[0]);
                        _markerPaint.BlendMode = SKBlendMode.SrcOver;
                        Debug.WriteLine($"Marker color loaded: A={_markerPaint.Color.Alpha}, R={_markerPaint.Color.Red}, G={_markerPaint.Color.Green}, B={_markerPaint.Color.Blue}");
                    }

                    // ページキャンバスが存在しない場合は作成
                    if (_pageCanvases.Count == 0)
                    {
                        for (int i = 0; i < _pdfPages.Count; i++)
                        {
                            var pageCanvas = new PageCanvas
                            {
                                PageIndex = i,
                                DrawingElements = new ObservableCollection<DrawingStroke>()
                            };
                            _pageCanvases.Add(pageCanvas);
                        }
                    }

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
                                    StrokeWidth = elementData.StrokeWidth,
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
                                            path.AddCircle(elementData.Center.X, elementData.Center.Y, elementData.Radius);
                                            break;
                                        case ShapeType.Line:
                                            path.MoveTo(elementData.StartPoint);
                                            path.LineTo(elementData.EndPoint);
                                            break;
                                        case ShapeType.Triangle:
                                        case ShapeType.Rectangle:
                                            if (elementData.Vertices.Count > 0)
                                            {
                                                path.MoveTo(elementData.Vertices[0]);
                                                for (int i = 1; i < elementData.Vertices.Count; i++)
                                                {
                                                    path.LineTo(elementData.Vertices[i]);
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
                                        path.MoveTo(elementData.Points[0]);
                                        for (int i = 1; i < elementData.Points.Count; i++)
                                        {
                                            path.LineTo(elementData.Points[i]);
                                        }
                                    }
                                }

                                var stroke = new DrawingStroke(path, paint);
                                if (elementData.IsShape)
                                {
                                    stroke.IsShape = true;
                                    stroke.ShapeType = elementData.ShapeType;
                                }

                                pageCanvas.DrawingElements.Add(stroke);
                            }
                        }
                    }

                    InvalidateSurface();
                    Debug.WriteLine("Drawing data loaded successfully");
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
                var lastPoint = SKPoint.Empty;
                
                while ((pathVerb = iterator.Next(pathPoints)) != SKPathVerb.Done)
                {
                    switch (pathVerb)
                    {
                        case SKPathVerb.Move:
                            lastPoint = pathPoints[0];
                            points.Add(lastPoint);
                            break;
                        case SKPathVerb.Line:
                            lastPoint = pathPoints[1];
                            points.Add(lastPoint);
                            break;
                        case SKPathVerb.Quad:
                            lastPoint = pathPoints[2];
                            points.Add(lastPoint);
                            break;
                        case SKPathVerb.Cubic:
                            lastPoint = pathPoints[3];
                            points.Add(lastPoint);
                            break;
                        case SKPathVerb.Close:
                            if (points.Count > 0)
                            {
                                points.Add(points[0]); // 閉じたパスの場合は最初の点を追加
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

                // 保存された描画データがある場合は、それを優先して使用
                if (_savedDrawingData != null)
                {
                    // ページキャンバスの作成
                    for (int i = 0; i < _savedDrawingData.Pages.Count; i++)
                    {
                        var pageCanvas = new PageCanvas
                        {
                            PageIndex = i,
                            DrawingElements = new ObservableCollection<DrawingStroke>()
                        };
                        _pageCanvases.Add(pageCanvas);
                    }

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
                                    StrokeWidth = elementData.StrokeWidth,
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
                                            path.AddCircle(elementData.Center.X, elementData.Center.Y, elementData.Radius);
                                            break;
                                        case ShapeType.Line:
                                            path.MoveTo(elementData.StartPoint);
                                            path.LineTo(elementData.EndPoint);
                                            break;
                                        case ShapeType.Triangle:
                                        case ShapeType.Rectangle:
                                            if (elementData.Vertices.Count > 0)
                                            {
                                                path.MoveTo(elementData.Vertices[0]);
                                                for (int i = 1; i < elementData.Vertices.Count; i++)
                                                {
                                                    path.LineTo(elementData.Vertices[i]);
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
                                        path.MoveTo(elementData.Points[0]);
                                        for (int i = 1; i < elementData.Points.Count; i++)
                                        {
                                            path.LineTo(elementData.Points[i]);
                                        }
                                    }
                                }

                                var stroke = new DrawingStroke(path, paint);
                                if (elementData.IsShape)
                                {
                                    stroke.IsShape = true;
                                    stroke.ShapeType = elementData.ShapeType;
                                }

                                pageCanvas.DrawingElements.Add(stroke);
                            }
                        }
                    }

                    _savedDrawingData = null;
                }

                // PDFの読み込み
                _pdfStream = File.OpenRead(filePath);
                _pdfDocument = PdfDocument.Load(_pdfStream);
                if (_pdfDocument != null)
                {
                    _pdfPages = new List<SKBitmap>(new SKBitmap[_pdfDocument.PageCount]);

                    // ページの読み込み
                    for (int pageIndex = 0; pageIndex < _pdfDocument.PageCount; pageIndex++)
                    {
                        try
                        {
                            // 低画質と高画質の両方を読み込む
                            await LoadPdfPageAsync(pageIndex, LOW_DPI);
                            await LoadPdfPageAsync(pageIndex, HIGH_DPI);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading page {pageIndex}: {ex.Message}");
                        }
                    }
                }

                // ページキャンバスの更新
                UpdatePageCanvases();
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
            foreach (var textBox in _textBoxes)
            {
                textBox.Dispose();
            }
            _textBoxes.Clear();
        }

        private bool PathsIntersect(SKPath path1, SKPath path2)
        {
            if (path1 == null || path2 == null) return false;

            // パスのバウンディングボックスを取得
            path1.GetBounds(out var bounds1);
            path2.GetBounds(out var bounds2);

            // マージンを設定（消しゴムの太さを考慮）
            float margin = _currentEraserMode == EraserMode.Partial ? _partialEraserWidth : _eraserPaint.StrokeWidth;
            bounds1.Inflate(margin, margin);
            bounds2.Inflate(margin, margin);

            // バウンディングボックスが交差しない場合はfalse
            if (!bounds1.IntersectsWith(bounds2))
            {
                return false;
            }

            // より詳細な交差判定
            using (var measure1 = new SKPathMeasure(path1))
            using (var measure2 = new SKPathMeasure(path2))
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

        public void ShowColorMenu(SKPoint position)
        {
            _lastRightClickPoint = position;
            _isShowingContextMenu = true;
            InvalidateSurface();
        }

        public void InitializeCacheDirectory(string noteName)
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "AnkiPlus",
                $"{noteName}_temp"
            );

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

        // 消しゴムのモードを定義
        public enum EraserMode
        {
            Full,    // 一括削除
            Partial  // 部分削除
        }

        private EraserMode _currentEraserMode = EraserMode.Full;
        private float _partialEraserWidth = PARTIAL_ERASER_WIDTH_MEDIUM;

        private bool IsPointInEraserModeBox(SKPoint point, int modeIndex)
        {
            if (!_isShowingContextMenu || _currentTool != DrawingTool.Eraser) return false;

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            float y = menuRect.Top + 10 + (modeIndex * 40);
            var modeRect = new SKRect(menuRect.Left + 10, y, menuRect.Right - 10, y + 30);
            return modeRect.Contains(point);
        }

        private List<SKPath> SplitPathAtIntersection(List<SKPoint> points, SKPath eraserPath, DrawingStroke element)
        {
            var result = new List<SKPath>();
            var margin = _partialEraserWidth;

            // 図形の場合の特別処理
            if (element.IsShape)
            {
                switch (element.ShapeType)
                {
                    case ShapeType.Line:
                        // 直線の場合
                        if (points.Count >= 2)
                        {
                            var startPoint = points[0];
                            var endPoint = points[points.Count - 1];
                            var lineLength = Distance(startPoint, endPoint);
                            
                            // 消しゴムのパスと直線の交点を見つける
                            bool startIntersects = IsPointNearPath(eraserPath, startPoint, margin);
                            bool endIntersects = IsPointNearPath(eraserPath, endPoint, margin);
                            bool middleIntersects = false;
                            
                            // 直線の中間点をチェック
                            var steps = Math.Max(10, (int)(lineLength / margin));
                            for (int i = 1; i < steps - 1; i++)
                            {
                                var t = i / (float)steps;
                                var point = new SKPoint(
                                    startPoint.X + (endPoint.X - startPoint.X) * t,
                                    startPoint.Y + (endPoint.Y - startPoint.Y) * t
                                );
                                if (IsPointNearPath(eraserPath, point, margin))
                                {
                                    middleIntersects = true;
                                    break;
                                }
                            }

                            // 交点の状態に応じて分割
                            if (!startIntersects && !endIntersects && !middleIntersects)
                            {
                                // 交点なし - 元の直線をそのまま返す
                                var path = new SKPath();
                                path.MoveTo(startPoint);
                                path.LineTo(endPoint);
                                result.Add(path);
                            }
                            else if (startIntersects && endIntersects)
                            {
                                // 両端が交差 - 直線を削除
                            }
                            else if (startIntersects || endIntersects || middleIntersects)
                            {
                                // 部分的に交差 - 最も近い非交差点を見つけて分割
                                var nonIntersectingPoints = new List<SKPoint>();
                                for (int i = 0; i <= steps; i++)
                                {
                                    var t = i / (float)steps;
                                    var point = new SKPoint(
                                        startPoint.X + (endPoint.X - startPoint.X) * t,
                                        startPoint.Y + (endPoint.Y - startPoint.Y) * t
                                    );
                                    if (!IsPointNearPath(eraserPath, point, margin))
                                    {
                                        nonIntersectingPoints.Add(point);
                                    }
                                }

                                // 非交差点から新しい線分を作成
                                for (int i = 0; i < nonIntersectingPoints.Count - 1; i++)
                                {
                                    if (Distance(nonIntersectingPoints[i], nonIntersectingPoints[i + 1]) > margin)
                                    {
                                        var path = new SKPath();
                                        path.MoveTo(nonIntersectingPoints[i]);
                                        path.LineTo(nonIntersectingPoints[i + 1]);
                                        result.Add(path);
                                    }
                                }
                            }
                        }
                        break;

                    // 他の図形タイプの処理（必要に応じて追加）
                    default:
                        // 通常の処理を適用
                        var currentPath = new SKPath();
                        var isErasing = false;
                        var lastPoint = SKPoint.Empty;

                        for (int i = 0; i < points.Count; i++)
                        {
                            var point = points[i];
                            var isIntersecting = IsPointNearPath(eraserPath, point, margin);

                            if (!isIntersecting)
                            {
                                if (!isErasing)
                                {
                                    if (currentPath.PointCount == 0)
                                    {
                                        currentPath.MoveTo(point);
                                    }
                                    else
                                    {
                                        currentPath.LineTo(point);
                                    }
                                }
                                else
                                {
                                    if (currentPath.PointCount > 0)
                                    {
                                        result.Add(currentPath);
                                        currentPath = new SKPath();
                                        currentPath.MoveTo(point);
                                    }
                                    isErasing = false;
                                }
                            }
                            else
                            {
                                if (!isErasing)
                                {
                                    if (currentPath.PointCount > 0)
                                    {
                                        result.Add(currentPath);
                                        currentPath = new SKPath();
                                    }
                                    isErasing = true;
                                }
                            }
                            lastPoint = point;
                        }

                        if (currentPath.PointCount > 0 && !isErasing)
                        {
                            result.Add(currentPath);
                        }
                        break;
                }
            }
            else
            {
                // 通常の描画要素の処理
                var currentPath = new SKPath();
                var isErasing = false;
                var lastPoint = SKPoint.Empty;

                // 短い線分の場合の特別処理
                if (points.Count == 2 && Distance(points[0], points[1]) < margin * 2)
                {
                    // 線分の両端のいずれかが消しゴムと交差していれば削除
                    if (IsPointNearPath(eraserPath, points[0], margin) || 
                        IsPointNearPath(eraserPath, points[1], margin))
                    {
                        return result; // 空のリストを返して削除
                    }
                    else
                    {
                        // 交差していない場合は保持
                        currentPath.MoveTo(points[0]);
                        currentPath.LineTo(points[1]);
                        result.Add(currentPath);
                        return result;
                    }
                }

                for (int i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    var isIntersecting = IsPointNearPath(eraserPath, point, margin);

                    if (!isIntersecting)
                    {
                        if (!isErasing)
                        {
                            if (currentPath.PointCount == 0)
                            {
                                currentPath.MoveTo(point);
                            }
                            else
                            {
                                currentPath.LineTo(point);
                            }
                        }
                        else
                        {
                            if (currentPath.PointCount > 0)
                            {
                                result.Add(currentPath);
                                currentPath = new SKPath();
                                currentPath.MoveTo(point);
                            }
                            isErasing = false;
                        }
                    }
                    else
                    {
                        if (!isErasing)
                        {
                            if (currentPath.PointCount > 0)
                            {
                                result.Add(currentPath);
                                currentPath = new SKPath();
                            }
                            isErasing = true;
                        }
                    }
                    lastPoint = point;
                }

                if (currentPath.PointCount > 0 && !isErasing)
                {
                    result.Add(currentPath);
                }
            }

            return result;
        }

        public void SetPartialEraserWidth(string size)
        {
            switch (size.ToLower())
            {
                case "small":
                    _partialEraserWidth = PARTIAL_ERASER_WIDTH_SMALL;
                    break;
                case "medium":
                    _partialEraserWidth = PARTIAL_ERASER_WIDTH_MEDIUM;
                    break;
                case "large":
                    _partialEraserWidth = PARTIAL_ERASER_WIDTH_LARGE;
                    break;
            }
            InvalidateSurface();
        }

        public void CyclePartialEraserWidth()
        {
            if (_partialEraserWidth == PARTIAL_ERASER_WIDTH_SMALL)
            {
                _partialEraserWidth = PARTIAL_ERASER_WIDTH_MEDIUM;
            }
            else if (_partialEraserWidth == PARTIAL_ERASER_WIDTH_MEDIUM)
            {
                _partialEraserWidth = PARTIAL_ERASER_WIDTH_LARGE;
            }
            else
            {
                _partialEraserWidth = PARTIAL_ERASER_WIDTH_SMALL;
            }
            InvalidateSurface();
        }

        private bool IsPointInPartialEraserWidthButton(SKPoint point, int buttonIndex)
        {
            if (!_isShowingContextMenu || _currentTool != DrawingTool.Eraser || _currentEraserMode != EraserMode.Partial)
                return false;

            var menuRect = new SKRect(
                _lastRightClickPoint.X,
                _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH,
                _lastRightClickPoint.Y + CONTEXT_MENU_HEIGHT
            );

            float y = menuRect.Top + 90; // 部分削除ボタンの下の位置
            var buttonWidth = (menuRect.Width - 40) / 3;
            var buttonRect = new SKRect(
                menuRect.Left + 10 + (buttonIndex * (buttonWidth + 5)),
                y,
                menuRect.Left + 10 + (buttonIndex * (buttonWidth + 5)) + buttonWidth,
                y + 30
            );

            return buttonRect.Contains(point);
        }

        public class TextBox
        {
            public SKRect Bounds { get; set; }
            public string Text { get; set; }
            public SKColor Color { get; set; }
            public float FontSize { get; set; }
            public bool IsEditing { get; set; }
            public SKPaint Paint { get; set; }

            public TextBox(SKRect bounds, SKColor color, float fontSize)
            {
                Bounds = bounds;
                Text = "";
                Color = color;
                FontSize = fontSize;
                IsEditing = true;
                Paint = new SKPaint
                {
                    Color = color,
                    TextSize = fontSize,
                    IsAntialias = true
                };
            }

            public void Dispose()
            {
                Paint?.Dispose();
            }
        }

        private TextBox _currentTextBox;
        private List<TextBox> _textBoxes = new List<TextBox>();
        private const float DEFAULT_FONT_SIZE = 20f;
        private const float TEXT_BOX_PADDING = 10f;

        public void AddTextBox(SKPoint position)
        {
            var bounds = new SKRect(
                position.X,
                position.Y,
                position.X + 200, // デフォルトの幅
                position.Y + 50   // デフォルトの高さ
            );

            _currentTextBox = new TextBox(bounds, _currentPaint.Color, DEFAULT_FONT_SIZE);
            _textBoxes.Add(_currentTextBox);
            InvalidateSurface();
        }

        public void UpdateTextBoxText(string text)
        {
            if (_currentTextBox != null)
            {
                _currentTextBox.Text = text;
                InvalidateSurface();
            }
        }

        public void FinishTextBoxEditing()
        {
            if (_currentTextBox != null)
            {
                _currentTextBox.IsEditing = false;
                _currentTextBox = null;
                InvalidateSurface();
            }
        }

        private bool IsPointInTextBox(SKPoint point, TextBox textBox)
        {
            return textBox.Bounds.Contains(point);
        }
    }
} 