using System;
using System.Globalization;
using System.Text;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Exports a <see cref="DrawingDocument"/> into standalone vector SVG markup.
    /// </summary>
    public static class DrawingSvgExporter
    {
        public static string Export(DrawingDocument document, bool isDarkTheme = false)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));

            var sb = new StringBuilder();
            const string bgFill = "#FFFFFF";
            const string defaultInk = "#0F172A";
            double w = Math.Max(400, document.Width);
            double h = Math.Max(300, document.Height);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">",
                w, h));

            var defs = new StringBuilder();
            var body = new StringBuilder();
            body.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{bgFill}\" />");

            int clipIdx = 0;
            foreach (var s in document.Strokes)
            {
                if (s.Points.Count == 0)
                    continue;

                bool hasClip = s.ClipRect is { Width: > 0, Height: > 0 };
                if (hasClip && s.ClipRect is { } cr)
                    AppendClipPath(defs, body, ref clipIdx, cr);

                AppendStrokeSvg(body, s, bgFill, defaultInk);

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

        private static void AppendClipPath(StringBuilder defs, StringBuilder body, ref int clipIdx, DrawingRect cr)
        {
            string clipId = $"clip_{clipIdx++}";
            if (defs.Length == 0)
                defs.AppendLine("  <defs>");
            defs.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    <clipPath id=\"{0}\"><rect x=\"{1:0.##}\" y=\"{2:0.##}\" width=\"{3:0.##}\" height=\"{4:0.##}\" /></clipPath>",
                clipId, cr.X, cr.Y, cr.Width, cr.Height));
            body.AppendLine($"  <g clip-path=\"url(#{clipId})\">");
        }

        private static void AppendStrokeSvg(StringBuilder body, DrawingStroke s, string bgFill, string defaultInk)
        {
            string strokeColor = string.Equals(s.ColorHex, "AUTO", StringComparison.OrdinalIgnoreCase)
                ? defaultInk
                : s.ColorHex;
            string op = s.Opacity.ToString("0.##", CultureInfo.InvariantCulture);
            string sw = s.Thickness.ToString("0.##", CultureInfo.InvariantCulture);

            switch (s.Tool)
            {
                case DrawingToolKind.Pen:
                case DrawingToolKind.Highlighter:
                case DrawingToolKind.Eraser:
                    AppendFreehandSvg(body, s, bgFill, strokeColor, op, sw);
                    break;
                case DrawingToolKind.Line:
                case DrawingToolKind.Arrow:
                    AppendLineOrArrowSvg(body, s, strokeColor, op, sw);
                    break;
                case DrawingToolKind.Rectangle:
                    AppendRectangleSvg(body, s, strokeColor, op, sw);
                    break;
                case DrawingToolKind.Ellipse:
                    AppendEllipseSvg(body, s, strokeColor, op, sw);
                    break;
                case DrawingToolKind.TextLabel:
                    AppendTextLabelSvg(body, s, strokeColor);
                    break;
                case DrawingToolKind.Image:
                    AppendImageSvg(body, s);
                    break;
            }
        }

        private static void AppendFreehandSvg(StringBuilder body, DrawingStroke s, string bgFill, string strokeColor, string op, string sw)
        {
            string pathColor = s.Tool == DrawingToolKind.Eraser ? bgFill : strokeColor;
            string pathOpacity = s.Tool == DrawingToolKind.Eraser ? "1" : op;
            if (s.Points.Count == 1)
            {
                var p = s.Points[0];
                body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  <circle cx=\"{0:0.##}\" cy=\"{1:0.##}\" r=\"{2:0.##}\" fill=\"{3}\" fill-opacity=\"{4}\" />",
                    p.X, p.Y, s.Thickness / 2.0, pathColor, pathOpacity));
                return;
            }

            var path = new StringBuilder();
            path.AppendFormat(CultureInfo.InvariantCulture, "M {0:0.##} {1:0.##}", s.Points[0].X, s.Points[0].Y);
            for (int i = 1; i < s.Points.Count; i++)
                path.AppendFormat(CultureInfo.InvariantCulture, " L {0:0.##} {1:0.##}", s.Points[i].X, s.Points[i].Y);

            body.AppendLine(
                $"  <path d=\"{path}\" fill=\"none\" stroke=\"{pathColor}\" stroke-width=\"{sw}\" stroke-opacity=\"{pathOpacity}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
        }

        private static void AppendLineOrArrowSvg(StringBuilder body, DrawingStroke s, string strokeColor, string op, string sw)
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
        }

        private static void AppendRectangleSvg(StringBuilder body, DrawingStroke s, string strokeColor, string op, string sw)
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
        }

        private static void AppendEllipseSvg(StringBuilder body, DrawingStroke s, string strokeColor, string op, string sw)
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
        }

        private static void AppendTextLabelSvg(StringBuilder body, DrawingStroke s, string strokeColor)
        {
            var p = s.Points[0];
            string escaped = System.Security.SecurityElement.Escape(s.TextContent ?? string.Empty) ?? string.Empty;
            body.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  <text x=\"{0:0.##}\" y=\"{1:0.##}\" fill=\"{2}\" font-size=\"16\" font-family=\"sans-serif\">{3}</text>",
                p.X, p.Y, strokeColor, escaped));
        }

        private static void AppendImageSvg(StringBuilder body, DrawingStroke s)
        {
            if (s.Points.Count < 2 || string.IsNullOrWhiteSpace(s.ImageBase64))
                return;

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
    }
}
