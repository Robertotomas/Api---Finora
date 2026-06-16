using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Investment;

/// <summary>
/// Adiciona uma transação (compra/venda). Cria a posição se ainda não existir para o instrumento.
/// </summary>
public record AddTransactionRequest
{
    // ── Instrumento (da pesquisa) ──
    [Required]
    [MaxLength(32)]
    public string Symbol { get; init; } = string.Empty;

    [MaxLength(32)]
    public string Exchange { get; init; } = string.Empty;

    [MaxLength(16)]
    public string MicCode { get; init; } = string.Empty;

    /// <summary>Símbolo Yahoo resolvido (da pesquisa). Se vier, é usado diretamente como chave da posição.</summary>
    [MaxLength(48)]
    public string ProviderSymbol { get; init; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Domínio da marca para o logo (ex.: "adidas.com"). Da pesquisa; opcional.</summary>
    [MaxLength(255)]
    public string? LogoDomain { get; init; }

    [MaxLength(8)]
    public string Currency { get; init; } = "EUR";

    public InstrumentType Type { get; init; }

    // ── Transação ──
    public InvestmentOperation Operation { get; init; }

    [Required]
    public DateTime Date { get; init; }

    [Range(0.0000001, double.MaxValue, ErrorMessage = "A quantidade tem de ser positiva.")]
    public decimal Quantity { get; init; }

    /// <summary>Preço por unidade, na moeda do instrumento.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Comissão da transação, na moeda do instrumento.</summary>
    public decimal Commission { get; init; }

    /// <summary>Margem de câmbio do broker em % (ex.: 0,5% na XTB). Ignorado se o instrumento for em EUR.</summary>
    [Range(0, 100)]
    public decimal FxFeePercent { get; init; }
}
