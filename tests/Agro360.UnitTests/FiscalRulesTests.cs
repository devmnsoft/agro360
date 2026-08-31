using Agro360.Domain.Fiscal;
using Agro360.SharedKernel;
namespace Agro360.UnitTests;
public sealed class FiscalRulesTests
{
 [Fact] public void Calculates_total_in_backend_with_decimal_values()=>Assert.Equal(210m,FiscalRules.InvoiceTotal([(2m,100m,10m)],10m,2m,3m,5m));
 [Fact] public void Rejects_invoice_without_items()=>Assert.Throws<DomainException>(()=>FiscalRules.InvoiceTotal([],0,0,0,0));
 [Fact] public void Rejects_discount_that_makes_item_negative()=>Assert.Throws<DomainException>(()=>FiscalRules.InvoiceTotal([(1m,10m,11m)],0,0,0,0));
 [Theory,InlineData(-0.0001),InlineData(100.0001)] public void Rejects_invalid_decimal_percentage(decimal value)=>Assert.Throws<DomainException>(()=>FiscalRules.ValidatePercentage(value));
 [Fact] public void Accepts_decimal_percentage()=>FiscalRules.ValidatePercentage(17.1250m,0m,100m);
 [Fact] public void Rejects_invalid_access_key()=>Assert.Throws<DomainException>(()=>FiscalRules.ValidateAccessKey("123"));
 [Fact] public void Accepts_44_digit_access_key()=>FiscalRules.ValidateAccessKey(new string('1',44));
}
