using System.Runtime.CompilerServices;
using Noggog.IO;

namespace Mutagen.Bethesda.UnitTests;

public static class TestPathing
{
    public static readonly string TempFolderPath = "MutagenUnitTests";

    // C67: one per test run (process), so runs at the same time (net9.0 and net10.0, or two checkouts) never share a
    // test's folder: TempFolder empties its folder when made and deletes it when disposed
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..12];

    public static TempFolder GetTempFolder(string folderName, [CallerMemberName] string? testName = null)
    {
        return TempFolder.FactoryByAddedPath(Path.Combine(TempFolderPath, folderName, $"{testName}-{RunId}"));
    }
}