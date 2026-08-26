# API pública e chaves

Crie a chave em `POST /api/api-keys`. O valor `ag360_...` aparece uma única vez; o banco guarda SHA-256, prefixo, escopos, expiração, status, último uso e limite/minuto. Escopos aceitos: `properties`, `inventory`, `finance`, `traceability`, `logistics`, `compliance`, `reports`, `mobile`. Revogue ao suspeitar de exposição. Respostas de limite usam HTTP 429.
