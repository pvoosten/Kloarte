using System;

namespace Kloarte.Model;

/// <summary>A change to the show was refused; the message says why (shown to the user).</summary>
public class ShowEditException : Exception
{
    /// <summary>Creates the exception with the reason.</summary>
    public ShowEditException(string message) : base(message) { }
}
