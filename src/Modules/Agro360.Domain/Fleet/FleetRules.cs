namespace Agro360.Domain.Fleet;

public static class FleetRules
{
    public static readonly string[] AssetStatuses = ["AVAILABLE", "OPERATING", "MAINTENANCE", "UNAVAILABLE", "RESERVED", "WRITTEN_OFF", "SOLD", "INACTIVE"];
    public static void ValidateAsset(string code, string status, decimal odometer, decimal hourMeter)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Código interno é obrigatório.");
        if (!AssetStatuses.Contains(status)) throw new ArgumentException("Status do ativo inválido.");
        if (odometer < 0 || hourMeter < 0) throw new ArgumentException("Odômetro e horímetro não podem ser negativos.");
    }
    public static void ValidateMeterChange(decimal current, decimal next, string? justification, bool canOverride)
    {
        if (next < 0) throw new ArgumentException("Leitura não pode ser negativa.");
        if (next < current && (!canOverride || string.IsNullOrWhiteSpace(justification)))
            throw new InvalidOperationException("Redução de leitura exige justificativa e permissão.");
    }
    public static void ValidateWorkOrderTransition(string status, string? performed, string? cancellationReason)
    {
        if (status == "COMPLETED" && string.IsNullOrWhiteSpace(performed)) throw new InvalidOperationException("Informe os serviços realizados.");
        if (status == "CANCELLED" && string.IsNullOrWhiteSpace(cancellationReason)) throw new InvalidOperationException("Informe o motivo do cancelamento.");
    }
    public static decimal RefuelingTotal(decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new ArgumentException("Quantidade deve ser positiva.");
        if (unitPrice < 0) throw new ArgumentException("Valor unitário não pode ser negativo.");
        return decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
    }
    public static decimal Availability(int periodMinutes, int downtimeMinutes) => periodMinutes <= 0 ? 0 : decimal.Round(Math.Max(0, periodMinutes - downtimeMinutes) * 100m / periodMinutes, 2);
}
