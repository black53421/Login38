using System.IO;
using Xunit;

namespace Login38.Tests;

/// <summary>
/// The operator's real <c>list.txt</c> and <c>config.ini</c>, as copied into a test
/// project's <c>TestData</c> directory.
/// </summary>
/// <remarks>
/// Those two files are one server's live configuration — its address, and the RSA triple
/// the client decrypts the packet-encryption challenge with — so they are not in the
/// repository. A fresh clone gets a <c>TestData</c> directory with nothing in it.
/// </remarks>
public static class ShippingPackage
{
    /// <summary>Where a test looks for the package.</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "TestData");

    /// <summary>Whether this clone has one.</summary>
    public static bool IsPresent =>
        File.Exists(Path.Combine(Directory, "list.txt")) &&
        File.Exists(Path.Combine(Directory, "config.ini"));
}

/// <summary>
/// A fact that needs the shipping package, and reports as skipped without one.
/// </summary>
/// <remarks>
/// Skipped rather than replaced by a generated fixture: what these prove is that this
/// implementation reads the bytes the previous build already put in players' hands. A
/// file this code wrote itself only proves the code agrees with itself, which the
/// round-trip tests cover already.
///
/// To run them, put the operator's <c>list.txt</c> and <c>config.ini</c> into the test
/// project's <c>TestData</c> directory. They are copied to the output on build.
/// </remarks>
public sealed class ShippingPackageFactAttribute : FactAttribute
{
    public ShippingPackageFactAttribute()
    {
        if (!ShippingPackage.IsPresent)
        {
            Skip = "No shipping package in this clone: TestData/list.txt and TestData/config.ini are absent.";
        }
    }
}
