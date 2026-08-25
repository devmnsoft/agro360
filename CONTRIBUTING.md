# Contribuição

1. crie branch curta a partir de `main`;
2. não altere migração já aplicada; adicione nova versão;
3. mantenha Domain sem dependência de banco/HTTP;
4. não use `double` para dinheiro ou medidas;
5. consulta operacional deve aplicar contexto de tenant;
6. `try/catch` vazio e mock em produção são proibidos;
7. adicione teste de regra e, quando houver SQL, teste de integração;
8. execute `scripts/verify.sh` antes do pull request;
9. atualize catálogo/roadmap apenas quando o gate for comprovado.

Commits devem explicar o resultado de negócio, não apenas o arquivo alterado.
