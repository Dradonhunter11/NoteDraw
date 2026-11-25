using System;
using Avalonia.Input;

namespace NoteDraw.Services
{
    /// <summary>
    /// Resolves the platform-standard command modifier (<c>Cmd</c>/<see cref="KeyModifiers.Meta"/> on macOS,
    /// <c>Ctrl</c>/<see cref="KeyModifiers.Control"/> on Windows and Linux) for shortcuts, menus, and tooltips.
    /// </summary>
    public static class PlatformShortcuts
    {
        internal static bool? IsMacOsOverride { get; set; }

        public static bool IsMacOS => IsMacOsOverride ?? OperatingSystem.IsMacOS();

        public static KeyModifiers PrimaryModifier => IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control;

        public static string PrimaryModifierLabel => IsMacOS ? "Cmd" : "Ctrl";

        /// <summary>
        /// Returns true when the OS-native command key is pressed (<c>Meta</c>/<c>Cmd</c> on macOS,
        /// <c>Control</c>/<c>Ctrl</c> on Windows and Linux) without the opposite platform modifier.
        /// </summary>
        public static bool HasPrimaryModifier(KeyModifiers modifiers) => IsMacOS
            ? modifiers.HasFlag(KeyModifiers.Meta) && !modifiers.HasFlag(KeyModifiers.Control)
            : modifiers.HasFlag(KeyModifiers.Control) && !modifiers.HasFlag(KeyModifiers.Meta);

        /// <summary>
        /// Returns true if any command or Alt modifier is held (used to prevent single-key shortcuts from firing on modified combos).
        /// </summary>
        public static bool HasAnyCommandOrAltModifier(KeyModifiers modifiers)
            => modifiers.HasFlag(KeyModifiers.Control) ||
               modifiers.HasFlag(KeyModifiers.Meta) ||
               modifiers.HasFlag(KeyModifiers.Alt);

        public static string Format(string keyCombo) => $"{PrimaryModifierLabel}+{keyCombo}";
    }
}
