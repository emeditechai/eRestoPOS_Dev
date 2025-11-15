using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class RoleMenuSelectionRequest
    {
        [Required]
        public int RoleId { get; set; }

        public List<int> MenuIds { get; set; } = new();
    }
}
