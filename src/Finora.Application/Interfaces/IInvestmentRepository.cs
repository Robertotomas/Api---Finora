using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IInvestmentRepository
{
    /// <summary>Inclui as transações.</summary>
    Task<InvestmentHolding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Posição do agregado para um símbolo do fornecedor (inclui transações), ou null.</summary>
    Task<InvestmentHolding?> GetByProviderSymbolAsync(Guid householdId, string providerSymbol, CancellationToken cancellationToken = default);

    /// <summary>Inclui as transações de cada posição.</summary>
    Task<IReadOnlyList<InvestmentHolding>> GetByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default);

    Task<InvestmentHolding> CreateAsync(InvestmentHolding holding, CancellationToken cancellationToken = default);
    Task DeleteAsync(InvestmentHolding holding, CancellationToken cancellationToken = default);

    Task AddTransactionAsync(InvestmentTransaction transaction, CancellationToken cancellationToken = default);
    Task<InvestmentTransaction?> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken = default);
    Task UpdateTransactionAsync(InvestmentTransaction transaction, CancellationToken cancellationToken = default);
    Task DeleteTransactionAsync(InvestmentTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Todos os ProviderSymbol distintos em uso (para o job diário de preços).</summary>
    Task<IReadOnlyList<string>> GetDistinctProviderSymbolsAsync(CancellationToken cancellationToken = default);

    /// <summary>Movimentos de tesouraria (depósitos/levantamentos) do agregado.</summary>
    Task<IReadOnlyList<InvestmentDeposit>> GetDepositsByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default);

    /// <summary>Grava vários depósitos de uma vez (import).</summary>
    Task AddDepositsAsync(IEnumerable<InvestmentDeposit> deposits, CancellationToken cancellationToken = default);

    Task<InvestmentDeposit?> GetDepositByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateDepositAsync(InvestmentDeposit deposit, CancellationToken cancellationToken = default);
    Task DeleteDepositAsync(InvestmentDeposit deposit, CancellationToken cancellationToken = default);
}
