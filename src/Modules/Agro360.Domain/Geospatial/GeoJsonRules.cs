using System.Text.Json;

namespace Agro360.Domain.Geospatial;

public static class GeoJsonRules
{
    private static readonly HashSet<string> Types = ["Point", "LineString", "Polygon", "MultiPolygon"];

    public static JsonDocument ParseGeometry(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson)) throw new ArgumentException("A geometria GeoJSON é obrigatória.");
        JsonDocument document;
        try { document = JsonDocument.Parse(geoJson); }
        catch (JsonException exception) { throw new ArgumentException("GeoJSON inválido.", nameof(geoJson), exception); }
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || !Types.Contains(type.GetString() ?? "") ||
            !root.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
        { document.Dispose(); throw new ArgumentException("GeoJSON deve ser uma geometria Point, LineString, Polygon ou MultiPolygon."); }
        ValidateCoordinates(coordinates);
        return document;
    }

    public static void ValidateCoordinates(JsonElement value)
    {
        if (value.GetArrayLength() == 0) throw new ArgumentException("GeoJSON não pode ter coordenadas vazias.");
        if (value[0].ValueKind == JsonValueKind.Number)
        {
            if (value.GetArrayLength() < 2 || !value[0].TryGetDecimal(out var lon) || !value[1].TryGetDecimal(out var lat) || lon is < -180 or > 180 || lat is < -90 or > 90)
                throw new ArgumentException("Coordenadas GeoJSON fora dos limites de longitude/latitude.");
            return;
        }
        foreach (var child in value.EnumerateArray()) { if (child.ValueKind != JsonValueKind.Array) throw new ArgumentException("Estrutura de coordenadas inválida."); ValidateCoordinates(child); }
    }
}
