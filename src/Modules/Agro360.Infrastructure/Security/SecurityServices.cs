using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Agro360.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Agro360.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}

public sealed class PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        EnsurePassword(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashSize);
        return $"pbkdf2-sha512${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        try
        {
            var parts = encodedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha512" || !int.TryParse(parts[1], out var iterations))
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void EnsurePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 12
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "A senha deve possuir ao menos 12 caracteres, maiúscula, minúscula, número e símbolo.",
                nameof(password));
        }
    }
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "MNSOFT.Agro360";

    public string Audience { get; init; } = "MNSOFT.Agro360.Clients";

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 20;

    public int RefreshTokenDays { get; init; } = 14;
}

public static class TotpVerifier
{
    public static bool Verify(string base32Secret, string? code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit)) return false;
        byte[] secret;
        try { secret = DecodeBase32(base32Secret); }
        catch (FormatException) { return false; }
        var counter = now.ToUnixTimeSeconds() / 30;
        var input = new byte[8];
        for (var offset = -1; offset <= 1; offset++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(input, counter + offset);
            var hash = HMACSHA256.HashData(secret, input);
            var index = hash[^1] & 0x0f;
            var value = ((hash[index] & 0x7f) << 24) | (hash[index + 1] << 16) | (hash[index + 2] << 8) | hash[index + 3];
            if ((value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture) == code) return true;
        }
        return false;
    }

    public static bool IsValidSecret(string value)
    {
        try { return DecodeBase32(value).Length >= 20; }
        catch (FormatException) { return false; }
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var normalized = value.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        if (normalized.Length == 0 || normalized.Any(character => !alphabet.Contains(character))) throw new FormatException("Segredo TOTP inválido.");
        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in normalized)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits < 8) continue;
            output.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
            buffer &= (1 << bits) - 1;
        }
        return output.ToArray();
    }
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private readonly JwtOptions _options = Validate(options.Value);

    public TokenPair Create(Guid tenantId, Guid userId, string email, IReadOnlyCollection<string> permissions, IReadOnlyCollection<string> roles)
    {
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new("tenant_id", tenantId.ToString())
        };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        var random = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        var refreshToken = $"{tenantId:N}.{random}";
        return new TokenPair(accessToken, refreshToken, expiresAt);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JwtOptions Validate(JwtOptions options)
    {
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey deve possuir pelo menos 32 bytes.");
        }

        return options;
    }
}
