using System;
using System.IO;
using System.Reflection;

namespace Login38.TestSupport;

/// <summary>Where the sources are, for the tests that read them rather than run them.</summary>
/// <remarks>
/// Some rules are cheaper to check against the text than against the behaviour, and a few
/// cannot be reached any other way — a line inside a method that only a running game gets
/// to, for instance. Those tests need the checkout, which is found by walking up from the
/// test assembly to the solution file rather than by a path written down anywhere.
/// </remarks>
public static class Checkout
{
    /// <summary>The directory holding <c>Login38.slnx</c>.</summary>
    /// <exception cref="InvalidOperationException">The test assembly is not inside a checkout.</exception>
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Login38.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("No Login38.slnx above the test assembly.");
        }
    }

    /// <summary>Whether a path is inside a build folder rather than the sources.</summary>
    public static bool IsBuildOutput(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var separator = Path.DirectorySeparatorChar;

        return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal) ||
               path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }
}
