using System.Security.Cryptography;
using Agro360.SharedKernel;

namespace Agro360.Domain.Documents;

public static class DocumentRules
{
    public static readonly ISet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".csv", ".txt", ".xml", ".docx", ".xlsx" };
    public const long DefaultMaximumBytes = 25 * 1024 * 1024;

    public static string ValidateFile(string fileName, string contentType, long length, long maximumBytes = DefaultMaximumBytes)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName) throw new DomainException("Nome de arquivo inválido.", "documents.invalid_name");
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) throw new DomainException("Extensão de arquivo não permitida.", "documents.invalid_extension");
        if (length <= 0 || length > maximumBytes) throw new DomainException($"O arquivo deve ter até {maximumBytes / 1024 / 1024} MB.", "documents.invalid_size");
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Contains('\r') || contentType.Contains('\n')) throw new DomainException("Tipo MIME inválido.", "documents.invalid_mime");
        ValidateMimeCompatibility(extension, contentType);
        return extension;
    }

    public static void ValidateMimeCompatibility(string extension, string contentType)
    {
        var mime = contentType.Trim().ToLowerInvariant();
        var valid = extension switch
        {
            ".pdf" => mime == "application/pdf" || mime == "application/octet-stream",
            ".png" => mime == "image/png",
            ".jpg" or ".jpeg" => mime is "image/jpeg" or "image/jpg",
            ".webp" => mime == "image/webp",
            ".csv" => mime is "text/csv" or "application/vnd.ms-excel" or "text/plain" or "application/octet-stream",
            ".txt" => mime is "text/plain",
            ".xml" => mime is "text/xml" or "application/xml",
            ".docx" => mime is "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/zip" or "application/octet-stream",
            ".xlsx" => mime is "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or "application/zip" or "application/octet-stream",
            _ => false
        };
        if (!valid) throw new DomainException($"Tipo MIME '{contentType}' incompatível com a extensão '{extension}'.", "documents.invalid_mime");
    }

    public static void ValidateContentSignature(string extension, byte[] header)
    {
        if (header is null || header.Length == 0)
            throw new DomainException("O arquivo enviado está vazio ou não pôde ser lido.", "documents.empty_file");

        var ext = (extension ?? "").Trim().ToLowerInvariant();
        bool valid = ext switch
        {
            ".pdf" => header.Length >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D, // %PDF-
            ".png" => header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".webp" => header.Length >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50, // RIFF....WEBP
            ".docx" or ".xlsx" => header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08), // PK zip container
            ".xml" => IsValidXmlHeader(header),
            ".csv" or ".txt" => IsValidTextHeader(header),
            _ => false
        };

        if (!valid)
            throw new DomainException($"O conteúdo do arquivo é incompatível com a extensão '{extension}'.", "documents.invalid_content");
    }

    private static bool IsValidXmlHeader(byte[] header)
    {
        var text = System.Text.Encoding.UTF8.GetString(header).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || text.StartsWith('<');
    }

    private static bool IsValidTextHeader(byte[] header)
    {
        // Rejeita executáveis DOS/PE (MZ) e ELF
        if (header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A) return false;
        if (header.Length >= 4 && header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46) return false;

        // Arquivos de texto não devem conter bytes nulos no início
        for (int i = 0; i < Math.Min(header.Length, 512); i++)
        {
            if (header[i] == 0x00) return false;
        }
        return true;
    }


    public static async Task<string> Sha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void ValidateDecision(string status, string? reason)
    {
        if (status is not ("VALIDATED" or "REJECTED")) throw new DomainException("Decisão de evidência inválida.", "documents.validation");
        if (status == "REJECTED" && string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo da rejeição.", "documents.validation");
    }

    public static void RequireReason(string? reason, string operation)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException($"Informe o motivo para {operation}.", "documents.reason_required");
    }
}
