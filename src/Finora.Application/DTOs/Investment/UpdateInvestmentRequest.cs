using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Investment;

public record UpdateTransactionRequest
{
    public InvestmentOperation Operation { get; init; }

    [Required]
    public DateTime Date { get; init; }

    [Range(0.0000001, double.MaxValue, ErrorMessage = "A quantidade tem de ser positiva.")]
    public decimal Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal Commission { get; init; }

    [Range(0, 100)]
    public decimal FxFeePercent { get; init; }
}
