using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Services;
using RestaurantManagementSystem.Utilities;
using RestaurantManagementSystem.ViewModels.Authorization;
using RestaurantManagementSystem.ViewModels;

namespace RestaurantManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;
        private readonly UserRoleService _userRoleService;
        private readonly ILogger<AccountController> _logger;
        
        public AccountController(IAuthService authService, UserRoleService userRoleService, ILogger<AccountController> logger)
        {
            _authService = authService;
            _userRoleService = userRoleService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> ReportClientIp([FromForm] string token, [FromForm] string ip)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(ip)) return BadRequest();

            try
            {
                var updated = await (_authService as Services.AuthService)?.UpdateSessionIpAsync(token, ip);
                if (updated == true) return Ok();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating client IP");
            }

            return StatusCode(500);
        }
        
        [HttpGetAttribute]
        [AllowAnonymousAttribute]
        public IActionResult Login(string returnUrl = null)
        {
            // If user is already authenticated, redirect to home
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }
        
        [HttpPostAttribute]
        [HttpPost]
        [AllowAnonymousAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var (success, message, principal) = await _authService.AuthenticateUserAsync(model.Username, model.Password);
                
                if (success && principal != null)
                {
                    // Check if MFA is required
                    if (principal.HasClaim(c => c.Type == "RequiresMFA" && c.Value == "true"))
                    {
                        // Redirect to MFA verification
                        // In a real application, you would generate and send an MFA code here
                        // For now, we'll simulate MFA by just showing the MFA view
                        TempData["Username"] = principal.Identity.Name;
                        TempData["RememberMe"] = model.RememberMe;
                        TempData["ReturnUrl"] = model.ReturnUrl;
                        
                        return RedirectToAction("VerifyMFA");
                    }
                    
                    // Sign in the user
                    await _authService.SignInUserAsync(principal, model.RememberMe);
                    
                    // Redirect to returnUrl or home page
                    if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                    {
                        return Redirect(model.ReturnUrl);
                    }
                    
                    return RedirectToAction("Index", "Home");
                }
                
                ModelState.AddModelError(string.Empty, message);
            }
            
            return View(model);
        }
        
        [HttpGetAttribute]
        [AllowAnonymousAttribute]
        public IActionResult VerifyMFA()
        {
            // Check if we have the necessary TempData
            if (TempData["UserId"] == null)
            {
                return RedirectToAction("Login");
            }
            
            var model = new MFAViewModel
            {
                UserId = TempData["UserId"].ToString(),
                ReturnUrl = TempData["ReturnUrl"]?.ToString(),
                FactorType = "Email" // Default to Email MFA
            };
            
            // Preserve the TempData for the post action
            TempData.Keep("UserId");
            TempData.Keep("RememberMe");
            TempData.Keep("ReturnUrl");
            
            return View(model);
        }
        
        [HttpPostAttribute]
        [AllowAnonymousAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public async Task<IActionResult> VerifyMFA(MFAViewModel model)
        {
            if (ModelState.IsValid)
            {
                // In a real application, you would validate the MFA code here
                // For this demo, we'll accept any 6-digit code
                if (model.VerificationCode.Length == 6 && int.TryParse(model.VerificationCode, out _))
                {
                    // Get the user from the database
                    int userId = int.Parse(TempData["UserId"].ToString());
                    bool rememberMe = (bool)TempData["RememberMe"];
                    
                    // For a real app, you would validate the MFA code against what was sent to the user
                    // Here we're just simulating a successful MFA
                    
                    // Get the user details
                    var (_, _, user) = await _authService.AuthenticateUserAsync("admin", "Admin@123"); // This is just to get the user object
                    
                    if (user != null)
                    {
                        // Sign in the user
                        await _authService.SignInUserAsync(user, rememberMe);
                        
                        // Redirect to returnUrl or home page
                        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                        {
                            return Redirect(model.ReturnUrl);
                        }
                        
                        return RedirectToAction("Index", "Home");
                    }
                }
                
                ModelState.AddModelError(string.Empty, "Invalid verification code");
            }
            
            return View(model);
        }
        
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public async Task<IActionResult> Logout()
        {
            await _authService.SignOutUserAsync();
            return RedirectToAction("Login");
        }
        
        [HttpGetAttribute]
        [AllowAnonymousAttribute]
        public IActionResult Register()
        {
            // If user is already authenticated, redirect to home
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            
            return View();
        }
        
        [HttpPostAttribute]
        [AllowAnonymousAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Convert RegisterViewModel to User model
                var user = new User
                {
                    Username = model.Username,
                    Password = model.Password,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    Phone = model.PhoneNumber,
                    IsActive = true,
                    RequiresMFA = false,
                    SelectedRoleIds = new List<int> { 2 } // Default to regular user role
                };
                
                // Set default role to Staff
                string roleName = "Staff";
                
                var result = await _authService.RegisterUserAsync(user, user.Password, roleName);
                
                if (result.success)
                {
                    // Registration successful, redirect to login
                    TempData["SuccessMessage"] = "Registration successful. You can now log in.";
                    return RedirectToAction("Login");
                }
                
                ModelState.AddModelError(string.Empty, result.message);
            }
            
            return View(model);
        }
        
        [HttpGetAttribute]
        [AuthorizeAttribute]
        public IActionResult ChangePassword()
        {
            return View();
        }
        
        [HttpPostAttribute]
        [AuthorizeAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                {
                    var result = await _authService.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword);
                    
                    if (result.success)
                    {
                        TempData["SuccessMessage"] = "Password changed successfully.";
                        return RedirectToAction("Index", "Home");
                    }
                    
                    ModelState.AddModelError(string.Empty, result.message);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "User ID not found");
                }
            }
            
            return View(model);
        }
        
        [HttpGetAttribute]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> UserList()
        {
            var users = await _authService.GetUsersAsync();
            var model = new UserListViewModel
            {
                Users = users,
                Pagination = new PaginationViewModel
                {
                    CurrentPage = 1,
                    ItemsPerPage = 20,
                    TotalItems = users.Count
                }
            };
            
            return View(model);
        }
        
        [HttpGetAttribute]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> EditUser(int id)
        {
            var model = await _authService.GetUserForEditAsync(id);
            
            if (model == null)
            {
                return NotFound();
            }
            
            return View(model);
        }
        
        [HttpPostAttribute]
        [Authorize(Roles = "Administrator")]
        [ValidateAntiForgeryTokenAttribute]
        public async Task<IActionResult> EditUser(User model)
        {
            if (ModelState.IsValid)
            {
                // Get the current user ID for audit
                int updatedByUserId = 1; // Default to system user
                if (User.Identity.IsAuthenticated && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
                {
                    updatedByUserId = userId;
                }
                
                var result = await _authService.UpdateUserAsync(model, updatedByUserId);
                
                if (result.success)
                {
                    TempData["SuccessMessage"] = "User updated successfully.";
                    return RedirectToAction("UserList");
                }
                
                ModelState.AddModelError(string.Empty, result.message);
            }
            
            // Re-populate available roles for the dropdown
            ViewBag.Roles = await _userRoleService.GetAllRolesAsync();
            
            return View(model);
        }
        
        [HttpGetAttribute]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> UserRoles()
        {
            var userId = User.GetUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var roles = await _userRoleService.GetUserRolesAsync(userId.Value);
            var activeRoleId = User.GetActiveRoleId();

            var payload = roles.Select(role => new RoleSelectionOptionViewModel
            {
                RoleId = role.Id,
                Name = role.Name,
                Description = role.Description,
                IsActive = activeRoleId.HasValue && activeRoleId.Value == role.Id
            }).ToList();

            return Json(payload);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchRole([FromBody] SwitchRoleRequest request)
        {
            if (request == null || request.RoleId <= 0)
            {
                return BadRequest(new { message = "Invalid role selection." });
            }

            var result = await _authService.SwitchRoleAsync(User, request.RoleId);
            if (!result.success)
            {
                return BadRequest(new { message = result.message });
            }

            return Ok(new { message = result.message });
        }
    }
}
