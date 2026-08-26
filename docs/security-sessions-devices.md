# Segurança, sessões e dispositivos

Sessões e dispositivos pertencem ao tenant/usuário e podem ser revogados; a ação é auditada. `login_history` registra sucesso, motivo de falha e origem, nunca senha. O bloqueio temporário deve ser aplicado pelo serviço de identidade após tentativas sucessivas. Tokens permanecem curtos e refresh tokens rotativos; revogação encerra confiança no registro correspondente.
