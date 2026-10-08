using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>The ids of new actors: slugs of their names, unique.</summary>
public sealed class ActorIdsTests
{
    /// <summary>Names become lower case ASCII slugs; accents go, other characters become one dash.</summary>
    [Theory]
    [InlineData("Anna", "anna")]
    [InlineData("Man 1", "man-1")]
    [InlineData("Jean-François Müller", "jean-francois-muller")]
    [InlineData("  Mevrouw  De Smet!! ", "mevrouw-de-smet")]
    [InlineData("People 1 #3", "people-1-3")]
    [InlineData("---", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SluggifiesNames(string name, string slug) => Assert.Equal(slug, ActorIds.Slug(name));

    /// <summary>A taken id gets a number, case ignored; a name with no letters or digits gets the fallback.</summary>
    [Fact]
    public void IdsAreUnique()
    {
        Assert.Equal("anna", ActorIds.For("Anna", new string[0]));
        Assert.Equal("anna-2", ActorIds.For("Anna", new[] { "anna" }));
        Assert.Equal("anna-3", ActorIds.For("ANNA", new[] { "Anna", "anna-2" }));
        Assert.Equal("actor", ActorIds.For("???", null));
        Assert.Equal("actor-2", ActorIds.For("!", new[] { "actor" }));
    }

    /// <summary>A long name is cut at a dash, never mid-word when it can help it.</summary>
    [Fact]
    public void LongNamesAreCut()
    {
        string id = ActorIds.Slug("The Very Honourable Lady Margaret of Wensleydale-Upon-Tyne");
        Assert.True(id.Length <= 32);
        Assert.False(id.EndsWith("-"));
        Assert.StartsWith("the-very-honourable-lady", id);
    }
}
