namespace Agro360.Application.Contracts;

public sealed record AccountCommand(string Code, string Name, string Type, string Nature, string? Category, Guid? ParentId, bool Active = true, int DisplayOrder = 0);
public sealed record CostCenterCommand(string Code, string Name, string Kind, Guid? ReferenceId, bool Active = true);
public sealed record TitleCommand(string PartyName, string? Document, decimal OriginalAmount, decimal Discount, decimal Interest, decimal Fine, DateOnly IssuedOn, DateOnly DueOn, Guid AccountId, Guid? CostCenterId, string? Notes, Guid? SourceId = null);
public sealed record SettlementCommand(decimal Amount, DateOnly Date, Guid AccountId, string? Notes);
public sealed record CancelFinanceCommand(string Reason);
public sealed record ManualEntryCommand(string Type, decimal Amount, DateOnly Date, Guid AccountId, Guid? CostCenterId, Guid? PropertyId, Guid? SeasonId, string? Notes, string Origin = "MANUAL");
public sealed record SaleCommand(string Buyer, Guid ProductId, string OriginType, Guid? OriginId, decimal Quantity, string Unit, decimal UnitPrice, DateOnly SoldOn, DateOnly? DeliveryOn, string PaymentTerms, int Installments = 1, Guid? WarehouseId = null, Guid? CostCenterId = null);
public sealed record FinanceQuery(DateOnly? From = null, DateOnly? To = null, Guid? PropertyId = null, Guid? SeasonId = null, Guid? PlotId = null, Guid? HerdId = null, Guid? MachineId = null, string? GroupBy = null);
public sealed record BudgetCommand(string Name, string Type, DateOnly PeriodStart, DateOnly PeriodEnd, decimal PlannedAmount, Guid CategoryId, Guid? CostCenterId, Guid? SeasonId, Guid? PropertyId, Guid? PlotId, string? Notes);
public sealed record ApproveBudgetCommand(string? Notes);
public sealed record ReconciliationCommand(Guid SettlementId, string Reference);

public interface IFinanceService
{
    Task<IReadOnlyList<dynamic>> ListAccountsAsync(CancellationToken ct); Task<Guid> SaveAccountAsync(Guid? id, AccountCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListCostCentersAsync(CancellationToken ct); Task<Guid> SaveCostCenterAsync(Guid? id, CostCenterCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListTitlesAsync(bool payable, CancellationToken ct); Task<dynamic?> GetTitleAsync(bool payable, Guid id, CancellationToken ct);
    Task<Guid> SaveTitleAsync(bool payable, Guid? id, TitleCommand command, CancellationToken ct); Task SettleAsync(bool payable, Guid id, SettlementCommand command, CancellationToken ct); Task CancelAsync(bool payable, Guid id, CancelFinanceCommand command, CancellationToken ct);
    Task<Guid> AddManualEntryAsync(ManualEntryCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> CashFlowAsync(FinanceQuery query, CancellationToken ct); Task<IReadOnlyList<dynamic>> ResultsAsync(FinanceQuery query, CancellationToken ct); Task<dynamic> DashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListBudgetsAsync(CancellationToken ct); Task<Guid> CreateBudgetAsync(BudgetCommand command, CancellationToken ct); Task ApproveBudgetAsync(Guid id, CancellationToken ct);
    Task<Guid> ReconcileAsync(ReconciliationCommand command, CancellationToken ct); Task ReverseReconciliationAsync(Guid id, string reason, CancellationToken ct);
}

public interface ISalesService
{
    Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct); Task<dynamic?> GetAsync(Guid id, CancellationToken ct); Task<Guid> SaveAsync(Guid? id, SaleCommand command, CancellationToken ct);
    Task ConfirmAsync(Guid id, CancellationToken ct); Task InvoiceAsync(Guid id, CancellationToken ct); Task CancelAsync(Guid id, string reason, CancellationToken ct);
}
