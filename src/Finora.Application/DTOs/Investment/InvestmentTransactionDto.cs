using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Investment;

public record InvestmentTransactionDto
{
    public Guid Id { get; init; }
    public InvestmentOperation Operation { get; init; }
    public DateTime Date { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Commission { get; init; }
    public decimal FxFeePercent { get; init; }
}
