using Finora.Application.DTOs.Search;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class SearchService : ISearchService
{
    private const int TransactionLimit = 6;
    private const int RecurringLimit = 5;
    private const int AccountLimit = 5;
    private const int ObjectiveLimit = 5;

    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ISavingsObjectiveRepository _objectivesRepository;

    public SearchService(
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringRepository,
        IAccountRepository accountRepository,
        ISavingsObjectiveRepository objectivesRepository)
    {
        _transactionRepository = transactionRepository;
        _recurringRepository = recurringRepository;
        _accountRepository = accountRepository;
        _objectivesRepository = objectivesRepository;
    }

    public async Task<GlobalSearchResultDto> SearchAsync(Guid householdId, string query, CancellationToken cancellationToken = default)
    {
        var q = query.Trim();
        if (q.Length < 2)
            return new GlobalSearchResultDto();

        // Categorias cujo rótulo PT bate com o termo (permite pesquisar por categoria, ex.: "alimentação").
        var matchingCategories = TransactionCategoryLabels.MatchByQuery(q);

        var transactions = await _transactionRepository.SearchAsync(householdId, q, matchingCategories, TransactionLimit, cancellationToken);
        var recurrings = await _recurringRepository.SearchAsync(householdId, q, matchingCategories, RecurringLimit, cancellationToken);

        var accounts = (await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken))
            .Where(a => !a.IsArchived && a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(AccountLimit)
            .Select(a => new SearchAccountDto { Id = a.Id, Name = a.Name, Balance = a.Balance })
            .ToList();

        var objectives = (await _objectivesRepository.GetByHouseholdAsync(householdId, cancellationToken))
            .Where(o => o.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(ObjectiveLimit)
            .Select(o => new SearchObjectiveDto { Id = o.Id, Name = o.Name, Completed = o.CompletedAt.HasValue })
            .ToList();

        return new GlobalSearchResultDto
        {
            Transactions = transactions.Select(t => new SearchTransactionDto
            {
                Id = t.Id,
                Description = t.Description,
                EntityName = t.EntityName,
                Amount = t.Amount,
                Type = (int)t.Type,
                Category = (int)t.Category,
                Date = t.Date,
            }).ToList(),
            Recurrings = recurrings.Select(r => new SearchRecurringDto
            {
                Id = r.Id,
                Description = r.Description,
                EntityName = r.EntityName,
                Amount = r.Amount,
                Type = (int)r.Type,
                Category = (int)r.Category,
                Frequency = (int)r.Frequency,
            }).ToList(),
            Accounts = accounts,
            Objectives = objectives,
        };
    }
}
