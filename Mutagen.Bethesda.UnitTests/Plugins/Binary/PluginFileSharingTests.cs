using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

// C175: a plugin an overlay holds open could not be replaced (a mod manager installing a new version): Mutagen opened
// plugin files with FileShare.Read only. Now the default also allows delete (and so rename/replace); the overlay keeps
// reading the file it opened. Real files, not the mock file system: the sharing is the operating system's.
public class PluginFileSharingTests
{
    private static string WriteMod(string path, string keywordEditorId)
    {
        var modKey = ModKey.FromFileName(Path.GetFileName(path));
        var mod = new StarfieldMod(modKey, StarfieldRelease.Starfield);
        mod.Keywords.AddNew(keywordEditorId);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
        return path;
    }

    [Fact]
    public void OpenOverlay_FileCanBeReplaced_OverlayKeepsReadingTheOldFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MutagenSharing" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = WriteMod(Path.Combine(dir, "Shared.esp"), "OldKeyword");
            Directory.CreateDirectory(Path.Combine(dir, "new"));
            var replacement = WriteMod(Path.Combine(dir, "new", "Shared.esp"), "NewKeyword");

            using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
            // A mod manager replacing the plugin while it is open: delete it, then put the new one in its place. (Windows
            // refuses to rename a file over one that is open, whatever the sharing, unless the tool asks for POSIX rename
            // semantics; deleting first works, the open handle keeps the old file.)
            File.Delete(path);
            File.Move(replacement, path);

            overlay.Keywords.Single().EditorID.ShouldBe("OldKeyword");
            using var reread = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
            reread.Keywords.Single().EditorID.ShouldBe("NewKeyword");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ReadOnlySharing_StillAvailable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MutagenSharing" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = WriteMod(Path.Combine(dir, "Locked.esp"), "Keyword");
            using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield,
                new BinaryReadParameters { FileShare = FileShare.Read });
            if (OperatingSystem.IsWindows())
            {
                Should.Throw<IOException>(() => File.Delete(path));
            }
            overlay.Keywords.Single().EditorID.ShouldBe("Keyword");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
