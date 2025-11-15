using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RestaurantManagementSystem.Models.Authorization;
using RestaurantManagementSystem.Services;

namespace RestaurantManagementSystem.ViewComponents
{
    public class NavigationMenuViewComponent : ViewComponent
    {
        private readonly RolePermissionService _permissionService;
        private readonly ILogger<NavigationMenuViewComponent> _logger;

        public NavigationMenuViewComponent(RolePermissionService permissionService, ILogger<NavigationMenuViewComponent> logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            IReadOnlyCollection<NavigationMenuNode> nodes = Array.Empty<NavigationMenuNode>();

            try
            {
                nodes = await _permissionService.GetNavigationTreeForUserAsync(HttpContext.User);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unable to load navigation tree. Rendering empty navigation.");
            }

            return View(nodes);
        }
    }
}
