using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kloarte.MagicQVerify;

/// <summary>
/// MagicQ PC's main window, driven with posted window messages: they reach Qt without focus and leave the user's mouse
/// alone. Most of MagicQ's UI is drawn by itself (no UI Automation elements), so its controls are window positions,
/// valid for the Full layout at the pinned size.
/// </summary>
internal sealed partial class MagicQWindow
{
    /// <summary>The pinned window position and size.</summary>
    private const int Left = 2, Top = 4, Width = 1816, Height = 959;

    /// <summary>Y of the row of Go (&gt;) buttons, from a capture at the pinned size.</summary>
    private const int GoRow = 779;

    /// <summary>Y of a fader's handle at 0 % and of the top of its travel, from a capture at the pinned size.</summary>
    private const int FaderBottom = 905, FaderTop = 815;

    /// <summary>A click: time on the button before and during the press. MagicQ misses shorter clicks now and then.</summary>
    private const int HoverMilliseconds = 120, PressMilliseconds = 200;

    /// <summary>A point in the background of playback 1's tile, and the step to the next tile.</summary>
    private const int TileLeft = 96, TileRow = 690, TileWidth = 140;

    /// <summary>X of the Go (&gt;) button per playback, from a capture at the pinned size.</summary>
    private static readonly int[] PlaybackColumns = { 158, 298, 440, 580, 721, 862, 1002, 1143, 1284, 1425 };

    /// <summary>Window messages.</summary>
    private const uint MouseMove = 0x200, ButtonDown = 0x201, ButtonUp = 0x202;

    /// <summary>The left mouse button in a message's wParam.</summary>
    private const int LeftButton = 1;

    /// <summary>SetWindowPos: keep the z-order, don't activate.</summary>
    private const uint NoZOrder = 0x4, NoActivate = 0x10;

    /// <summary>PrintWindow: render what Qt draws with OpenGL too.</summary>
    private const uint FullContent = 2;

    /// <summary>The window handle.</summary>
    private readonly IntPtr _handle;

    /// <summary>Top left of the client area, relative to the window.</summary>
    private readonly Win32Point _client;

    /// <summary>Wraps a window that is pinned to its position.</summary>
    private MagicQWindow(IntPtr handle)
    {
        _handle = handle;
        SetWindowPos(handle, IntPtr.Zero, Left, Top, Width, Height, NoZOrder | NoActivate);
        Thread.Sleep(500);
        var origin = new Win32Point();
        ClientToScreen(handle, ref origin);
        _client = new Win32Point { X = origin.X - Left, Y = origin.Y - Top };
    }

    /// <summary>MagicQ's window once it is there, or null after `timeout`.</summary>
    public static MagicQWindow Wait(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            var process = Process.GetProcessesByName("mqqt").FirstOrDefault();
            if (process != null && process.MainWindowHandle != IntPtr.Zero)
                return new MagicQWindow(process.MainWindowHandle);
            Thread.Sleep(250);
        }
        return null;
    }

    /// <summary>Presses the Go (&gt;) button of a playback (1..10).</summary>
    public void Go(int playback) => Click(PlaybackColumns[playback - 1], GoRow);

    /// <summary>
    /// Drags a playback's fader (1..10) to full: MagicQ starts with the faders down, and a playback at 0 % outputs its
    /// intensities at 0.
    /// </summary>
    public void RaiseFader(int playback)
    {
        int x = PlaybackColumns[playback - 1];
        PostMessage(_handle, MouseMove, IntPtr.Zero, Position(x, FaderBottom));
        Thread.Sleep(30);
        PostMessage(_handle, ButtonDown, LeftButton, Position(x, FaderBottom));
        for (int y = FaderBottom; y >= FaderTop; y -= 5)
        {
            PostMessage(_handle, MouseMove, LeftButton, Position(x, y));
            Thread.Sleep(15);
        }
        PostMessage(_handle, ButtonUp, IntPtr.Zero, Position(x, FaderTop));
    }

    /// <summary>A position in window coordinates (as in a capture) as a message's lParam.</summary>
    private nint Position(int x, int y) => x - _client.X | y - _client.Y << 16;

    /// <summary>Clicks at a position in window coordinates (as in a capture).</summary>
    private void Click(int x, int y)
    {
        nint position = Position(x, y);
        PostMessage(_handle, MouseMove, IntPtr.Zero, position);
        Thread.Sleep(HoverMilliseconds);
        PostMessage(_handle, ButtonDown, LeftButton, position);
        Thread.Sleep(PressMilliseconds);
        PostMessage(_handle, ButtonUp, IntPtr.Zero, position);
    }

    /// <summary>True when a playback (1..10) runs: MagicQ draws its tile red.</summary>
    public bool IsActive(int playback)
    {
        using var bitmap = Grab();
        var colour = bitmap.GetPixel(TileLeft + (playback - 1) * TileWidth, TileRow);
        return colour.R > 2 * colour.G && colour.R > 2 * colour.B;
    }

    /// <summary>Saves a capture of the window as PNG.</summary>
    public void Capture(string path)
    {
        using var bitmap = Grab();
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>The window's current image.</summary>
    private Bitmap Grab()
    {
        var bitmap = new Bitmap(Width, Height);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        PrintWindow(_handle, dc, FullContent);
        graphics.ReleaseHdc(dc);
        return bitmap;
    }

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr window, ref Win32Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    /// <summary>A Win32 POINT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        /// <summary>X.</summary>
        public int X;

        /// <summary>Y.</summary>
        public int Y;
    }
}
