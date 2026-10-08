using System;

using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>A patch change that would break the show (address in use, wrong fixture type...); the message says why.</summary>
public sealed class PatchException : ShowEditException
{
    /// <summary>Creates the exception with a message for the user.</summary>
    public PatchException(string message) : base(message) { }
}
