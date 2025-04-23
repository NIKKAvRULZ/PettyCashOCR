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

        public string? Date { get; set; }

        public string? VoucherNo { get; set; }

        public string? Email { get; set; }

        public string? ContactNo { get; set; }

        [Required]
        public decimal TotalAmount { get; set; }

        // Navigation property
        public List<VoucherLineItem>? LineItems { get; set; }  // Changed from ICollection<>
    }

}
