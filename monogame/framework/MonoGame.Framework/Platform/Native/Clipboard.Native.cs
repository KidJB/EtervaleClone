// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the system clipboard through the runtime's SDL (monogame/issues/020-clipboard.md).

using System;
using System.Text;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Input;

public static partial class Clipboard
{
    private const int MaximumBytes = 1024 * 1024;

    private static unsafe string PlatformGetText()
    {
        var length = MGP.Clipboard_GetText(null, 0);
        if (length <= 0)
        {
            return string.Empty;
        }

        var bytes = new byte[Math.Min(length, MaximumBytes) + 1];
        fixed (byte* buffer = bytes)
        {
            MGP.Clipboard_GetText(buffer, bytes.Length);
        }

        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    private static unsafe void PlatformSetText(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);
        fixed (byte* buffer = bytes)
        {
            MGP.Clipboard_SetText(buffer);
        }
    }
}
