namespace Finora.Application.Interfaces;

public interface IFxRateService
{
    /// <summary>
    /// Multiplicadores moeda → EUR (EUR = 1), à data de hoje. Ex.: X USD em EUR = X × rates["USD"].
    /// Cache diária; degrada com EUR-only se o serviço falhar.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetRatesToEurAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Taxa moeda → EUR à data indicada (mid, BCE). Usa o dia útil mais próximo anterior.
    /// EUR → 1. Fallback para a taxa de hoje (e depois 1) se o serviço falhar.
    /// </summary>
    Task<decimal> GetRateToEurAsync(string currency, DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Série diária de multiplicadores moeda → EUR no intervalo [from, to] (BCE, frankfurter timeseries).
    /// Devolve dicionário data → taxa (só dias úteis; o consumidor faz carry-forward). EUR → vazio (taxa 1).
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetRateSeriesToEurAsync(string currency, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
