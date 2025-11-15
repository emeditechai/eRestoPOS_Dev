using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using RestaurantManagementSystem.Models;

namespace RestaurantManagementSystem.Services
{
    public interface IAuthService
    {
        Task<(bool Success, string Message, ClaimsPrincipal Principal)> AuthenticateUserAsync(string username, string password);
        Task<bool> IsUserInRoleAsync(int userId, string roleName);
        Task<(bool success, string message)> RegisterUserAsync(User user, string password, string roleName = "Staff");
        Task<(bool success, string message)> UpdatePasswordAsync(int userId, string newPassword);
        Task<(bool success, string message)> LockUserAsync(int userId);
        Task<(bool success, string message)> UnlockUserAsync(int userId);
        
        // Additional methods required by AccountController
        Task SignInUserAsync(ClaimsPrincipal principal, bool rememberMe);
        Task SignOutUserAsync();
        Task<(bool success, string message)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
        Task<List<User>> GetUsersAsync();
        Task<User> GetUserForEditAsync(int userId);
        Task<(bool success, string message)> UpdateUserAsync(User user, int updatedByUserId);
        Task<(bool success, string message)> SwitchRoleAsync(ClaimsPrincipal currentUser, int roleId);
    }
}
