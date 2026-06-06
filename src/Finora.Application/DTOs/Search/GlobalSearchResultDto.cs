namespace Finora.Application.DTOs.Search;

public record GlobalSearchResultDto
{
    public IReadOnlyList<SearchTransactionDto> Transactions { get; init; } = [];
    public IReadOnlyList<SearchAccountDto> Accounts { get; init; } = [];
    public IReadOnlyList<SearchObjectiveDto> Objectives { get; init; } = [];
}

public record SearchTransactionDto
{
    public Guid Id { get; init; }
    public string? Description { get; init; }
    public string? EntityName { get; init; }
    public decimal Amount { get; init; }
    public int Type { get; init; }
    public int Category { get; init; }
    public DateTime Date { get; init; }
}

public record SearchAccountDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Balance { get; init; }
}

public record SearchObjectiveDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool Completed { get; init; }
}
