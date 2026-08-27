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
    public const string CommercialRead = "commercial.read";
    public const string CommercialApproveOrder = "commercial.orders.approve";
    public const string CommercialOverrideBlock = "commercial.customers.override-block";
    public const string CommercialManageCommission = "commercial.commissions.manage";
    public const string CommercialApproveSplit = "commercial.splits.approve";
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
    public const string IntelligenceRead = "intelligence.read";
    public const string IntelligenceWrite = "intelligence.write";
    public const string ComplianceRead = "compliance.read";
    public const string ComplianceWrite = "compliance.write";
    public const string ComplianceApprove = "compliance.approve";
    public const string EsgRead = "esg.read";
    public const string EsgWrite = "esg.write";
    public const string IntegrationsRead = "integrations.read";
    public const string IntegrationsWrite = "integrations.write";
    public const string MapsRead = "maps.read";
    public const string MapsWrite = "maps.write";
    public const string CooperativeRead = "cooperative.read";
    public const string CooperativeWrite = "cooperative.write";
    public const string CooperativeApprove = "cooperative.approve";
    public const string RuralHrRead = "rural-hr.read";
    public const string RuralHrWrite = "rural-hr.write";
    public const string RuralHrSafety = "rural-hr.safety";
    public const string DeploymentRead = "deployment.read";
    public const string DeploymentWrite = "deployment.write";
    public const string DocumentsRead = "documents.read";
    public const string DocumentsUpload = "documents.upload";
    public const string DocumentsDownload = "documents.download";
    public const string EvidencesValidate = "evidences.validate";
    public const string DossiersCreate = "dossiers.create";
    public const string DossiersApprove = "dossiers.approve";
    public const string CertificatesIssue = "certificates.issue";
    public const string CertificatesRevoke = "certificates.revoke";
    public const string WorkRead = "work.read";
    public const string WorkWrite = "work.write";
    public const string WorkApprove = "work.approve";
    public const string MobileRead = "mobile.read";
    public const string MobileWrite = "mobile.write";
    public const string MobileSync = "mobile.sync";
    public const string MobileResolveConflicts = "mobile.conflicts.resolve";
    public const string FieldChecklistsManage = "field-checklists.manage";
    public const string PortalManage = "portal.manage";
    public const string PortalAccess = "portal.access";

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
        CommercialRead, CommercialWrite, CommercialApproveOrder, CommercialOverrideBlock, CommercialManageCommission, CommercialApproveSplit,
        FinanceRead, FinanceWrite,
        PurchasingRead, PurchasingWrite, PurchasingApprove, FleetRead, FleetWrite, MaintenanceRead, MaintenanceWrite,
        DashboardRead, StorageRead, StorageWrite, LogisticsRead, LogisticsWrite,
        TraceabilityRead, TraceabilityWrite, LedgerValidate, RegionalLogisticsRead,
        RegionalLogisticsWrite, SalesNetworkRead, SalesNetworkWrite, SalesNetworkApprove,
        IntelligenceRead, IntelligenceWrite, ComplianceRead, ComplianceWrite, ComplianceApprove, EsgRead, EsgWrite,
        IntegrationsRead, IntegrationsWrite, MapsRead, MapsWrite, CooperativeRead, CooperativeWrite, CooperativeApprove,
        RuralHrRead, RuralHrWrite, RuralHrSafety, DeploymentRead, DeploymentWrite,
        DocumentsRead, DocumentsUpload, DocumentsDownload, EvidencesValidate, DossiersCreate, DossiersApprove, CertificatesIssue, CertificatesRevoke,
        WorkRead, WorkWrite, WorkApprove, MobileRead, MobileWrite, MobileSync, MobileResolveConflicts, FieldChecklistsManage, PortalManage
    ];
}
