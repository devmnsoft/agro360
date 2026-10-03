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

    [Fact]
    public void ValidatesPdfMagicBytesCorrectly()
    {
        var pdfHeader = "%PDF-1.7\r\n"u8.ToArray();
        DocumentRules.ValidateContentSignature(".pdf", pdfHeader);
    }

    [Fact]
    public void RejectsDisguisedExecutableWithPdfExtension()
    {
        var mzHeader = "MZ\x90\x00\x03\x00\x00\x00"u8.ToArray();
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateContentSignature(".pdf", mzHeader));
        Assert.Equal("documents.invalid_content", ex.Code);
    }

    [Fact]
    public void RejectsEmptyHeaderSignature()
    {
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateContentSignature(".pdf", Array.Empty<byte>()));
        Assert.Equal("documents.empty_file", ex.Code);
    }

    [Fact]
    public void RejectsBinaryContentInCsv()
    {
        var binaryCsv = new byte[] { (byte)'a', (byte)',', (byte)'b', 0x00, (byte)'c' };
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateContentSignature(".csv", binaryCsv));
        Assert.Equal("documents.invalid_content", ex.Code);
    }

    [Fact]
    public void RejectsArbitraryZipAsDocxOrXlsx()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("payload.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not an office document");
        }
        ms.Position = 0;

        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateOfficeStructure(".docx", ms));
        Assert.Equal("documents.invalid_content", ex.Code);
    }

    [Fact]
    public void AcceptsValidDocxStructure()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var ctEntry = zip.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open())) writer.Write("<Types/>");
            var wordEntry = zip.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(wordEntry.Open())) writer.Write("<document/>");
        }
        ms.Position = 0;

        DocumentRules.ValidateOfficeStructure(".docx", ms);
    }

    [Fact]
    public void AcceptsValidXlsxStructure()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var ctEntry = zip.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open())) writer.Write("<Types/>");
            var xlEntry = zip.CreateEntry("xl/workbook.xml");
            using (var writer = new StreamWriter(xlEntry.Open())) writer.Write("<workbook/>");
        }
        ms.Position = 0;

        DocumentRules.ValidateOfficeStructure(".xlsx", ms);
    }

    [Fact]
    public void RejectsMalformedXmlContent()
    {
        var malformedXml = "<root><unclosedTag></root>"u8.ToArray();
        using var ms = new MemoryStream(malformedXml);
        var ex = Assert.Throws<DomainException>(() => DocumentRules.ValidateXmlContent(ms));
        Assert.Equal("documents.invalid_content", ex.Code);
    }

    [Fact]
    public void AcceptsValidXmlContent()
    {
        var validXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><item>OK</item></root>"u8.ToArray();
        using var ms = new MemoryStream(validXml);
        DocumentRules.ValidateXmlContent(ms);
    }
}
