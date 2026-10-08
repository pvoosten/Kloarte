namespace Kloarte.Model;

/// <summary>
/// The key a property has in the project files: camelCase (<c>RigItem</c> is <c>rigItem</c>, as in the files and the
/// JSON Schemas).
/// </summary>
public static class FileKeys
{
    /// <summary>The file key of a property name.</summary>
    public static string Of(string propertyName) =>
        string.IsNullOrEmpty(propertyName) ? propertyName : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
