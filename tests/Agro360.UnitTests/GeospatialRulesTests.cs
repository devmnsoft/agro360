using Agro360.Domain.Geospatial;
namespace Agro360.UnitTests;
public sealed class GeospatialRulesTests
{
 [Theory]
 [InlineData("{\"type\":\"Point\",\"coordinates\":[-48.5,-1.4]}")]
 [InlineData("{\"type\":\"LineString\",\"coordinates\":[[-48.5,-1.4],[-48.4,-1.3]]}")]
 [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[-48.5,-1.5],[-48.4,-1.5],[-48.5,-1.4],[-48.5,-1.5]]]}")]
 public void Accepts_supported_geometries(string json){using var result=GeoJsonRules.ParseGeometry(json);Assert.NotNull(result);}
 [Theory]
 [InlineData("")][InlineData("not-json")][InlineData("{\"type\":\"Point\",\"coordinates\":[]}")][InlineData("{\"type\":\"Point\",\"coordinates\":[181,0]}")]
 public void Rejects_invalid_geojson(string json)=>Assert.Throws<ArgumentException>(()=>GeoJsonRules.ParseGeometry(json));
}
