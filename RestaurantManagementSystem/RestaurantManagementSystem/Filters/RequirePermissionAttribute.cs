using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantManagementSystem.Models.Authorization;
using RestaurantManagementSystem.Services;

namespace RestaurantManagementSystem.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string menuCode, PermissionAction action = PermissionAction.View)
            : base(typeof(RequirePermissionFilter))
        {
            Arguments = new object[] { menuCode, action };
        }
    }

    public class RequirePermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly RolePermissionService _permissionService;
        private readonly string _menuCode;
        private readonly PermissionAction _action;

        public RequirePermissionFilter(RolePermissionService permissionService, string menuCode, PermissionAction action)
        {
            _permissionService = permissionService;
            _menuCode = menuCode;
            _action = action;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var allowed = await _permissionService.HasPermissionAsync(user, _menuCode, _action);
            if (!allowed)
            {
                context.Result = new ForbidResult();
            }
        }
    }
}
