using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QueueManagement.Shared;

namespace QueueManagement.Api.Data;

public class QueueDbContext : IdentityDbContext<IdentityUser>
{
    public QueueDbContext(DbContextOptions<QueueDbContext> options) : base(options)
    {
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ServiceItem> Services => Set<ServiceItem>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>().HasData(
            new Branch { Id = 1, Name = "Main Branch", Location = "Head Office" }
        );

        modelBuilder.Entity<ServiceItem>().HasData(
            new ServiceItem { Id = 1, Name = "Teller Services", Description = "Cash withdrawals and deposits", ServiceCode = "TELL", BranchId = 1 },
            new ServiceItem { Id = 2, Name = "Account Opening", Description = "New customer onboarding", ServiceCode = "ACCT", BranchId = 1 },
            new ServiceItem { Id = 3, Name = "Loan Support", Description = "Loan application assistance", ServiceCode = "LOAN", BranchId = 1 },
            new ServiceItem { Id = 4, Name = "Card Services", Description = "Debit and credit card support", ServiceCode = "CARD", BranchId = 1 },
            new ServiceItem { Id = 5, Name = "Wealth Advice", Description = "Investment consultation", ServiceCode = "WEAL", BranchId = 1 },
            new ServiceItem { Id = 6, Name = "Customer Support", Description = "General service enquiries", ServiceCode = "CUST", BranchId = 1 }
        );

        modelBuilder.Entity<Appointment>().Property(a => a.CustomerName).HasMaxLength(150);
        modelBuilder.Entity<Appointment>().Property(a => a.CustomerEmail).HasMaxLength(200);
        modelBuilder.Entity<Appointment>().Property(a => a.CustomerPhone).HasMaxLength(25);
        modelBuilder.Entity<Appointment>().Property(a => a.TimeSlot).HasMaxLength(50);
        modelBuilder.Entity<Appointment>().Property(a => a.QueueCode).HasMaxLength(20);

        base.OnModelCreating(modelBuilder);
    }
}
