using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    public class BankViewModel
    {
        public byte Id { get; set; }

        [Required(ErrorMessage = "Bank name is required")]
        [StringLength(100)]
        public string BankName { get; set; }

        public bool Status { get; set; } = true;
    }
}
