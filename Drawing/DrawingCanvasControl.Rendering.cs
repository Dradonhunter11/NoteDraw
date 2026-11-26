using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NoteDraw.Drawing.Commands;
using NoteDraw.Drawing.Tools;
using NoteDraw.Rendering.Latex;
using NoteDraw.Services;

namespace NoteDraw.Drawing
{
    public sealed partial class DrawingCanvasControl
    {
        public void ApplyPaletteColors(string? canvasBackgroundHex, string? gridColorHex)
        {
            _customCanvasBgHex = canvasBackgroundHex;
            _customGridColorHex = gridColorHex;
            InvalidateVisual();
        }

        private Color GetEffectiveCanvasBackgroundColor()
            => Color.TryParse(_customCanvasBgHex, out var customBg) ? customBg : Colors.White;

        private bool IsCanvasBackgroundLight()
        {
            var c = GetEffectiveCanvasBackgroundColor();
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128.0;
        }

        private bool HasEraserStrokes()
        {
            if (_activeStroke?.Tool == DrawingToolKind.Eraser)
                return true;
            foreach (var s in _document.Strokes)
            {
                if (s.Tool == DrawingToolKind.Eraser)
                    return true;
            }
            return false;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            var bgColor = GetEffectiveCanvasBackgroundColor();
            context.FillRectangle(new SolidColorBrush(bgColor), bounds);

            DrawBackgroundPattern(context, bounds, patternOpacity: 1.0);

            var transform = Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(PanOffset.X, PanOffset.Y);
            using (context.PushTransform(transform))
            {
                foreach (var stroke in _document.Strokes)
                    DrawStroke(context, stroke);

                if (_activeStroke is not null)
                    DrawStroke(context, _activeStroke);

                DrawSelectionOverlay(context);
            }

            if (_document.Background != CanvasBackgroundKind.Blank && HasEraserStrokes())
                DrawBackgroundPattern(context, bounds, patternOpacity: 0.45);

            if (_hoverScreenPos.HasValue)
                DrawMousePreview(context, bounds, _hoverScreenPos.Value);
        }

        private void DrawSelectionOverlay(DrawingContext context)
        {
            Rect? overlayRect = _draftSelectionArea ?? SelectionArea;
            if (overlayRect is not { } sel)
                return;

            double invScale = 1.0 / Math.Max(0.25, Zoom);
            if (_draftSelectionArea is null && sel.Width >= 5.0 && sel.Height >= 5.0)
            {
                double docW = Math.Max(_document.Width > 0 ? _document.Width : 1200, sel.Right + 200);
                double docH = Math.Max(_document.Height > 0 ? _document.Height : 720, sel.Bottom + 200);
                var outsideTint = new SolidColorBrush(Color.FromArgb(34, 15, 23, 42));
                if (sel.Top > 0)
                    context.FillRectangle(outsideTint, new Rect(0, 0, docW, sel.Top));
                if (sel.Bottom < docH)
                    context.FillRectangle(outsideTint, new Rect(0, sel.Bottom, docW, docH - sel.Bottom));
                if (sel.Left > 0)
                    context.FillRectangle(outsideTint, new Rect(0, sel.Top, sel.Left, sel.Height));
                if (sel.Right < docW)
                    context.FillRectangle(outsideTint, new Rect(sel.Right, sel.Top, docW - sel.Right, sel.Height));
            }

            var haloPen = new Pen(Brushes.White, 2.0 * invScale);
            var dashPen = new Pen(new SolidColorBrush(Color.Parse("#0078D4")), 1.6 * invScale, new DashStyle(new[] { 5.0, 4.0 }, 0));
            context.DrawRectangle(null, haloPen, sel);
            context.DrawRectangle(null, dashPen, sel);

            if (_draftSelectionArea is null && ActiveTool == DrawingToolKind.ResizeSelection)
            {
                double hs = 8.0 * invScale;
                double half = hs / 2.0;
                var handleFill = Brushes.White;
                var handlePen = new Pen(new SolidColorBrush(Color.Parse("#0078D4")), 1.5 * invScale);
                foreach (var pt in ResizeSelectionTool.GetSelectionHandleCenters(sel))
                {
                    context.DrawRectangle(handleFill, handlePen, new Rect(pt.X - half, pt.Y - half, hs, hs));
                }
            }
        }

        private void DrawMousePreview(DrawingContext context, Rect bounds, Point sp)
        {
            if (!bounds.Contains(sp))
                return;

            Point cp = ScreenToCanvas(sp);
            string readoutText;
            if (ActiveTool == DrawingToolKind.Pipette)
            {
                string sampledHex = SampleColorHexAtCanvasPoint(cp);
                var sampledColor = Color.TryParse(sampledHex, out var sc) ? sc : Colors.White;
                var outerRing = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1.6);
                var innerRing = new Pen(Brushes.White, 1.2);
                context.DrawEllipse(new SolidColorBrush(sampledColor), outerRing, sp, 8.0, 8.0);
                context.DrawEllipse(null, innerRing, sp, 6.4, 6.4);
                var crossPen = new Pen(new SolidColorBrush(Color.Parse("#0F172A")), 1.0);
                context.DrawLine(crossPen, new Point(sp.X - 4, sp.Y), new Point(sp.X + 4, sp.Y));
                context.DrawLine(crossPen, new Point(sp.X, sp.Y - 4), new Point(sp.X, sp.Y + 4));
                readoutText = $"Pipette ({sampledHex}) • ({cp.X:0}, {cp.Y:0}) • {Math.Round(Zoom * 100):0}%";
            }
            else if (ActiveTool is DrawingToolKind.Pen or DrawingToolKind.Highlighter or DrawingToolKind.Eraser)
            {
                double diameter = ActiveTool switch
                {
                    DrawingToolKind.Highlighter => Math.Max(12.0, ActiveThickness * 3.0),
                    DrawingToolKind.Eraser => Math.Max(6.0, ActiveThickness * 2.0),
                    _ => ActiveThickness
                };
                double r = Math.Max(3.0, (diameter * Zoom) / 2.0);

                if (ActiveTool == DrawingToolKind.Eraser)
                {
                    var eraserFill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                    var outerRing = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1.4);
                    var innerRing = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1.0);
                    context.DrawEllipse(eraserFill, outerRing, sp, r, r);
                    if (r > 2.5)
                        context.DrawEllipse(null, innerRing, sp, r - 1.2, r - 1.2);
                }
                else
                {
                    var resolved = ResolveStrokeColor(ActiveColorHex);
                    var fill = ResolveStrokeBrush(ActiveColorHex, 0.28);
                    var ring = new Pen(ResolveStrokeBrush(ActiveColorHex, 0.95), 1.4);
                    context.DrawEllipse(fill, ring, sp, r, r);
                    if (IsCanvasBackgroundLight() && (resolved.R > 220 && resolved.G > 220 && resolved.B > 220))
                        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#475569")), 1.0), sp, r + 1.0, r + 1.0);
                    context.DrawEllipse(ResolveStrokeBrush(ActiveColorHex, 0.95), null, sp, 1.5, 1.5);
                }

                readoutText = $"{ActiveTool} • ({cp.X:0}, {cp.Y:0}) • {ActiveThickness:0.#}px • {Math.Round(Zoom * 100):0}%";
            }
            else
            {
                var ringBrush = ResolveStrokeBrush(ActiveColorHex, 0.9);
                var crossPen = new Pen(new SolidColorBrush(Color.Parse("#475569")), 1.1);
                context.DrawLine(crossPen, new Point(sp.X - 8, sp.Y), new Point(sp.X + 8, sp.Y));
                context.DrawLine(crossPen, new Point(sp.X, sp.Y - 8), new Point(sp.X, sp.Y + 8));
                context.DrawEllipse(null, new Pen(ringBrush, 1.4), sp, 3.0, 3.0);
                readoutText = $"{ActiveTool} • ({cp.X:0}, {cp.Y:0}) • {ActiveThickness:0.#}px • {Math.Round(Zoom * 100):0}%";
            }
            var ft = new FormattedText(
                readoutText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
                11.0,
                new SolidColorBrush(Color.Parse("#0F172A")));
            double pillW = ft.Width + 14;
            double pillH = ft.Height + 6;
            if (bounds.Width > pillW + 16 && bounds.Height > pillH + 16)
            {
                var pillRect = new Rect(bounds.Right - pillW - 10, bounds.Bottom - pillH - 10, pillW, pillH);
                context.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(220, 241, 245, 249)),
                    new Pen(new SolidColorBrush(Color.Parse("#CBD5E1")), 1.0),
                    pillRect, 4, 4);
                context.DrawText(ft, new Point(pillRect.X + 7, pillRect.Y + 3));
            }
        }

        private void DrawBackgroundPattern(DrawingContext context, Rect bounds, double patternOpacity = 1.0)
        {
            if (_document.Background == CanvasBackgroundKind.Blank)
                return;

            double step = Math.Max(10.0, 24.0 * Zoom);
            double startX = ((PanOffset.X % step) + step) % step;
            double startY = ((PanOffset.Y % step) + step) % step;

            bool lightCanvas = IsCanvasBackgroundLight();
            var fallbackGrid = lightCanvas ? "#E5E5E5" : "#2E2E2E";
            var gridColor = Color.TryParse(_customGridColorHex, out var customGrid)
                ? customGrid
                : Color.Parse(fallbackGrid);
            byte lineAlpha = (byte)Math.Clamp((int)Math.Round(gridColor.A * patternOpacity), 0, 255);
            var lineBrush = new SolidColorBrush(Color.FromArgb(lineAlpha, gridColor.R, gridColor.G, gridColor.B));
            var pen = new Pen(lineBrush, 1.0);

            switch (_document.Background)
            {
                case CanvasBackgroundKind.Grid:
                    for (double x = startX; x < bounds.Width; x += step)
                        context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
                    for (double y = startY; y < bounds.Height; y += step)
                        context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
                    break;

                case CanvasBackgroundKind.Dotted:
                {
                    var dotBase = Color.Parse(lightCanvas ? "#C8C8C8" : "#444444");
                    byte dotAlpha = (byte)Math.Clamp((int)Math.Round(dotBase.A * patternOpacity), 0, 255);
                    var dotBrush = new SolidColorBrush(Color.FromArgb(dotAlpha, dotBase.R, dotBase.G, dotBase.B));
                    double r = Math.Max(1.0, 1.3 * Zoom);
                    for (double x = startX; x < bounds.Width; x += step)
                    {
                        for (double y = startY; y < bounds.Height; y += step)
                            context.DrawEllipse(dotBrush, null, new Point(x, y), r, r);
                    }
                    break;
                }

                case CanvasBackgroundKind.Ruled:
                {
                    for (double y = startY; y < bounds.Height; y += step)
                        context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
                    double marginX = PanOffset.X + 64.0 * Zoom;
                    if (marginX >= 0 && marginX <= bounds.Width)
                    {
                        var marginBase = Color.Parse(lightCanvas ? "#FCA5A5" : "#4C1D2A");
                        byte marginAlpha = (byte)Math.Clamp((int)Math.Round(marginBase.A * patternOpacity), 0, 255);
                        var marginPen = new Pen(new SolidColorBrush(Color.FromArgb(marginAlpha, marginBase.R, marginBase.G, marginBase.B)), 1.2);
                        context.DrawLine(marginPen, new Point(marginX, 0), new Point(marginX, bounds.Height));
                    }
                    break;
                }
            }
        }

        private void DrawStroke(DrawingContext context, DrawingStroke stroke)
        {
            if (stroke.Points.Count == 0)
                return;

            if (stroke.ClipRect is { Width: > 0, Height: > 0 } clip)
            {
                using (context.PushClip(clip.ToAvaloniaRect()))
                    DrawStrokeCore(context, stroke);
                return;
            }

            DrawStrokeCore(context, stroke);
        }

        private void DrawStrokeCore(DrawingContext context, DrawingStroke stroke)
        {
            var brush = stroke.Tool == DrawingToolKind.Eraser
                ? new SolidColorBrush(GetEffectiveCanvasBackgroundColor())
                : ResolveStrokeBrush(stroke.ColorHex, stroke.Opacity);
            var pen = new Pen(brush, stroke.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            switch (stroke.Tool)
            {
                case DrawingToolKind.Pen:
                case DrawingToolKind.Highlighter:
                case DrawingToolKind.Eraser:
                {
                    if (stroke.Points.Count == 1)
                    {
                        var p = stroke.Points[0];
                        double r = stroke.Thickness / 2.0;
                        context.DrawEllipse(brush, null, new Point(p.X, p.Y), r, r);
                        return;
                    }

                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(stroke.Points[0].X, stroke.Points[0].Y), false);
                        for (int i = 1; i < stroke.Points.Count - 1; i++)
                        {
                            var p1 = stroke.Points[i];
                            var p2 = stroke.Points[i + 1];
                            var mid = new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
                            g.QuadraticBezierTo(new Point(p1.X, p1.Y), mid);
                        }
                        var last = stroke.Points[^1];
                        g.LineTo(new Point(last.X, last.Y));
                        g.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geo);
                    break;
                }

                case DrawingToolKind.Line:
                case DrawingToolKind.Arrow:
                {
                    var p0 = new Point(stroke.Points[0].X, stroke.Points[0].Y);
                    var p1 = new Point(stroke.Points[^1].X, stroke.Points[^1].Y);
                    context.DrawLine(pen, p0, p1);
                    if (stroke.Tool == DrawingToolKind.Arrow && (Math.Abs(p1.X - p0.X) > 2 || Math.Abs(p1.Y - p0.Y) > 2))
                    {
                        double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
                        double headLen = Math.Max(11.0, stroke.Thickness * 3.4);
                        double wing = Math.PI / 6.0;
                        var w1 = new Point(p1.X - headLen * Math.Cos(angle - wing), p1.Y - headLen * Math.Sin(angle - wing));
                        var w2 = new Point(p1.X - headLen * Math.Cos(angle + wing), p1.Y - headLen * Math.Sin(angle + wing));
                        context.DrawLine(pen, w1, p1);
                        context.DrawLine(pen, w2, p1);
                    }
                    break;
                }

                case DrawingToolKind.Rectangle:
                {
                    var p0 = stroke.Points[0];
                    var p1 = stroke.Points[^1];
                    var rect = new Rect(
                        Math.Min(p0.X, p1.X),
                        Math.Min(p0.Y, p1.Y),
                        Math.Abs(p1.X - p0.X),
                        Math.Abs(p1.Y - p0.Y));
                    context.DrawRectangle(null, pen, rect, 4.0, 4.0);
                    break;
                }

                case DrawingToolKind.Ellipse:
                {
                    var p0 = stroke.Points[0];
                    var p1 = stroke.Points[^1];
                    var center = new Point((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0);
                    double rx = Math.Abs(p1.X - p0.X) / 2.0;
                    double ry = Math.Abs(p1.Y - p0.Y) / 2.0;
                    context.DrawEllipse(null, pen, center, rx, ry);
                    break;
                }

                case DrawingToolKind.TextLabel:
                {
                    var p = new Point(stroke.Points[0].X, stroke.Points[0].Y);
                    string text = stroke.TextContent ?? string.Empty;
                    if (stroke.IsLatex)
                    {
                        var parsed = LatexParser.Parse(text, isDisplayMode: false);
                        var box = LatexMathRenderer.Measure(parsed.Root, 18.0, brush, isDisplayMode: false);
                        var badgeRect = new Rect(p.X - 6, p.Y - 4, box.Width + 12, box.Height + 8);
                        bool darkCanvas = !IsCanvasBackgroundLight();
                        var badgeFill = new SolidColorBrush(darkCanvas ? Color.FromArgb(200, 30, 41, 59) : Color.FromArgb(220, 241, 245, 249));
                        var badgeBorder = new Pen(new SolidColorBrush(Color.Parse(darkCanvas ? "#475569" : "#94A3B8")), 1.0);
                        context.DrawRectangle(badgeFill, badgeBorder, badgeRect, 5, 5);
                        box.Draw(context, p);
                    }
                    else
                    {
                        var ft = new FormattedText(
                            text,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
                            15.0,
                            brush);
                        context.DrawText(ft, p);
                    }
                    break;
                }

                case DrawingToolKind.Image:
                {
                    if (stroke.Points.Count >= 2)
                    {
                        var bmp = GetOrDecodeImageBitmap(stroke.ImageBase64);
                        if (bmp is not null)
                        {
                            var p0 = stroke.Points[0];
                            var p1 = stroke.Points[^1];
                            var rect = new Rect(
                                Math.Min(p0.X, p1.X),
                                Math.Min(p0.Y, p1.Y),
                                Math.Max(1.0, Math.Abs(p1.X - p0.X)),
                                Math.Max(1.0, Math.Abs(p1.Y - p0.Y)));
                            context.DrawImage(bmp, rect);
                        }
                    }
                    break;
                }
            }
        }

        private Color ResolveStrokeColor(string colorHex)
        {
            bool darkCanvas = !IsCanvasBackgroundLight();
            if (string.Equals(colorHex, "AUTO", StringComparison.OrdinalIgnoreCase))
                return darkCanvas ? Color.Parse("#F1F5F9") : Color.Parse("#0F172A");

            if (Color.TryParse(colorHex, out var parsed))
                return parsed;

            return darkCanvas ? Colors.White : Color.Parse("#0F172A");
        }

        private IBrush ResolveStrokeBrush(string colorHex, double opacity)
        {
            Color color = ResolveStrokeColor(colorHex);
            byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private DrawingStroke? HitTestStroke(Point pt, double tolerance, bool includeImages = true)
        {
            for (int i = _document.Strokes.Count - 1; i >= 0; i--)
            {
                var s = _document.Strokes[i];
                if (s.Points.Count == 0) continue;

                if (s.Tool == DrawingToolKind.Image)
                {
                    if (includeImages && s.Points.Count >= 2)
                    {
                        var p0 = s.Points[0];
                        var p1 = s.Points[^1];
                        var imgRect = new Rect(
                            Math.Min(p0.X, p1.X),
                            Math.Min(p0.Y, p1.Y),
                            Math.Abs(p1.X - p0.X),
                            Math.Abs(p1.Y - p0.Y)).Inflate(tolerance);
                        if (imgRect.Contains(pt))
                            return s;
                    }
                    continue;
                }

                double r = tolerance + s.Thickness / 2.0;

                if (s.Tool == DrawingToolKind.TextLabel)
                {
                    var p = s.Points[0];
                    var rect = new Rect(p.X - 10, p.Y - 10, 140, 40);
                    if (rect.Contains(pt)) return s;
                    continue;
                }

                if (s.Tool is DrawingToolKind.Rectangle or DrawingToolKind.Ellipse)
                {
                    var p0 = s.Points[0];
                    var p1 = s.Points[^1];
                    double minX = Math.Min(p0.X, p1.X) - r;
                    double maxX = Math.Max(p0.X, p1.X) + r;
                    double minY = Math.Min(p0.Y, p1.Y) - r;
                    double maxY = Math.Max(p0.Y, p1.Y) + r;
                    if (pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY)
                    {
                        bool nearEdge = Math.Abs(pt.X - Math.Min(p0.X, p1.X)) <= r ||
                                        Math.Abs(pt.X - Math.Max(p0.X, p1.X)) <= r ||
                                        Math.Abs(pt.Y - Math.Min(p0.Y, p1.Y)) <= r ||
                                        Math.Abs(pt.Y - Math.Max(p0.Y, p1.Y)) <= r;
                        if (nearEdge) return s;
                    }
                    continue;
                }

                if (s.Points.Count == 1)
                {
                    if (Distance(pt, new Point(s.Points[0].X, s.Points[0].Y)) <= r)
                        return s;
                    continue;
                }

                for (int j = 0; j < s.Points.Count - 1; j++)
                {
                    var a = new Point(s.Points[j].X, s.Points[j].Y);
                    var b = new Point(s.Points[j + 1].X, s.Points[j + 1].Y);
                    if (DistanceToSegment(pt, a, b) <= r)
                        return s;
                }
            }
            return null;
        }


        private static void TranslateStroke(DrawingStroke stroke, double dx, double dy)
        {
            foreach (var pt in stroke.Points)
            {
                pt.X += dx;
                pt.Y += dy;
            }
            if (stroke.ClipRect is not null)
            {
                stroke.ClipRect.X += dx;
                stroke.ClipRect.Y += dy;
            }
        }

        private static bool StrokeIntersectsRect(DrawingStroke s, Rect sel)
        {
            if (s.Points.Count == 0)
                return false;

            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            double pad = s.Tool == DrawingToolKind.Image ? 0.0 : Math.Max(4.0, s.Thickness / 2.0);
            foreach (var pt in s.Points)
            {
                minX = Math.Min(minX, pt.X - pad);
                minY = Math.Min(minY, pt.Y - pad);
                maxX = Math.Max(maxX, pt.X + pad);
                maxY = Math.Max(maxY, pt.Y + pad);
            }

            var bounds = new Rect(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY));
            if (s.ClipRect is { Width: > 0, Height: > 0 } clip)
                bounds = bounds.Intersect(clip.ToAvaloniaRect());
            return bounds.Width > 0 && bounds.Height > 0 && bounds.Intersects(sel);
        }

        private static double Distance(Point a, Point b)
            => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001) return Distance(p, a);

            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0.0, 1.0);
            var proj = new Point(a.X + t * dx, a.Y + t * dy);
            return Distance(p, proj);
        }
    }
}
