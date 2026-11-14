# 020. The clipboard's text

Parts: runtime, framework.

## Need

Etervale's chat line copies, cuts and pastes text with Ctrl+C, Ctrl+X and Ctrl+V. MonoGame has no clipboard: the game
could neither read what the player copied elsewhere nor give them what they copied in the game.

## Cause

The runtime's SDL layer exposes no clipboard, and the framework has no type for it.

## Change

**Runtime** (`src/sdl/MGP_sdl.cpp`): `MGP_Clipboard_GetText` (`SDL_GetClipboardText`, copied into the caller's buffer
with the text's whole length returned) and `MGP_Clipboard_SetText` (`SDL_SetClipboardText`), UTF-8, on every platform
SDL drives.

**Framework** (`Input/Clipboard.cs`, `Platform/Native/Clipboard.Native.cs`, `Platform/Native/Platform.Interop.cs`):
`Clipboard.GetText()` (empty without text; at most 1 MiB) and `Clipboard.SetText(string)`.

Interface version 8 (issue 002).

## Test

Etervale's client on Windows: text copied in another program pastes into the chat line with Ctrl+V, and a chat line
selected with Ctrl+A and copied with Ctrl+C pastes into another program.
