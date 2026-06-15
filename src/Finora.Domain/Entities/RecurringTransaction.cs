using System.Linq.Expressions;
using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class RecurringTransaction : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public TransactionType Type { get; set; }
    public TransactionCategory Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }

    /// <summary>Origem/destino: uma entidade (empresa/serviço) ou uma pessoa.</summary>
    public TransactionEntityType EntityType { get; set; } = TransactionEntityType.Entity;
    /// <summary>Nome da entidade ou da pessoa associada (opcional).</summary>
    public string? EntityName { get; set; }

    public Guid? DestinationAccountId { get; set; }
    public Account? DestinationAccount { get; set; }

    /// <summary>
    /// Membro responsável pela recorrente (só aplicável no plano Couple e fora de transferências).
    /// Null = sem responsável atribuído.
    /// </summary>
    public Guid? ResponsibleUserId { get; set; }
    public User? ResponsibleUser { get; set; }

    /// <summary>Monthly, Quarterly, SemiAnnual or Annual.</summary>
    public RecurringFrequency Frequency { get; set; } = RecurringFrequency.Monthly;

    /// <summary>
    /// Reference/anchor month (1-12) of the first payment for non-monthly frequencies.
    /// When set, the amount is charged in full on each payment month (cash-flow real).
    /// When null, the amount is spread evenly across the 12 months (diluído). Ignored for Monthly.
    /// </summary>
    public int? AnnualMonth { get; set; }

    /// <summary>First month (1-12) when this recurring applies.</summary>
    public int StartMonth { get; set; }
    /// <summary>First year when this recurring applies.</summary>
    public int StartYear { get; set; }
    /// <summary>When removed: first month (exclusive) when it no longer applies. Null = continues indefinitely.</summary>
    public int? EndMonth { get; set; }
    /// <summary>When removed: first year (exclusive) when it no longer applies.</summary>
    public int? EndYear { get; set; }

    /// <summary>Number of payments per year implied by the frequency (12 / 4 / 2 / 1).</summary>
    public int OccurrencesPerYear => Frequency switch
    {
        RecurringFrequency.Monthly => 12,
        RecurringFrequency.Quarterly => 4,
        RecurringFrequency.SemiAnnual => 2,
        RecurringFrequency.Annual => 1,
        _ => 12
    };

    /// <summary>
    /// Amount this recurring contributes to a given calendar month (1-12), assuming it is
    /// already known to be active that month. Monthly: full amount every month. Non-monthly
    /// diluído (AnnualMonth == null): amount × occurrences / 12 every month. Non-monthly real
    /// (AnnualMonth set): full amount on each payment month, 0 otherwise.
    /// </summary>
    public decimal AmountForMonth(int month)
    {
        if (Frequency == RecurringFrequency.Monthly)
            return Amount;

        if (AnnualMonth is null)
            return Math.Round(Amount * OccurrencesPerYear / 12m, 2);

        var interval = 12 / OccurrencesPerYear;
        var diff = ((month - AnnualMonth.Value) % interval + interval) % interval;
        return diff == 0 ? Amount : 0m;
    }

    /// <summary>
    /// Regra do "fim exclusivo": uma recorrente está ativa num (ano, mês) sse
    /// início ≤ mês E (sem fim OU fim > mês). O fim é EXCLUSIVO — deixa de contar
    /// no próprio mês de fim. Fonte de verdade partilhada pelo repositório.
    /// <para>
    /// ⚠️ Espelhada em <see cref="ActiveInMonthExpr"/> (versão traduzível pelo EF) e no
    /// frontend (`recurringActiveEnd`/`activeRecurring` em TransactionsView.vue). Manter os
    /// três em sincronia.
    /// </para>
    /// </summary>
    public bool IsActiveInMonth(int year, int month)
    {
        var started = StartYear < year || (StartYear == year && StartMonth <= month);
        var notEnded = EndYear is null || EndYear > year || (EndYear == year && (EndMonth ?? 13) > month);
        return started && notEnded;
    }

    /// <summary>
    /// Versão em árvore de expressão de <see cref="IsActiveInMonth"/>, para o EF Core
    /// conseguir traduzir o predicado para SQL. Mantém a MESMA lógica do método de instância
    /// (garantido por teste de equivalência).
    /// </summary>
    public static Expression<Func<RecurringTransaction, bool>> ActiveInMonthExpr(int year, int month) =>
        r => (r.StartYear < year || (r.StartYear == year && r.StartMonth <= month))
            && (r.EndYear == null || r.EndYear > year || (r.EndYear == year && (r.EndMonth ?? 13) > month));
}
