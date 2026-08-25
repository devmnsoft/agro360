using System.Security.Cryptography;
using System.Text;
using Agro360.SharedKernel;

namespace Agro360.Domain.Traceability;

public static class Sprint10Rules
{
    public static void RequireOrigin(Guid? propertyId, string? producer) { if (propertyId is null && string.IsNullOrWhiteSpace(producer)) throw new DomainException("O lote deve possuir origem identificada.", "traceability.origin_required"); }
    public static bool ProcessingComplies(DateTimeOffset start, DateTimeOffset end, decimal? temperature, int minimumMinutes, decimal? minimumTemperature)
    { if (end <= start) throw new DomainException("Período de beneficiamento inválido."); return (end-start).TotalMinutes >= minimumMinutes && (minimumTemperature is null || temperature >= minimumTemperature); }
    public static string LedgerHash(string? previousHash, string payload, string metadata) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{previousHash}|{payload}|{metadata}"))).ToLowerInvariant();
    public static decimal Commission(decimal saleAmount, decimal value, bool percentage) { if (saleAmount < 0 || value < 0 || percentage && value > 100) throw new DomainException("Regra de comissão inválida."); return decimal.Round(percentage ? saleAmount*value/100 : value, 2); }
    public static void ValidateSplit(decimal total, IEnumerable<decimal> parts) { if (total < 0 || parts.Any(x=>x<0) || decimal.Round(parts.Sum(),2) != decimal.Round(total,2)) throw new DomainException("O split deve distribuir exatamente o total.", "sales.split_invalid"); }
    public static void RequireRouteAuthorization(string condition, bool authorized) { if (condition.Equals("INTERDICTED",StringComparison.OrdinalIgnoreCase) && !authorized) throw new DomainException("Trecho interditado exige autorização.", "logistics.authorization_required"); }
}
