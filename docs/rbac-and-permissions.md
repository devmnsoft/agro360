# RBAC e permissões

Perfis são isolados por tenant, possuem nível e uma lista de permissões cadastradas. Os seeds operacionais esperados são Super Admin Plataforma, Admin Organização, Gestor Agro, Técnico Agrícola, Técnico Pecuário, Operador de Campo, Financeiro, Comercial, Logística e Consulta/Auditoria. O perfil de plataforma é filtrado das consultas do tenant. Alterações são auditadas; a API exige JWT e políticas/roles, e usuários desativados não podem manter acesso.
