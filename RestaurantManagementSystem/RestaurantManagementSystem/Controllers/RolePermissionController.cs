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
    [RequirePermission("NAV_SETTINGS_ROLE_PERMISSIONS", PermissionAction.View)]
    public class RolePermissionController : Controller
    {
        private readonly RolePermissionService _rolePermissionService;
        private readonly UserRoleService _userRoleService;

        public RolePermissionController(RolePermissionService rolePermissionService, UserRoleService userRoleService)
        {
            _rolePermissionService = rolePermissionService;
            _userRoleService = userRoleService;
        }

        public async Task<IActionResult> Index()
        {
            var roles = await _userRoleService.GetAllRolesAsync();
            var model = new RolePermissionMatrixViewModel
            {
                Roles = roles
                    .OrderBy(r => r.Name)
                    .Select(r => new SelectListItem(r.Name, r.Id.ToString()))
                    .ToList()
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Matrix(int roleId)
        {
            var matrix = await _rolePermissionService.GetPermissionMatrixAsync(roleId, assignedMenusOnly: true);
            return Json(matrix);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("NAV_SETTINGS_ROLE_PERMISSIONS", PermissionAction.Edit)]
        public async Task<IActionResult> Save([FromBody] RolePermissionSaveRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid permission payload." });
            }

            var userId = User.GetUserId();
            await _rolePermissionService.SavePermissionMatrixAsync(request.RoleId, request.Permissions, userId);
            return Ok(new { message = "Permissions updated successfully." });
        }
    }
}
