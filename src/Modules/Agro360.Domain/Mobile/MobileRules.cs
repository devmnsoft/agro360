using Agro360.SharedKernel;

namespace Agro360.Domain.Mobile;

public static class MobileRules
{
    private static readonly HashSet<string> EntityTypes = new(StringComparer.OrdinalIgnoreCase)
        { "property", "plot", "animal", "animal-lot", "machine", "stock-item", "stored-lot", "receipt", "certificate" };

    public static void ValidateLocation(decimal? latitude, decimal? longitude, decimal? accuracy)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new DomainException("Latitude ou longitude fora da faixa válida.", "mobile.invalid_location");
        if ((latitude is null) != (longitude is null) || accuracy < 0)
            throw new DomainException("Informe latitude e longitude juntas e precisão não negativa.", "mobile.invalid_location");
    }

    public static void ValidateEntity(string type, Guid id)
    {
        if (!EntityTypes.Contains(type) || id == Guid.Empty)
            throw new DomainException("Entidade vinculada inválida.", "mobile.invalid_entity");
    }

    public static void ValidateQuickRecord(string kind, decimal? quantity, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new DomainException("Tipo do registro é obrigatório.");
        if (quantity < 0) throw new DomainException("Quantidade não pode ser negativa.", "mobile.negative_quantity");
        if (occurredAt == default || occurredAt > DateTimeOffset.UtcNow.AddMinutes(10)) throw new DomainException("Data do registro é inválida.");
    }

    public static void ValidateManualLocation(string source, string? reason, decimal? latitude, decimal? longitude, decimal? accuracy)
    {
        ValidateLocation(latitude, longitude, accuracy);
        if (source is not ("GPS" or "MANUAL")) throw new DomainException("Origem da localização inválida.", "mobile.invalid_location_source");
        if (source == "GPS" && latitude is null) throw new DomainException("O GPS não retornou coordenadas. Use o modo manual com justificativa.", "mobile.gps_required");
        if (source == "MANUAL" && string.IsNullOrWhiteSpace(reason)) throw new DomainException("Justifique o check-in sem GPS.", "mobile.manual_reason_required");
    }
}
