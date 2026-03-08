using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantManagementSystem.Models
{
    /// <summary>
    /// Party Master – EF entity mapped to the Parties table.
    /// Used by MasterController for CRUD via Entity Framework.
    /// </summary>
    public class PartyMaster
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Party Code")]
        public string PartyCode { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Party Name")]
        public string PartyName { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "Party Type")]
        public string PartyType { get; set; } = "Vendor";

        [EmailAddress]
        [StringLength(200)]
        public string? Email { get; set; }

        [StringLength(20)]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(10)]
        [Display(Name = "PIN Code")]
        public string? PinCode { get; set; }

        [Display(Name = "Credit Allowed")]
        public bool IsCreditAllow { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Credit / Allow Balance")]
        public decimal? AllowBalance { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [NotMapped]
        public int DisplayOrder { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
