using Microsoft.EntityFrameworkCore;
using PettyCashOCR.Models;

namespace PettyCashOCR.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<PettyCashVoucher> PettyCashVouchers { get; set; }
        public DbSet<VoucherLineItem> VoucherLineItems { get; set; }
        public DbSet<BudgeteryDetails> BudgeteryDetails { get; set; }
        public DbSet<AccountingAllocation> AccountingAllocations { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Example method to execute a raw SQL query
        public async Task<List<PettyCashVoucher>> GetVouchersAsync()
        {
            return await PettyCashVouchers
                .FromSqlRaw("SELECT * FROM PettyCashVoucher")
                .ToListAsync();
        }
    }
}
