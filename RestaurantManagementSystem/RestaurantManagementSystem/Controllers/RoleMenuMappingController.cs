using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RestaurantManagementSystem.Filters;
using RestaurantManagementSystem.Models.Authorization;
using RestaurantManagementSystem.Services;
using RestaurantManagementSystem.Utilities;
using RestaurantManagementSystem.ViewModels.Authorization;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize]
    [RequirePermission("NAV_SETTINGS_ROLE_MENU", PermissionAction.View)]
    public class RoleMenuMappingController : Controller
    {
        private readonly RolePermissionService _rolePermissionService;
        private readonly UserRoleService _userRoleService;

        public RoleMenuMappingController(RolePermissionService rolePermissionService, UserRoleService userRoleService)
        {
            _rolePermissionService = rolePermissionService;
            _userRoleService = userRoleService;
        }

        public async Task<IActionResult> Index()
        {
            var roles = await _userRoleService.GetAllRolesAsync();
            var viewModel = new RoleMenuMappingViewModel
            {
                Roles = roles
                    .OrderBy(r => r.Name)
                    .Select(r => new SelectListItem(r.Name, r.Id.ToString()))
                    .ToList()
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Tree(int roleId)
        {
            var tree = await _rolePermissionService.GetRoleMenuTreeAsync(roleId);
            return Json(tree);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("NAV_SETTINGS_ROLE_MENU", PermissionAction.Edit)]
        public async Task<IActionResult> Save([FromBody] RoleMenuSelectionRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid request payload." });
            }

            var userId = User.GetUserId();
            await _rolePermissionService.SaveRoleMenuAssignmentsAsync(request.RoleId, request.MenuIds, userId);
            return Ok(new { message = "Role menu mapping saved." });
        }
    }
}
