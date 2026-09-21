begin;

-- 9.8.0 / AG-COM-OPS-001: contratos comerciais são agregados próprios; não reservam,
-- movimentam estoque nem criam recebíveis.
create sequence if not exists agro360.sales_contract_number_seq;

alter table agro360.sales_contracts add column if not exists contract_number varchar(40);
alter table agro360.sales_contracts add column if not exists unit varchar(20);
alter table agro360.sales_contracts add column if not exists unit_price numeric(18,4);
alter table agro360.sales_contracts add column if not exists currency char(3);
alter table agro360.sales_contracts add column if not exists commercial_terms text;
alter table agro360.sales_contracts add column if not exists incoterm varchar(20);
alter table agro360.sales_contracts add column if not exists idempotency_key varchar(120);
alter table agro360.sales_contracts add column if not exists version bigint not null default 1;

update agro360.sales_contracts
set contract_number=coalesce(contract_number,'CTR-LEGACY-'||substr(replace(id::text,'-',''),1,12)),
    unit=coalesce(unit,'UN'), unit_price=coalesce(unit_price,case when contracted_quantity>0 then contracted_value/contracted_quantity else 0 end),
    currency=coalesce(currency,'BRL'), commercial_terms=coalesce(commercial_terms,payment_terms,'Condições legadas não informadas')
where contract_number is null or unit is null or unit_price is null or currency is null or commercial_terms is null;

alter table agro360.sales_contracts alter column contract_number set not null;
alter table agro360.sales_contracts alter column unit set not null;
alter table agro360.sales_contracts alter column unit_price set not null;
alter table agro360.sales_contracts alter column currency set not null;
alter table agro360.sales_contracts alter column commercial_terms set not null;
alter table agro360.sales_contracts drop constraint if exists sales_contracts_type_check;
alter table agro360.sales_contracts add constraint sales_contracts_type_check check(type in('INTERNAL_SALE','COOPERATIVE','EXPORT','RECURRING_SUPPLY','TRADING','FUTURE_SALE','INSTITUTIONAL_PURCHASE','PARTNERSHIP'));
alter table agro360.sales_contracts drop constraint if exists sales_contracts_status_check;
alter table agro360.sales_contracts add constraint sales_contracts_status_check check(status in('DRAFT','UNDER_REVIEW','APPROVED','ACTIVE','SUSPENDED','FULFILLED','CANCELLED','CLOSED','EXPIRED'));
alter table agro360.sales_contracts drop constraint if exists ck_sales_contract_export_terms;
alter table agro360.sales_contracts add constraint ck_sales_contract_export_terms check(type<>'EXPORT' or (currency is not null and nullif(trim(commercial_terms),'') is not null and nullif(trim(incoterm),'') is not null)) not valid;
create unique index if not exists uq_sales_contract_number on agro360.sales_contracts(tenant_id,contract_number) where deleted_at is null;
create unique index if not exists uq_sales_contract_idempotency on agro360.sales_contracts(tenant_id,idempotency_key) where idempotency_key is not null;

create table if not exists agro360.sales_contract_versions(
 id uuid primary key, tenant_id uuid not null, contract_id uuid not null, version_number bigint not null,
 snapshot jsonb not null, reason text not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,contract_id,version_number),
 foreign key(tenant_id,contract_id) references agro360.sales_contracts(tenant_id,id));
create index if not exists ix_sales_contract_versions_timeline on agro360.sales_contract_versions(tenant_id,contract_id,version_number desc);
select agro360.platform_enable_tenant_rls('agro360.sales_contract_versions');

grant select,insert on agro360.sales_contract_versions to agro360_app;
grant usage,select on sequence agro360.sales_contract_number_seq to agro360_app;
insert into agro360.platform_schema_versions(version,description,installed_at) values('9.8.0','Contratos comerciais operacionais e versionados',now()) on conflict(version) do nothing;
commit;
