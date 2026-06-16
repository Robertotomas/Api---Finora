namespace Finora.Application.Interfaces;

public interface IMarketDataRefreshService
{
    /// <summary>Atualiza o cache de cotações de todos os símbolos em uso (job diário). Devolve quantos foram atualizados.</summary>
    Task<int> RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Atualiza o cache apenas para os símbolos indicados (refresh pontual).</summary>
    Task<int> RefreshSymbolsAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default);
}
