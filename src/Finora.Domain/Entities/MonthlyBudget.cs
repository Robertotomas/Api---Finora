using Finora.Domain.Common;

namespace Finora.Domain.Entities;

public class MonthlyBudget : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household? Household { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal ExpectedIncome { get; set; }
    public decimal ExpectedExpenses { get; set; }
}
