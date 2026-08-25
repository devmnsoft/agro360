# Agro 360 v0.2.0 — Release Interna de Homologação

## Entregue nesta candidata

- Outbox com retry exponencial limitado, dead-letter, última tentativa e correlação sem payload em log;
- migration corretiva segura e aditiva;
- CI com segunda execução do Migrator, auditoria de pacotes, TRX e quatro imagens;
- scripts defensivos de backup/restauração e runbooks de staging;
- classificação de módulos ajustada ao estado real: backend vertical não equivale a fluxo Web `CORE`.

## Limitações conhecidas

- Web é Command Center/PWA e ainda não contém formulários operacionais dos fluxos agrícola e pecuário;
- não existe seed idempotente completo com quatro perfis e cenário rural;
- não existe E2E de browser; integração real requer PostgreSQL/PostGIS;
- alimentação animal, dieta, confinamento e fábrica de ração são planejados para Sprints 7/14;
- Redis/MinIO não têm consumidor nesta versão e não fazem parte do runtime necessário.

Esta candidata não deve ser promovida como release comercial nem ter os fluxos pendentes anunciados como `CORE`.
