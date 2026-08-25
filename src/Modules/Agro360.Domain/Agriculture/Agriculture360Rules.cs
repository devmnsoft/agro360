using Agro360.SharedKernel;

namespace Agro360.Domain.Agriculture;

public static class Agriculture360Rules
{
    public static void Required(Guid? value, string field)
    {
        if (value is null || value == Guid.Empty) throw new DomainException($"{field} é obrigatório.", $"agriculture.{field.ToLowerInvariant()}_required");
    }

    public static void Positive(decimal? value, string field, bool allowZero = false)
    {
        if (value is null || (allowZero ? value < 0 : value <= 0))
            throw new DomainException($"{field} possui valor inválido.", $"agriculture.{field.ToLowerInvariant()}_invalid");
    }

    public static void Period(DateTimeOffset? start, DateTimeOffset? finish)
    {
        if (start is not null && finish is not null && finish < start)
            throw new DomainException("A data final não pode ser anterior à inicial.", "agriculture.period_invalid");
    }

    public static void Weather(decimal? rainfall, decimal? temperature, decimal? humidity, decimal? wind)
    {
        if (rainfall is < 0 or > 1000 || wind is < 0 or > 250 || temperature is < -60 or > 70 || humidity is < 0 or > 100)
            throw new DomainException("Os valores climáticos estão fora das faixas permitidas.", "agriculture.weather_invalid");
    }

    public static void UniqueFields(IEnumerable<Guid>? fields)
    {
        var values = fields?.ToArray() ?? [];
        if (values.Length == 0) throw new DomainException("Selecione ao menos um talhão.", "agriculture.fields_required");
        if (values.Distinct().Count() != values.Length) throw new DomainException("Um talhão não pode aparecer duas vezes no plano.", "agriculture.field_duplicate");
    }
}
