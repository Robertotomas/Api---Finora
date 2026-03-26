namespace Finora.Application.DTOs.Objectives;

public record SavingsObjectiveActiveDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal TargetAmount { get; init; }
    public DateOnly? TargetDate { get; init; }
    public int SortOrder { get; init; }
    public decimal AllocatedAmount { get; init; }
    public decimal ProgressPercent { get; init; }
    public bool CanFinalize { get; init; }
}

public record SavingsObjectiveHistoryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal TargetAmount { get; init; }
    public DateOnly? TargetDate { get; init; }
    public int SortOrder { get; init; }
    public DateTime CompletedAt { get; init; }
}

public record SavingsObjectivesOverviewDto
{
    public decimal TotalSavings { get; init; }
    public decimal ReservedByCompletedObjectives { get; init; }
    public decimal AvailableForActiveObjectives { get; init; }
    public IReadOnlyList<SavingsObjectiveActiveDto> ActiveObjectives { get; init; } = [];
    public IReadOnlyList<SavingsObjectiveHistoryDto> HistoryObjectives { get; init; } = [];
}
