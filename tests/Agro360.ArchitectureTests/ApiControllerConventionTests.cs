using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class ApiControllerConventionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly string Controllers = Path.Combine(Root, "src/Hosts/Agro360.Api/Controllers");

    //[Fact]
    //public void ControllerClassNamesAreUnique()
    //{
    //    var names = Sources()
    //        .SelectMany(source => Regex.Matches(source, @"\bclass\s+(\w+Controller)\b").Select(match => match.Groups[1].Value));

    //    Assert.Empty(names.GroupBy(name => name).Where(group => group.Count() > 1));
    //}

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

    [Fact]
    public void AttributeRoutesDoNotUseReservedMvcParametersOrUnbalancedBraces()
    {
        foreach (var source in Sources())
        {
            foreach (Match attribute in Regex.Matches(source, @"\[(?:Route|Http(?:Get|Post|Put|Patch|Delete))\(""([^""]*)""\)"))
            {
                var template = attribute.Groups[1].Value;

                Assert.DoesNotMatch(@"\{(?:action|controller)(?=[:}?])", template.ToLowerInvariant());
                Assert.Equal(template.Count(character => character == '{'), template.Count(character => character == '}'));
            }
        }
    }

    [Fact]
    public void ComplianceLotTransitionsUseExplicitRoutes()
    {
        var source = File.ReadAllText(Path.Combine(Controllers, "ComplianceControllers.cs"));

        Assert.Contains("lots/{id:guid}/block", source);
        Assert.Contains("lots/{id:guid}/unblock", source);
        Assert.DoesNotContain("lots/{id:guid}/{", source);
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Controllers, "*.cs").Select(File.ReadAllText);
}
