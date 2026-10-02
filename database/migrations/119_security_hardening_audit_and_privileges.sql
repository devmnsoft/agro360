-- Migration 119: Hardening de seguranca, protecao de audit logs e privilegios do papel runtime agro360_app
-- Schema version 11.9.0
set local search_path to agro360, public;

create table if not exists agro360.platform_schema_migrations (
    version varchar(160) primary key,
    name varchar(260) not null default '',
    checksum varchar(64) not null,
    applied_at timestamptz not null default now()
);

do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        -- Revoga privilegios destrutivos em tabelas de auditoria e razoes contabeis/imutaveis
        revoke update, delete, truncate on table
            agro360.audit_logs,
            agro360.audit_saas_events,
            agro360.documents_document_access_logs,
            agro360.documents_public_certificate_access_logs,
            agro360.documents_certificate_events,
            agro360.documents_evidence_validations,
            agro360.inventory_stock_movements,
            agro360.finance_audit_events,
            agro360.portal_external_audit_events,
            agro360.sales_delivery_schedule_operations
        from agro360_app;

        -- Revoga escrita do papel runtime nas tabelas de controle de versao do schema
        revoke insert, update, delete, truncate on table
            agro360.platform_schema_migrations,
            agro360.platform_schema_versions
        from agro360_app;

        -- Garante leitura nos logs para auditoria pelo sistema
        grant select on table
            agro360.audit_logs,
            agro360.audit_saas_events,
            agro360.documents_document_access_logs,
            agro360.documents_public_certificate_access_logs,
            agro360.documents_certificate_events,
            agro360.documents_evidence_validations,
            agro360.inventory_stock_movements,
            agro360.finance_audit_events,
            agro360.portal_external_audit_events,
            agro360.sales_delivery_schedule_operations,
            agro360.platform_schema_migrations,
            agro360.platform_schema_versions
        to agro360_app;

        -- Permite insercao append-only nos logs e movimentos
        grant insert on table
            agro360.audit_logs,
            agro360.audit_saas_events,
            agro360.documents_document_access_logs,
            agro360.documents_public_certificate_access_logs,
            agro360.documents_certificate_events,
            agro360.documents_evidence_validations,
            agro360.inventory_stock_movements,
            agro360.finance_audit_events,
            agro360.portal_external_audit_events,
            agro360.sales_delivery_schedule_operations
        to agro360_app;

        -- Garante USAGE e SELECT em todas as sequencias do schema agro360 para geracao de numeros e identificadores sequenciais
        grant usage, select on all sequences in schema agro360 to agro360_app;
    end if;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.9.0', 'Hardening de Seguranca, Protecao de Logs Imutaveis e Privilegios de Runtime (AG-SEC-AUDIT-001)', now())
on conflict (version) do update set description = excluded.description;
