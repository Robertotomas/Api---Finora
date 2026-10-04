using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Investment;

/// <summary>Uma transação parseada no cliente (a partir de Excel/CSV da corretora), pronta a importar.</summary>
public class BrokerTradeDto
{
    public string ProviderSymbol { get; set; } = string.Empty;
    public string BaseSymbol { get; set; } = string.Empty;
    /// <summary>ISIN do instrumento (se conhecido) — usado para resolver ticker/nome/cotação quando não há símbolo.</summary>
    public string? Isin { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public string Exchange { get; set; } = string.Empty;
    public InstrumentType Type { get; set; }
    public InvestmentOperation Operation { get; set; }
    /// <summary>Data (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Câmbio → EUR à data (derivado do montante do extrato). Null → o servidor calcula pela taxa histórica.</summary>
    public decimal? FxRateToEur { get; set; }
    /// <summary>Chave estável da transação (ex.: "xtb:{id}") para deduplicação.</summary>
    public string ExternalId { get; set; } = string.Empty;
}

/// <summary>Movimento de tesouraria parseado no cliente (depósito/levantamento).</summary>
public class BrokerDepositDto
{
    /// <summary>Data (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Montante; positivo = depósito, negativo = levantamento.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    /// <summary>Chave estável (ex.: "xtb:{id}") para deduplicação.</summary>
    public string ExternalId { get; set; } = string.Empty;
}

/// <summary>Pedido de importação (transações já parseadas no cliente).</summary>
public class BrokerImportRequest
{
    public List<BrokerTradeDto> Items { get; set; } = new();
    /// <summary>Depósitos/levantamentos do extrato (para a métrica "Depósitos").</summary>
    public List<BrokerDepositDto> Deposits { get; set; } = new();
    /// <summary>true se o parser do cliente detetou linhas que não conseguiu interpretar.</summary>
    public bool HasUnparsedRows { get; set; }
}

/// <summary>Resultado da importação de um extrato de corretora.</summary>
public class InvestmentImportResultDto
{
    /// <summary>true = apenas pré-visualização (nada foi gravado).</summary>
    public bool DryRun { get; set; }

    /// <summary>Total de transações detetadas no PDF.</summary>
    public int Detected { get; set; }

    /// <summary>Transações novas (criadas, ou que seriam criadas em dry-run).</summary>
    public int Created { get; set; }

    /// <summary>Duplicados ignorados (já existiam pelo mesmo ID de posição).</summary>
    public int Skipped { get; set; }

    /// <summary>true se o PDF tinha linhas que não foram interpretadas (layout inesperado).</summary>
    public bool HasUnparsedRows { get; set; }

    /// <summary>Mensagem de erro amigável quando a leitura falha (ex.: PDF não é da XTB).</summary>
    public string? Error { get; set; }

    /// <summary>Depósitos/levantamentos novos gravados (ou que o seriam em dry-run).</summary>
    public int DepositsImported { get; set; }

    public List<InvestmentImportItemDto> Items { get; set; } = new();
}

public class InvestmentImportItemDto
{
    public string ProviderSymbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty; // "Compra" | "Venda"
    public string Date { get; set; } = string.Empty;       // yyyy-MM-dd
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = "new";            // "new" | "duplicate"
}
