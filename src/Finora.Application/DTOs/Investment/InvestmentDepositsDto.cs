namespace Finora.Application.DTOs.Investment;

/// <summary>Resumo dos depósitos do agregado (dinheiro transferido para a corretora), em EUR.</summary>
public class InvestmentDepositsDto
{
    /// <summary>Total líquido depositado (depósitos − levantamentos), convertido para EUR.</summary>
    public decimal TotalEur { get; set; }

    /// <summary>Nº de movimentos considerados.</summary>
    public int Count { get; set; }

    /// <summary>Lista dos movimentos (mais recente primeiro), para gerir (ver/editar/eliminar).</summary>
    public List<InvestmentDepositItemDto> Items { get; set; } = new();
}

/// <summary>Um movimento de depósito/levantamento.</summary>
public class InvestmentDepositItemDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    /// <summary>"import" (do extrato) ou "manual" (inserido à mão).</summary>
    public string Source { get; set; } = "manual";
    /// <summary>Conta debitada (se houve), para a UI mostrar.</summary>
    public Guid? AccountId { get; set; }
}
