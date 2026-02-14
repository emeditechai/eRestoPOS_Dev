using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantManagementSystem.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [StringLength(255)]
        public string PasswordHash { get; set; }

        [StringLength(100)]
        public string Salt { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [StringLength(50)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }
        
        // Property for full name that controllers expect
        [Display(Name = "Full Name")]
        public string FullName 
        {
            get { return $"{FirstName} {LastName}".Trim(); }
        }

        [EmailAddress]
        [StringLength(100)]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Phone]
        [StringLength(20)]
        [Display(Name = "Phone")]
        public string Phone { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
        
        [Display(Name = "Locked Out")]
        public bool IsLockedOut { get; set; } = false;
        
        [Display(Name = "Failed Login Attempts")]
        public int FailedLoginAttempts { get; set; } = 0;
        
        [Display(Name = "Last Login")]
        public DateTime? LastLoginDate { get; set; }
        
        [Display(Name = "Requires MFA")]
        public bool RequiresMFA { get; set; } = false;
        
        [Display(Name = "Must Change Password")]
        public bool MustChangePassword { get; set; } = false;
        
        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        [Display(Name = "Updated At")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        
        [Display(Name = "Created By")]
        public int? CreatedBy { get; set; }
        
        [Display(Name = "Updated By")]
        public int? UpdatedBy { get; set; }
        
        // Non-persisted properties for form handling
        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, ErrorMessage = "Password must be between {2} and {1} characters", MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }
        
        [Compare("Password", ErrorMessage = "Confirm password doesn't match")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; }
        
        // For the database-backed role-based system
        [NotMapped]
        public List<Role> Roles { get; set; } = new List<Role>();
        
        // Helper property to get/set selected roles
        [Display(Name = "Roles")]
        public List<int> SelectedRoleIds { get; set; } = new List<int>();

        [NotMapped]
        [Display(Name = "Branches")]
        public List<int> SelectedBranchIds { get; set; } = new List<int>();

        [NotMapped]
        public List<BranchMaster> Branches { get; set; } = new List<BranchMaster>();
        
        // Helper property for backward compatibility
        [NotMapped]
        public int PrimaryRoleId 
        {
            get { return Roles.Count > 0 ? Roles[0].Id : 0; }
        }
    }
}
