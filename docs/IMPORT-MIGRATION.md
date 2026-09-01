# Importação e Migração

O fluxo real é: envio de CSV (máximo 10 MB), seleção de módulo, leitura segura, mapeamento, pré-validação, confirmação explícita diante de erro crítico, processamento e resultado. O backend registra lote e erros com linha, coluna, código, motivo e severidade. CPF/CNPJ são normalizados e validados; e-mails e duplicidades no arquivo são validados antes da gravação. Lotes em processamento ou concluídos não podem ser cancelados/reprocessados.

Módulos: tenants, usuários, perfis, fazendas, talhões, safras, produtores, rebanhos, estoque, fornecedores, produtos/insumos, clientes comerciais e documentos. Paginação é server-side e limitada a 100 itens. O relatório de inconsistências deriva exclusivamente dos erros persistidos.

A gravação nas tabelas de destino deve ser implementada por adaptador específico de módulo; até lá o lote permanece `VALIDATED`, sem simular sucesso ou completar campos. Essa é uma pendência operacional explícita.
