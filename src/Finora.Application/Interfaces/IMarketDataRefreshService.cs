namespace Finora.Application.Interfaces;

public interface IMarketDataRefreshService
{
    /// <summary>Atualiza o cache de cotações de todos os símbolos em uso (job diário). Devolve quantos foram atualizados.</summary>
    Task<int> RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atualiza o cache apenas para os símbolos indicados (refresh pontual). Por defeito aplica um
    /// cooldown global por símbolo (só vai ao Yahoo buscar cotações velhas/em falta); <paramref name="force"/>
    /// = true ignora-o (usado pelo job diário). Devolve quantos foram efetivamente atualizados.
    /// </summary>
    Task<int> RefreshSymbolsAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default, bool force = false);
}
