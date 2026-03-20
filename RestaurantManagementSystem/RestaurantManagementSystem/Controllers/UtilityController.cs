using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        public async Task<IActionResult> SaveMenuItemRate([FromBody] SaveRateRequest req)
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
                // Read old values for audit
                decimal oldBase = 0; decimal? oldTakeout = null, oldDelivery = null, oldRoomSvc = null;
                string itemName = "";
                using (var connR = new SqlConnection(_connectionString))
                {
                    connR.Open();
                    using var cmdR = new SqlCommand(
                        "SELECT ISNULL(Name,''), ISNULL(Price,0), TakeoutPrice, DeliveryPrice, RoomServicePrice FROM dbo.MenuItems WHERE Id=@Id", connR);
                    cmdR.Parameters.AddWithValue("@Id", req.MenuItemId);
                    using var rdr = cmdR.ExecuteReader();
                    if (rdr.Read())
                    {
                        itemName   = rdr.GetString(0);
                        oldBase    = rdr.GetDecimal(1);
                        oldTakeout = rdr.IsDBNull(2) ? null : (decimal?)rdr.GetDecimal(2);
                        oldDelivery= rdr.IsDBNull(3) ? null : (decimal?)rdr.GetDecimal(3);
                        oldRoomSvc = rdr.IsDBNull(4) ? null : (decimal?)rdr.GetDecimal(4);
                    }
                }

                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                SqlAuditContext.Apply(conn, User, HttpContext, activeBranchId.Value, "MenuItemRate");
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

                // Audit
                var uid  = User.GetUserId() ?? 0;
                var uname= User.Identity?.Name ?? "Unknown";
                var oldSummary = $"Base:{oldBase}, Takeout:{oldTakeout}, Delivery:{oldDelivery}, RoomSvc:{oldRoomSvc}";
                var newSummary = $"Base:{req.BasePrice}, Takeout:{req.TakeoutPrice}, Delivery:{req.DeliveryPrice}, RoomSvc:{req.RoomServicePrice}";
                try { await AuditTrailController.LogSystemAuditAsync(
                    _connectionString, "MenuItemRate", "Update",
                    req.MenuItemId, itemName, "Prices",
                    oldSummary, newSummary,
                    activeBranchId.Value, uid, uname,
                    HttpContext.Connection.RemoteIpAddress?.ToString()); } catch { }

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
        public async Task<IActionResult> SaveAllMenuItemRates([FromBody] SaveAllRatesRequest req)
        {
            if (req == null || req.Items == null || req.Items.Count == 0)
                return Json(new { success = false, message = "No items to save." });

            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
                return Json(new { success = false, message = "No active branch." });

            bool isMain = IsActiveBranchMain(activeBranchId.Value);
            int saved = 0;
            var errors = new List<string>();
            var uid   = User.GetUserId() ?? 0;
            var uname = User.Identity?.Name ?? "Unknown";
            var ip    = HttpContext.Connection.RemoteIpAddress?.ToString();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            SqlAuditContext.Apply(conn, User, HttpContext, activeBranchId.Value, "MenuItemRate");

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
                    // Read old values
                    string itemName = ""; decimal oldBase = 0;
                    decimal? oldTakeout = null, oldDelivery = null, oldRoomSvc = null;
                    using (var cmdR = new SqlCommand(
                        "SELECT ISNULL(Name,''), ISNULL(Price,0), TakeoutPrice, DeliveryPrice, RoomServicePrice FROM dbo.MenuItems WHERE Id=@Id", conn))
                    {
                        cmdR.Parameters.AddWithValue("@Id", item.MenuItemId);
                        using var rdr = cmdR.ExecuteReader();
                        if (rdr.Read())
                        {
                            itemName    = rdr.GetString(0);
                            oldBase     = rdr.GetDecimal(1);
                            oldTakeout  = rdr.IsDBNull(2) ? null : (decimal?)rdr.GetDecimal(2);
                            oldDelivery = rdr.IsDBNull(3) ? null : (decimal?)rdr.GetDecimal(3);
                            oldRoomSvc  = rdr.IsDBNull(4) ? null : (decimal?)rdr.GetDecimal(4);
                        }
                    }

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

                    var old = $"Base:{oldBase}, Takeout:{oldTakeout}, Delivery:{oldDelivery}, RoomSvc:{oldRoomSvc}";
                    var nw  = $"Base:{item.BasePrice}, Takeout:{item.TakeoutPrice}, Delivery:{item.DeliveryPrice}, RoomSvc:{item.RoomServicePrice}";
                    try
                    {
                        await AuditTrailController.LogSystemAuditAsync(
                            _connectionString, "MenuItemRate", "Update",
                            item.MenuItemId, itemName, "Prices", old, nw,
                            activeBranchId.Value, uid, uname, ip);
                    }
                    catch { /* audit must not break bulk save */ }
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
