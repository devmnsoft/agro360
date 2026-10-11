using Agro360.Domain.Agriculture;

namespace Agro360.Infrastructure.Services;

/// <summary>
/// Único mapeador canônico do status numérico de safra (coluna
/// <c>agriculture_seasons.status</c>, CHECK 1..5) para os códigos estáveis da API.
/// Derivado exclusivamente de <see cref="SeasonStatus"/> (fonte única — qualquer
/// mudança no enum propaga para os códigos); não duplicar este CASE em outros serviços.
/// </summary>
public static class SeasonStatusCodes
{
    public static string ToCode(int status) =>
        Enum.IsDefined(typeof(SeasonStatus), status) ? ((SeasonStatus)status).ToString().ToUpperInvariant() : "UNKNOWN";

    /// <summary>CASE SQL gerado do mesmo mapeamento, para queries que projetam o código no banco.</summary>
    public static string SqlCase(string column)
    {
        var whens = Enum.GetValues<SeasonStatus>()
            .Aggregate(string.Empty, (acc, value) => acc + $"when {(int)value} then '{ToCode((int)value)}' ");
        return $"case {column} {whens}else 'UNKNOWN' end";
    }
}
