using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class ApiControllerConventionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly string Controllers = Path.Combine(Root, "src/Hosts/Agro360.Api/Controllers");

    [Fact]
    public void ControllerClassNamesAreUnique()
    {
        var names = Sources()
            .SelectMany(source => Regex.Matches(source, @"\bclass\s+(\w+Controller)\b").Select(match => match.Groups[1].Value));

        Assert.Empty(names.GroupBy(name => name).Where(group => group.Count() > 1));
    }

    [Fact]
    public void ActionsDoNotHideControllerBaseMembers()
    {
        var reservedNames = new[] { "Request", "Response", "User", "File" };

        foreach (var source in Sources())
            foreach (var name in reservedNames)
                Assert.DoesNotMatch($@"\bpublic\s+(?:async\s+)?[^\r\n{{;=]+\s+{name}\s*\(", source);
    }

    [Fact]
    public void ControllersDoNotCombinePartialClassesWithPrimaryConstructors()
    {
        foreach (var source in Sources())
            Assert.DoesNotMatch(@"\bpartial\s+class\s+\w+Controller\s*\(", source);
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Controllers, "*.cs").Select(File.ReadAllText);
}
