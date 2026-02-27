using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly ApplicationDbContext _context;

    public AccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .Where(a => a.HouseholdId == householdId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Account> CreateAsync(Account account, CancellationToken cancellationToken = default)
    {
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<Account> UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task DeleteAsync(Account account, CancellationToken cancellationToken = default)
    {
        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
