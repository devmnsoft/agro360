using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Agro360.Application.Abstractions;
using Agro360.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Agro360.UnitTests;

public sealed class AccessTokenSessionTests
{
    [Fact]
    public void CreatedAccessTokenCarriesItsPersistableSessionId()
    {
        var tokenService = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "MNSOFT.Agro360",
                Audience = "MNSOFT.Agro360.Clients",
                SigningKey = "test-signing-key-with-more-than-32-bytes-secure!"
            }),
            new FixedClock(DateTimeOffset.UtcNow));

        var pair = tokenService.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user@agro360.local",
            ["work.read"],
            ["operator"]);

        using var payload = JsonDocument.Parse(DecodePayload(pair.AccessToken));
        var tokenId = Guid.Parse(payload.RootElement.GetProperty("jti").GetString()!);

        Assert.Equal(pair.AccessTokenId, tokenId);
    }

    [Fact]
    public void JwtBearerClaimMappingPreservesSessionIdClaim()
    {
        const string signingKey = "test-signing-key-with-more-than-32-bytes-secure!";
        var tokenService = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "MNSOFT.Agro360",
                Audience = "MNSOFT.Agro360.Clients",
                SigningKey = signingKey
            }),
            new FixedClock(DateTimeOffset.UtcNow));
        var pair = tokenService.Create(Guid.NewGuid(), Guid.NewGuid(), "user@agro360.local", ["work.read"], ["operator"]);
        var principal = new JwtSecurityTokenHandler().ValidateToken(
            pair.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "MNSOFT.Agro360",
                ValidateAudience = true,
                ValidAudience = "MNSOFT.Agro360.Clients",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true
            },
            out _);

        Assert.Equal(pair.AccessTokenId.ToString(), principal.FindFirst("jti")?.Value);
    }

    private static byte[] DecodePayload(string token)
    {
        var payloadSegment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(payloadSegment.PadRight(payloadSegment.Length + (4 - payloadSegment.Length % 4) % 4, '='));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
