using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace PettyCashOCR.Models
{
    public class BudgeteryDetails
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // <- this line is important

        public int Id { get; set; }

        [ForeignKey("PettyCashVoucher")]
        public int VoucherId { get; set; }

        public string? Account { get; set; }

        public string? CC { get; set; }

        public int Budget { get; set; }

        public int Utilised { get; set; }

        public int Variance { get; set; }

        [Required]
        public decimal ThisPayment { get; set; }

        // Navigation property
        public PettyCashVoucher? PettyCashVoucher { get; set; }
    }
}