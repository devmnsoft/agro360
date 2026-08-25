-- Dados mínimos da v0.4.0 são idempotentes e permanecem no instalador consolidado.
insert into platform.schema_versions(version,description,installed_at) values('0.4.0','Sprint 7 - Pecuaria 360',now()) on conflict(version) do nothing;
