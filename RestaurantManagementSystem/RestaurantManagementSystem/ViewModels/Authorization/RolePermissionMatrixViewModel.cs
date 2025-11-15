using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class RolePermissionMatrixViewModel
    {
        public List<SelectListItem> Roles { get; set; } = new();
    }
}
