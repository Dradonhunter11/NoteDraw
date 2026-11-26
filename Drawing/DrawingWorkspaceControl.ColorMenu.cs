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
    public sealed partial class DrawingWorkspaceControl
    {
        private void BuildContextSubMenuPopup()
        {
            var headerGrid = BuildPopupHeader();
            var rainbowBorder = BuildColorSpectrumSection();
            var (rgbStack, hexRow, rememberedHeader) = BuildRgbAndHexSection();
            var (presetsWrap, quickToolsWrap, clipboardRow, sizeRow, zoomRow) = BuildPopupToolsAndSliders();

            _contextSubMenuPopup.IsVisible = false;
            _contextSubMenuPopup.Width = 282;
            _contextSubMenuPopup.CornerRadius = new CornerRadius(8);
            _contextSubMenuPopup.BorderThickness = new Thickness(1);
            _contextSubMenuPopup.Padding = new Thickness(10);
            _contextSubMenuPopup.Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    headerGrid,
                    _spectrumPicker,
                    rainbowBorder,
                    rgbStack,
                    hexRow,
                    rememberedHeader,
                    _popupRememberedColorsWrap,
                    presetsWrap,
                    new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70, 148, 163, 184)) },
                    quickToolsWrap,
                    clipboardRow,
                    sizeRow,
                    zoomRow
                }
            };
        }

        private Grid BuildPopupHeader()
        {
            var closePopupBtn = new Button
            {
                Content = "×",
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            closePopupBtn.Click += (_, _) =>
            {
                RememberColor(_canvas.ActiveColorHex);
                RebuildQuickColorStrips();
                _contextSubMenuPopup.IsVisible = false;
            };

            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            headerGrid.Children.Add(new TextBlock
            {
                Text = "🎨 Drawing Sub-Menu & Color Selector",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(closePopupBtn, 1);
            headerGrid.Children.Add(closePopupBtn);
            return headerGrid;
        }

        private Border BuildColorSpectrumSection()
        {
            _spectrumPicker.Height = 115;
            _spectrumPicker.ColorPicked += (_, hsv) =>
            {
                var c = HsvToColor(hsv.H, hsv.S, hsv.V);
                ApplyColorFromControls(c);
            };

            _hueSlider.Minimum = 0;
            _hueSlider.Maximum = 360;
            _hueSlider.VerticalAlignment = VerticalAlignment.Center;
            _hueSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingColorSelector && args.Property == Slider.ValueProperty)
                {
                    _spectrumPicker.SetHsv(_hueSlider.Value, _spectrumPicker.Saturation, _spectrumPicker.Value);
                    var c = HsvToColor(_hueSlider.Value, _spectrumPicker.Saturation, _spectrumPicker.Value);
                    ApplyColorFromControls(c, syncSpectrum: false);
                }
            };

            return new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4, 1),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#FF0000"), 0.00),
                        new GradientStop(Color.Parse("#FFFF00"), 0.17),
                        new GradientStop(Color.Parse("#00FF00"), 0.33),
                        new GradientStop(Color.Parse("#00FFFF"), 0.50),
                        new GradientStop(Color.Parse("#0000FF"), 0.67),
                        new GradientStop(Color.Parse("#FF00FF"), 0.83),
                        new GradientStop(Color.Parse("#FF0000"), 1.00)
                    }
                },
                Child = _hueSlider
            };
        }

        private (StackPanel RgbStack, Grid HexRow, Grid RememberedHeader) BuildRgbAndHexSection()
        {
            var rgbStack = new StackPanel { Spacing = 2 };
            rgbStack.Children.Add(CreateChannelRow("R", _redSlider, _redValLabel));
            rgbStack.Children.Add(CreateChannelRow("G", _greenSlider, _greenValLabel));
            rgbStack.Children.Add(CreateChannelRow("B", _blueSlider, _blueValLabel));

            _selectorPreviewSwatch.Width = 28;
            _selectorPreviewSwatch.Height = 24;
            _selectorPreviewSwatch.CornerRadius = new CornerRadius(5);
            _selectorPreviewSwatch.BorderThickness = new Thickness(1);
            _selectorPreviewSwatch.BorderBrush = new SolidColorBrush(Color.Parse("#64748B"));

            _colorHexInput.FontSize = 11.5;
            _colorHexInput.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            _colorHexInput.PlaceholderText = "#RRGGBB";
            _colorHexInput.TextChanged += (_, _) =>
            {
                if (_syncingColorSelector) return;
                string raw = (_colorHexInput.Text ?? string.Empty).Trim();
                if (Color.TryParse(raw, out var parsed))
                    SelectCustomColor($"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}");
            };

            var rememberBtn = new Button
            {
                Content = "+ Remember",
                FontSize = 10.5,
                Padding = new Thickness(6, 2),
                CornerRadius = new CornerRadius(4)
            };
            rememberBtn.Click += (_, _) =>
            {
                RememberColor(_canvas.ActiveColorHex);
                RebuildQuickColorStrips();
            };

            var rememberedHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            rememberedHeader.Children.Add(new TextBlock
            {
                Text = "Remembered Quick Colors:",
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(rememberBtn, 1);
            rememberedHeader.Children.Add(rememberBtn);

            var popupPipetteBtn = new Button
            {
                Content = "🧪 Pipette",
                FontSize = 10.5,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            ToolTip.SetTip(popupPipetteBtn, "Pick color directly from canvas strokes (Shortcut: I, K, or 4)");
            popupPipetteBtn.Click += (_, _) =>
            {
                if (_toolCommands.TryGetValue(DrawingToolKind.Pipette, out var pipetteCmd))
                    pipetteCmd.Execute(null);
                else
                    _canvas.ActiveTool = DrawingToolKind.Pipette;
                _contextSubMenuPopup.IsVisible = false;
                RefreshSelectionVisuals();
            };

            var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
            hexRow.Children.Add(_selectorPreviewSwatch);
            Grid.SetColumn(_colorHexInput, 1);
            hexRow.Children.Add(_colorHexInput);
            Grid.SetColumn(popupPipetteBtn, 2);
            hexRow.Children.Add(popupPipetteBtn);

            return (rgbStack, hexRow, rememberedHeader);
        }

        private (WrapPanel Presets, WrapPanel QuickTools, WrapPanel ClipboardRow, Grid SizeRow, Grid ZoomRow) BuildPopupToolsAndSliders()
        {
            var presetsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            var presets = new (string Hex, string Name)[]
            {
                ("#0F172A", "Ink Black"),
                ("#3B82F6", "Blue"),
                ("#EF4444", "Red"),
                ("#10B981", "Green"),
                ("#F59E0B", "Orange"),
                ("#A855F7", "Purple"),
                ("#06B6D4", "Teal"),
                ("#FACC15", "Yellow"),
                ("#EC4899", "Pink"),
                ("#78350F", "Brown"),
                ("#64748B", "Slate"),
                ("#FFFFFF", "White")
            };
            foreach (var (hex, name) in presets)
                presetsWrap.Children.Add(BuildColorChip(hex, $"{name} ({hex})", size: 22));

            var quickToolsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var tool in DrawingToolRegistry.Default.All)
                AddToolButton(quickToolsWrap, tool, buttonSize: 30, iconSize: 16);

            _popupThicknessSlider.Minimum = 1;
            _popupThicknessSlider.Maximum = 24;
            _popupThicknessSlider.Value = 3;
            _popupThicknessSlider.VerticalAlignment = VerticalAlignment.Center;
            _popupThicknessSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingThickness && args.Property == Slider.ValueProperty)
                    SetActiveThickness(_popupThicknessSlider.Value);
            };
            _popupThicknessLabel.Text = "3px";
            _popupThicknessLabel.FontSize = 11;
            _popupThicknessLabel.VerticalAlignment = VerticalAlignment.Center;

            var sizeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
            sizeRow.Children.Add(new TextBlock { Text = "Size:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_popupThicknessSlider, 1);
            sizeRow.Children.Add(_popupThicknessSlider);
            Grid.SetColumn(_popupThicknessLabel, 2);
            sizeRow.Children.Add(_popupThicknessLabel);

            _popupZoomSlider.Minimum = 25;
            _popupZoomSlider.Maximum = 400;
            _popupZoomSlider.Value = 100;
            _popupZoomSlider.VerticalAlignment = VerticalAlignment.Center;
            _popupZoomSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingZoom && args.Property == Slider.ValueProperty)
                    SetZoomPercent(_popupZoomSlider.Value);
            };
            _popupZoomLabel.Text = "100%";
            _popupZoomLabel.FontSize = 11;
            _popupZoomLabel.VerticalAlignment = VerticalAlignment.Center;

            var popupZoomControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children =
                {
                    CreateActionButton("-", () => _canvas.ZoomByFactor(1.0 / 1.15)),
                    _popupZoomLabel,
                    CreateActionButton("+", () => _canvas.ZoomByFactor(1.15)),
                    CreateActionButton("1:1", () => _canvas.ResetZoomAndPan())
                }
            };
            var zoomRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
            zoomRow.Children.Add(new TextBlock { Text = "Zoom:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_popupZoomSlider, 1);
            zoomRow.Children.Add(_popupZoomSlider);
            Grid.SetColumn(popupZoomControls, 2);
            zoomRow.Children.Add(popupZoomControls);

            var popupPasteBtn = new Button
            {
                Content = "📋 Paste Image",
                FontSize = 11,
                Padding = new Thickness(7, 4),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5)
            };
            ToolTip.SetTip(popupPasteBtn, $"Paste image from clipboard onto canvas ({PlatformShortcuts.Format("V")})");
            popupPasteBtn.Click += (_, _) =>
            {
                _contextSubMenuPopup.IsVisible = false;
                _ = PasteImageFromClipboardAsync(_lastContextSubMenuCanvasPoint, allowFilePickerFallback: true);
            };

            var popupCopyBtn = new Button
            {
                Content = "📋 Copy Canvas",
                FontSize = 11,
                Padding = new Thickness(7, 4),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5)
            };
            ToolTip.SetTip(popupCopyBtn, $"Copy canvas image to clipboard ({PlatformShortcuts.Format("C")})");
            popupCopyBtn.Click += (_, _) =>
            {
                _contextSubMenuPopup.IsVisible = false;
                _ = CopyCanvasToClipboardAsync();
            };

            var clipboardRow = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { popupPasteBtn, popupCopyBtn }
            };

            return (presetsWrap, quickToolsWrap, clipboardRow, sizeRow, zoomRow);
        }

        private Grid CreateChannelRow(string label, Slider slider, TextBlock valLabel)
        {
            slider.Minimum = 0;
            slider.Maximum = 255;
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.PropertyChanged += (_, args) =>
            {
                if (!_syncingColorSelector && args.Property == Slider.ValueProperty)
                {
                    var c = Color.FromRgb(
                        (byte)Math.Round(_redSlider.Value),
                        (byte)Math.Round(_greenSlider.Value),
                        (byte)Math.Round(_blueSlider.Value));
                    ApplyColorFromControls(c);
                }
            };

            valLabel.Text = "0";
            valLabel.FontSize = 10.5;
            valLabel.Width = 26;
            valLabel.TextAlignment = TextAlignment.Right;
            valLabel.VerticalAlignment = VerticalAlignment.Center;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,28"), ColumnSpacing = 4 };
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(valLabel, 2);
            row.Children.Add(valLabel);
            return row;
        }

        public void ShowContextSubMenuAt(Point canvasRelativePos)
        {
            _lastContextSubMenuCanvasPoint = new Point(
                (canvasRelativePos.X - _canvas.PanOffset.X) / Math.Max(0.25, _canvas.Zoom),
                (canvasRelativePos.Y - _canvas.PanOffset.Y) / Math.Max(0.25, _canvas.Zoom));

            double maxW = _canvasFrame.Bounds.Width > 40 ? _canvasFrame.Bounds.Width : 540;
            double maxH = _canvasFrame.Bounds.Height > 40 ? _canvasFrame.Bounds.Height : 490;
            double popupW = _contextSubMenuPopup.Width;
            double popupH = 490;

            double x = Math.Clamp(canvasRelativePos.X + 8, 6, Math.Max(6, maxW - popupW - 8));
            double y = Math.Clamp(canvasRelativePos.Y + 8, 6, Math.Max(6, maxH - popupH - 8));

            Avalonia.Controls.Canvas.SetLeft(_contextSubMenuPopup, x);
            Avalonia.Controls.Canvas.SetTop(_contextSubMenuPopup, y);
            _contextSubMenuPopup.IsVisible = true;
        }

        public static void RememberColor(string? hex)
        {
            string normalized;
            if (string.IsNullOrWhiteSpace(hex) || string.Equals(hex, "AUTO", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "#0F172A";
            }
            else if (Color.TryParse(hex.Trim(), out var parsed))
            {
                normalized = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
            }
            else
            {
                return;
            }

            LastSelectedColorHex = normalized;
            _rememberedColors.RemoveAll(c => string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase));
            _rememberedColors.Insert(0, normalized);
            if (_rememberedColors.Count > 10)
                _rememberedColors.RemoveRange(10, _rememberedColors.Count - 10);

            SaveRememberedColorsToDisk();
        }

        private static string GetRememberedColorsFilePath()
            => Path.Combine(AppContext.BaseDirectory, "canvas", "remembered_colors.json");

        private static void LoadRememberedColorsFromDisk()
        {
            try
            {
                string path = GetRememberedColorsFilePath();
                if (!File.Exists(path))
                    return;

                var loaded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
                if (loaded is null || loaded.Count == 0)
                    return;

                var valid = new List<string>();
                foreach (string item in loaded)
                {
                    if (Color.TryParse(item?.Trim(), out var c))
                    {
                        string norm = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                        if (!valid.Contains(norm, StringComparer.OrdinalIgnoreCase))
                            valid.Add(norm);
                    }
                }

                if (valid.Count > 0)
                {
                    _rememberedColors.Clear();
                    _rememberedColors.AddRange(valid.Count > 10 ? valid.GetRange(0, 10) : valid);
                    LastSelectedColorHex = _rememberedColors[0];
                }
            }
            catch
            {
                // Non-fatal fallback to in-memory defaults
            }
        }

        private static void SaveRememberedColorsToDisk()
        {
            try
            {
                string path = GetRememberedColorsFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(_rememberedColors, MemoryJsonOptions));
            }
            catch
            {
                // Ignore disk errors in read-only or restricted paths
            }
        }

        private Button BuildColorChip(string hex, string tooltip, double size)
        {
            bool isActive = string.Equals(hex, _canvas.ActiveColorHex, StringComparison.OrdinalIgnoreCase);
            var accentBrush = _palette is not null
                ? ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4")
                : new SolidColorBrush(Color.Parse("#0078D4"));

            var chip = new Button
            {
                Width = size,
                Height = size,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(size / 2.0),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(isActive ? 2 : 1),
                BorderBrush = isActive ? accentBrush : new SolidColorBrush(Color.Parse("#64748B"))
            };
            ToolTip.SetTip(chip, tooltip);
            chip.Click += (_, _) => SelectCustomColor(hex);
            return chip;
        }

        private void RebuildQuickColorStrips()
        {
            _toolbarQuickColorsPanel.Children.Clear();
            int quickLimit = Math.Min(6, _rememberedColors.Count);
            for (int i = 0; i < quickLimit; i++)
            {
                string hex = _rememberedColors[i];
                _toolbarQuickColorsPanel.Children.Add(BuildColorChip(hex, $"Quick Select {hex}", size: 18));
            }

            _popupRememberedColorsWrap.Children.Clear();
            foreach (string hex in _rememberedColors)
                _popupRememberedColorsWrap.Children.Add(BuildColorChip(hex, $"Remembered {hex}", size: 20));
        }

        public void SelectCustomColor(string hex)
        {
            string normalized;
            Color color;
            if (string.Equals(hex, "AUTO", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "#0F172A";
                color = Color.Parse(normalized);
                _canvas.ActiveColorHex = normalized;
            }
            else if (Color.TryParse(hex?.Trim(), out var parsed))
            {
                normalized = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
                color = parsed;
                _canvas.ActiveColorHex = normalized;
            }
            else
            {
                return;
            }

            RememberColor(normalized);
            ApplyColorToSelectorUi(color, normalized, syncSpectrum: true, syncHexText: true);
            RebuildQuickColorStrips();
            RefreshSelectionVisuals();
        }

        private void ApplyColorFromControls(Color c, bool syncSpectrum = true)
        {
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _canvas.ActiveColorHex = hex;
            LastSelectedColorHex = hex;
            ApplyColorToSelectorUi(c, hex, syncSpectrum, syncHexText: true);
            RefreshSelectionVisuals();
        }

        private void ApplyColorToSelectorUi(Color c, string hex, bool syncSpectrum, bool syncHexText)
        {
            _syncingColorSelector = true;
            try
            {
                var brush = new SolidColorBrush(c);
                _colorBtnDot.Background = brush;
                _colorBtnText.Text = $"Color ({hex})";
                _selectorPreviewSwatch.Background = brush;

                _redSlider.Value = c.R;
                _greenSlider.Value = c.G;
                _blueSlider.Value = c.B;
                _redValLabel.Text = c.R.ToString();
                _greenValLabel.Text = c.G.ToString();
                _blueValLabel.Text = c.B.ToString();

                if (syncSpectrum)
                {
                    var (h, s, v) = ColorToHsv(c);
                    _hueSlider.Value = h;
                    _spectrumPicker.SetHsv(h, s, v);
                }

                if (syncHexText && !string.Equals(_colorHexInput.Text, hex, StringComparison.OrdinalIgnoreCase))
                    _colorHexInput.Text = hex;
            }
            finally
            {
                _syncingColorSelector = false;
            }
        }


        private static (double H, double S, double V) ColorToHsv(Color c)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            double h = 0;
            if (d > 0.0001)
            {
                if (max == r) h = 60.0 * (((g - b) / d) % 6.0);
                else if (max == g) h = 60.0 * (((b - r) / d) + 2.0);
                else h = 60.0 * (((r - g) / d) + 4.0);
                if (h < 0) h += 360.0;
            }
            double s = max <= 0.0001 ? 0.0 : d / max;
            return (h, s, max);
        }

        internal static Color HsvToColor(double h, double s, double v)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            s = Math.Clamp(s, 0.0, 1.0);
            v = Math.Clamp(v, 0.0, 1.0);

            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;

            double r, g, b;
            if (h < 60) (r, g, b) = (c, x, 0);
            else if (h < 120) (r, g, b) = (x, c, 0);
            else if (h < 180) (r, g, b) = (0, c, x);
            else if (h < 240) (r, g, b) = (0, x, c);
            else if (h < 300) (r, g, b) = (x, 0, c);
            else (r, g, b) = (c, 0, x);

            return Color.FromRgb(
                (byte)Math.Clamp((int)Math.Round((r + m) * 255.0), 0, 255),
                (byte)Math.Clamp((int)Math.Round((g + m) * 255.0), 0, 255),
                (byte)Math.Clamp((int)Math.Round((b + m) * 255.0), 0, 255));
        }
    }
}
