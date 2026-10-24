using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Documentation;

public class InterfaceDocumentationTests
{
    private static string RepoRoot([CallerFilePath] string sourceFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
    }

    private static IEnumerable<string> ContentLines(string path)
    {
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.TrimEnd());
    }

    /// <summary>
    /// The docs pages are published copies of the generated documentation.
    /// They add a page title, nest the generated content one heading level down,
    /// and add blank lines for markdown rendering, but should otherwise match.
    /// </summary>
    [Theory]
    [InlineData("docs/game-specific/oblivion/Oblivion-Link-Interfaces.md", "Mutagen.Bethesda.Oblivion/Documentation/LinkInterfaceDocumentation_Generated.md")]
    [InlineData("docs/game-specific/oblivion/Oblivion-Aspect-Interfaces.md", "Mutagen.Bethesda.Oblivion/Documentation/AspectInterfaceDocumentation_Generated.md")]
    [InlineData("docs/game-specific/skyrim/Skyrim-Link-Interfaces.md", "Mutagen.Bethesda.Skyrim/Documentation/LinkInterfaceDocumentation_Generated.md")]
    [InlineData("docs/game-specific/skyrim/Skyrim-Aspect-Interfaces.md", "Mutagen.Bethesda.Skyrim/Documentation/AspectInterfaceDocumentation_Generated.md")]
    public void DocsPageMatchesGeneratedDocumentation(string docsPage, string generated)
    {
        var root = RepoRoot();
        var docsLines = ContentLines(Path.Combine(root, docsPage)).ToArray();
        docsLines[0].ShouldStartWith("# ");
        var expected = ContentLines(Path.Combine(root, generated))
            .Select(l => l.StartsWith("# ") ? $"#{l}" : l)
            .ToArray();
        docsLines.Skip(1).ShouldBe(expected);
    }
}
