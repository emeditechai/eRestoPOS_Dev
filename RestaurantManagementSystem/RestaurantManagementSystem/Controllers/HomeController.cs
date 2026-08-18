using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RestaurantManagementSystem.Utilities;
using RestaurantManagementSystem.Services;

namespace RestaurantManagementSystem.Controllers
{
    [AuthorizeAttribute]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ILicensingService _licensingService;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, ILicensingService licensingService)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _licensingService = licensingService;
        }

        public async Task<IActionResult> Index()
        {
            // Get current user information
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = User.FindFirstValue(ClaimTypes.Name);
            var userFirstName = User.FindFirstValue(ClaimTypes.GivenName) ?? "User";
            var userLastName = User.FindFirstValue(ClaimTypes.Surname) ?? "";
            var userFullName = $"{userFirstName} {userLastName}".Trim();
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            var userRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            
            // Get user's permissions
            var userPermissions = User.FindAll("Permission").Select(c => c.Value).ToList();

            int? userIdNumeric = null;
            if (int.TryParse(userId, out var parsedUserId))
            {
                userIdNumeric = parsedUserId;
            }

            var canViewAllDashboardData = UserHasFullDashboardVisibility();
            var activeBranchId = User.GetActiveBranchId();
            
            // Get live dashboard data from database
            var dashboardStats = await GetDashboardStatsAsync(userIdNumeric, canViewAllDashboardData, activeBranchId);
            var recentOrders = await GetRecentOrdersAsync(userIdNumeric, canViewAllDashboardData, activeBranchId);
            
            // Get last login date from database
            DateTime? lastLoginDate = await GetLastLoginDateAsync(userId);

            string? activeBranchNameFromDb = null;
            bool isActiveMainBranchFromDb = false;

            // Resolve active branch display from DB using ActiveBranchId claim to avoid stale branch-name claims
            try
            {
                var activeBranchIdValue = User.FindFirst("ActiveBranchId")?.Value;
                if (int.TryParse(activeBranchIdValue, out var activeBranchIdFromClaim) && activeBranchIdFromClaim > 0)
                {
                    using var branchCon = new SqlConnection(_connectionString);
                    await branchCon.OpenAsync();
                    using var branchCmd = new SqlCommand(@"
                        SELECT TOP 1
                            CASE WHEN bl.LocationName IS NOT NULL AND bl.LocationName <> ''
                                 THEN b.BranchName + ' - ' + bl.LocationName
                                 ELSE b.BranchName
                            END AS DisplayName,
                            ISNULL(b.Is_MainBranch, 0) AS IsMain
                        FROM dbo.Branches b
                        LEFT JOIN dbo.BranchLocations bl ON bl.LocationId = b.BranchLocationId
                        WHERE b.BranchId = @BranchId AND ISNULL(b.IsActive, 1) = 1", branchCon);
                    branchCmd.Parameters.AddWithValue("@BranchId", activeBranchIdFromClaim);

                    using var branchReader = await branchCmd.ExecuteReaderAsync();
                    if (await branchReader.ReadAsync())
                    {
                        activeBranchNameFromDb = branchReader.IsDBNull(0) ? null : branchReader.GetString(0);
                        isActiveMainBranchFromDb = !branchReader.IsDBNull(1) && branchReader.GetBoolean(1);
                        ViewBag.ActiveBranchName = string.IsNullOrWhiteSpace(activeBranchNameFromDb) ? "Not Selected" : activeBranchNameFromDb;
                        ViewBag.IsActiveMainBranch = isActiveMainBranchFromDb;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to resolve active branch display from database");
            }

            // Load restaurant logo path (fall back to default if not set)
            string? logoPath = null;
            string? restaurantName = null;
            try
            {
                using var logoCon = new SqlConnection(_connectionString);
                await logoCon.OpenAsync();

                var hasRestaurantSettingsBranchColumn = await HasColumnAsync("RestaurantSettings", "BranchId");
                SqlCommand logoCmd;

                if (hasRestaurantSettingsBranchColumn && activeBranchId.HasValue)
                {
                    logoCmd = new SqlCommand(@"
                        SELECT TOP 1 LogoPath, RestaurantName
                        FROM dbo.RestaurantSettings
                        WHERE BranchId = @BranchId
                        ORDER BY ISNULL(UpdatedAt, CreatedAt) DESC, Id DESC", logoCon);
                    logoCmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                }
                else
                {
                    logoCmd = new SqlCommand("SELECT TOP 1 LogoPath, RestaurantName FROM dbo.RestaurantSettings ORDER BY ISNULL(UpdatedAt, CreatedAt) DESC, Id DESC", logoCon);
                }

                using (var reader = await logoCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(reader.GetOrdinal("LogoPath")))
                        {
                            var raw = reader.GetString(reader.GetOrdinal("LogoPath"));
                            if (!string.IsNullOrWhiteSpace(raw)) logoPath = raw;
                        }
                        if (!reader.IsDBNull(reader.GetOrdinal("RestaurantName")))
                        {
                            var rawName = reader.GetString(reader.GetOrdinal("RestaurantName"));
                            if (!string.IsNullOrWhiteSpace(rawName)) restaurantName = rawName;
                        }
                    }
                }

                // If branch-specific setting row is missing, fallback to latest global settings row.
                if (string.IsNullOrWhiteSpace(logoPath) && string.IsNullOrWhiteSpace(restaurantName) && hasRestaurantSettingsBranchColumn)
                {
                    using var fallbackCmd = new SqlCommand("SELECT TOP 1 LogoPath, RestaurantName FROM dbo.RestaurantSettings ORDER BY ISNULL(UpdatedAt, CreatedAt) DESC, Id DESC", logoCon);
                    using var fallbackReader = await fallbackCmd.ExecuteReaderAsync();
                    if (await fallbackReader.ReadAsync())
                    {
                        if (!fallbackReader.IsDBNull(fallbackReader.GetOrdinal("LogoPath")))
                        {
                            var raw = fallbackReader.GetString(fallbackReader.GetOrdinal("LogoPath"));
                            if (!string.IsNullOrWhiteSpace(raw)) logoPath = raw;
                        }
                        if (!fallbackReader.IsDBNull(fallbackReader.GetOrdinal("RestaurantName")))
                        {
                            var rawName = fallbackReader.GetString(fallbackReader.GetOrdinal("RestaurantName"));
                            if (!string.IsNullOrWhiteSpace(rawName)) restaurantName = rawName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to load restaurant logo path");
            }
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                // Provide a default placeholder (ensure file exists or use a generic path)
                logoPath = "/images/logo.png"; // fallback
            }
            if (string.IsNullOrWhiteSpace(restaurantName))
            {
                restaurantName = !string.IsNullOrWhiteSpace(activeBranchNameFromDb)
                    ? activeBranchNameFromDb
                    : "Restaurant"; // fallback label
            }
            
            // Create a dashboard view model with live data
            var model = new DashboardViewModel
            {
                UserName = userName,
                UserFullName = userFullName,
                UserEmail = userEmail,
                UserRoles = userRoles,
                UserPermissions = userPermissions,
                LastLoginDate = lastLoginDate ?? DateTime.Now, // Use database value or current time as fallback
                TodaySales = dashboardStats.TodaySales,
                TodayOrders = dashboardStats.TodayOrders,
                ActiveTables = dashboardStats.ActiveTables,
                UpcomingReservations = dashboardStats.UpcomingReservations,
                RecentOrders = recentOrders,
                LowInventoryItems = new List<InventoryItemViewModel>
                {
                    new InventoryItemViewModel { Name = "Fresh Tomatoes", CurrentStock = 2.5m, MinimumStock = 5.0m, Unit = "kg" },
                    new InventoryItemViewModel { Name = "Olive Oil", CurrentStock = 1.0m, MinimumStock = 2.0m, Unit = "L" }
                },
                PopularMenuItems = new List<MenuItemPopularityViewModel>
                {
                    new MenuItemPopularityViewModel { Name = "Margherita Pizza", OrderCount = 32 },
                    new MenuItemPopularityViewModel { Name = "Chicken Parmesan", OrderCount = 28 },
                    new MenuItemPopularityViewModel { Name = "Caesar Salad", OrderCount = 24 },
                    new MenuItemPopularityViewModel { Name = "Tiramisu", OrderCount = 18 }
                },
                SalesData = new List<SalesDataViewModel>
                {
                    new SalesDataViewModel { Day = "Monday", Amount = 850.00m },
                    new SalesDataViewModel { Day = "Tuesday", Amount = 920.50m },
                    new SalesDataViewModel { Day = "Wednesday", Amount = 1100.25m },
                    new SalesDataViewModel { Day = "Thursday", Amount = 980.75m },
                    new SalesDataViewModel { Day = "Friday", Amount = 1450.00m },
                    new SalesDataViewModel { Day = "Saturday", Amount = 1750.50m },
                    new SalesDataViewModel { Day = "Sunday", Amount = 1200.25m }
                },
                CustomersByTime = new List<CustomersByTimeViewModel>
                {
                    new CustomersByTimeViewModel { Hour = 11, CustomerCount = 15 },
                    new CustomersByTimeViewModel { Hour = 12, CustomerCount = 25 },
                    new CustomersByTimeViewModel { Hour = 13, CustomerCount = 30 },
                    new CustomersByTimeViewModel { Hour = 14, CustomerCount = 20 },
                    new CustomersByTimeViewModel { Hour = 18, CustomerCount = 35 },
                    new CustomersByTimeViewModel { Hour = 19, CustomerCount = 40 },
                    new CustomersByTimeViewModel { Hour = 20, CustomerCount = 30 }
                },
                LogoPath = logoPath,
                RestaurantName = restaurantName
            };

            // Fetch active alert message from ClientAppLicense
            try
            {
                var alertMessage = await _licensingService.GetActiveAlertMessageAsync();
                if (!string.IsNullOrWhiteSpace(alertMessage))
                {
                    ViewBag.AlertMessage = alertMessage;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to fetch alert message");
            }

            // Fetch Master Dependency Setup Wizard data
            try
            {
                var wizardData = await GetSetupWizardDataAsync(userIdNumeric, activeBranchId, restaurantName);
                ViewBag.SetupWizard = wizardData;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to evaluate Setup Wizard status");
                ViewBag.SetupWizard = new SetupWizardViewModel { ShowWizard = false, IsSignupUser = false };
            }
            
            return View(model);
        }

        private async Task<(decimal TodaySales, int TodayOrders, int ActiveTables, int UpcomingReservations)> GetDashboardStatsAsync(int? userId, bool canViewAll, int? activeBranchId)
        {
            if (!activeBranchId.HasValue)
            {
                return (0m, 0, 0, 0);
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand(@"
                    DECLARE @hasOrdersBranch bit = CASE WHEN COL_LENGTH('dbo.Orders','BranchId') IS NULL THEN 0 ELSE 1 END;
                    DECLARE @hasTablesBranch bit = CASE WHEN COL_LENGTH('dbo.Tables','BranchId') IS NULL THEN 0 ELSE 1 END;
                    DECLARE @hasResBranch bit = CASE WHEN OBJECT_ID('dbo.Reservations','U') IS NOT NULL AND COL_LENGTH('dbo.Reservations','BranchId') IS NOT NULL THEN 1 ELSE 0 END;
                    DECLARE @hasResTableId bit = CASE WHEN OBJECT_ID('dbo.Reservations','U') IS NOT NULL AND COL_LENGTH('dbo.Reservations','TableId') IS NOT NULL THEN 1 ELSE 0 END;

                    SELECT
                        ISNULL(SUM(
                            CASE
                                WHEN o.Status = 3
                                 AND CAST(ISNULL(o.CompletedAt, ISNULL(o.UpdatedAt, o.CreatedAt)) AS date) = CAST(GETDATE() AS date)
                                THEN ISNULL(o.TotalAmount, 0)
                                ELSE 0
                            END
                        ), 0) AS TodaySales,
                        ISNULL(SUM(CASE WHEN CAST(o.CreatedAt AS date) = CAST(GETDATE() AS date) THEN 1 ELSE 0 END), 0) AS TodayOrders,
                        ISNULL((
                            SELECT COUNT(1)
                            FROM dbo.TableTurnovers tt
                            INNER JOIN dbo.Tables t ON t.Id = tt.TableId
                            WHERE tt.Status < 5
                              AND (@hasTablesBranch = 0 OR t.BranchId = @BranchId)
                        ), 0) AS ActiveTables,
                        ISNULL((
                            SELECT COUNT(1)
                            FROM dbo.Reservations r
                            WHERE CAST(r.ReservationDate AS date) >= CAST(GETDATE() AS date)
                              AND ISNULL(r.Status, -1) NOT IN (3, 4) -- 3=Completed, 4=Cancelled
                              AND (
                                  (@hasResBranch = 1 AND r.BranchId = @BranchId)
                                  OR
                                  (@hasResBranch = 0 AND @hasResTableId = 1 AND @hasTablesBranch = 1 AND EXISTS (
                                      SELECT 1 FROM dbo.Tables tRes WHERE tRes.Id = r.TableId AND tRes.BranchId = @BranchId
                                  ))
                                  OR
                                  (@hasResBranch = 0 AND @hasResTableId = 0)
                                  OR
                                  (@hasResBranch = 0 AND @hasTablesBranch = 0)
                              )
                        ), 0) AS UpcomingReservations
                    FROM dbo.Orders o
                    WHERE (@CanViewAll = 1 OR o.UserId = @UserId)
                      AND (@hasOrdersBranch = 0 OR o.BranchId = @BranchId);", connection);

                command.Parameters.AddWithValue("@UserId", userId.HasValue ? userId.Value : (object)DBNull.Value);
                command.Parameters.AddWithValue("@CanViewAll", canViewAll ? 1 : 0);
                command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    decimal todaySales = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0);
                    int todayOrders = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    int activeTables = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    int upcomingRes = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                    return (todaySales, todayOrders, activeTables, upcomingRes);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting branch-filtered dashboard stats");
            }
            return (0m, 0, 0, 0);
        }

        private static decimal SafeGetDecimal(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0m : reader.GetDecimal(ordinal);
        }

        private static int SafeGetInt(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        private async Task<List<DashboardOrderViewModel>> GetRecentOrdersAsync(int? userId, bool canViewAll, int? activeBranchId)
        {
            var orders = new List<DashboardOrderViewModel>();

            if (!activeBranchId.HasValue)
            {
                return orders;
            }
            
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (var command = new SqlCommand(@"
                        DECLARE @hasOrdersBranch bit = CASE WHEN COL_LENGTH('dbo.Orders','BranchId') IS NULL THEN 0 ELSE 1 END;

                        SELECT TOP (@OrderCount)
                            o.Id AS OrderId,
                            ISNULL(NULLIF(LTRIM(RTRIM(o.OrderNumber)), ''), CONCAT('ORD-', o.Id)) AS OrderNumber,
                            ISNULL(NULLIF(LTRIM(RTRIM(CASE WHEN o.OrderType = 0 THEN ISNULL(tt.GuestName, o.CustomerName) ELSE o.CustomerName END)), ''), 'Walk-in Customer') AS CustomerName,
                            ISNULL(t.TableName, 'Takeout/Delivery') AS TableNumber,
                            ISNULL(o.TotalAmount, 0) AS TotalAmount,
                            CASE o.Status
                                WHEN 0 THEN 'Open'
                                WHEN 1 THEN 'In Progress'
                                WHEN 2 THEN 'Ready'
                                WHEN 3 THEN 'Completed'
                                WHEN 4 THEN 'Cancelled'
                                ELSE 'Unknown'
                            END AS Status,
                            CONVERT(varchar(20), o.CreatedAt, 100) AS OrderTime
                        FROM dbo.Orders o
                        LEFT JOIN dbo.TableTurnovers tt ON tt.Id = o.TableTurnoverId
                        LEFT JOIN dbo.Tables t ON t.Id = tt.TableId
                        WHERE (@CanViewAll = 1 OR o.UserId = @UserId)
                          AND (@hasOrdersBranch = 0 OR o.BranchId = @BranchId)
                        ORDER BY o.CreatedAt DESC;", connection))
                    {
                        command.Parameters.AddWithValue("@OrderCount", 5);
                        command.Parameters.AddWithValue("@UserId", userId.HasValue ? userId.Value : (object)DBNull.Value);
                        command.Parameters.AddWithValue("@CanViewAll", canViewAll ? 1 : 0);
                        command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                orders.Add(new DashboardOrderViewModel
                                {
                                    OrderId = reader.GetInt32("OrderId"),
                                    OrderNumber = reader.GetString("OrderNumber"),
                                    CustomerName = reader.GetString("CustomerName"),
                                    TableNumber = reader.GetString("TableNumber"),
                                    TotalAmount = reader.GetDecimal("TotalAmount"),
                                    Status = reader.GetString("Status"),
                                    OrderTime = reader.GetString("OrderTime")
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting recent orders for dashboard");
                // Return empty list on error - no fallback data
            }
            
            return orders;
        }

        private bool UserHasFullDashboardVisibility()
        {
            try
            {
                var roles = User?.FindAll(ClaimTypes.Role)?.Select(r => r.Value) ?? Enumerable.Empty<string>();
                string[] privilegedRoles = ["Administrator", "FloorManager", "Floor Manager"];
                return roles.Any(role => privilegedRoles.Any(privileged => string.Equals(role, privileged, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> HasColumnAsync(string tableName, string columnName)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(1)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName", connection);
                cmd.Parameters.AddWithValue("@TableName", tableName);
                cmd.Parameters.AddWithValue("@ColumnName", columnName);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
            }
            catch
            {
                return false;
            }
        }
        
        private async Task<DateTime?> GetLastLoginDateAsync(string userIdString)
        {
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return null;
            }
            
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First, determine which column name exists
                    string columnName = null;
                    using (var checkCmd = new SqlCommand(@"
                        SELECT COLUMN_NAME 
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'Users' 
                        AND COLUMN_NAME IN ('LastLoginDate', 'LastLoginAt', 'LastLogin')
                        ORDER BY CASE 
                            WHEN COLUMN_NAME = 'LastLoginDate' THEN 1 
                            WHEN COLUMN_NAME = 'LastLoginAt' THEN 2 
                            ELSE 3 
                        END", connection))
                    {
                        var result = await checkCmd.ExecuteScalarAsync();
                        columnName = result?.ToString();
                    }
                    
                    if (!string.IsNullOrEmpty(columnName))
                    {
                        var query = $"SELECT {columnName} FROM Users WHERE Id = @UserId";
                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                return Convert.ToDateTime(result);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting last login date for user {UserId}", userId);
            }
            
            return null;
        }

        [HttpPost]
        public async Task<IActionResult> DismissSetupWizard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userId, out int uid))
            {
                try
                {
                    using var con = new SqlConnection(_connectionString);
                    await con.OpenAsync();
                    using var cmd = new SqlCommand(@"
                        IF COL_LENGTH('dbo.Users', 'SetupWizardCompleted') IS NOT NULL
                            UPDATE dbo.Users SET SetupWizardCompleted = 1 WHERE Id = @UserId", con);
                    cmd.Parameters.AddWithValue("@UserId", uid);
                    await cmd.ExecuteNonQueryAsync();
                    return Json(new { success = true, message = "Setup wizard dismissed." });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error dismissing setup wizard for user {UserId}", uid);
                }
            }
            return Json(new { success = false });
        }

        [HttpPost]
        public async Task<IActionResult> ResetSetupWizard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userId, out int uid))
            {
                try
                {
                    using var con = new SqlConnection(_connectionString);
                    await con.OpenAsync();
                    using var cmd = new SqlCommand(@"
                        IF COL_LENGTH('dbo.Users', 'SetupWizardCompleted') IS NOT NULL
                            UPDATE dbo.Users SET SetupWizardCompleted = 0 WHERE Id = @UserId", con);
                    cmd.Parameters.AddWithValue("@UserId", uid);
                    await cmd.ExecuteNonQueryAsync();
                    return Json(new { success = true, message = "Setup wizard reset." });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error resetting setup wizard for user {UserId}", uid);
                }
            }
            return Json(new { success = false });
        }

        [HttpGet]
        public async Task<IActionResult> GetSetupWizardStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? uid = int.TryParse(userId, out int parsedId) ? parsedId : null;
            var activeBranchId = User.GetActiveBranchId();
            var wizard = await GetSetupWizardDataAsync(uid, activeBranchId, null);
            return Json(wizard);
        }

        private async Task<int> SafeExecuteScalarTableCountAsync(SqlConnection con, string tableName, int? branchId, string additionalWhere = "")
        {
            try
            {
                // 1. Check if table exists in INFORMATION_SCHEMA
                using (var chkTable = new SqlCommand("SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TName", con))
                {
                    chkTable.Parameters.AddWithValue("@TName", tableName);
                    var tableExists = Convert.ToInt32(await chkTable.ExecuteScalarAsync()) > 0;
                    if (!tableExists) return 0;
                }

                // 2. Check if BranchId column exists
                bool hasBranchCol = false;
                using (var chkCol = new SqlCommand("SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TName AND COLUMN_NAME = 'BranchId'", con))
                {
                    chkCol.Parameters.AddWithValue("@TName", tableName);
                    hasBranchCol = Convert.ToInt32(await chkCol.ExecuteScalarAsync()) > 0;
                }

                string sql;
                if (hasBranchCol && branchId.HasValue)
                {
                    // Branch-specific query
                    sql = $"SELECT COUNT(1) FROM dbo.[{tableName}] WHERE BranchId = @BranchId";
                    if (!string.IsNullOrWhiteSpace(additionalWhere))
                    {
                        sql += $" AND ({additionalWhere})";
                    }

                    using var cmd = new SqlCommand(sql, con);
                    cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                    var res = await cmd.ExecuteScalarAsync();
                    return (res != null && res != DBNull.Value) ? Convert.ToInt32(res) : 0;
                }
                else
                {
                    // Global query
                    sql = $"SELECT COUNT(1) FROM dbo.[{tableName}]";
                    if (!string.IsNullOrWhiteSpace(additionalWhere))
                    {
                        sql += $" WHERE {additionalWhere}";
                    }

                    using var cmd = new SqlCommand(sql, con);
                    var res = await cmd.ExecuteScalarAsync();
                    return (res != null && res != DBNull.Value) ? Convert.ToInt32(res) : 0;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SafeExecuteScalarTableCountAsync error for table {TableName}, Branch {BranchId}", tableName, branchId);
                return 0;
            }
        }

        private async Task<SetupWizardViewModel> GetSetupWizardDataAsync(int? userId, int? activeBranchId, string? branchName)
        {
            var vm = new SetupWizardViewModel
            {
                UserId = userId ?? 0,
                CurrentBranchId = activeBranchId,
                CurrentBranchName = branchName ?? "Current Branch"
            };

            if (!userId.HasValue)
            {
                vm.ShowWizard = false;
                vm.IsSignupUser = false;
                return vm;
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // 1. Check if user is from signup and whether setup wizard is completed (Safe conversion)
                bool isSignupUser = false;
                bool setupWizardCompleted = false;

                try
                {
                    using var cmd = new SqlCommand(@"
                        SELECT 
                            CAST(CASE WHEN COL_LENGTH('dbo.Users', 'from_Signup') IS NOT NULL THEN ISNULL(from_Signup, 0) ELSE 0 END AS INT),
                            CAST(CASE WHEN COL_LENGTH('dbo.Users', 'SetupWizardCompleted') IS NOT NULL THEN ISNULL(SetupWizardCompleted, 0) ELSE 0 END AS INT)
                        FROM dbo.Users WHERE Id = @UserId", con);
                    cmd.Parameters.AddWithValue("@UserId", userId.Value);
                    using var rdr = await cmd.ExecuteReaderAsync();
                    if (await rdr.ReadAsync())
                    {
                        isSignupUser = (Convert.ToInt32(rdr[0]) == 1);
                        setupWizardCompleted = (Convert.ToInt32(rdr[1]) == 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error reading from_Signup/SetupWizardCompleted for user {UserId}", userId.Value);
                }

                vm.IsSignupUser = isSignupUser;
                // Auto show wizard for signup users who have not completed or dismissed it
                vm.ShowWizard = isSignupUser && !setupWizardCompleted;

                // 2. Query Master Data counts strictly respecting active branch
                // 2.1 Restaurant Settings (Branch specific, or fallback to global settings)
                int restaurantSettingsCount = await SafeExecuteScalarTableCountAsync(con, "RestaurantSettings", activeBranchId, "ISNULL(RestaurantName, '') <> ''");
                if (restaurantSettingsCount == 0 && !activeBranchId.HasValue)
                {
                    restaurantSettingsCount = await SafeExecuteScalarTableCountAsync(con, "RestaurantSettings", null, "ISNULL(RestaurantName, '') <> ''");
                }

                // 2.2 Kitchen Stations (Branch specific if column exists, or global)
                int kitchenStationsCount = await SafeExecuteScalarTableCountAsync(con, "KitchenStations", activeBranchId);

                // 2.3 Table Sections (Strictly per active branch)
                int tableSectionsCount = await SafeExecuteScalarTableCountAsync(con, "TableSections", activeBranchId);

                // 2.4 Dining Tables (Strictly per active branch)
                int tableCount = await SafeExecuteScalarTableCountAsync(con, "Tables", activeBranchId);

                // 2.5 Categories (Global catalog or branch specific)
                int categoryCount = await SafeExecuteScalarTableCountAsync(con, "Categories", activeBranchId);
                if (categoryCount == 0)
                {
                    categoryCount = await SafeExecuteScalarTableCountAsync(con, "menuitemgroup", activeBranchId);
                }

                // 2.6 Sub-Categories (Global catalog or branch specific)
                int subCategoryCount = await SafeExecuteScalarTableCountAsync(con, "SubCategories", activeBranchId);

                // 2.7 Units of Measurement (UOM)
                int uomCount = await SafeExecuteScalarTableCountAsync(con, "tbl_mst_uom", activeBranchId);
                if (uomCount == 0)
                {
                    uomCount = await SafeExecuteScalarTableCountAsync(con, "Uom", activeBranchId);
                }

                // 2.8 Menu Items (Strictly per active branch)
                int menuItemCount = await SafeExecuteScalarTableCountAsync(con, "MenuItems", activeBranchId);

                // 2.9 Payment / UPI Settings
                int upiCount = await SafeExecuteScalarTableCountAsync(con, "UPISettings", activeBranchId, "ISNULL(UPIId, '') <> ''");

                // 3. Build Ordered Dependency Steps
                var steps = new List<SetupWizardStep>
                {
                    // Step 1: Restaurant Profile & Settings (Foundation)
                    new SetupWizardStep
                    {
                        StepNumber = 1,
                        StepKey = "settings",
                        Phase = "Foundation",
                        Title = "Restaurant Profile & Settings",
                        Subtitle = "Setup basic restaurant details, GST code, address & logo",
                        Description = "Configure your restaurant's legal name, currency symbol, tax GST identification, contact numbers, and brand logo used across all receipts, bills, and KOTs.",
                        IconCss = "fas fa-store",
                        ThemeColor = "#7c3aed",
                        TargetUrl = "/Settings",
                        ActionButtonText = restaurantSettingsCount > 0 ? "Edit Profile" : "Setup Profile",
                        IsConfigured = restaurantSettingsCount > 0,
                        CurrentCount = restaurantSettingsCount,
                        CountBadgeText = restaurantSettingsCount > 0 ? "Profile Configured" : "Needs Setup",
                        IsUnlocked = true,
                        DependencyNote = "Independent foundation master."
                    },

                    // Step 2: Kitchen Stations (Foundation)
                    new SetupWizardStep
                    {
                        StepNumber = 2,
                        StepKey = "kitchen_stations",
                        Phase = "Foundation",
                        Title = "Kitchen Stations (KOT / BOT Routing)",
                        Subtitle = "Setup cooking & bar areas (Main Kitchen, Bar, Grill, Bakery)",
                        Description = "Create preparation stations so that food orders automatically dispatch KOT tickets to the kitchen, and drink orders dispatch BOT tickets to the bar.",
                        IconCss = "fas fa-fire-burner",
                        ThemeColor = "#ea580c",
                        TargetUrl = "/Kitchen/Stations",
                        ActionButtonText = kitchenStationsCount > 0 ? "Manage Stations" : "Create Stations",
                        IsConfigured = kitchenStationsCount > 0,
                        CurrentCount = kitchenStationsCount,
                        CountBadgeText = kitchenStationsCount > 0 ? $"{kitchenStationsCount} Station(s) Available" : "Needs Setup",
                        IsUnlocked = true,
                        DependencyNote = "Menu items depend on Kitchen Stations for automated order ticket routing."
                    },

                    // Step 3: Dining Table Sections (Floor & Seating)
                    new SetupWizardStep
                    {
                        StepNumber = 3,
                        StepKey = "table_sections",
                        Phase = "Floor & Seating",
                        Title = "Dining Table Sections",
                        Subtitle = "Organize dining floor into zones (AC Hall, Patio, Rooftop, Bar)",
                        Description = "Define distinct dining areas and sections in your restaurant. Dining tables must belong to a section for floor management and guest seating.",
                        IconCss = "fas fa-layer-group",
                        ThemeColor = "#0284c7",
                        TargetUrl = "/Reservation/TableSections",
                        ActionButtonText = tableSectionsCount > 0 ? "Manage Sections" : "Create Sections",
                        IsConfigured = tableSectionsCount > 0,
                        CurrentCount = tableSectionsCount,
                        CountBadgeText = tableSectionsCount > 0 ? $"{tableSectionsCount} Section(s) Available" : "Needs Setup",
                        IsUnlocked = true,
                        DependencyNote = "Tables require a Table Section to be created first."
                    },

                    // Step 4: Floor Layout & Dining Tables (Floor & Seating - DEPENDS ON Table Sections)
                    new SetupWizardStep
                    {
                        StepNumber = 4,
                        StepKey = "tables",
                        Phase = "Floor & Seating",
                        Title = "Dining Tables & Seating",
                        Subtitle = "Create table numbers (T1, T2, T3) and seat capacities",
                        Description = "Add specific dining tables with chair capacity in each section. Required to seat guests, track live table turnover, and punch Dine-In POS orders.",
                        IconCss = "fas fa-chair",
                        ThemeColor = "#0369a1",
                        TargetUrl = "/Reservation/Tables",
                        ActionButtonText = tableCount > 0 ? "Manage Tables" : "Setup Tables",
                        IsConfigured = tableCount > 0,
                        CurrentCount = tableCount,
                        CountBadgeText = tableCount > 0 ? $"{tableCount} Table(s) Available" : "Needs Setup",
                        IsUnlocked = tableSectionsCount > 0,
                        DependencyNote = "Requires at least 1 Table Section (Step 3) to place dining tables.",
                        DependsOnStepNumbers = new List<int> { 3 }
                    },

                    // Step 5: Units of Measurement (Menu Catalog)
                    new SetupWizardStep
                    {
                        StepNumber = 5,
                        StepKey = "uom",
                        Phase = "Menu Catalog",
                        Title = "Units of Measurement (UOM)",
                        Subtitle = "Setup units for stock and dishes (Portion, Plate, Glass, Pcs, Kg)",
                        Description = "Standardize portioning and inventory units across kitchen ingredients, billing, and recipe cost estimation.",
                        IconCss = "fas fa-scale-balanced",
                        ThemeColor = "#059669",
                        TargetUrl = "/Uom",
                        ActionButtonText = uomCount > 0 ? "Manage UOM" : "Add Units",
                        IsConfigured = uomCount > 0,
                        CurrentCount = uomCount,
                        CountBadgeText = uomCount > 0 ? $"{uomCount} Unit(s) Available" : "Needs Setup",
                        IsUnlocked = true,
                        DependencyNote = "Used for accurate portion sizing and recipe ingredient tracking."
                    },

                    // Step 6: Food Categories & Sub-Categories (Menu Catalog)
                    new SetupWizardStep
                    {
                        StepNumber = 6,
                        StepKey = "categories",
                        Phase = "Menu Catalog",
                        Title = "Food Categories & Sub-Categories",
                        Subtitle = "Organize menu into Starters, Main Course, Drinks, Desserts",
                        Description = "Group your dishes into structured categories and subcategories for fast POS search, recipe management, and accurate sales reporting.",
                        IconCss = "fas fa-tags",
                        ThemeColor = "#8b5cf6",
                        TargetUrl = "/Category",
                        ActionButtonText = (categoryCount > 0 || subCategoryCount > 0) ? "Manage Categories" : "Add Categories",
                        IsConfigured = (categoryCount > 0 || subCategoryCount > 0),
                        CurrentCount = categoryCount,
                        CountBadgeText = (categoryCount > 0 || subCategoryCount > 0) ? $"{categoryCount} Categories, {subCategoryCount} Sub-Categories Available" : "Needs Setup",
                        IsUnlocked = true,
                        DependencyNote = "Menu items must belong to a Category and Subcategory."
                    },

                    // Step 7: Menu Items & Pricing (Menu Catalog - DEPENDS ON Stations + Categories)
                    new SetupWizardStep
                    {
                        StepNumber = 7,
                        StepKey = "menu_items",
                        Phase = "Menu Catalog",
                        Title = "Menu Items & Pricing",
                        Subtitle = "Add dishes with price, tax, category & kitchen routing",
                        Description = "Build your complete food and beverage catalog with prices, GST tax rates, station assignments, and delicious descriptions.",
                        IconCss = "fas fa-utensils",
                        ThemeColor = "#d97706",
                        TargetUrl = "/Menu",
                        ActionButtonText = menuItemCount > 0 ? "Manage Menu Items" : "Add Dishes",
                        IsConfigured = menuItemCount > 0,
                        CurrentCount = menuItemCount,
                        CountBadgeText = menuItemCount > 0 ? $"{menuItemCount} Item(s) in Catalog" : "Needs Setup",
                        IsUnlocked = (kitchenStationsCount > 0 && (categoryCount > 0 || subCategoryCount > 0)),
                        DependencyNote = "Requires at least 1 Kitchen Station (Step 2) and 1 Category (Step 6) created first.",
                        DependsOnStepNumbers = new List<int> { 2, 6 }
                    },

                    // Step 8: Payment QR & Cash Counter Setup (Payments & POS)
                    new SetupWizardStep
                    {
                        StepNumber = 8,
                        StepKey = "upi_payments",
                        Phase = "Payments & POS",
                        Title = "Payment QR & Counter Setup",
                        Subtitle = "Configure dynamic UPI QR code payments and payment methods",
                        Description = "Setup dynamic UPI QR code payments and configure payment modes for seamless settlement at billing time.",
                        IconCss = "fas fa-qrcode",
                        ThemeColor = "#0d9488",
                        TargetUrl = "/UPISettings",
                        ActionButtonText = (upiCount > 0 || restaurantSettingsCount > 0) ? "Manage Payments" : "Setup Payments",
                        IsConfigured = (upiCount > 0 || restaurantSettingsCount > 0),
                        CurrentCount = upiCount,
                        CountBadgeText = (upiCount > 0 || restaurantSettingsCount > 0) ? "Payments Configured" : "Needs Setup",
                        IsUnlocked = restaurantSettingsCount > 0,
                        DependencyNote = "Requires Restaurant Profile (Step 1) to configure business payment methods.",
                        DependsOnStepNumbers = new List<int> { 1 }
                    },

                    // Step 9: Launch Point of Sale (POS) (Payments & POS - DEPENDS ON Tables + Menu Items)
                    new SetupWizardStep
                    {
                        StepNumber = 9,
                        StepKey = "pos_ready",
                        Phase = "Payments & POS",
                        Title = "Point of Sale (POS) Launch",
                        Subtitle = "Start punching live orders, seat guests, and print bills!",
                        Description = "Your restaurant is ready! Take your first live order on the point of sale screen, seat guests, and generate instant KOTs.",
                        IconCss = "fas fa-cash-register",
                        ThemeColor = "#10b981",
                        TargetUrl = "/Order/Create",
                        ActionButtonText = "Launch POS Screen",
                        IsConfigured = (tableCount > 0 && menuItemCount > 0),
                        CurrentCount = (tableCount > 0 && menuItemCount > 0) ? 1 : 0,
                        CountBadgeText = (tableCount > 0 && menuItemCount > 0) ? "Ready to Sell" : "Needs Tables & Dishes",
                        IsUnlocked = (tableCount > 0 && menuItemCount > 0),
                        DependencyNote = "Requires Dining Tables (Step 4) and Menu Items (Step 7) to start selling.",
                        DependsOnStepNumbers = new List<int> { 4, 7 }
                    }
                };

                vm.Steps = steps;
                vm.TotalStepsCount = steps.Count;
                vm.CompletedStepsCount = steps.Count(s => s.IsConfigured);
                vm.ReadinessPercentage = vm.TotalStepsCount > 0 
                    ? (int)Math.Round((double)vm.CompletedStepsCount / vm.TotalStepsCount * 100.0) 
                    : 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error assembling SetupWizard data for user {UserId}", userId);
            }

            return vm;
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCacheAttribute(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Maintenance()
        {
            return View();
        }
    }
}
