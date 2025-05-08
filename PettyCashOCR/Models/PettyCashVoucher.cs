using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PettyCashOCR.Models
{
    public class PettyCashVoucher
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "Paid To")]
        public string? PaidTo { get; set; }

        public string? StaffNo { get; set; }

        public string? Department { get; set; }

        public string? CostCenter { get; set; }

        public string? Station { get; set; }

        public string? Date { get; set; }

        public string? VoucherNo { get; set; }

        public string? AmountInWords { get; set; }

        public string? ApprovedBy { get; set; }

        public string? ReceivedCash { get; set; }

        [Required]
        public decimal TotalAmount { get; set; }

        // Navigation property
        public List<VoucherLineItem>? LineItems { get; set; }  // Changed from ICollection<>
        public List<BudgeteryDetails>? BudgeteryDetails { get; set; }  // Changed from ICollection<>
        public List<AccountingAllocation> AccountingAllocations { get; set; } // Changed from ICollection<>

    }
}