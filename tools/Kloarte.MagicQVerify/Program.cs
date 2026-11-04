using System;
using System.IO;
using Kloarte.MagicQ;

namespace Kloarte.MagicQVerify;

/// <summary>
/// Verifies an exported show in real MagicQ PC: a developer tool, not a test, because it needs MagicQ and closes it.
/// Exit code 0 = every cue passed, 1 = a cue failed, 2 = it could not run.
/// </summary>
internal static class Program
{
    /// <summary>Runs the tool.</summary>
    private static int Main(string[] args)
    {
        var options = VerifyOptions.Parse(args);
        if (options == null)
        {
            Console.Error.WriteLine(VerifyOptions.Usage);
            return 2;
        }
        if (!File.Exists(options.Show))
        {
            Console.Error.WriteLine($"Show not found: {options.Show}");
            return 2;
        }
        if (MagicQPc.Running && !options.Restart)
        {
            Console.Error.WriteLine("MagicQ PC is running and would be killed: close it, or pass --restart.");
            return 2;
        }
        try
        {
            return new ShowRun(options).Run() ? 0 : 1;
        }
        catch (Exception e) when (e is ArgumentException or IOException or TimeoutException or InvalidDataException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }
}
