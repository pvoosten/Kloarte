namespace Kloarte.Mcp;

/// <summary>The result of a tool call as the tool writes it: text and images, in order.</summary>
public interface IMcpReply
{
    /// <summary>Adds a text.</summary>
    void Text(string text);

    /// <summary>Adds an image (`data` in the format of `mimeType`, e.g. "image/png").</summary>
    void Image(byte[] data, string mimeType);
}
