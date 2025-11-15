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
    [RequirePermission("NAV_SETTINGS_MENU_BUILDER", PermissionAction.View)]
    public class MenuManagementController : Controller
    {
        private readonly RolePermissionService _rolePermissionService;

        public MenuManagementController(RolePermissionService rolePermissionService)
        {
            _rolePermissionService = rolePermissionService;
        }

        public async Task<IActionResult> Index()
        {
            var menus = await _rolePermissionService.GetAllMenusAsync();
            var parentOptions = menus
                .Where(m => string.IsNullOrWhiteSpace(m.ParentCode))
                .OrderBy(m => m.DisplayOrder)
                .Select(m => new SelectListItem { Value = m.Code, Text = m.DisplayName })
                .ToList();
            parentOptions.Insert(0, new SelectListItem("-- Root (Top Level) --", string.Empty));

            var viewModel = new MenuManagementViewModel
            {
                Menus = menus,
                ParentOptions = parentOptions
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("NAV_SETTINGS_MENU_BUILDER", PermissionAction.Add)]
        public async Task<IActionResult> Create(MenuEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please check the input values.";
                return RedirectToAction(nameof(Index));
            }

            var userId = User.GetUserId();
            await _rolePermissionService.CreateMenuAsync(model, userId);
            TempData["SuccessMessage"] = $"Menu '{model.DisplayName}' created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("NAV_SETTINGS_MENU_BUILDER", PermissionAction.Edit)]
        public async Task<IActionResult> Update(MenuEditViewModel model)
        {
            if (!ModelState.IsValid || model.Id is null)
            {
                TempData["ErrorMessage"] = "Invalid menu payload.";
                return RedirectToAction(nameof(Index));
            }

            var userId = User.GetUserId();
            await _rolePermissionService.UpdateMenuAsync(model, userId);
            TempData["SuccessMessage"] = $"Menu '{model.DisplayName}' updated.";
            return RedirectToAction(nameof(Index));
        }
    }
}
