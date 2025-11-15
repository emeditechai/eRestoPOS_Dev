using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class RolePermissionSaveRequest
    {
        [Required]
        public int RoleId { get; set; }

        public List<RolePermissionMatrixRow> Permissions { get; set; } = new();
    }
}
