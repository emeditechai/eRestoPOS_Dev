using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantManagementSystem.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RestaurantManagementSystem.Utilities;

namespace RestaurantManagementSystem.Controllers
{
    [AuthorizeAttribute]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
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
                        SELECT TOP 1 BranchName, ISNULL(Is_MainBranch, 0) AS IsMain
                        FROM dbo.Branches
                        WHERE BranchId = @BranchId AND ISNULL(IsActive, 1) = 1", branchCon);
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

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCacheAttribute(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
