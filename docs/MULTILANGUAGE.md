# Multi-idioma

Culturas iniciais: `pt-BR` (fallback), `en-US` e `es-ES`. A escolha é persistida como preferência, aplicada ao atributo `lang` e deve reger recursos, validações, ajuda, datas e valores. Recursos ausentes sempre retornam `pt-BR`. CPF, CNPJ e códigos fiscais não são traduzidos; decimais são recebidos como números JSON e valores monetários usam `decimal`/`numeric`, nunca `double`.
