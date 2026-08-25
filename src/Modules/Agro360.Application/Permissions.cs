namespace Agro360.Application;

public static class Permissions
{
    public const string PropertiesRead = "properties.read";
    public const string PropertiesWrite = "properties.write";
    public const string AgricultureRead = "agriculture.read";
    public const string AgricultureWrite = "agriculture.write";
    public const string InventoryRead = "inventory.read";
    public const string InventoryMove = "inventory.move";
    public const string InventoryAdjust = "inventory.adjust";
    public const string LivestockRead = "livestock.read";
    public const string LivestockWrite = "livestock.write";
    public const string LivestockSell = "livestock.sell";
    public const string CommercialWrite = "commercial.write";
    public const string FinanceRead = "finance.read";
    public const string FinanceWrite = "finance.write";
    public const string PurchasingRead = "purchasing.read";
    public const string PurchasingWrite = "purchasing.write";
    public const string PurchasingApprove = "purchasing.approve";
    public const string FleetRead = "fleet.read";
    public const string FleetWrite = "fleet.write";
    public const string MaintenanceRead = "maintenance.read";
    public const string MaintenanceWrite = "maintenance.write";
    public const string DashboardRead = "dashboard.read";
    public const string StorageRead = "storage.read";
    public const string StorageWrite = "storage.write";
    public const string LogisticsRead = "logistics.read";
    public const string LogisticsWrite = "logistics.write";
    public const string TraceabilityRead = "traceability.read";
    public const string TraceabilityWrite = "traceability.write";
    public const string LedgerValidate = "ledger.validate";
    public const string RegionalLogisticsRead = "regional-logistics.read";
    public const string RegionalLogisticsWrite = "regional-logistics.write";
    public const string SalesNetworkRead = "sales-network.read";
    public const string SalesNetworkWrite = "sales-network.write";
    public const string SalesNetworkApprove = "sales-network.approve";

    public static IReadOnlyCollection<string> Administrator =>
    [
        PropertiesRead,
        PropertiesWrite,
        AgricultureRead,
        AgricultureWrite,
        InventoryRead,
        InventoryMove,
        InventoryAdjust,
        LivestockRead,
        LivestockWrite,
        LivestockSell,
        CommercialWrite,
        FinanceRead, FinanceWrite,
        PurchasingRead, PurchasingWrite, PurchasingApprove, FleetRead, FleetWrite, MaintenanceRead, MaintenanceWrite,
        DashboardRead, StorageRead, StorageWrite, LogisticsRead, LogisticsWrite,
        TraceabilityRead, TraceabilityWrite, LedgerValidate, RegionalLogisticsRead,
        RegionalLogisticsWrite, SalesNetworkRead, SalesNetworkWrite, SalesNetworkApprove
    ];
}
