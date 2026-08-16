using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.ViewModels
{
    public class SignUpViewModel
    {
        [Required(ErrorMessage = "First Name is required.")]
        [RegularExpression(@"^[a-zA-Z\s]+$", ErrorMessage = "First Name must contain alphabetic characters only.")]
        [StringLength(50, ErrorMessage = "First Name cannot exceed 50 characters.")]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [StringLength(50, ErrorMessage = "Last Name cannot exceed 50 characters.")]
        [RegularExpression(@"^[a-zA-Z\s]*$", ErrorMessage = "Last Name must contain alphabetic characters only.")]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Phone Number is required.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Phone Number must be exactly 10 digits.")]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }

        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(100, ErrorMessage = "Email Address cannot exceed 100 characters.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Location is required.")]
        [StringLength(100, ErrorMessage = "Location cannot exceed 100 characters.")]
        [Display(Name = "Location")]
        public string Location { get; set; }

        [Required(ErrorMessage = "Restaurant Name is required.")]
        [StringLength(150, ErrorMessage = "Restaurant Name cannot exceed 150 characters.")]
        [Display(Name = "Restaurant Name")]
        public string RestaurantName { get; set; }

        [Required(ErrorMessage = "City is required.")]
        [Display(Name = "City")]
        public string City { get; set; }

        [Required(ErrorMessage = "State is required.")]
        [Display(Name = "State")]
        public string State { get; set; }

        [Required(ErrorMessage = "Pincode is required.")]
        [Display(Name = "Pincode")]
        public string Pincode { get; set; }

        [Required(ErrorMessage = "Country is required.")]
        [Display(Name = "Country")]
        public string Country { get; set; }

        [Required(ErrorMessage = "GST Code is required.")]
        [Display(Name = "GST Code")]
        public string GSTCode { get; set; }

        [Display(Name = "Website")]
        public string Website { get; set; }

        [Display(Name = "Restaurant Logo")]
        public Microsoft.AspNetCore.Http.IFormFile Logo { get; set; }

        // Auto-generated, displayed read-only on form
        [Display(Name = "Username")]
        public string Username { get; set; }
    }

    public class SignUpResultViewModel
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
