using QueueManagement.Shared;

namespace QueueManagement.Api.Data;

public static class SeedData
{
    public static void Initialize(QueueDbContext context)
    {
        context.Database.EnsureCreated();

        if (!context.Branches.Any())
        {
            context.Branches.Add(new Branch { Name = "Main Branch", Location = "Head Office" });
        }

        if (!context.Services.Any())
        {
            context.Services.AddRange(
                new ServiceItem { Name = "Teller Services", Description = "Cash withdrawals and deposits", ServiceCode = "TELL", BranchId = 1 },
                new ServiceItem { Name = "Account Opening", Description = "New customer onboarding", ServiceCode = "ACCT", BranchId = 1 },
                new ServiceItem { Name = "Loan Support", Description = "Loan application assistance", ServiceCode = "LOAN", BranchId = 1 },
                new ServiceItem { Name = "Card Services", Description = "Debit and credit card support", ServiceCode = "CARD", BranchId = 1 },
                new ServiceItem { Name = "Wealth Advice", Description = "Investment consultation", ServiceCode = "WEAL", BranchId = 1 },
                new ServiceItem { Name = "Customer Support", Description = "General service enquiries", ServiceCode = "CUST", BranchId = 1 }
            );
        }

        context.SaveChanges();
    }
}
