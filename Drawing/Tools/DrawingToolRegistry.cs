using System;
using System.Collections.Generic;
using Avalonia.Input;
using NoteDraw.Services;

namespace NoteDraw.Drawing.Tools
{
    /// <summary>
    /// Canonical registry of all interactive drawing tools and their keyboard shortcut bindings.
    /// </summary>
    public sealed class DrawingToolRegistry
    {
        private readonly List<IDrawingTool> _orderedTools;
        private readonly Dictionary<DrawingToolKind, IDrawingTool> _byKind = new();
        private readonly Dictionary<Key, DrawingToolKind> _keyBindings = new();

        public static DrawingToolRegistry Default { get; } = new();

        private DrawingToolRegistry()
        {
            _orderedTools = CreateOrderedToolList();

            foreach (var tool in _orderedTools)
            {
                _byKind[tool.Kind] = tool;
                foreach (var key in tool.ShortcutKeys)
                    _keyBindings[key] = tool.Kind;
            }
        }

        private static List<IDrawingTool> CreateOrderedToolList() => new()
        {
            new PenTool(),
            new HighlighterTool(),
            new EraserTool(),
            new PipetteTool(),
            new LineTool(),
            new ArrowTool(),
            new RectangleTool(),
            new EllipseTool(),
            new TextLabelTool(),
            new SelectPanTool(),
            new MovePositionTool(),
            new SelectAreaTool(),
            new MoveSelectionTool(),
            new ResizeSelectionTool()
        };

        /// <summary>
        /// Creates an isolated per-canvas tool map so interactive drag states never leak between canvases.
        /// </summary>
        public static Dictionary<DrawingToolKind, IDrawingTool> CreateToolSet()
        {
            var dict = new Dictionary<DrawingToolKind, IDrawingTool>();
            foreach (var tool in CreateOrderedToolList())
                dict[tool.Kind] = tool;
            return dict;
        }

        public IReadOnlyList<IDrawingTool> All => _orderedTools;

        public IDrawingTool Get(DrawingToolKind kind)
            => _byKind.TryGetValue(kind, out var tool) ? tool : _byKind[DrawingToolKind.Pen];

        public bool TryGet(DrawingToolKind kind, out IDrawingTool tool)
            => _byKind.TryGetValue(kind, out tool!);

        public bool TryMapKey(Key key, KeyModifiers modifiers, out DrawingToolKind tool)
        {
            tool = default;
            if (PlatformShortcuts.HasAnyCommandOrAltModifier(modifiers))
                return false;

            return _keyBindings.TryGetValue(key, out tool);
        }
    }
}
