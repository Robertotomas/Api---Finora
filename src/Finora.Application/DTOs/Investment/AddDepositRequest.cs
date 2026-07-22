using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Investment;

/// <summary>
/// Adiciona um depósito à mão (dinheiro transferido para a corretora). Opcionalmente debita uma
/// conta do agregado (o dinheiro que saiu para a corretora). Sem <see cref="AccountId"/> é só métrica.
/// </summary>
public record AddDepositRequest
{
    [Required]
    public DateTime Date { get; init; }

    [Range(0.01, double.MaxValue, ErrorMessage = "O montante tem de ser positivo.")]
    public decimal Amount { get; init; }

    /// <summary>Conta a debitar (o saldo desce pelo montante). Null = não mexe em contas.</summary>
    public Guid? AccountId { get; init; }
}

/// <summary>Edita um depósito manual (data, montante, conta a debitar). O saldo é reconciliado.</summary>
public record UpdateDepositRequest
{
    [Required]
    public DateTime Date { get; init; }

    [Range(0.01, double.MaxValue, ErrorMessage = "O montante tem de ser positivo.")]
    public decimal Amount { get; init; }

    /// <summary>Conta a debitar (null = nenhuma). Ao mudar, o saldo antigo é revertido e o novo aplicado.</summary>
    public Guid? AccountId { get; init; }
}
