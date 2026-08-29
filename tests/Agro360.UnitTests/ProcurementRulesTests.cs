using Agro360.Domain.Procurement;
using Agro360.SharedKernel;
namespace Agro360.UnitTests;
public sealed class ProcurementRulesTests
{
 [Fact] public void Calculates_order_total_on_backend()=>Assert.Equal(217m,ProcurementRules.OrderTotal([(2m,100m,5m)],10m,12m));
 [Fact] public void Rejects_order_without_items()=>Assert.Throws<DomainException>(()=>ProcurementRules.OrderTotal([],0,0));
 [Fact] public void Rejected_supplier_requires_reason()=>Assert.Throws<DomainException>(()=>ProcurementRules.Supplier("Fornecedor",null,"REJECTED",null));
 [Fact] public void Urgent_requisition_requires_reason()=>Assert.Throws<DomainException>(()=>ProcurementRules.Requisition("URGENT",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),1,""));
 [Fact] public void Receipt_requires_lot_and_expiry()=>Assert.Throws<DomainException>(()=>ProcurementRules.Receipt(10,0,5,false,null,true,null,true,null));
 [Fact] public void Excess_receipt_requires_override_and_reason()=>Assert.Throws<DomainException>(()=>ProcurementRules.Receipt(10,8,3,false,null,false,null,false,null));
 [Fact] public void Non_lowest_quotation_requires_reason()=>Assert.Throws<DomainException>(()=>ProcurementRules.Decision(110,100,null));
}
