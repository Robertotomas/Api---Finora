using Finora.Application.DTOs.Transaction;

namespace Finora.Application.Interfaces;

public interface ITransactionService
{
    Task<IReadOnlyList<TransactionDto>> GetByHouseholdAsync(Guid householdId, Guid userId, Guid? accountId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    Task<TransactionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<TransactionDto?> CreateAsync(CreateTransactionRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<TransactionDto?> UpdateAsync(Guid id, UpdateTransactionRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
