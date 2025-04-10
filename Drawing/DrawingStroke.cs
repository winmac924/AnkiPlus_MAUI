using SkiaSharp;
using System;

namespace AnkiPlus_MAUI.Drawing
{
    public class DrawingStroke : IDisposable
    {
        public SKPath DrawingPath { get; set; }
        public SKPaint DrawingPaint { get; set; }
        public bool IsMoved { get; set; }
        private SKPath _originalPath;
        private SKPath _movedPath;

        public DrawingStroke(SKPath path, SKPaint paint)
        {
            DrawingPath = path;
            DrawingPaint = paint;
            _originalPath = new SKPath(path);
            IsMoved = false;
        }

        public void MoveTo(SKPath newPath)
        {
            if (!IsMoved)
            {
                _movedPath = new SKPath(newPath);
                IsMoved = true;
            }
            DrawingPath = newPath;
        }

        public void RestoreOriginalPosition()
        {
            if (IsMoved)
            {
                DrawingPath = new SKPath(_originalPath);
                IsMoved = false;
            }
        }

        public void RestoreMovedPosition()
        {
            if (IsMoved)
            {
                DrawingPath = new SKPath(_movedPath);
            }
        }

        public void UpdatePath(SKPath newPath)
        {
            var oldPath = DrawingPath;
            DrawingPath = newPath;
            oldPath?.Dispose();
        }

        public void Dispose()
        {
            DrawingPath?.Dispose();
            DrawingPaint?.Dispose();
            DrawingPath = null;
            DrawingPaint = null;
        }
    }
} 