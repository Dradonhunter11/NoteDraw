using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Available interactive tools on the vector drawing canvas.
    /// Maintainers: Add new tool kinds here and handle them in DrawingCanvasControl & DrawingWorkspaceControl.
    /// </summary>
    public enum DrawingToolKind
    {
        Pen,
        Highlighter,
        Eraser,
        Pipette,
        Line,
        Arrow,
        Rectangle,
        Ellipse,
        TextLabel,
        SelectPan,
        MovePosition,
        SelectArea,
        MoveSelection,
        ResizeSelection,
        Image
    }

    /// <summary>
    /// Supported paper/grid backgrounds for drawing canvases.
    /// </summary>
    public enum CanvasBackgroundKind
    {
        Grid,
        Dotted,
        Ruled,
        Blank
    }

    /// <summary>
    /// Single point along a vector stroke in logical canvas coordinates.
    /// </summary>
    public sealed class DrawingPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Pressure { get; set; } = 0.5;

        public DrawingPoint() { }

        public DrawingPoint(double x, double y, double pressure = 0.5)
        {
            X = x;
            Y = y;
            Pressure = pressure;
        }

        public DrawingPoint Clone() => new(X, Y, Pressure);
    }

    /// <summary>
    /// Serializable logical clipping rectangle restricting where a stroke renders.
    /// </summary>
    public sealed class DrawingRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public DrawingRect() { }

        public DrawingRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public DrawingRect Clone() => new(X, Y, Width, Height);

        public Rect ToAvaloniaRect() => new(X, Y, Math.Max(0, Width), Math.Max(0, Height));
    }

    /// <summary>
    /// Single vector stroke, geometric shape, or text/LaTeX annotation stored in a <see cref="DrawingDocument"/>.
    /// </summary>
    public sealed class DrawingStroke
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DrawingToolKind Tool { get; set; } = DrawingToolKind.Pen;
        public string ColorHex { get; set; } = "#2563EB";
        public double Thickness { get; set; } = 3.0;
        public double Opacity { get; set; } = 1.0;
        public List<DrawingPoint> Points { get; set; } = new();
        public string? TextContent { get; set; }
        public bool IsLatex { get; set; }
        public string? ImageBase64 { get; set; }
        public DrawingRect? ClipRect { get; set; }

        public DrawingStroke Clone()
        {
            var copy = new DrawingStroke
            {
                Id = Id,
                Tool = Tool,
                ColorHex = ColorHex,
                Thickness = Thickness,
                Opacity = Opacity,
                TextContent = TextContent,
                IsLatex = IsLatex,
                ImageBase64 = ImageBase64,
                ClipRect = ClipRect?.Clone(),
                Points = new List<DrawingPoint>(Points.Count)
            };
            foreach (var pt in Points)
                copy.Points.Add(pt.Clone());
            return copy;
        }
    }

    /// <summary>
    /// Serializable vector drawing document shared by standalone `.ndraw` files
    /// and inline ```drawing Markdown blocks.
    /// </summary>
    public sealed class DrawingDocument
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        public CanvasBackgroundKind Background { get; set; } = CanvasBackgroundKind.Grid;
        public double Width { get; set; } = 1200;
        public double Height { get; set; } = 720;
        public List<DrawingStroke> Strokes { get; set; } = new();

        /// <summary>
        /// Computes the tight bounding rectangle (with padding) enclosing all drawn content.
        /// Used to crop document previews, copied images, and fit canvas size to used content.
        /// </summary>
        public Rect GetUsedBounds(double padding = 20.0)
        {
            double pad = Math.Max(0, padding);
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;

            void ExpandForStrokes(bool includeEraser)
            {
                foreach (var stroke in Strokes)
                {
                    if (stroke.Points.Count == 0)
                        continue;
                    if (!includeEraser && stroke.Tool == DrawingToolKind.Eraser)
                        continue;

                    if (stroke.Tool == DrawingToolKind.TextLabel)
                    {
                        var p = stroke.Points[0];
                        string text = stroke.TextContent ?? string.Empty;
                        double estW = stroke.IsLatex
                            ? Math.Max(64.0, text.Length * 11.0 + 24.0)
                            : Math.Max(48.0, text.Length * 9.5 + 16.0);
                        double estH = stroke.IsLatex ? 44.0 : 28.0;

                        double tMinX = p.X - 6.0;
                        double tMinY = p.Y - 4.0;
                        double tMaxX = p.X + estW;
                        double tMaxY = p.Y + estH;
                        if (stroke.ClipRect is { Width: > 0, Height: > 0 } tClip)
                        {
                            tMinX = Math.Max(tMinX, tClip.X);
                            tMinY = Math.Max(tMinY, tClip.Y);
                            tMaxX = Math.Min(tMaxX, tClip.X + tClip.Width);
                            tMaxY = Math.Min(tMaxY, tClip.Y + tClip.Height);
                            if (tMaxX <= tMinX || tMaxY <= tMinY)
                                continue;
                        }

                        minX = Math.Min(minX, tMinX);
                        minY = Math.Min(minY, tMinY);
                        maxX = Math.Max(maxX, tMaxX);
                        maxY = Math.Max(maxY, tMaxY);
                        continue;
                    }

                    double strokeExpand = stroke.Tool switch
                    {
                        DrawingToolKind.Image => 0.0,
                        DrawingToolKind.Highlighter => Math.Max(6.0, stroke.Thickness * 1.5),
                        DrawingToolKind.Arrow => Math.Max(Math.Max(2.0, stroke.Thickness / 2.0), Math.Max(11.0, stroke.Thickness * 3.4) * 0.6),
                        _ => Math.Max(2.0, stroke.Thickness / 2.0)
                    };

                    double sMinX = double.PositiveInfinity;
                    double sMinY = double.PositiveInfinity;
                    double sMaxX = double.NegativeInfinity;
                    double sMaxY = double.NegativeInfinity;
                    foreach (var pt in stroke.Points)
                    {
                        sMinX = Math.Min(sMinX, pt.X - strokeExpand);
                        sMinY = Math.Min(sMinY, pt.Y - strokeExpand);
                        sMaxX = Math.Max(sMaxX, pt.X + strokeExpand);
                        sMaxY = Math.Max(sMaxY, pt.Y + strokeExpand);
                    }

                    if (stroke.ClipRect is { Width: > 0, Height: > 0 } sClip)
                    {
                        sMinX = Math.Max(sMinX, sClip.X);
                        sMinY = Math.Max(sMinY, sClip.Y);
                        sMaxX = Math.Min(sMaxX, sClip.X + sClip.Width);
                        sMaxY = Math.Min(sMaxY, sClip.Y + sClip.Height);
                        if (sMaxX <= sMinX || sMaxY <= sMinY)
                            continue;
                    }

                    minX = Math.Min(minX, sMinX);
                    minY = Math.Min(minY, sMinY);
                    maxX = Math.Max(maxX, sMaxX);
                    maxY = Math.Max(maxY, sMaxY);
                }
            }

            ExpandForStrokes(includeEraser: false);
            if (double.IsPositiveInfinity(minX))
                ExpandForStrokes(includeEraser: true);

            if (double.IsPositiveInfinity(minX))
            {
                double emptyW = Math.Clamp(Width > 0 ? Width : 400.0, 160.0, 420.0);
                double emptyH = Math.Clamp(Height > 0 ? Height : 220.0, 100.0, 240.0);
                return new Rect(0, 0, emptyW, emptyH);
            }

            double x = minX >= 0 ? Math.Max(0, Math.Floor(minX - pad)) : Math.Floor(minX - pad);
            double y = minY >= 0 ? Math.Max(0, Math.Floor(minY - pad)) : Math.Floor(minY - pad);
            double right = Math.Ceiling(maxX + pad);
            double bottom = Math.Ceiling(maxY + pad);
            double w = Math.Max(80.0, right - x);
            double h = Math.Max(60.0, bottom - y);
            return new Rect(x, y, w, h);
        }

        public DrawingDocument Clone()
        {
            var copy = new DrawingDocument
            {
                Background = Background,
                Width = Width,
                Height = Height,
                Strokes = new List<DrawingStroke>(Strokes.Count)
            };
            foreach (var stroke in Strokes)
                copy.Strokes.Add(stroke.Clone());
            return copy;
        }

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static DrawingDocument FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new DrawingDocument();

            try
            {
                return JsonSerializer.Deserialize<DrawingDocument>(json, JsonOptions) ?? new DrawingDocument();
            }
            catch
            {
                return new DrawingDocument();
            }
        }

        /// <summary>
        /// Exports the vector drawing to standard SVG markup.
        /// </summary>
        public string ExportToSvg(bool isDarkTheme = false)
        {
            var sb = new StringBuilder();
            string bgFill = "#FFFFFF";
            string defaultInk = "#0F172A";
            double w = Math.Max(400, Width);
            double h = Math.Max(300, Height);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">",
                w, h));

            var defs = new StringBuilder();
            var body = new StringBuilder();
            body.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{bgFill}\" />");

            int clipIdx = 0;
            foreach (var s in Strokes)
            {
                if (s.Points.Count == 0) continue;
                string strokeColor = string.Equals(s.ColorHex, "AUTO", StringComparison.OrdinalIgnoreCase)
                    ? defaultInk
                    : s.ColorHex;
                string op = s.Opacity.ToString("0.##", CultureInfo.InvariantCulture);
                string sw = s.Thickness.ToString("0.##", CultureInfo.InvariantCulture);

                bool hasClip = s.ClipRect is { Width: > 0, Height: > 0 };
                if (hasClip && s.ClipRect is { } cr)
                {
                    string clipId = $"clip_{clipIdx++}";
                    if (defs.Length == 0)
                        defs.AppendLine("  <defs>");
                    defs.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    <clipPath id=\"{0}\"><rect x=\"{1:0.##}\" y=\"{2:0.##}\" width=\"{3:0.##}\" height=\"{4:0.##}\" /></clipPath>",
                        clipId, cr.X, cr.Y, cr.Width, cr.Height));
                    body.AppendLine($"  <g clip-path=\"url(#{clipId})\">");
                }

                switch (s.Tool)
                {
                    case DrawingToolKind.Pen:
                    case DrawingToolKind.Highlighter:
                    case DrawingToolKind.Eraser:
                    {
                        string pathColor = s.Tool == DrawingToolKind.Eraser ? bgFill : strokeColor;
                        string pathOpacity = s.Tool == DrawingToolKind.Eraser ? "1" : op;
                        if (s.Points.Count == 1)
                        {
                            var p = s.Points[0];
                            body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "  <circle cx=\"{0:0.##}\" cy=\"{1:0.##}\" r=\"{2:0.##}\" fill=\"{3}\" fill-opacity=\"{4}\" />",
                                p.X, p.Y, s.Thickness / 2.0, pathColor, pathOpacity));
                        }
                        else
                        {
                            var path = new StringBuilder();
                            path.AppendFormat(CultureInfo.InvariantCulture, "M {0:0.##} {1:0.##}", s.Points[0].X, s.Points[0].Y);
                            for (int i = 1; i < s.Points.Count; i++)
                                path.AppendFormat(CultureInfo.InvariantCulture, " L {0:0.##} {1:0.##}", s.Points[i].X, s.Points[i].Y);

                            body.AppendLine(
                                $"  <path d=\"{path}\" fill=\"none\" stroke=\"{pathColor}\" stroke-width=\"{sw}\" stroke-opacity=\"{pathOpacity}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
                        }
                        break;
                    }

                    case DrawingToolKind.Line:
                    case DrawingToolKind.Arrow:
                    {
                        var p0 = s.Points[0];
                        var p1 = s.Points[^1];
                        body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  <line x1=\"{0:0.##}\" y1=\"{1:0.##}\" x2=\"{2:0.##}\" y2=\"{3:0.##}\" stroke=\"{4}\" stroke-width=\"{5}\" stroke-opacity=\"{6}\" stroke-linecap=\"round\" />",
                            p0.X, p0.Y, p1.X, p1.Y, strokeColor, sw, op));

                        if (s.Tool == DrawingToolKind.Arrow)
                        {
                            double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
                            double headLen = Math.Max(10.0, s.Thickness * 3.2);
                            double a1 = angle - Math.PI / 6.0;
                            double a2 = angle + Math.PI / 6.0;
                            double hx1 = p1.X - headLen * Math.Cos(a1);
                            double hy1 = p1.Y - headLen * Math.Sin(a1);
                            double hx2 = p1.X - headLen * Math.Cos(a2);
                            double hy2 = p1.Y - headLen * Math.Sin(a2);
                            body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "  <polyline points=\"{0:0.##},{1:0.##} {2:0.##},{3:0.##} {4:0.##},{5:0.##}\" fill=\"none\" stroke=\"{6}\" stroke-width=\"{7}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />",
                                hx1, hy1, p1.X, p1.Y, hx2, hy2, strokeColor, sw));
                        }
                        break;
                    }

                    case DrawingToolKind.Rectangle:
                    {
                        var p0 = s.Points[0];
                        var p1 = s.Points[^1];
                        double x = Math.Min(p0.X, p1.X);
                        double y = Math.Min(p0.Y, p1.Y);
                        double rw = Math.Abs(p1.X - p0.X);
                        double rh = Math.Abs(p1.Y - p0.Y);
                        body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  <rect x=\"{0:0.##}\" y=\"{1:0.##}\" width=\"{2:0.##}\" height=\"{3:0.##}\" fill=\"none\" stroke=\"{4}\" stroke-width=\"{5}\" stroke-opacity=\"{6}\" rx=\"4\" />",
                            x, y, rw, rh, strokeColor, sw, op));
                        break;
                    }

                    case DrawingToolKind.Ellipse:
                    {
                        var p0 = s.Points[0];
                        var p1 = s.Points[^1];
                        double cx = (p0.X + p1.X) / 2.0;
                        double cy = (p0.Y + p1.Y) / 2.0;
                        double rx = Math.Abs(p1.X - p0.X) / 2.0;
                        double ry = Math.Abs(p1.Y - p0.Y) / 2.0;
                        body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  <ellipse cx=\"{0:0.##}\" cy=\"{1:0.##}\" rx=\"{2:0.##}\" ry=\"{3:0.##}\" fill=\"none\" stroke=\"{4}\" stroke-width=\"{5}\" stroke-opacity=\"{6}\" />",
                            cx, cy, rx, ry, strokeColor, sw, op));
                        break;
                    }

                    case DrawingToolKind.TextLabel:
                    {
                        var p = s.Points[0];
                        string escaped = System.Security.SecurityElement.Escape(s.TextContent ?? string.Empty) ?? string.Empty;
                        body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  <text x=\"{0:0.##}\" y=\"{1:0.##}\" fill=\"{2}\" font-size=\"16\" font-family=\"sans-serif\">{3}</text>",
                            p.X, p.Y, strokeColor, escaped));
                        break;
                    }

                    case DrawingToolKind.Image:
                    {
                        if (s.Points.Count >= 2 && !string.IsNullOrWhiteSpace(s.ImageBase64))
                        {
                            var p0 = s.Points[0];
                            var p1 = s.Points[^1];
                            double x = Math.Min(p0.X, p1.X);
                            double y = Math.Min(p0.Y, p1.Y);
                            double rw = Math.Max(1.0, Math.Abs(p1.X - p0.X));
                            double rh = Math.Max(1.0, Math.Abs(p1.Y - p0.Y));
                            body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "  <image x=\"{0:0.##}\" y=\"{1:0.##}\" width=\"{2:0.##}\" height=\"{3:0.##}\" href=\"data:image/png;base64,{4}\" preserveAspectRatio=\"none\" />",
                                x, y, rw, rh, s.ImageBase64));
                        }
                        break;
                    }
                }

                if (hasClip)
                    body.AppendLine("  </g>");
            }

            if (defs.Length > 0)
            {
                defs.AppendLine("  </defs>");
                sb.Append(defs);
            }
            sb.Append(body);
            sb.AppendLine("</svg>");
            return sb.ToString();
        }
    }
}
