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
        return extension;
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
