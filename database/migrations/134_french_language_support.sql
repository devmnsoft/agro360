-- Migration 134: francês (fr-FR) na cadeia canônica de idiomas.
-- O conjunto habilitado de culturas vive em platform_languages.active e alimenta login, refresh,
-- validação de sessão, X-Culture e a preferência do usuário; o que faltava era a linha fr-FR e a
-- aceitação da cultura nos templates persistidos (docs de desenvolvedor, notificações, ajuda
-- contextual, mensagens e confirmações de ação). As alterações são aditivas: linhas históricas
-- continuam válidas e nada é reescrito. Nenhuma outra tabela restringe cultura por CHECK —
-- platform_tenant_settings, platform_user_preferences, platform_translations e
-- platform_contextual_help usam FK para platform_languages(culture).
begin;

insert into agro360.platform_languages(culture, native_name, active, is_fallback)
values ('fr-FR', 'Français (France)', true, false)
on conflict (culture) do update set active = excluded.active, updated_at = now();

-- Solta o CHECK antigo somente quando ele restringe culturas sem fr-FR (idempotente e
-- independente do nome automático gerado pelo PostgreSQL na criação original).
do $$
declare c record;
begin
  for c in select conname from pg_constraint
           where conrelid = 'agro360.platform_developer_docs'::regclass and contype = 'c'
             and pg_get_constraintdef(oid) like '%culture%'
             and pg_get_constraintdef(oid) like '%pt-BR%'
             and pg_get_constraintdef(oid) not like '%fr-FR%'
  loop execute format('alter table agro360.platform_developer_docs drop constraint %I', c.conname);
  end loop;
   if not exists (select 1 from pg_constraint where conrelid = 'agro360.platform_developer_docs'::regclass and conname = 'ck_platform_developer_docs_culture') then
     execute 'alter table agro360.platform_developer_docs add constraint ck_platform_developer_docs_culture check (culture in (''pt-BR'', ''en-US'', ''es-ES'', ''fr-FR''))';
   end if;
end $$;

do $$
declare c record;
begin
  for c in select conname from pg_constraint
           where conrelid = 'agro360.operations_notification_templates'::regclass and contype = 'c'
             and pg_get_constraintdef(oid) like '%culture%'
             and pg_get_constraintdef(oid) like '%pt-BR%'
             and pg_get_constraintdef(oid) not like '%fr-FR%'
  loop execute format('alter table agro360.operations_notification_templates drop constraint %I', c.conname);
  end loop;
   if not exists (select 1 from pg_constraint where conrelid = 'agro360.operations_notification_templates'::regclass and conname = 'ck_operations_notification_templates_culture') then
     execute 'alter table agro360.operations_notification_templates add constraint ck_operations_notification_templates_culture check (culture in (''pt-BR'', ''en-US'', ''es-ES'', ''fr-FR''))';
   end if;
end $$;

do $$
declare c record;
begin
  for c in select conname from pg_constraint
           where conrelid = 'agro360.ui_contextual_help'::regclass and contype = 'c'
             and pg_get_constraintdef(oid) like '%culture%'
             and pg_get_constraintdef(oid) like '%pt-BR%'
             and pg_get_constraintdef(oid) not like '%fr-FR%'
  loop execute format('alter table agro360.ui_contextual_help drop constraint %I', c.conname);
  end loop;
   if not exists (select 1 from pg_constraint where conrelid = 'agro360.ui_contextual_help'::regclass and conname = 'ck_ui_contextual_help_culture') then
     execute 'alter table agro360.ui_contextual_help add constraint ck_ui_contextual_help_culture check (culture in (''pt-BR'', ''en-US'', ''es-ES'', ''fr-FR''))';
   end if;
end $$;

do $$
declare c record;
begin
  for c in select conname from pg_constraint
           where conrelid = 'agro360.ui_message_templates'::regclass and contype = 'c'
             and pg_get_constraintdef(oid) like '%culture%'
             and pg_get_constraintdef(oid) like '%pt-BR%'
             and pg_get_constraintdef(oid) not like '%fr-FR%'
  loop execute format('alter table agro360.ui_message_templates drop constraint %I', c.conname);
  end loop;
   if not exists (select 1 from pg_constraint where conrelid = 'agro360.ui_message_templates'::regclass and conname = 'ck_ui_message_templates_culture') then
     execute 'alter table agro360.ui_message_templates add constraint ck_ui_message_templates_culture check (culture in (''pt-BR'', ''en-US'', ''es-ES'', ''fr-FR''))';
   end if;
end $$;

do $$
declare c record;
begin
  for c in select conname from pg_constraint
           where conrelid = 'agro360.ui_action_confirmations'::regclass and contype = 'c'
             and pg_get_constraintdef(oid) like '%culture%'
             and pg_get_constraintdef(oid) like '%pt-BR%'
             and pg_get_constraintdef(oid) not like '%fr-FR%'
  loop execute format('alter table agro360.ui_action_confirmations drop constraint %I', c.conname);
  end loop;
   if not exists (select 1 from pg_constraint where conrelid = 'agro360.ui_action_confirmations'::regclass and conname = 'ck_ui_action_confirmations_culture') then
     execute 'alter table agro360.ui_action_confirmations add constraint ck_ui_action_confirmations_culture check (culture in (''pt-BR'', ''en-US'', ''es-ES'', ''fr-FR''))';
   end if;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.24.0', 'Idioma francês (fr-FR) habilitado no catálogo de culturas e aceito nos templates persistidos de ajuda, mensagens, confirmações, notificações e documentação', now())
on conflict (version) do update set description = excluded.description;
commit;
