# Força de vendas e split

Parceiros podem ser produtor, cooperativa, vendedor, representante ou plataforma. Regras têm vigência e cálculo percentual/fixo. Uma venda aceita vários participantes; o cálculo rejeita valores negativos e exige que as parcelas conciliem exatamente o bruto.

`IPaymentSplitProvider` desacopla gateways. O adaptador controlado apenas registra referência `SIM-*`, status e auditoria: não movimenta dinheiro. A aprovação é permissionada e registrada no ledger. Futuramente um gateway real pode substituir o provider sem alterar Domain/Application.
