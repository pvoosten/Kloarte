using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>The rig items of a show document and how to add and remove them (the document keeps its own format).</summary>
public interface IRigDocument
{
    /// <summary>All rig items, in document order.</summary>
    IReadOnlyList<IRigItem> RigItems { get; }

    /// <summary>Adds a copy of `template` (same kind, layout and settings) with a new id after the last rig item.</summary>
    IRigItem AddRigCopy(IRigItem template);

    /// <summary>Removes a rig item.</summary>
    void RemoveRigItem(IRigItem item);
}
