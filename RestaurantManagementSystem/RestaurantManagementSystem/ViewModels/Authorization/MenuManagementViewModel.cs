using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using RestaurantManagementSystem.Models.Authorization;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class MenuManagementViewModel
    {
        public List<NavigationMenu> Menus { get; set; } = new();
        public MenuEditViewModel MenuForm { get; set; } = new();
        public List<SelectListItem> ParentOptions { get; set; } = new();
    }
}
