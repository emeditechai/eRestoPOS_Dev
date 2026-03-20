using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize]
    public class UtilityController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public UtilityController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        }

        private int? GetActiveBranchId() => User.GetActiveBranchId();

        private bool IsActiveBranchMain(int branchId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.Branches WHERE BranchId = @BranchId AND Is_MainBranch = 1", conn);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            return (int)cmd.ExecuteScalar() > 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  GET – Menu Item Rate Edit
        // ═══════════════════════════════════════════════════════════════
        public IActionResult MenuItemRateEdit(int? branchId)
        {
            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                TempData["ErrorMessage"] = "Please select an active branch first.";
                return RedirectToAction("Index", "Home");
            }

            bool isMainBranch = IsActiveBranchMain(activeBranchId.Value);

            // Main branch can view any branch; sub-branch can only view their own
            int targetBranchId = (isMainBranch && branchId.HasValue) ? branchId.Value : activeBranchId.Value;

            var items = LoadMenuItemRates(targetBranchId);

            // Branch list for dropdown (main branch only)
            ViewBag.IsMainBranch = isMainBranch;
            ViewBag.SelectedBranchId = targetBranchId;
            ViewBag.AllBranches = isMainBranch ? LoadAllBranches() : null;
            ViewBag.ReadOnly = !isMainBranch;

            return View(items);
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Save rate for a single menu item
        // ═══════════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveMenuItemRate([FromBody] SaveRateRequest req)
        {
            if (req == null || req.MenuItemId == 0)
                return Json(new { success = false, message = "Invalid request." });

            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
                return Json(new { success = false, message = "No active branch." });

            // Validate access: only if item belongs to the active branch or user is main branch
            bool isMain = IsActiveBranchMain(activeBranchId.Value);
            if (!isMain && !MenuItemBelongsToBranch(req.MenuItemId, activeBranchId.Value))
                return Json(new { success = false, message = "Access denied: item not in active branch." });

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var cmd = new SqlCommand(@"
UPDATE dbo.MenuItems
SET    Price             = @BasePrice,
       TakeoutPrice      = @TakeoutPrice,
       DeliveryPrice     = @DeliveryPrice,
       RoomServicePrice  = @RoomServicePrice
WHERE  Id = @MenuItemId", conn);
                cmd.Parameters.AddWithValue("@MenuItemId",      req.MenuItemId);
                cmd.Parameters.AddWithValue("@BasePrice",       req.BasePrice);
                cmd.Parameters.AddWithValue("@TakeoutPrice",    (object?)req.TakeoutPrice    ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeliveryPrice",   (object?)req.DeliveryPrice   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RoomServicePrice",(object?)req.RoomServicePrice ?? DBNull.Value);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    return Json(new { success = false, message = "Menu item not found." });
                return Json(new { success = true, message = "Rate updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Save rates for multiple menu items in one shot
        // ═══════════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveAllMenuItemRates([FromBody] SaveAllRatesRequest req)
        {
            if (req == null || req.Items == null || req.Items.Count == 0)
                return Json(new { success = false, message = "No items to save." });

            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
                return Json(new { success = false, message = "No active branch." });

            bool isMain = IsActiveBranchMain(activeBranchId.Value);
            int saved = 0;
            var errors = new List<string>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            foreach (var item in req.Items)
            {
                if (item.MenuItemId == 0) continue;
                if (!isMain && !MenuItemBelongsToBranch(item.MenuItemId, activeBranchId.Value))
                {
                    errors.Add($"Item {item.MenuItemId}: access denied.");
                    continue;
                }
                try
                {
                    using var cmd = new SqlCommand(@"
UPDATE dbo.MenuItems
SET    Price             = @BasePrice,
       TakeoutPrice      = @TakeoutPrice,
       DeliveryPrice     = @DeliveryPrice,
       RoomServicePrice  = @RoomServicePrice
WHERE  Id = @MenuItemId", conn);
                    cmd.Parameters.AddWithValue("@MenuItemId",       item.MenuItemId);
                    cmd.Parameters.AddWithValue("@BasePrice",         item.BasePrice);
                    cmd.Parameters.AddWithValue("@TakeoutPrice",      (object?)item.TakeoutPrice    ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@DeliveryPrice",     (object?)item.DeliveryPrice   ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@RoomServicePrice",  (object?)item.RoomServicePrice ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                    saved++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Item {item.MenuItemId}: {ex.Message}");
                }
            }

            return Json(new
            {
                success = errors.Count == 0,
                saved,
                errors,
                message = errors.Count == 0
                    ? $"{saved} item(s) updated successfully."
                    : $"{saved} saved, {errors.Count} error(s): {string.Join("; ", errors)}"
            });
        }

        // ─────────────────────────────────────────────────────────
        //  PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────

        private List<MenuItemRateViewModel> LoadMenuItemRates(int branchId)
        {
            var list = new List<MenuItemRateViewModel>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT
    mi.Id                              AS MenuItemId,
    ISNULL(mi.PLUCode, '')             AS PLUCode,
    mi.Name                            AS MenuItemName,
    ISNULL(c.Name, 'Uncategorized')    AS CategoryName,
    ISNULL(mi.Price, 0)                AS BasePrice,
    mi.TakeoutPrice,
    mi.DeliveryPrice,
    mi.RoomServicePrice
FROM   dbo.MenuItems mi
LEFT JOIN dbo.Categories c ON c.Id = mi.CategoryId
WHERE  ISNULL(mi.IsAvailable, 1) = 1
  AND  mi.BranchId = @BranchId
ORDER  BY c.Name, mi.Name", conn);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new MenuItemRateViewModel
                {
                    MenuItemId       = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                    PLUCode          = reader["PLUCode"]?.ToString() ?? "",
                    MenuItemName     = reader["MenuItemName"]?.ToString() ?? "",
                    CategoryName     = reader["CategoryName"]?.ToString() ?? "",
                    BasePrice        = reader.GetDecimal(reader.GetOrdinal("BasePrice")),
                    TakeoutPrice     = reader["TakeoutPrice"]     == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("TakeoutPrice")),
                    DeliveryPrice    = reader["DeliveryPrice"]    == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("DeliveryPrice")),
                    RoomServicePrice = reader["RoomServicePrice"] == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("RoomServicePrice")),
                });
            }
            return list;
        }

        private List<(int BranchId, string BranchName)> LoadAllBranches()
        {
            var list = new List<(int, string)>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT b.BranchId,
       ISNULL(b.BranchName,'') + CASE WHEN ISNULL(bl.LocationName,'') <> ''
           THEN ' - ' + bl.LocationName ELSE '' END AS BranchName
FROM   dbo.Branches b
LEFT JOIN dbo.BranchLocations bl ON bl.LocationId = b.BranchLocationId
WHERE  ISNULL(b.IsActive,1) = 1
ORDER  BY b.BranchName", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetInt32(0), reader["BranchName"]?.ToString() ?? ""));
            return list;
        }

        private bool MenuItemBelongsToBranch(int menuItemId, int branchId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.MenuItems WHERE Id = @Id AND BranchId = @BranchId", conn);
            cmd.Parameters.AddWithValue("@Id", menuItemId);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            return (int)cmd.ExecuteScalar() > 0;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  View Model
    // ─────────────────────────────────────────────────────────
    public class MenuItemRateViewModel
    {
        public int      MenuItemId       { get; set; }
        public string   PLUCode          { get; set; } = "";
        public string   MenuItemName     { get; set; } = "";
        public string   CategoryName     { get; set; } = "";
        public decimal  BasePrice        { get; set; }
        public decimal? TakeoutPrice     { get; set; }
        public decimal? DeliveryPrice    { get; set; }
        public decimal? RoomServicePrice { get; set; }
    }

    // ─────────────────────────────────────────────────────────
    //  Request models
    // ─────────────────────────────────────────────────────────
    public class SaveRateRequest
    {
        public int      MenuItemId       { get; set; }
        public decimal  BasePrice        { get; set; }
        public decimal? TakeoutPrice     { get; set; }
        public decimal? DeliveryPrice    { get; set; }
        public decimal? RoomServicePrice { get; set; }
    }

    public class SaveAllRatesRequest
    {
        public List<SaveRateRequest> Items { get; set; } = new();
    }
}
