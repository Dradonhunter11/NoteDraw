using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NoteDraw.Drawing.Commands;
using NoteDraw.Drawing.Tools;
using NoteDraw.Services;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// 2D Saturation (horizontal) × Value/Brightness (vertical) HSV spectrum control.
    /// </summary>
    internal sealed class ColorSpectrumPickerControl : Control
    {
        private bool _isDragging;

        public double Hue { get; private set; } = 220;
        public double Saturation { get; private set; } = 0.8;
        public double Value { get; private set; } = 0.8;

        public event EventHandler<(double H, double S, double V)>? ColorPicked;

        public ColorSpectrumPickerControl()
        {
            ClipToBounds = true;
        }

        public void SetHsv(double h, double s, double v)
        {
            Hue = ((h % 360.0) + 360.0) % 360.0;
            Saturation = Math.Clamp(s, 0.0, 1.0);
            Value = Math.Clamp(v, 0.0, 1.0);
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (pt.Properties.IsLeftButtonPressed || e.Pointer.Type is PointerType.Pen or PointerType.Touch)
            {
                e.PreventGestureRecognition();
                _isDragging = true;
                e.Pointer.Capture(this);
                PickFromPoint(pt.Position);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isDragging)
            {
                e.PreventGestureRecognition();
                PickFromPoint(e.GetPosition(this));
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        private void PickFromPoint(Point pt)
        {
            double w = Math.Max(1.0, Bounds.Width);
            double h = Math.Max(1.0, Bounds.Height);
            Saturation = Math.Clamp(pt.X / w, 0.0, 1.0);
            Value = Math.Clamp(1.0 - (pt.Y / h), 0.0, 1.0);
            InvalidateVisual();
            ColorPicked?.Invoke(this, (Hue, Saturation, Value));
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var rect = new Rect(Bounds.Size);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var pureHue = DrawingWorkspaceControl.HsvToColor(Hue, 1.0, 1.0);
            var satBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(pureHue, 1)
                }
            };
            var valBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(255, 0, 0, 0), 1)
                }
            };

            context.DrawRectangle(satBrush, null, rect, 5, 5);
            context.DrawRectangle(valBrush, new Pen(new SolidColorBrush(Color.Parse("#64748B")), 1.0), rect, 5, 5);

            var selectorPos = new Point(Saturation * rect.Width, (1.0 - Value) * rect.Height);
            context.DrawEllipse(null, new Pen(Brushes.Black, 2.2), selectorPos, 5.5, 5.5);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.4), selectorPos, 5.5, 5.5);
        }
    }
}
