begin;

-- A avaliação é append-only: cada decisão preserva o critério e o snapshot das ações avaliadas.
alter table agro360.compliance_nc_verifications add column if not exists criterion_type varchar(16) not null default 'QUALITATIVE';
alter table agro360.compliance_nc_verifications add column if not exists criterion_version integer not null default 1;
alter table agro360.compliance_nc_verifications add column if not exists method text not null default 'Revisão documentada';
alter table agro360.compliance_nc_verifications add column if not exists responsible_id uuid;
alter table agro360.compliance_nc_verifications add column if not exists due_on date;
alter table agro360.compliance_nc_verifications add column if not exists evidence_requirements text not null default 'Conforme critério aprovado';
alter table agro360.compliance_nc_verifications add column if not exists expected_value numeric(20,6);
alter table agro360.compliance_nc_verifications add column if not exists observed_value numeric(20,6);
alter table agro360.compliance_nc_verifications add column if not exists unit varchar(30);
alter table agro360.compliance_nc_verifications add column if not exists comparison_operator varchar(8);
alter table agro360.compliance_nc_verifications add column if not exists percentage_basis numeric(20,6);
alter table agro360.compliance_nc_verifications add column if not exists result_observed text not null default 'Registro legado';
alter table agro360.compliance_nc_verifications add column if not exists conclusion text not null default 'Registro legado';
alter table agro360.compliance_nc_verifications add column if not exists idempotency_key varchar(120);
alter table agro360.compliance_nc_verifications add column if not exists decided_at timestamptz not null default now();
alter table agro360.compliance_nc_verifications add column if not exists decided_by uuid;
alter table agro360.compliance_nc_verifications add column if not exists created_at timestamptz not null default now();
alter table agro360.compliance_nc_verifications add column if not exists created_by uuid;
alter table agro360.compliance_nc_verifications add column if not exists updated_at timestamptz;
alter table agro360.compliance_nc_verifications add column if not exists updated_by uuid;
alter table agro360.compliance_nc_verifications add column if not exists deleted_at timestamptz;
alter table agro360.compliance_nc_verifications add column if not exists deleted_by uuid;
alter table agro360.compliance_nc_verifications add column if not exists deletion_reason text;
update agro360.compliance_nc_verifications set responsible_id=verified_by,decided_by=verified_by,created_by=verified_by,due_on=verified_at::date where responsible_id is null;
alter table agro360.compliance_nc_verifications alter column responsible_id set not null;
alter table agro360.compliance_nc_verifications alter column due_on set not null;
alter table agro360.compliance_nc_verifications alter column decided_by set not null;
alter table agro360.compliance_nc_verifications alter column created_by set not null;
alter table agro360.compliance_nc_verifications add constraint compliance_nc_verification_criterion_check check(criterion_type in('QUALITATIVE','QUANTITATIVE','DOCUMENTAL')) not valid;
alter table agro360.compliance_nc_verifications add constraint compliance_nc_verification_quantitative_check check(criterion_type<>'QUANTITATIVE' or (expected_value is not null and observed_value is not null and nullif(trim(unit),'') is not null and comparison_operator in('GT','GTE','EQ','LTE','LT') and (unit<>'%' or percentage_basis>0))) not valid;
create unique index if not exists ux_compliance_nc_verification_idempotency on agro360.compliance_nc_verifications(tenant_id,idempotency_key) where idempotency_key is not null;
create index if not exists ix_compliance_nc_verification_queue on agro360.compliance_nc_verifications(tenant_id,responsible_id,due_on) where deleted_at is null;

create table if not exists agro360.compliance_nc_related_cases(
 id uuid primary key, tenant_id uuid not null, non_conformity_id uuid not null, related_non_conformity_id uuid not null,
 justification varchar(500) not null, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz, updated_by uuid, deleted_at timestamptz, deleted_by uuid, deletion_reason text,
 unique(tenant_id,id), unique(tenant_id,non_conformity_id,related_non_conformity_id),
 foreign key(non_conformity_id) references agro360.compliance_non_conformities(id),
 foreign key(related_non_conformity_id) references agro360.compliance_non_conformities(id),
 check(non_conformity_id<>related_non_conformity_id));
select agro360.platform_enable_tenant_rls('agro360.compliance_nc_related_cases');
insert into agro360.platform_schema_versions(version,description,installed_at) values('9.1.0','Verificação de eficácia versionada e casos relacionados',now()) on conflict(version) do nothing;
commit;
