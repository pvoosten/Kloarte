using System;
using System.IO;
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

    /// <summary>The INT, POS, COL and BEAM buttons (palette windows), from a capture at the pinned size.</summary>
    private static readonly (string Name, int X, int Y)[] PaletteButtons =
        { ("intensity", 1600, 778), ("position", 1571, 808), ("colour", 1601, 808), ("beam", 1630, 808) };

    /// <summary>The VIEW PALETTES soft button of the intensity window, from a capture at the pinned size.</summary>
    private static readonly (int X, int Y) ViewPalettes = (133, 83);

    /// <summary>Opens each palette window (INT, POS, COL, BEAM) and saves a capture of it in a folder.</summary>
    public void CapturePalettes(string folder)
    {
        foreach (var (name, x, y) in PaletteButtons)
        {
            Click(x, y);
            Thread.Sleep(1500);
            if (name == "intensity")
            {
                // The intensity window opens on the programmer: VIEW PALETTES shows the palettes.
                Click(ViewPalettes.X, ViewPalettes.Y);
                Thread.Sleep(1500);
            }
            Capture(Path.Combine(folder, $"palettes_{name}.png"));
        }
    }

    /// <summary>Y of the row of select (S) buttons, from a capture at the pinned size.</summary>
    private const int SelectRow = 751;

    /// <summary>The CUE STK button (cue stack window), from a capture at the pinned size.</summary>
    private static readonly (int X, int Y) CueStackButton = (1647, 270);

    /// <summary>Selects a playback (1..10) and saves a capture of its cue stack window (the steps, numbers and texts).</summary>
    public void CaptureCueStack(int playback, string path)
    {
        Click(PlaybackColumns[playback - 1], SelectRow);
        Thread.Sleep(800);
        Click(CueStackButton.X, CueStackButton.Y);
        Thread.Sleep(1500);
        Capture(path);
    }

    /// <summary>The VIS button (MagicQ's visualiser window), from a capture at the pinned size.</summary>
    private static readonly (int X, int Y) VisButton = (1613, 352);

    /// <summary>The visualiser window's view soft buttons, from a capture at the pinned size.</summary>
    private static readonly (string Name, int X, int Y)[] VisViews =
        { ("perspective", 572, 83), ("top", 660, 83), ("front", 748, 83), ("side", 836, 83) };

    /// <summary>
    /// Opens MagicQ's visualiser window (VIS: MagicVis in MagicQ, the show's H records) and saves a capture of each
    /// view (perspective, top, front, side: vis_*.png) in a folder, to check the rig by eye.
    /// </summary>
    public void CaptureVis(string folder)
    {
        Click(VisButton.X, VisButton.Y);
        Thread.Sleep(3000);
        foreach (var (name, x, y) in VisViews)
        {
            Click(x, y);
            Thread.Sleep(2000);
            Capture(Path.Combine(folder, $"vis_{name}.png"));
        }
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

    /// <summary>ShowWindow: restore a minimised window without activating it.</summary>
    private const int ShowNoActivate = 4;

    /// <summary>The window's current image. A minimised window (someone else's click) draws nothing: it is restored first.</summary>
    private Bitmap Grab()
    {
        if (IsIconic(_handle))
        {
            ShowWindow(_handle, ShowNoActivate);
            SetWindowPos(_handle, IntPtr.Zero, Left, Top, Width, Height, NoZOrder | NoActivate);
            Thread.Sleep(1000);
        }
        var bitmap = new Bitmap(Width, Height);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        PrintWindow(_handle, dc, FullContent);
        graphics.ReleaseHdc(dc);
        return bitmap;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);

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
