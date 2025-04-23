using Microsoft.EntityFrameworkCore;

namespace PettyCashOCR.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<PettyCashVoucher> PettyCashVouchers { get; set; }
        public DbSet<VoucherLineItem> VoucherLineItems { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PettyCashVoucher>()
                .Property(v => v.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<VoucherLineItem>()
                .Property(v => v.Amount)
                .HasColumnType("decimal(18,2)");
        }

        // Add OnModelCreating if you need to configure relationships or table names
    }

}
