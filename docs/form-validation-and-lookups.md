# Validação e lookups

Relações são escolhidas por busca/select alimentado pelo bootstrap (propriedades, talhões, culturas, animais e estoque); IDs técnicos ficam internos ao option. Campos obrigatórios usam `required`, quantidades `min=0`, datas são convertidas para ISO e mensagens aparecem em região acessível. O backend repete toda regra crítica: entidade/tipo permitido, UUID não vazio, quantidade, data, coordenadas, arquivo/tipo/tamanho e respostas obrigatórias. Nunca confie somente na validação do navegador.
