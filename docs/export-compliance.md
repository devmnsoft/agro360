# Exportação e dossiê de conformidade

O dossiê vincula lote, produto, origem, comprador e mercado. Sua emissão consulta as regras obrigatórias aplicáveis ao produto/mercado e falha se houver requisito ausente. Evidências, certificações, documentos, qualidade, beneficiamento, ledger, armazenagem, transporte e expedição permanecem relacionados às entidades do tenant e podem compor evoluções do relatório sem duplicar a fonte oficial.

`GET /api/compliance/export-dossiers/{id}/report` exporta o relatório. O certificado usa código aleatório hexadecimal de 80 bits. A consulta pública `GET /api/public/compliance/certificates/{codigo}` aceita somente 20 caracteres hexadecimais, retorna dados mínimos, não expõe IDs nem dados pessoais e só publica dossiês emitidos. O `verificationHash` permite conferir a resposta; o QR deve apontar para essa URL HTTPS, nunca conter token de sessão.
