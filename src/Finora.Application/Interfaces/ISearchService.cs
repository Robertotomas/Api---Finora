using Finora.Application.DTOs.Search;

namespace Finora.Application.Interfaces;

public interface ISearchService
{
    /// <summary>Global search across transactions, accounts and objectives for the household.</summary>
    Task<GlobalSearchResultDto> SearchAsync(Guid householdId, string query, CancellationToken cancellationToken = default);
}
