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

    /// <summary>Set when another member leaves a Couple household; the remaining member(s) see assistance to keep or reset shared data.</summary>
    public DateTime? PartnerLeftNoticeAtUtc { get; set; }

    /// <summary>Stripe Customer id for this household (one customer per household). Set on first checkout/portal use.</summary>
    public string? StripeCustomerId { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<SavingsObjective> SavingsObjectives { get; set; } = new List<SavingsObjective>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<MonthlyReport> MonthlyReports { get; set; } = new List<MonthlyReport>();
    public ICollection<MonthlyBudget> MonthlyBudgets { get; set; } = new List<MonthlyBudget>();
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<InvestmentHolding> InvestmentHoldings { get; set; } = new List<InvestmentHolding>();
}
