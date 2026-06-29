using Finora.Application.DTOs.Investment;

namespace Finora.Application.Interfaces;

public interface IInvestmentService
{
    Task<IReadOnlyList<InvestmentHoldingDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<InvestmentHoldingDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adiciona uma transação (compra/venda); cria a posição se ainda não existir.</summary>
    Task<InvestmentHoldingDto?> AddTransactionAsync(AddTransactionRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<InvestmentHoldingDto?> UpdateTransactionAsync(Guid transactionId, UpdateTransactionRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<InvestmentHoldingDto?> DeleteTransactionAsync(Guid transactionId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Elimina a posição inteira (e as suas transações).</summary>
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstrumentSearchResultDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvestmentHoldingDto>> RefreshHouseholdQuotesAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Série de valor da carteira inteira (EUR) no intervalo. from/to nulos = desde a 1ª compra até hoje.</summary>
    Task<InvestmentHistoryDto> GetHouseholdHistoryAsync(Guid householdId, Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Série de valor de uma posição (EUR) no intervalo. from/to nulos = desde a 1ª compra até hoje.</summary>
    Task<InvestmentHistoryDto?> GetHoldingHistoryAsync(Guid holdingId, Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Cotação histórica de um ticker (na moeda do instrumento), para o mini-gráfico de pré-visualização.</summary>
    Task<InstrumentPriceHistoryDto> GetInstrumentHistoryAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Importa transações já parseadas no cliente (Excel/CSV). dryRun = só pré-visualização. Duplicados (mesmo ExternalId) são ignorados.</summary>
    Task<InvestmentImportResultDto> ImportTradesAsync(BrokerImportRequest request, Guid householdId, Guid userId, bool dryRun, CancellationToken cancellationToken = default);
}
