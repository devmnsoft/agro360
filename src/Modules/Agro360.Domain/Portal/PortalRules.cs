using System.Net.Mail;
using System.Text.RegularExpressions;
using Agro360.SharedKernel;

namespace Agro360.Domain.Portal;

public static class PortalRules
{
    public static readonly string[] Profiles =
    [
        "PRODUCER",
        "COOPERATIVE_MEMBER",
        "B2B_CUSTOMER",
        "BUYER",
        "SUPPLIER",
        "TRANSPORTER",
        "EXTERNAL_REPRESENTATIVE",
        "EXTERNAL_AUDITOR",
        "PARTNER_TECHNICIAN"
    ];

    public static string Profile(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && Profiles.Contains(normalized)
            ? normalized
            : throw new DomainException("Selecione um perfil externo válido.", "portal.profile");
    }

    public static string Email(string value)
    {
        try
        {
            var candidate = value.Trim();
            var email = new MailAddress(candidate).Address;
            return string.Equals(email, candidate, StringComparison.OrdinalIgnoreCase)
                ? email.ToLowerInvariant()
                : throw new FormatException();
        }
        catch
        {
            throw new DomainException("Informe um e-mail válido.", "portal.email");
        }
    }

    public static void Invitation(DateTimeOffset expiresAt, DateTimeOffset now, DateTimeOffset? revokedAt, DateTimeOffset? acceptedAt)
    {
        if (revokedAt is not null)
            throw new DomainException("Este convite foi revogado.", "portal.invitation.revoked");
        if (acceptedAt is not null)
            throw new DomainException("Este convite já foi utilizado.", "portal.invitation.accepted");
        if (expiresAt <= now)
            throw new DomainException("Este convite expirou. Solicite um novo acesso.", "portal.invitation.expired");
    }

    public static void Quote(IReadOnlyCollection<(Guid ListingId, decimal Quantity)> items)
    {
        if (items.Count == 0)
            throw new DomainException("Adicione ao menos um item à cotação.", "portal.quote.items");
        if (items.Any(x => x.ListingId == Guid.Empty || x.Quantity <= 0))
            throw new DomainException("A quantidade de cada item deve ser maior que zero.", "portal.quote.quantity");
    }

    public static void Request(string subject, string description)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 160)
            throw new DomainException("Informe um assunto com até 160 caracteres.", "portal.request.subject");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 10)
            throw new DomainException("Descreva a solicitação com pelo menos 10 caracteres.", "portal.request.description");
    }

    public static void CancelRequest(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new DomainException("Informe o motivo do cancelamento (mínimo de 5 caracteres).", "portal.request.cancel_reason");
    }

    public static void ResolveRequest(string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution) || resolution.Trim().Length < 5)
            throw new DomainException("Informe a resolução da solicitação (mínimo de 5 caracteres).", "portal.request.resolution");
    }

    public static void RejectRequest(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new DomainException("Informe o motivo da rejeição (mínimo de 5 caracteres).", "portal.request.rejection_reason");
    }

    public static void Password(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new DomainException("A senha deve ter pelo menos 12 caracteres.", "portal.password.length");

        var hasUpper = password.Any(char.IsUpper);
        var hasLower = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);
        var hasSpecial = password.Any(ch => !char.IsLetterOrDigit(ch));

        if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
            throw new DomainException("A senha deve conter letras maiúsculas, minúsculas, números e caracteres especiais.", "portal.password.complexity");
    }

    public static void EvidenceSubmission(string description, string entityType, Guid entityId)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 5)
            throw new DomainException("Informe uma descrição para a evidência com no mínimo 5 caracteres.", "portal.evidence.description");
        if (string.IsNullOrWhiteSpace(entityType) || entityId == Guid.Empty)
            throw new DomainException("Entidade vinculada à evidência inválida.", "portal.evidence.entity");
    }
}
