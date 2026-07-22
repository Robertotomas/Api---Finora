using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class InvestmentRepository : IInvestmentRepository
{
    private readonly ApplicationDbContext _context;

    public InvestmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InvestmentHolding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentHoldings
            .Include(h => h.Transactions)
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<InvestmentHolding?> GetByProviderSymbolAsync(Guid householdId, string providerSymbol, CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentHoldings
            .Include(h => h.Transactions)
            .FirstOrDefaultAsync(h => h.HouseholdId == householdId && h.ProviderSymbol == providerSymbol, cancellationToken);
    }

    public async Task<IReadOnlyList<InvestmentHolding>> GetByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentHoldings
            .AsNoTracking()
            .Include(h => h.Transactions)
            .Where(h => h.HouseholdId == householdId)
            .OrderBy(h => h.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<InvestmentHolding> CreateAsync(InvestmentHolding holding, CancellationToken cancellationToken = default)
    {
        _context.InvestmentHoldings.Add(holding);
        await _context.SaveChangesAsync(cancellationToken);
        return holding;
    }

    public async Task DeleteAsync(InvestmentHolding holding, CancellationToken cancellationToken = default)
    {
        _context.InvestmentHoldings.Remove(holding);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddTransactionAsync(InvestmentTransaction transaction, CancellationToken cancellationToken = default)
    {
        _context.InvestmentTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<InvestmentTransaction?> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentTransactions
            .Include(t => t.InvestmentHolding)
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
    }

    public async Task UpdateTransactionAsync(InvestmentTransaction transaction, CancellationToken cancellationToken = default)
    {
        _context.InvestmentTransactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTransactionAsync(InvestmentTransaction transaction, CancellationToken cancellationToken = default)
    {
        _context.InvestmentTransactions.Remove(transaction);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetDistinctProviderSymbolsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentHoldings
            .AsNoTracking()
            .Where(h => h.ProviderSymbol != "")
            .Select(h => h.ProviderSymbol)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InvestmentDeposit>> GetDepositsByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentDeposits
            .AsNoTracking()
            .Where(d => d.HouseholdId == householdId)
            .OrderBy(d => d.Date)
            .ToListAsync(cancellationToken);
    }

    public async Task AddDepositsAsync(IEnumerable<InvestmentDeposit> deposits, CancellationToken cancellationToken = default)
    {
        var list = deposits.ToList();
        if (list.Count == 0) return;
        _context.InvestmentDeposits.AddRange(list);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<InvestmentDeposit?> GetDepositByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.InvestmentDeposits.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task UpdateDepositAsync(InvestmentDeposit deposit, CancellationToken cancellationToken = default)
    {
        _context.InvestmentDeposits.Update(deposit);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDepositAsync(InvestmentDeposit deposit, CancellationToken cancellationToken = default)
    {
        _context.InvestmentDeposits.Remove(deposit);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
