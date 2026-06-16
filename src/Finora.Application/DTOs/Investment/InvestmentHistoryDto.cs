namespace Finora.Application.DTOs.Investment;

/// <summary>Série temporal do valor da carteira/posição (em EUR), para o gráfico de evolução.</summary>
public class InvestmentHistoryDto
{
    /// <summary>Moeda da série (sempre EUR — valores convertidos).</summary>
    public string Currency { get; set; } = "EUR";

    public List<InvestmentHistoryPointDto> Points { get; set; } = new();
}

public class InvestmentHistoryPointDto
{
    /// <summary>Data do ponto (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Valor de mercado a essa data, em EUR.</summary>
    public decimal Value { get; set; }

    /// <summary>Custo investido (capital aplicado) a essa data, em EUR.</summary>
    public decimal Cost { get; set; }
}

/// <summary>Cotação histórica de um ticker (na moeda do instrumento), para o mini-gráfico do modal.</summary>
public class InstrumentPriceHistoryDto
{
    public List<InstrumentPricePointDto> Points { get; set; } = new();
}

public class InstrumentPricePointDto
{
    /// <summary>Data (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Preço de fecho na moeda do instrumento.</summary>
    public decimal Value { get; set; }
}
