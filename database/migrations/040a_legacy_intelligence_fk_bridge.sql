begin;

-- Sprint 41 published tenant-composite foreign keys before declaring matching
-- tenant keys on their parent tables. Pre-create the affected graph with the
-- published shape plus those keys, preserving the historical migration bytes.
create table if not exists intelligence_kpi_definitions(
    id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(160) not null,
    code varchar(60) not null, description text, category varchar(30) not null
        check(category in('FINANCE','PRODUCTION','INVENTORY','PROCUREMENT','SALES','EXPORT','FISCAL','LOGISTICS','QUALITY','COMPLIANCE','LIVESTOCK','AGRICULTURE','TRACEABILITY','SUSTAINABILITY','OPERATIONAL','STRATEGIC')),
    formula varchar(60) not null check(formula in('SUM','COUNT','AVERAGE','PERCENTAGE','BALANCE','MARGIN','PRODUCTIVITY')),
    data_source varchar(40) not null, periodicity varchar(20) not null
        check(periodicity in('REAL_TIME','DAILY','WEEKLY','MONTHLY','QUARTERLY','YEARLY')),
    unit varchar(20) not null check(unit in('NUMBER','CURRENCY','PERCENT','QUANTITY','DAYS','HOURS')),
    target numeric(20,6), attention_limit numeric(20,6), critical_limit numeric(20,6),
    active boolean not null default true, strategic boolean not null default false,
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid, updated_by uuid, deleted_at timestamptz,
    unique(tenant_id,id), unique(tenant_id,code),
    check(unit<>'PERCENT' or (target between 0 and 100 and attention_limit between 0 and 100 and critical_limit between 0 and 100))
);

create table if not exists intelligence_alert_rules(
    id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), kpi_id uuid, module varchar(40),
    name varchar(160) not null, condition varchar(60) not null,
    operator varchar(30) not null check(operator in('GREATER_THAN','LESS_THAN','EQUAL','NOT_EQUAL','BETWEEN','OUTSIDE_RANGE','OVERDUE','NEAR_DUE','NO_MOVEMENT','DIVERGENT','BLOCKED')),
    threshold numeric(20,6), threshold_end numeric(20,6),
    severity varchar(20) not null check(severity in('INFORMATIONAL','ATTENTION','HIGH','CRITICAL')),
    suggested_action text not null, active boolean not null default true, group_duplicates boolean not null default true,
    valid_from timestamptz not null, valid_until timestamptz,
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid, updated_by uuid, deleted_at timestamptz, unique(tenant_id,id),
    foreign key(tenant_id,kpi_id) references intelligence_kpi_definitions(tenant_id,id),
    check(kpi_id is not null or module is not null), check(valid_until is null or valid_until>=valid_from)
);

create table if not exists intelligence_alerts(
    id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), rule_id uuid,
    type varchar(40) not null, category varchar(30) not null,
    severity varchar(20) not null check(severity in('INFORMATIONAL','ATTENTION','HIGH','CRITICAL')),
    origin varchar(160) not null, source_module varchar(40) not null, related_entity_type varchar(80), related_entity_id uuid,
    description text not null, recommendation text,
    status varchar(20) not null default 'NEW' check(status in('NEW','UNDER_REVIEW','ASSIGNED','RESOLVED','IGNORED','CANCELLED')),
    due_at timestamptz, responsible_id uuid, viewed_by uuid, resolved_by uuid, resolved_at timestamptz,
    fingerprint varchar(160), created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid, updated_by uuid, unique(tenant_id,id),
    foreign key(tenant_id,rule_id) references intelligence_alert_rules(tenant_id,id),
    check(status<>'ASSIGNED' or responsible_id is not null)
);

create table if not exists intelligence_recommendations(
    id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), rule_id uuid not null,
    title varchar(200) not null, description text not null,
    severity varchar(20) not null check(severity in('INFORMATIONAL','ATTENTION','HIGH','CRITICAL')),
    source_module varchar(40) not null, related_entity_type varchar(80), related_entity_id uuid,
    status varchar(20) not null default 'NEW' check(status in('NEW','ANALYSED','ACCEPTED','REJECTED','COMPLETED')),
    decision_reason text, created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid, updated_by uuid, deleted_at timestamptz, unique(tenant_id,id),
    foreign key(tenant_id,rule_id) references intelligence_alert_rules(tenant_id,id)
);

insert into platform.schema_versions(version,description,installed_at)
values('4.0.1','Bridge das chaves compostas de inteligência para a Sprint 41',now())
on conflict(version) do update set description=excluded.description;

commit;
