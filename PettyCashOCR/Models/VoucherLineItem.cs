using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PettyCashOCR.Models
{
    public class VoucherLineItem
    {
        internal static readonly object LineItems;

        [Key]
        public int Id { get; set; }

        [ForeignKey("PettyCashVoucher")]
        public int VoucherId { get; set; }

        [Required]
        public string Details { get; set; }

        [Required]
        public decimal Amount { get; set; }

        // Navigation property
        public PettyCashVoucher? PettyCashVoucher { get; set; }
    }
}