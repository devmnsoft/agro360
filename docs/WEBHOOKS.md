# Webhooks

`POST /api/webhooks` registra URL HTTPS, eventos e referência da credencial de assinatura. Eventos suportados incluem lote criado/certificado, venda, expedição, split, alerta, não conformidade, documento vencido e erro mobile. A fila registra payload, assinatura HMAC, tentativas, próxima tentativa, resposta e duração. Conectores sem segredo ficam aguardando configuração; eventos falhos podem ser reenviados em `/api/webhooks/events/{id}/retry`. Consumidores devem verificar assinatura e idempotência.
