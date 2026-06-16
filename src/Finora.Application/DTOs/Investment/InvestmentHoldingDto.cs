using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Investment;

public record InvestmentHoldingDto
{
    public Guid Id { get; init; }
    public Guid HouseholdId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Exchange { get; init; } = string.Empty;
    public string ProviderSymbol { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Currency { get; init; } = "EUR";
    public InstrumentType Type { get; init; }
    public string? LogoDomain { get; init; }

    /// <summary>Quantidade líquida (compras − vendas), calculada das transações.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Custo médio por unidade (das compras, incl. comissões), na moeda do instrumento.</summary>
    public decimal AverageCost { get; init; }

    /// <summary>Preço atual em cache (na moeda do instrumento). Null se ainda não foi obtido.</summary>
    public decimal? CurrentPrice { get; init; }
    public DateTime? PriceAsOf { get; init; }

    /// <summary>Valores já convertidos para EUR (para o património).</summary>
    public decimal InvestedEur { get; init; }
    public decimal? CurrentValueEur { get; init; }
    public decimal? ReturnEur { get; init; }
    public decimal? ReturnPct { get; init; }

    public IReadOnlyList<InvestmentTransactionDto> Transactions { get; init; } = Array.Empty<InvestmentTransactionDto>();
}
