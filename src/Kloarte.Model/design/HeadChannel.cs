using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// One channel of a head as cues and palettes set it: its key in the files, its DMX offsets in the head (coarse first;
/// two for 16 bit) and how a <see cref="LookValue"/> becomes DMX and back. Values round-trip: a value read from DMX
/// (<see cref="FromDmx"/>) gives the same DMX again (<see cref="ToDmx(LookValue)"/>), with an exact DMX value added when the
/// readable part alone wouldn't.
/// </summary>
public sealed class HeadChannel
{
    /// <summary>The most decimals a readable number gets.</summary>
    private const int MaxDecimals = 6;

    /// <summary>Its key in the files: "intensity", a GDTF attribute ("Pan"), a channel name, or "#12" (channel 12).</summary>
    public string Key { get; init; } = "";

    /// <summary>How its values read.</summary>
    public HeadChannelKind Kind { get; init; }

    /// <summary>DMX offsets in the head (1-based), coarse first.</summary>
    public int[] Offsets { get; init; } = { 1 };

    /// <summary>The GDTF channel (kind <see cref="HeadChannelKind.Gdtf"/>, or an intensity of a GDTF head), or null.</summary>
    public GdtfChannel Gdtf { get; init; }

    /// <summary>What the channel is: I, P, C, B (the fixture type's class of its coarse channel).</summary>
    public char Class { get; init; } = 'I';

    /// <summary>The fixture type's name of its coarse channel.</summary>
    public string Name { get; init; } = "";

    /// <summary>Highest DMX value (255, or 65535 for 16 bit).</summary>
    public int Max => Offsets.Length >= 2 ? 65535 : 255;

    /// <summary>The value of these DMX bytes (address -&gt; byte) for a head at `address`; null when a byte is missing.</summary>
    public int? Read(IReadOnlyDictionary<int, int> dmx, int address)
    {
        int value = 0;
        foreach (int offset in Offsets)
        {
            if (!dmx.TryGetValue(address + offset - 1, out int b))
                return null;
            value = value << 8 | (b & 0xff);
        }
        return value;
    }

    /// <summary>Writes a value as DMX bytes (address -&gt; byte) for a head at `address`.</summary>
    public void Write(IDictionary<int, int> dmx, int address, int value)
    {
        value = Math.Clamp(value, 0, Max);
        for (int i = 0; i < Offsets.Length; i++)
            dmx[address + Offsets[i] - 1] = value >> (8 * (Offsets.Length - 1 - i)) & 0xff;
    }

    /// <summary>
    /// The DMX value (channel resolution) of a value that is not a palette reference; null when the readable part means
    /// nothing for this channel (a name it doesn't have).
    /// </summary>
    public int? ToDmx(LookValue value)
    {
        if (value == null || value.IsPalette)
            return null;
        if (value.Dmx is { } exact)
            return Math.Clamp(exact, 0, Max);
        return ToDmx(value.Text);
    }

    /// <summary>The DMX value of a readable text, or null.</summary>
    private int? ToDmx(string text)
    {
        double? number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : null;
        switch (Kind)
        {
            case HeadChannelKind.Intensity:
                return number is { } p ? (int)Math.Clamp(Math.Round(p / 100.0 * Max, MidpointRounding.AwayFromZero), 0, Max) : null;
            case HeadChannelKind.Raw:
                return number is { } r ? (int)Math.Clamp(Math.Round(r, MidpointRounding.AwayFromZero), 0, Max) : null;
        }
        if (Gdtf == null)
            return null;
        if (number is { } physical)
            return MainFunction?.Dmx((float)physical);
        foreach (var f in Gdtf.Functions)
            for (int i = 0; i < f.Sets.Count; i++)
                if (f.Sets[i].Name.Trim().Length > 0 && string.Equals(f.Sets[i].Name.Trim(), text, StringComparison.OrdinalIgnoreCase))
                    return Middle(f, i);
        int space = text.LastIndexOf(' ');
        if (space > 0 && double.TryParse(text[(space + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
        {
            string name = text[..space].Trim();
            var function = Gdtf.Functions.FirstOrDefault(f => string.Equals(f.Attribute, name, StringComparison.OrdinalIgnoreCase))
                           ?? Gdtf.Functions.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            return function?.Dmx((float)v);
        }
        return null;
    }

    /// <summary>
    /// The value of a DMX value (channel resolution), as readable as it can be: a number, a set name, "function value";
    /// with the exact DMX value added when the readable part alone gives another DMX value.
    /// </summary>
    public LookValue FromDmx(int dmx)
    {
        dmx = Math.Clamp(dmx, 0, Max);
        switch (Kind)
        {
            case HeadChannelKind.Raw:
                return LookValue.Of(dmx);
            case HeadChannelKind.Intensity:
                return Shortest(dmx * 100.0 / Max, n => n, dmx) is { } percent ? percent
                    : LookValue.Of(LookValue.Number(Math.Round(dmx * 100.0 / Max, 2)), dmx);
        }
        if (Gdtf == null)
            return LookValue.OfDmx(dmx);
        var function = Gdtf.FunctionAt(dmx);
        if (function != null)
        {
            string setName = function.SetAt(dmx)?.Name.Trim() ?? "";
            if (setName.Length > 0 && ToDmx(setName) == dmx && !IsNumber(setName))
                return LookValue.Of(setName);
            if (ReferenceEquals(function, MainFunction) && Shortest(function.Physical(dmx), n => n, dmx) is { } number)
                return number;
            string label = Label(function);
            if (label != null && Shortest(function.Physical(dmx), n => $"{label} {LookValue.Number(n)}", dmx) is { } named)
                return named;
            if (setName.Length > 0 && !IsNumber(setName))
                return LookValue.Of(setName, dmx);
            string readable = ReferenceEquals(function, MainFunction) || label == null
                ? LookValue.Number(Math.Round(function.Physical(dmx), 3))
                : $"{label} {LookValue.Number(Math.Round(function.Physical(dmx), 3))}";
            return LookValue.Of(readable, dmx);
        }
        return LookValue.OfDmx(dmx);
    }

    /// <summary>The function a plain number is a physical value of: the one of the channel's attribute, else the first.</summary>
    private GdtfFunction MainFunction => Gdtf?.Function(Gdtf.Attribute) ?? Gdtf?.Functions.FirstOrDefault();

    /// <summary>The name a function goes by in a value ("Shutter1Strobe 5"): its attribute, else its name; null when neither finds it.</summary>
    private string Label(GdtfFunction function)
    {
        foreach (string label in new[] { function.Attribute, function.Name })
            if (label.Length > 0 && !label.Contains(' ')
                && ReferenceEquals(Gdtf.Functions.FirstOrDefault(f => string.Equals(f.Attribute, label, StringComparison.OrdinalIgnoreCase))
                                   ?? Gdtf.Functions.FirstOrDefault(f => string.Equals(f.Name, label, StringComparison.OrdinalIgnoreCase)), function))
                return label;
        return null;
    }

    /// <summary>
    /// The value with the fewest decimals (0..6) of `number` whose text (made by `text`) gives `dmx` again, or null.
    /// </summary>
    private LookValue Shortest(double number, Func<double, object> text, int dmx)
    {
        for (int decimals = 0; decimals <= MaxDecimals; decimals++)
        {
            double rounded = Math.Round(number, decimals, MidpointRounding.AwayFromZero) + 0.0;
            object made = text(rounded);
            string written = made is double d ? LookValue.Number(d) : (string)made;
            if (ToDmx(written) == dmx)
                return LookValue.Of(written);
        }
        return null;
    }

    /// <summary>True when a text reads as a number (a set name like that would be read as a value).</summary>
    private static bool IsNumber(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>The middle DMX value of set `i` of a function (as a console's palette of a wheel slot).</summary>
    private static int Middle(GdtfFunction function, int i)
    {
        int from = function.Sets[i].DmxFrom;
        int to = i + 1 < function.Sets.Count ? function.Sets[i + 1].DmxFrom - 1 : function.DmxTo;
        return (from + Math.Max(from, to)) / 2;
    }

    /// <summary>
    /// A value as a number of this channel (intensity in %, a raw channel's DMX value, the main function's physical
    /// value), or null when its DMX value is not in the main function (a set, another function).
    /// </summary>
    public double? NumberOf(LookValue value)
    {
        if (ToDmx(value) is not { } dmx)
            return null;
        switch (Kind)
        {
            case HeadChannelKind.Intensity:
                return Math.Round(dmx * 100.0 / Max, 2);
            case HeadChannelKind.Raw:
                return dmx;
        }
        var main = MainFunction;
        return main != null && dmx >= main.DmxFrom && dmx <= main.DmxTo ? Math.Round(main.Physical(dmx), 4) : null;
    }

    /// <summary>The name of the set (wheel slot, named range) a value's DMX value falls in, or null.</summary>
    public string SetOf(LookValue value)
    {
        if (Gdtf == null || ToDmx(value) is not { } dmx)
            return null;
        string name = Gdtf.FunctionAt(dmx)?.SetAt(dmx)?.Name.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>The names of the channel's sets (wheel slots, named ranges), for pickers.</summary>
    public IReadOnlyList<string> SetNames =>
        Gdtf?.Functions.SelectMany(f => f.Sets).Select(s => s.Name.Trim()).Where(n => n.Length > 0 && !IsNumber(n)).Distinct().ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>The physical range of the main function (from, to), or null (not a GDTF channel).</summary>
    public (float From, float To)? Range => MainFunction is { } f ? (f.PhysicalFrom, f.PhysicalTo) : null;
}
