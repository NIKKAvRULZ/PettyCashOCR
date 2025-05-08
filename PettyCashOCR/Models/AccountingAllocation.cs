using System.ComponentModel.DataAnnotations.Schema;

namespace PettyCashOCR.Models
{
    public class AccountingAllocation
    {
        public int Id { get; set; }

        [ForeignKey("PettyCashVoucher")]
        public int VoucherId { get; set; }

        public string? Account { get; set; }

        public string? CC { get; set; }

        public string? FLTNO { get; set; }

        public string? ACRFT { get; set; }

        public string? PROJ { get; set; }

        public string? Amount { get; set; }

        public string? CrossReference { get; set; }

        public string? Description { get; set; }

        // Navigation property
        public PettyCashVoucher? PettyCashVoucher { get; set; }

    }
}
