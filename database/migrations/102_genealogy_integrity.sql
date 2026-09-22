begin;

-- Genealogy is an append-only fact ledger. Corrections are represented by a new
-- compensating/rectifying event; an accepted edge must never be silently edited.
create or replace function agro360.guard_operational_genealogy_immutable()
returns trigger language plpgsql as $$
begin
    if old.status <> 'PENDING_REVIEW' or tg_op = 'DELETE' then
        raise exception using errcode = '23514',
            message = 'confirmed genealogy links are immutable; append a rectifying link';
    end if;
    return new;
end;
$$;

drop trigger if exists trg_operational_genealogy_immutable on agro360.operational_genealogy_links;
create trigger trg_operational_genealogy_immutable
before update or delete on agro360.operational_genealogy_links
for each row execute function agro360.guard_operational_genealogy_immutable();

-- Final database protection for writers other than the API. The graph walk is
-- tenant-scoped and treats entity type + id as the node identity.
create or replace function agro360.guard_operational_genealogy_cycle()
returns trigger language plpgsql as $$
declare v_cycle boolean;
begin
    if upper(new.origin_type) = upper(new.destination_type)
       and new.origin_id = new.destination_id then
        raise exception using errcode = '23514', message = 'genealogy self reference';
    end if;

    with recursive descendants(entity_type, entity_id) as (
        select upper(destination_type), destination_id
        from agro360.operational_genealogy_links
        where tenant_id = new.tenant_id and status <> 'REVERSED' and id <> new.id
          and upper(origin_type) = upper(new.destination_type)
          and origin_id = new.destination_id
        union
        select upper(g.destination_type), g.destination_id
        from agro360.operational_genealogy_links g
        join descendants d on upper(g.origin_type) = d.entity_type and g.origin_id = d.entity_id
        where g.tenant_id = new.tenant_id and g.status <> 'REVERSED' and g.id <> new.id
    )
    select exists (
        select 1 from descendants
        where entity_type = upper(new.origin_type) and entity_id = new.origin_id
    ) into v_cycle;

    if v_cycle then
        raise exception using errcode = '23514', message = 'genealogy cycle detected';
    end if;
    return new;
end;
$$;

drop trigger if exists trg_operational_genealogy_cycle on agro360.operational_genealogy_links;
create constraint trigger trg_operational_genealogy_cycle
after insert on agro360.operational_genealogy_links
deferrable initially immediate
for each row execute function agro360.guard_operational_genealogy_cycle();

create index if not exists ix_genealogy_links_active_path
    on agro360.operational_genealogy_links
       (tenant_id, origin_type, origin_id, destination_type, destination_id)
    where status <> 'REVERSED';

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('102.0.0', 'Genealogia imutável e proteção contra ciclos por tenant', now())
on conflict(version) do nothing;

commit;
