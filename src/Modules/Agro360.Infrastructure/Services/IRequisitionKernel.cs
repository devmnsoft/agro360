using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Npgsql;

namespace Agro360.Infrastructure.Services;

/// <summary>
/// Núcleo canônico de criação de requisição, exposto para operações que criam requisições dentro
/// de uma transação própria (ex.: confirmação de necessidade de reposição). Garante que toda
/// requisição — manual ou automatizada — passe exatamente pelas mesmas validações (regras de
/// domínio, centro de custo ativo, unidade operacional efetiva, unidade catalogada) e entre no
/// mesmo fluxo aprovável (DRAFT/AWAITING_APPROVAL com evento de histórico), em vez de nascer em
/// um status paralelo fora da esteira de revisão humana. O chamador é responsável pela transação.
/// </summary>
public interface IRequisitionKernel
{
    Task<Guid> CreateRequisitionWithinTransactionAsync(NpgsqlConnection c, NpgsqlTransaction t, RequisitionCommand x, CancellationToken ct);
}
