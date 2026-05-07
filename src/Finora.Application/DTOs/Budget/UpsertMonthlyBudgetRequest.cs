using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Budget;

public record UpsertMonthlyBudgetRequest
{
    [Required]
    [Range(2000, 2100)]
    public int Year { get; init; }

    [Required]
    [Range(1, 12)]
    public int Month { get; init; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal ExpectedIncome { get; init; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal ExpectedExpenses { get; init; }
}
