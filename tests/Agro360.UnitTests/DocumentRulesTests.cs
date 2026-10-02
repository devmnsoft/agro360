using Agro360.Domain.Documents;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class DocumentRulesTests
{
    [Theory]
    [InlineData("contrato.pdf", "application/pdf")]
    [InlineData("foto.png", "image/png")]
    [InlineData("laudo.jpg", "image/jpeg")]
    [InlineData("dados.csv", "text/csv")]
    [InlineData("relatorio.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void AcceptsValidFileAndMime(string fileName, string mimeType)
    {
        var ext = DocumentRules.ValidateFile(fileName, mimeType, 1024);
        Assert.NotNull(ext);
    }

    [Theory]
    [InlineData("malicioso.exe", "application/x-msdownload")]
    [InlineData("script.sh", "application/x-sh")]
    [InlineData("payload.bat", "text/plain")]
    public void RejectsUnauthorizedExtension(string fileName, string mimeType)
    {
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateFile(fileName, mimeType, 1024));
        Assert.Equal("documents.invalid_extension", ex.Code);
    }

    [Theory]
    [InlineData("contrato.pdf", "image/png")]
    [InlineData("laudo.csv", "application/pdf")]
    public void RejectsMismatchedMimeType(string fileName, string mimeType)
    {
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateFile(fileName, mimeType, 1024));
        Assert.Equal("documents.invalid_mime", ex.Code);
    }

    [Fact]
    public void RejectsOversizedFile()
    {
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateFile("relatorio.pdf", "application/pdf", 30 * 1024 * 1024, 25 * 1024 * 1024));
        Assert.Equal("documents.invalid_size", ex.Code);
    }
}
