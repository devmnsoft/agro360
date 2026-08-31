# APIs e Webhooks
Aplicações pertencem ao tenant e definem limite gerencial. Uma chave `ag360_…` aparece apenas na criação; banco guarda prefixo e SHA-256. Cada endpoint público deve declarar um dos escopos cadastrados, validar aplicação/chave ativa, expiração, tenant e rate limit, e registrar somente metadados sem Authorization ou segredo.

Webhooks aceitam apenas HTTPS público, eventos permitidos e 1–10 tentativas. Entregas registram tentativa, HTTP, trecho sanitizado, próximo retry e resultado. O dispatcher deve usar outbox, assinatura HMAC derivada de segredo em cofre e falhar isoladamente da operação principal.
