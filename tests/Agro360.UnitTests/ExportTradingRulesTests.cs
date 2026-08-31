using Agro360.Domain.Export;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class ExportTradingRulesTests
{
    [Fact] public void Contract_total_is_calculated_in_backend_with_decimal()=>Assert.Equal(31.25m,ExportTradingRules.ContractTotal([(2.5m,10m),(1m,6.25m)]));
    [Fact] public void Contract_without_items_is_rejected()=>Assert.Throws<DomainException>(()=>ExportTradingRules.ContractTotal([]));
    [Fact] public void Currency_conversion_requires_positive_rate_and_date(){Assert.Equal(617.28m,ExportTradingRules.Convert(123.456m,5m,new DateOnly(2026,8,31)));Assert.Throws<DomainException>(()=>ExportTradingRules.Convert(1m,0m,DateOnly.FromDateTime(DateTime.UtcNow)));Assert.Throws<DomainException>(()=>ExportTradingRules.Convert(1m,1m,null));}
    [Fact] public void Margin_rejects_negative_costs(){Assert.Equal(70m,ExportTradingRules.Margin(100m,[10m,20m]));Assert.Throws<DomainException>(()=>ExportTradingRules.Margin(100m,[-1m]));}
}
