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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Household>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Type).HasConversion<int>();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);

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

            entity.HasOne(e => e.Household)
                .WithMany(h => h.Accounts)
                .HasForeignKey(e => e.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
