using System.Reflection;
using Agro360.Infrastructure.Services;

namespace Agro360.UnitTests;

public sealed class IntelligenceCsvTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("simples", "simples")]
    [InlineData("com,vírgula", "\"com,vírgula\"")]
    [InlineData("com \"aspas\"", "\"com \"\"aspas\"\"\"")]
    [InlineData("duas\nlinhas", "\"duas\nlinhas\"")]
    public void CsvFieldsAreEscapedWithoutLosingContent(string? value, string expected)
    {
        var method = typeof(IntelligenceService).GetMethod(
            "EscapeCsvField",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [value]));
    }
}
