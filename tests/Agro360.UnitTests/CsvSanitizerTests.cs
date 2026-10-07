using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class CsvSanitizerTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1:A10)", "'@SUM(A1:A10)")]
    [InlineData("  =1+1", "'  =1+1")]
    [InlineData(" \t=1+1", "' \t=1+1")]
    [InlineData("\tmalicious", "'\tmalicious")]
    [InlineData("\rmalicious", "\"'\rmalicious\"")]
    public void NeutralizesFormulaTriggers(string input, string expected)
    {
        var result = CsvSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Soja Safra 2026", "Soja Safra 2026")]
    [InlineData("12345", "12345")]
    [InlineData("Produto, com virgula", "\"Produto, com virgula\"")]
    [InlineData("Item \"especial\"", "\"Item \"\"especial\"\"\"")]
    public void PreservesSafeContentAndEscapesQuotes(string input, string expected)
    {
        var result = CsvSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, CsvSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, CsvSanitizer.Sanitize(string.Empty));
        Assert.Equal(string.Empty, CsvSanitizer.Sanitize("   "));
    }
}
