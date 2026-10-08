namespace Kloarte.Model;

/// <summary>The console's colour table: gels by id (<see cref="LampPatch.GelId"/>).</summary>
public interface IGelTable
{
    /// <summary>The gel with this id, or null.</summary>
    IGel this[int id] { get; }

    /// <summary>Every id of the table (gel pickers search them, <see cref="GelCodes.Search"/>).</summary>
    System.Collections.Generic.IEnumerable<int> Ids { get; }
}
