using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Collections.ObjectModel;

namespace AnkiPlus_MAUI.Views
{
    public class PDFPageCanvas : SKCanvasView
    {
        private SKBitmap _pageBitmap;
        private float _scale = 1.0f;
        private float _width;
        private float _height;
        private SKPaint _currentPaint;
        private SKPath _currentPath;
        private readonly ObservableCollection<DrawingElement> _drawingElements;
        private bool _isDrawing;
        private SKPoint _lastPoint;

        public PDFPageCanvas(SKBitmap pageBitmap, float width)
        {
            _pageBitmap = pageBitmap;
            _width = width;
            _height = pageBitmap.Height * (width / pageBitmap.Width);
            HeightRequest = _height;
            WidthRequest = _width;
            EnableTouchEvents = true;
            IgnorePixelScaling = true;

            _drawingElements = new ObservableCollection<DrawingElement>();
            _currentPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Black,
                StrokeWidth = 2,
                IsAntialias = true
            };
        }

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            // アスペクト比を維持して描画
            var scale = _width / _pageBitmap.Width;
            var destWidth = _pageBitmap.Width * scale;
            var destHeight = _pageBitmap.Height * scale;
            var dest = new SKRect(0, 0, destWidth, destHeight);
            canvas.DrawBitmap(_pageBitmap, dest);

            // 保存された要素を描画
            foreach (var element in _drawingElements)
            {
                canvas.DrawPath(element.Path, element.Paint);
            }

            // 現在描画中のパスを描画
            if (_isDrawing)
            {
                canvas.DrawPath(_currentPath, _currentPaint);
            }
        }

        protected override void OnTouch(SKTouchEventArgs e)
        {
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    _isDrawing = true;
                    _currentPath = new SKPath();
                    _currentPath.MoveTo(e.Location);
                    _lastPoint = e.Location;
                    break;

                case SKTouchAction.Moved:
                    if (_isDrawing)
                    {
                        _currentPath.LineTo(e.Location);
                        _lastPoint = e.Location;
                        InvalidateSurface();
                    }
                    break;

                case SKTouchAction.Released:
                    if (_isDrawing)
                    {
                        _drawingElements.Add(new DrawingElement(_currentPath, _currentPaint));
                        _isDrawing = false;
                        InvalidateSurface();
                    }
                    break;
            }
            e.Handled = true;
        }

        public void SetScale(float scale)
        {
            _scale = scale;
            var newWidth = _width * scale;
            var newHeight = _height * scale;
            WidthRequest = newWidth;
            HeightRequest = newHeight;
            InvalidateSurface();
        }

        public void SetTool(DrawingTool tool)
        {
            _currentPaint = tool switch
            {
                DrawingTool.Pen => new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Black,
                    StrokeWidth = 2,
                    IsAntialias = true
                },
                DrawingTool.Marker => new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Yellow.WithAlpha(128),
                    StrokeWidth = 10,
                    IsAntialias = true,
                    BlendMode = SKBlendMode.SrcOver
                },
                DrawingTool.Eraser => new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.White,
                    StrokeWidth = 20,
                    IsAntialias = true
                },
                _ => _currentPaint
            };
        }

        public void Clear()
        {
            _drawingElements.Clear();
            InvalidateSurface();
        }

        public void Dispose()
        {
            _pageBitmap?.Dispose();
            _pageBitmap = null;
        }
    }

    public class DrawingElement
    {
        public SKPath Path { get; }
        public SKPaint Paint { get; }

        public DrawingElement(SKPath path, SKPaint paint)
        {
            Path = path;
            Paint = paint;
        }
    }

    public enum DrawingTool
    {
        Pen,
        Marker,
        Eraser
    }
} 