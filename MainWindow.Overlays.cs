using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using NoteDraw.Drawing;
using NoteDraw.Rendering;
using NoteDraw.Rendering.Latex;
using NoteDraw.Rendering.Markdown;
using NoteDraw.Services;

namespace NoteDraw
{
    public partial class MainWindow
    {
        private void BuildSyntaxGuideOverlay()
        {
            _floatingSyntaxGuidePopup.Width = 680;
            _floatingSyntaxGuidePopup.MaxWidth = 740;
            _floatingSyntaxGuidePopup.HorizontalAlignment = HorizontalAlignment.Right;
            _floatingSyntaxGuidePopup.VerticalAlignment = VerticalAlignment.Top;
            _floatingSyntaxGuidePopup.Margin = new Thickness(0, 56, 20, 0);
            _floatingSyntaxGuidePopup.CloseRequested += (_, _) => _syntaxGuideOverlay.IsVisible = false;

            _syntaxGuideOverlay.IsVisible = false;
            _syntaxGuideOverlay.Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
            _syntaxGuideOverlay.Child = _floatingSyntaxGuidePopup;
            _syntaxGuideOverlay.PointerPressed += (_, e) =>
            {
                if (ReferenceEquals(e.Source, _syntaxGuideOverlay))
                {
                    _syntaxGuideOverlay.IsVisible = false;
                    e.Handled = true;
                }
            };
        }

        private void ShowSyntaxGuidePopup()
        {
            _syntaxGuideOverlay.IsVisible = true;
            _floatingSyntaxGuidePopup.FocusSearchBox();
        }

        private void ToggleSyntaxGuidePopup()
        {
            if (_syntaxGuideOverlay.IsVisible)
                _syntaxGuideOverlay.IsVisible = false;
            else
                ShowSyntaxGuidePopup();
        }

        private void BuildQuickSwitcherOverlay()
        {
            _quickSwitcherInput.PlaceholderText = "Jump to note or drawing by name... (Esc to close)";
            _quickSwitcherInput.FontSize = 13.5;
            _quickSwitcherInput.TextChanged += (_, _) => PopulateQuickSwitcherResults();
            _quickSwitcherInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _quickSwitcherOverlay.IsVisible = false;
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    var firstBtn = _quickSwitcherResults.Children.OfType<Button>().FirstOrDefault();
                    firstBtn?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }
            };

            _quickSwitcherResults.Spacing = 4;

            var card = new Border
            {
                Width = 520,
                MaxHeight = 420,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6")),
                Padding = new Thickness(14),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 72, 0, 0),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Quick Switcher", FontSize = 14, FontWeight = FontWeight.Bold },
                        _quickSwitcherInput,
                        new ScrollViewer { MaxHeight = 290, Content = _quickSwitcherResults },
                        new Button
                        {
                            Content = "Close (Esc)",
                            HorizontalAlignment = HorizontalAlignment.Right,
                            FontSize = 11
                        }.Also(b => b.Click += (_, _) => _quickSwitcherOverlay.IsVisible = false)
                    }
                }
            };

            _quickSwitcherOverlay.IsVisible = false;
            _quickSwitcherOverlay.Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            _quickSwitcherOverlay.Child = card;
        }

        private void BuildPromptOverlay()
        {
            _promptTitle.FontSize = 14;
            _promptTitle.FontWeight = FontWeight.Bold;
            _promptInput.FontSize = 13;
            _promptInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    SubmitPrompt();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    _promptOverlay.IsVisible = false;
                    e.Handled = true;
                }
            };

            var card = new Border
            {
                Width = 420,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6")),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        _promptTitle,
                        _promptInput,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                CreateToolbarBtn("Cancel", () => _promptOverlay.IsVisible = false),
                                CreateToolbarBtn("Confirm", SubmitPrompt, accent: true)
                            }
                        }
                    }
                }
            };

            _promptOverlay.IsVisible = false;
            _promptOverlay.Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            _promptOverlay.Child = card;
        }

        private void BuildUnsavedExitMessageBoxOverlay()
        {
            _unsavedExitMessageText.FontSize = 13;
            _unsavedExitMessageText.TextWrapping = TextWrapping.Wrap;

            var saveBtn = CreateToolbarBtn("Save Unsaved Work", () => ApplyUnsavedExitDecision(UnsavedExitDecision.Save, closeWindow: true), accent: true);
            saveBtn.Padding = new Thickness(12, 6);

            var discardBtn = CreateToolbarBtn("Discard Unsaved Work", () => ApplyUnsavedExitDecision(UnsavedExitDecision.Discard, closeWindow: true));
            discardBtn.Padding = new Thickness(12, 6);

            var cancelBtn = CreateToolbarBtn("Cancel", () => ApplyUnsavedExitDecision(UnsavedExitDecision.Cancel, closeWindow: false));
            cancelBtn.Padding = new Thickness(12, 6);

            var card = new Border
            {
                Width = 480,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#0078D4")),
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Unsaved Work — NoteDraw",
                            FontSize = 15,
                            FontWeight = FontWeight.Bold
                        },
                        _unsavedExitMessageText,
                        new WrapPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children =
                            {
                                saveBtn.Also(b => b.Margin = new Thickness(0, 0, 8, 4)),
                                discardBtn.Also(b => b.Margin = new Thickness(0, 0, 8, 4)),
                                cancelBtn.Also(b => b.Margin = new Thickness(0, 0, 0, 4))
                            }
                        }
                    }
                }
            };

            _unsavedExitOverlay.IsVisible = false;
            _unsavedExitOverlay.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            _unsavedExitOverlay.Child = card;
        }


        private string GetAutoSaveMenuHeader()
            => _autoSaveEnabled ? "[✓] Auto-Save Documents While Editing" : "[ ] Auto-Save Documents While Editing";

        public void SetAutoSaveEnabled(bool enabled)
        {
            _autoSaveEnabled = enabled;
            _themeConfig.Config.AutoSave = enabled;
            _themeConfig.SaveOptions();
            _autoSaveMenuItem.Header = GetAutoSaveMenuHeader();
            if (_autoSaveEnabled)
                SaveAllDirtyTabs();
            UpdateStatusBar();
            RefreshTabStrip();
        }

        private void SaveTabToDisk(OpenDocumentTab tab)
        {
            if (tab.IsImage)
                return;
            _vault.WriteFile(tab.FilePath, tab.Content);
            tab.IsDirty = false;
        }

        public void SaveAllDirtyTabs()
        {
            foreach (var tab in _openTabs)
            {
                if (tab.IsDirty && !tab.IsImage)
                    SaveTabToDisk(tab);
            }
            RefreshTabStrip();
            UpdateStatusBar();
        }

        public void ShowUnsavedExitMessageBox()
        {
            var dirtyNames = _openTabs
                .Where(t => t.IsDirty && !t.IsImage)
                .Select(t => $"• {t.Title}")
                .ToList();

            string listing = dirtyNames.Count > 0
                ? string.Join("\n", dirtyNames)
                : "• Active document";

            _unsavedExitMessageText.Text =
                $"You have unsaved changes (Auto-Save is turned off):\n\n{listing}\n\nDo you want to save your changes before exiting?";
            _unsavedExitOverlay.IsVisible = true;
        }

        public void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false)
        {
            switch (decision)
            {
                case UnsavedExitDecision.Save:
                    SaveAllDirtyTabs();
                    _unsavedExitOverlay.IsVisible = false;
                    _isConfirmedExit = true;
                    if (closeWindow)
                        Close();
                    break;

                case UnsavedExitDecision.Discard:
                    foreach (var tab in _openTabs)
                        tab.IsDirty = false;
                    _unsavedExitOverlay.IsVisible = false;
                    _isConfirmedExit = true;
                    RefreshTabStrip();
                    UpdateStatusBar();
                    if (closeWindow)
                        Close();
                    break;

                case UnsavedExitDecision.Cancel:
                default:
                    _unsavedExitOverlay.IsVisible = false;
                    _isConfirmedExit = false;
                    break;
            }
        }

        private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_isConfirmedExit)
                return;

            if (_autoSaveEnabled)
            {
                SaveAllDirtyTabs();
                return;
            }

            if (HasUnsavedDocuments)
            {
                e.Cancel = true;
                ShowUnsavedExitMessageBox();
            }
        }


        private void PromptCreateNote()
        {
            ShowPrompt("Create New Markdown Note", "New Note", title =>
            {
                var entry = _vault.CreateNote(title);
                RefreshVaultTree();
                OpenFileInTab(entry.FullPath);
            });
        }

        private void PromptCreateFolder()
        {
            ShowPrompt("Create New Vault Folder", "New Folder", folderName =>
            {
                _vault.CreateFolder(folderName);
                RefreshVaultTree();
            });
        }

        private void PromptCreateNoteInFolder(string subfolderRelative)
        {
            ShowPrompt($"Create New Note in '{subfolderRelative}'", "New Note", title =>
            {
                var entry = _vault.CreateNote(title, subfolderRelative: subfolderRelative);
                _collapsedVaultFolders.Remove(Path.GetFullPath(Path.Combine(_vault.VaultPath, subfolderRelative)));
                RefreshVaultTree();
                OpenFileInTab(entry.FullPath);
            });
        }

        private void PromptCreateDrawing()
        {
            ShowPrompt("Create New Drawing (.ndraw)", "New Sketch", title =>
            {
                var entry = _vault.CreateDrawing(title);
                RefreshVaultTree();
                OpenFileInTab(entry.FullPath);
            });
        }

        private void ShowPrompt(string title, string defaultText, Action<string> onSubmit)
        {
            _promptTitle.Text = title;
            _promptInput.Text = defaultText;
            _promptCallback = onSubmit;
            _promptOverlay.IsVisible = true;
            _promptInput.Focus();
            _promptInput.SelectAll();
        }

        private void SubmitPrompt()
        {
            string value = _promptInput.Text?.Trim() ?? string.Empty;
            _promptOverlay.IsVisible = false;
            if (!string.IsNullOrEmpty(value))
                _promptCallback?.Invoke(value);
        }

        private void OpenQuickSwitcher()
        {
            _quickSwitcherInput.Text = string.Empty;
            PopulateQuickSwitcherResults();
            _quickSwitcherOverlay.IsVisible = true;
            _quickSwitcherInput.Focus();
        }

        private void PopulateQuickSwitcherResults()
        {
            _quickSwitcherResults.Children.Clear();
            var entries = _vault.GetEntries(_quickSwitcherInput.Text)
                .Where(e => e.Kind != VaultEntryKind.Folder)
                .Take(15);

            foreach (var entry in entries)
            {
                string icon = entry.Kind == VaultEntryKind.DrawingFile ? "🎨" : "📄";
                var btn = new Button
                {
                    Content = $"{icon} {entry.RelativePath}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    FontSize = 13,
                    Padding = new Thickness(10, 7),
                    CornerRadius = new CornerRadius(6)
                };
                btn.Click += (_, _) =>
                {
                    _quickSwitcherOverlay.IsVisible = false;
                    OpenFileInTab(entry.FullPath);
                };
                _quickSwitcherResults.Children.Add(btn);
            }
        }
    }
}
