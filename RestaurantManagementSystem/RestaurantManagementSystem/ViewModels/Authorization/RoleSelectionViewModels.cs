using System.Collections.Generic;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class RoleSelectionOptionViewModel
    {
        public int RoleId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class SwitchRoleRequest
    {
        public int RoleId { get; set; }
    }
}
