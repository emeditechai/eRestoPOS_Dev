using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    public class BranchMaster
    {
        public int BranchId { get; set; }

        [Required]
        [StringLength(4, ErrorMessage = "Branch Code must be maximum 4 characters.")]
        [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Branch Code must be alphanumeric only.")]
        [Display(Name = "Branch Code")]
        public string BranchCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Display(Name = "Branch Name")]
        public string BranchName { get; set; } = string.Empty;

        [Display(Name = "Main Branch")]
        public bool Is_MainBranch { get; set; }

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
