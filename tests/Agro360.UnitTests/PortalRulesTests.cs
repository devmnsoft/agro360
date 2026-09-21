using Agro360.Domain.Portal;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class PortalRulesTests
{
    [Theory]
    [InlineData("PRODUCER")]
    [InlineData("COOPERATIVE_MEMBER")]
    [InlineData("B2B_CUSTOMER")]
    [InlineData("BUYER")]
    [InlineData("SUPPLIER")]
    [InlineData("TRANSPORTER")]
    [InlineData("EXTERNAL_REPRESENTATIVE")]
    [InlineData("EXTERNAL_AUDITOR")]
    [InlineData("PARTNER_TECHNICIAN")]
    public void ValidProfilesReturnNormalized(string validProfile)
    {
        var result = PortalRules.Profile(validProfile.ToLowerInvariant());
        Assert.Equal(validProfile, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ADMIN")]
    [InlineData("SUPER_ADMIN")]
    [InlineData("INTERNAL_OPERATOR")]
    [InlineData(null)]
    public void InvalidProfilesThrowDomainException(string? invalidProfile)
    {
        var ex = Assert.Throws<DomainException>(() => PortalRules.Profile(invalidProfile!));
        Assert.Equal("portal.profile", ex.Code);
    }

    [Theory]
    [InlineData("produtor@cooperativa.com.br", "produtor@cooperativa.com.br")]
    [InlineData("  COMPRADOR@EMPRESA.COM  ", "comprador@empresa.com")]
    public void ValidEmailReturnsNormalized(string input, string expected)
    {
        var result = PortalRules.Email(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("email-invalido")]
    [InlineData("sem-arroba.com")]
    public void InvalidEmailThrowsDomainException(string input)
    {
        var ex = Assert.Throws<DomainException>(() => PortalRules.Email(input));
        Assert.Equal("portal.email", ex.Code);
    }

    [Fact]
    public void ValidInvitationDoesNotThrow()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(7);
        PortalRules.Invitation(expiresAt, now, revokedAt: null, acceptedAt: null);
    }

    [Fact]
    public void ExpiredInvitationThrowsDomainException()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(-1);
        var ex = Assert.Throws<DomainException>(() => PortalRules.Invitation(expiresAt, now, null, null));
        Assert.Equal("portal.invitation.expired", ex.Code);
    }

    [Fact]
    public void RevokedInvitationThrowsDomainException()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(7);
        var ex = Assert.Throws<DomainException>(() => PortalRules.Invitation(expiresAt, now, revokedAt: now.AddHours(-1), null));
        Assert.Equal("portal.invitation.revoked", ex.Code);
    }

    [Fact]
    public void AcceptedInvitationThrowsDomainException()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(7);
        var ex = Assert.Throws<DomainException>(() => PortalRules.Invitation(expiresAt, now, null, acceptedAt: now.AddHours(-1)));
        Assert.Equal("portal.invitation.accepted", ex.Code);
    }

    [Fact]
    public void ValidQuoteItemsDoNotThrow()
    {
        var items = new[] { (Guid.NewGuid(), 100.5m), (Guid.NewGuid(), 50m) };
        PortalRules.Quote(items);
    }

    [Fact]
    public void EmptyQuoteItemsThrowDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => PortalRules.Quote(Array.Empty<(Guid, decimal)>()));
        Assert.Equal("portal.quote.items", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void InvalidQuoteQuantityThrowsDomainException(decimal invalidQuantity)
    {
        var items = new[] { (Guid.NewGuid(), invalidQuantity) };
        var ex = Assert.Throws<DomainException>(() => PortalRules.Quote(items));
        Assert.Equal("portal.quote.quantity", ex.Code);
    }

    [Fact]
    public void ValidRequestDoesNotThrow()
    {
        PortalRules.Request("Dúvida sobre colheita", "Gostaria de obter informações sobre o romaneio do lote.");
    }

    [Theory]
    [InlineData("", "Descrição detalhada com mais de 10 caracteres")]
    [InlineData(null, "Descrição detalhada com mais de 10 caracteres")]
    public void InvalidSubjectThrowsDomainException(string? subject, string description)
    {
        var ex = Assert.Throws<DomainException>(() => PortalRules.Request(subject!, description));
        Assert.Equal("portal.request.subject", ex.Code);
    }

    [Theory]
    [InlineData("Assunto válido", "Curto")]
    [InlineData("Assunto válido", "")]
    public void InvalidDescriptionThrowsDomainException(string subject, string description)
    {
        var ex = Assert.Throws<DomainException>(() => PortalRules.Request(subject, description));
        Assert.Equal("portal.request.description", ex.Code);
    }

    [Theory]
    [InlineData("Motivo claro de cancelamento pelo produtor")]
    [InlineData("Desistência")]
    public void ValidCancelReasonDoesNotThrow(string reason)
    {
        PortalRules.CancelRequest(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void InvalidCancelReasonThrowsDomainException(string reason)
    {
        var ex = Assert.Throws<DomainException>(() => PortalRules.CancelRequest(reason));
        Assert.Equal("portal.request.cancel_reason", ex.Code);
    }

    [Theory]
    [InlineData("SenhaForte@2026!")]
    [InlineData("Agro360#SecurePass!")]
    public void StrongPasswordDoesNotThrow(string password)
    {
        PortalRules.Password(password);
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("semnumeroESIMBOLO")]
    [InlineData("SEMMINUSCULANEMSIMBOLO123")]
    [InlineData("SemEspecial12345")]
    public void WeakPasswordThrowsDomainException(string password)
    {
        Assert.Throws<DomainException>(() => PortalRules.Password(password));
    }
}
