using System.Text.Json;
using Agro360.SharedKernel;

namespace Agro360.Domain.Properties;

public static class PropertyRules
{
    private static readonly HashSet<string> BoundaryTypes = new(StringComparer.Ordinal)
    {
        "Polygon",
        "MultiPolygon"
    };

    public static string NormalizeState(string state)
    {
        var normalized = string.IsNullOrWhiteSpace(state) ? string.Empty : state.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainException("Estado deve ser informado pela sigla com duas letras.", "agro360.properties_state_invalid");
        }

        return normalized;
    }

    public static string? NormalizeOptional(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainException($"{field} deve possuir no máximo {maxLength} caracteres.", "agro360.properties_value_too_long");
        }

        return normalized;
    }

    public static string? NormalizeBoundary(string? boundaryGeoJson)
    {
        if (string.IsNullOrWhiteSpace(boundaryGeoJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(boundaryGeoJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !BoundaryTypes.Contains(type.GetString() ?? string.Empty)
                || !root.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array
                || coordinates.GetArrayLength() == 0)
            {
                throw new DomainException(
                    "O limite do talhão deve ser um GeoJSON Polygon ou MultiPolygon com coordenadas.",
                    "agro360.properties_boundary_invalid");
            }

            return root.GetRawText();
        }
        catch (JsonException exception)
        {
            throw new DomainException(
                $"O limite do talhão não contém um GeoJSON válido: {exception.Message}",
                "agro360.properties_boundary_invalid");
        }
    }

    public static void EnsureFieldsFit(decimal farmAreaHa, decimal allocatedAreaHa, decimal candidateAreaHa)
    {
        Guard.Positive(farmAreaHa, "Área total da fazenda");
        Guard.NonNegative(allocatedAreaHa, "Área já alocada");
        Guard.Positive(candidateAreaHa, "Área do talhão");
        if (allocatedAreaHa + candidateAreaHa > farmAreaHa)
        {
            throw new DomainException(
                "A soma das áreas dos talhões não pode ultrapassar a área total da fazenda.",
                "agro360.properties_field_area_exceeded");
        }
    }
}
