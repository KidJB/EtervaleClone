// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the system clipboard's text (monogame/issues/020-clipboard.md).

namespace Microsoft.Xna.Framework.Input;

/// <summary>
/// The system clipboard's text, for copy and paste in text fields.
/// </summary>
public static partial class Clipboard
{
    /// <summary>
    /// The clipboard's text, or an empty string when it holds none.
    /// </summary>
    public static string GetText() => PlatformGetText();

    /// <summary>
    /// Puts text on the clipboard; null puts an empty string.
    /// </summary>
    public static void SetText(string text) => PlatformSetText(text ?? string.Empty);
}
