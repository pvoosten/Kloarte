using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>Gel codes as typed in a gel field (<see cref="GelCodes"/>), on MagicQ's colour table.</summary>
public sealed class GelCodesTests
{
    /// <summary>MagicQ's colour table (the copy in data/).</summary>
    private static readonly MagicQColours Table = MagicQColours.Load(TestEnvironment.File("res://data/allcols.csv"));

    /// <summary>Codes, numbers alone (= Lee), open white; unknown gels are refused.</summary>
    [Fact]
    public void ParsesWhatIsTyped()
    {
        Assert.Equal(1201, GelCodes.Parse("L201", Table));
        Assert.Equal(1201, GelCodes.Parse("l 201", Table));
        Assert.Equal(1201, GelCodes.Parse("201", Table));
        Assert.Equal(2080, GelCodes.Parse("R80", Table));
        Assert.Equal(0, GelCodes.Parse("O/W", Table));
        Assert.Equal(0, GelCodes.Parse("", Table));
        Assert.Null(GelCodes.Parse("L999", Table));
        Assert.Null(GelCodes.Parse("blue", Table));
        Assert.Equal("L201", GelCodes.Code(1201));
        Assert.Equal("R080", GelCodes.Code(2080));
    }

    /// <summary>The exact gel first, then code prefixes, then names; Lee before Rosco.</summary>
    [Fact]
    public void SearchesAsYouType()
    {
        var l20 = GelCodes.Search(Table, "L20");
        Assert.Equal(1020, l20[0]);   // "L20" is a gel itself: Lee 020
        Assert.Contains(1201, l20);
        Assert.All(l20.Skip(1), id => Assert.StartsWith("L20", GelCodes.Code(id)));
        Assert.Equal(1201, GelCodes.Search(Table, "201")[0]);
        Assert.Equal(2080, GelCodes.Search(Table, "R80")[0]);
        var blue = GelCodes.Search(Table, "C.T.Blue", 50);
        Assert.Contains(1201, blue);
        Assert.True(blue.IndexOf(1201) < blue.FindIndex(id => id >= 2000) || blue.TrueForAll(id => id < 2000));
    }
}
