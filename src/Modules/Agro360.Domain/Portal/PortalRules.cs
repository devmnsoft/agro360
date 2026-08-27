using System.Net.Mail;
using Agro360.SharedKernel;

namespace Agro360.Domain.Portal;

public static class PortalRules
{
    public static readonly string[] Profiles = ["PRODUCER", "COOPERATIVE_MEMBER", "B2B_CUSTOMER", "BUYER", "SUPPLIER", "TRANSPORTER", "EXTERNAL_REPRESENTATIVE", "EXTERNAL_AUDITOR", "PARTNER_TECHNICIAN"];
    public static string Profile(string value) { var normalized=value?.Trim().ToUpperInvariant(); return normalized is not null&&Profiles.Contains(normalized)?normalized:throw new DomainException("Selecione um perfil externo válido.", "portal.profile"); }
    public static string Email(string value) { try { var email = new MailAddress(value.Trim()).Address.ToLowerInvariant(); return email == value.Trim().ToLowerInvariant() ? email : throw new FormatException(); } catch { throw new DomainException("Informe um e-mail válido.", "portal.email"); } }
    public static void Invitation(DateTimeOffset expiresAt, DateTimeOffset now, DateTimeOffset? revokedAt, DateTimeOffset? acceptedAt) { if (revokedAt is not null) throw new DomainException("Este convite foi revogado.", "portal.invitation.revoked"); if (acceptedAt is not null) throw new DomainException("Este convite já foi utilizado.", "portal.invitation.accepted"); if (expiresAt <= now) throw new DomainException("Este convite expirou. Solicite um novo acesso.", "portal.invitation.expired"); }
    public static void Quote(IReadOnlyCollection<(Guid ListingId, decimal Quantity)> items) { if (items.Count == 0) throw new DomainException("Adicione ao menos um item à cotação.", "portal.quote.items"); if (items.Any(x => x.ListingId == Guid.Empty || x.Quantity <= 0)) throw new DomainException("A quantidade de cada item deve ser maior que zero.", "portal.quote.quantity"); }
    public static void Request(string subject, string description) { if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 160) throw new DomainException("Informe um assunto com até 160 caracteres.", "portal.request.subject"); if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 10) throw new DomainException("Descreva a solicitação com pelo menos 10 caracteres.", "portal.request.description"); }
}
