using Finora.Domain.Common;

namespace Finora.Domain.Entities;

/// <summary>
/// Movimento de tesouraria da conta da corretora (depósito/levantamento), a nível do agregado —
/// dinheiro transferido para/da corretora, independente das posições. Serve para a métrica
/// "Depósitos". <see cref="Amount"/> é positivo num depósito e negativo num levantamento.
/// </summary>
public class InvestmentDeposit : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>Data do movimento (UTC).</summary>
    public DateTime Date { get; set; }

    /// <summary>Montante na moeda; positivo = depósito, negativo = levantamento.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "EUR";

    /// <summary>Chave estável do movimento (ex.: "xtb:{id}") para deduplicação em reimportações.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Conta do agregado que foi debitada por este depósito (manual). Null = não debitou nenhuma.</summary>
    public Guid? AccountId { get; set; }
}
