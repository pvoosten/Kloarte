using System.Text.Json;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>The actors' shirt colours (<see cref="ShirtColors"/>; user: a colour per actor, the name printed on it).</summary>
public sealed class ShirtColorsTests
{
    /// <summary>A palette colour by the actor's place; an actor's own colour wins; letters dark on light shirts.</summary>
    [Fact]
    public void ColourPerActor()
    {
        var blocking = new Blocking();
        var anna = new ActorDef { Id = "a", Name = "Anna" };
        var bert = new ActorDef { Id = "b", Name = "Bert" };
        blocking.Actors.Add(anna);
        blocking.Actors.Add(bert);
        Assert.Equal(ShirtColors.Palette[0], ShirtColors.Of(blocking, anna));
        Assert.Equal(ShirtColors.Palette[1], ShirtColors.Of(blocking, bert));
        Assert.NotEqual(ShirtColors.Of(blocking, anna), ShirtColors.Of(blocking, bert));
        bert.ShirtColor = "#FFEE00";
        Assert.Equal("ffee00", ShirtColors.Of(blocking, bert));
        Assert.True(ShirtColors.DarkLetters("ffee00"));
        Assert.False(ShirtColors.DarkLetters("34495e"));
        bert.ShirtColor = "no colour";
        Assert.Equal(ShirtColors.Palette[1], ShirtColors.Of(blocking, bert));
    }

    /// <summary>The colour is saved with the blocking.</summary>
    [Fact]
    public void SavedWithTheBlocking()
    {
        var blocking = new Blocking();
        blocking.Actors.Add(new ActorDef { Id = "a", Name = "Anna", ShirtColor = "2471a3" });
        var json = JsonSerializer.Serialize(blocking, ShowModel<IShowDocument>.Json);
        var back = JsonSerializer.Deserialize<Blocking>(json, ShowModel<IShowDocument>.Json);
        Assert.Equal("2471a3", back.Actors[0].ShirtColor);
    }
}
