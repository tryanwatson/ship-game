using System;
using System.Runtime.InteropServices;

namespace ShipGame.Client.Input;

/// <summary>
/// The system clipboard, through the SDL2 that MonoGame's DesktopGL backend already ships and loads (MonoGame
/// doesn't expose it). Every call fails soft: no SDL, no text, or an SDL error reads as empty and writes as a no-op.
/// </summary>
public static class Clipboard
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetTextFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetTextFn([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeFn(IntPtr memory);

    private sealed record Sdl(GetTextFn GetText, SetTextFn SetText, FreeFn Free);

    // MonoGame's native asset names per platform, plus the generic ones a system SDL would have.
    private static readonly string[] LibraryNames = OperatingSystem.IsWindows()
        ? new[] { "SDL2.dll" }
        : OperatingSystem.IsMacOS()
            ? new[] { "libSDL2-2.0.0.dylib", "libSDL2.dylib" }
            : new[] { "libSDL2-2.0.so.0", "libSDL2.so" };

    private static readonly Lazy<Sdl?> Library = new(Load);

    /// <summary>The clipboard's text, or empty if it holds none (or can't be read).</summary>
    public static string GetText()
    {
        if (Library.Value is not { } sdl)
            return "";
        try
        {
            var text = sdl.GetText();
            if (text == IntPtr.Zero)
                return "";
            try
            {
                return Marshal.PtrToStringUTF8(text) ?? "";
            }
            finally
            {
                sdl.Free(text);
            }
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static void SetText(string text)
    {
        if (Library.Value is not { } sdl)
            return;
        try
        {
            sdl.SetText(text);
        }
        catch (Exception)
        {
            // Copying is a convenience; never let it take the game down.
        }
    }

    private static Sdl? Load()
    {
        foreach (var name in LibraryNames)
        {
            // The assembly overload probes like DllImport does, including the runtimes/<rid>/native folders.
            if (!NativeLibrary.TryLoad(name, typeof(Clipboard).Assembly, null, out var handle))
                continue;
            if (NativeLibrary.TryGetExport(handle, "SDL_GetClipboardText", out var get)
                && NativeLibrary.TryGetExport(handle, "SDL_SetClipboardText", out var set)
                && NativeLibrary.TryGetExport(handle, "SDL_free", out var free))
            {
                return new Sdl(
                    Marshal.GetDelegateForFunctionPointer<GetTextFn>(get),
                    Marshal.GetDelegateForFunctionPointer<SetTextFn>(set),
                    Marshal.GetDelegateForFunctionPointer<FreeFn>(free));
            }
        }
        return null;
    }
}
