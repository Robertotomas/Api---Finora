using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionSplit> TransactionSplits => Set<TransactionSplit>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<SavingsObjective> SavingsObjectives => Set<SavingsObjective>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<MonthlyReport> MonthlyReports => Set<MonthlyReport>();
    public DbSet<CoupleInvitation> CoupleInvitations => Set<CoupleInvitation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetValuation> AssetValuations => Set<AssetValuation>();
    public DbSet<InvestmentHolding> InvestmentHoldings => Set<InvestmentHolding>();
    public DbSet<InvestmentTransaction> InvestmentTransactions => Set<InvestmentTransaction>();
    public DbSet<InvestmentDeposit> InvestmentDeposits => Set<InvestmentDeposit>();
    public DbSet<InstrumentQuote> InstrumentQuotes => Set<InstrumentQuote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Household>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.StripeCustomerId).HasMaxLength(255);

            entity.HasOne(e => e.PrimaryAccount)
                .WithMany()
                .HasForeignKey(e => e.PrimaryAccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Gender).HasConversion<int>().IsRequired(false);
            entity.Property(e => e.TimeZoneId).HasMaxLength(100);

            entity.HasOne(e => e.Household)
                .WithMany(h => h.Users)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Balance).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.LogoDomain).HasMaxLength(500);

            entity.HasOne(e => e.Household)
                .WithMany(h => h.Accounts)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Category).HasConversion<int>();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.EntityType).HasConversion<int>();
            entity.Property(e => e.EntityName).HasMaxLength(200);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DestinationAccount)
                .WithMany()
                .HasForeignKey(e => e.DestinationAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasOne(e => e.Household)
                .WithMany(h => h.Transactions)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransactionSplit>(entity =>
        {
            entity.HasKey(e => new { e.TransactionId, e.UserId });
            entity.Property(e => e.Percentage).HasPrecision(5, 2);

            entity.HasOne(e => e.Transaction)
                .WithMany(t => t.Splits)
                .HasForeignKey(e => e.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Category).HasConversion<int>();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.EntityType).HasConversion<int>();
            entity.Property(e => e.EntityName).HasMaxLength(200);

            entity.HasOne(e => e.Account)
                .WithMany()
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DestinationAccount)
                .WithMany()
                .HasForeignKey(e => e.DestinationAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasOne(e => e.ResponsibleUser)
                .WithMany()
                .HasForeignKey(e => e.ResponsibleUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            entity.HasOne(e => e.Household)
                .WithMany()
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavingsObjective>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.TargetAmount).HasPrecision(18, 2);
            entity.Property(e => e.TargetDate);
            entity.HasIndex(e => new { e.HouseholdId, e.CompletedAt });
            entity.HasIndex(e => new { e.HouseholdId, e.SortOrder });

            entity.HasOne(e => e.Household)
                .WithMany(h => h.SavingsObjectives)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Plan).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.ExpiresAt);
            entity.Property(e => e.StripeSubscriptionId).HasMaxLength(255);

            entity.HasOne(e => e.Household)
                .WithMany(h => h.Subscriptions)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MonthlyReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileRelativePath).HasMaxLength(500);
            entity.Property(e => e.TemplateVersion).HasDefaultValue(0);
            entity.HasIndex(e => new { e.HouseholdId, e.Year, e.Month }).IsUnique();

            entity.HasOne(e => e.Household)
                .WithMany(h => h.MonthlyReports)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MonthlyBudget>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExpectedIncome).HasPrecision(18, 2);
            entity.Property(e => e.ExpectedExpenses).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.HouseholdId, e.Year, e.Month }).IsUnique();

            entity.HasOne(e => e.Household)
                .WithMany(h => h.MonthlyBudgets)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(128);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailConfirmationToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Message).HasMaxLength(500);
            entity.Property(e => e.RedirectUrl).HasMaxLength(300);
            entity.Property(e => e.DeduplicationKey).HasMaxLength(200);
            entity.HasIndex(e => new { e.HouseholdId, e.IsRead, e.CreatedAt });
            entity.HasIndex(e => e.DeduplicationKey).IsUnique().HasFilter("\"DeduplicationKey\" IS NOT NULL");

            entity.HasOne(e => e.Household)
                .WithMany()
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Category).HasConversion<int>();
            entity.Property(e => e.AcquisitionCost).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.HasIndex(e => e.HouseholdId);

            entity.HasOne(e => e.Household)
                .WithMany(h => h.Assets)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssetValuation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Value).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.AssetId, e.Date });

            entity.HasOne(e => e.Asset)
                .WithMany(a => a.Valuations)
                .HasForeignKey(e => e.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvestmentHolding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.Exchange).HasMaxLength(32);
            entity.Property(e => e.ProviderSymbol).HasMaxLength(48);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.LogoDomain).HasMaxLength(255);
            entity.Property(e => e.Currency).HasMaxLength(8);
            entity.Property(e => e.Type).HasConversion<int>();
            entity.HasIndex(e => e.HouseholdId);

            entity.HasOne(e => e.Household)
                .WithMany(h => h.InvestmentHoldings)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvestmentTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Operation).HasConversion<int>();
            entity.Property(e => e.Quantity).HasPrecision(18, 6);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 6);
            entity.Property(e => e.Commission).HasPrecision(18, 6);
            entity.Property(e => e.FxRateToEur).HasPrecision(18, 8);
            entity.Property(e => e.FxFeePercent).HasPrecision(7, 4);
            entity.Property(e => e.ExternalId).HasMaxLength(80);
            entity.HasIndex(e => e.InvestmentHoldingId);
            entity.HasIndex(e => e.ExternalId);

            entity.HasOne(e => e.InvestmentHolding)
                .WithMany(h => h.Transactions)
                .HasForeignKey(e => e.InvestmentHoldingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvestmentDeposit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(8);
            entity.Property(e => e.ExternalId).HasMaxLength(80);
            entity.HasIndex(e => e.HouseholdId);
            entity.HasIndex(e => e.ExternalId);

            entity.HasOne(e => e.Household)
                .WithMany()
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InstrumentQuote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderSymbol).HasMaxLength(48);
            entity.Property(e => e.Currency).HasMaxLength(8);
            entity.Property(e => e.Price).HasPrecision(18, 6);
            entity.HasIndex(e => e.ProviderSymbol).IsUnique();
        });

        modelBuilder.Entity<CoupleInvitation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InviteeEmail).HasMaxLength(256);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.Property(e => e.OtpHash).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.HasIndex(e => e.TokenHash);

            entity.HasOne(e => e.InviterUser)
                .WithMany()
                .HasForeignKey(e => e.InviterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.InviterHousehold)
                .WithMany()
                .HasForeignKey(e => e.InviterHouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
