using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class Household : BaseEntity
{
    public HouseholdType Type { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>When on Free with more than one account, only this account accepts new activity until the user upgrades or removes accounts.</summary>
    public Guid? PrimaryAccountId { get; set; }
    public Account? PrimaryAccount { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<SavingsObjective> SavingsObjectives { get; set; } = new List<SavingsObjective>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<MonthlyReport> MonthlyReports { get; set; } = new List<MonthlyReport>();
}
