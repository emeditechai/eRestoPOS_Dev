using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RestaurantManagementSystem.Controllers
{
    /// <summary>
    /// BOM (Bill of Material) Controller.
    /// Manages the mapping of Menu Items to their Ingredient components,
    /// with quantity, yield, and cost roll-up calculations.
    /// 
    /// Industry Logic:
    ///   LineCost      = Quantity (ConsumptionUOM) ÷ ConversionFactor × StandardCost
    ///   RawBOMCost    = SUM of all LineCosts for the menu item
    ///   ComputedCost  = RawBOMCost ÷ (YieldPercentage / 100)
    ///   GrossMargin % = (SellingPrice − ComputedCost) / SellingPrice × 100
    /// </summary>
    public class BOMController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public BOMController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        }

        private int? GetActiveBranchId() => User.GetActiveBranchId();

        private IActionResult NoBranchRedirect()
        {
            TempData["ErrorMessage"] = "Please select an active branch first.";
            return RedirectToAction("Index", "Home");
        }

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
        //  BOM LIST  –  all menu items with BOM status
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult BOMList()
        {
            var branchId = GetActiveBranchId();
            if (!branchId.HasValue) return NoBranchRedirect();

            EnsureBOMTablesReady();
            var list = LoadBOMList(branchId.Value);
            ViewBag.ActiveBranchId = branchId.Value;
            ViewBag.IsMainBranch   = IsActiveBranchMain(branchId.Value);
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Get branches available for BOM copy (main branch only)
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult GetBranchesForBOMCopy()
        {
            var branchId = GetActiveBranchId();
            if (!branchId.HasValue)
                return Json(new { success = false, message = "No active branch." });
            if (!IsActiveBranchMain(branchId.Value))
                return Json(new { success = false, message = "Only the main branch can copy BOMs." });

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT BranchId, BranchName
FROM   dbo.Branches
WHERE  ISNULL(IsActive, 1) = 1
  AND  BranchId <> @BranchId
ORDER  BY BranchName", conn);
            cmd.Parameters.AddWithValue("@BranchId", branchId.Value);

            var branches = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                branches.Add(new {
                    branchId   = reader.GetInt32(0),
                    branchName = reader["BranchName"]?.ToString() ?? ""
                });

            return Json(new { success = true, branches });
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Copy BOM configuration to other branches (by PLU code)
        // ═══════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CopyBOMToBranches([FromBody] CopyBOMRequest req)
        {
            if (req == null || req.MenuItemId == 0 || req.TargetBranchIds == null || req.TargetBranchIds.Count == 0)
                return Json(new { success = false, message = "Invalid request." });

            var sourceBranchId = GetActiveBranchId();
            if (!sourceBranchId.HasValue)
                return Json(new { success = false, message = "No active branch." });
            if (!IsActiveBranchMain(sourceBranchId.Value))
                return Json(new { success = false, message = "Only the main branch can copy BOMs." });

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // 1. Get source PLU code + recipe header
            string? pluCode  = null;
            int     yield    = 1;
            decimal yieldPct = 100;
            int?    prepTime = null;

            using (var cmd = new SqlCommand(@"
SELECT mi.PLUCode,
       ISNULL(r.Yield, 1)             AS Yield,
       ISNULL(r.YieldPercentage, 100) AS YieldPct,
       r.PreparationTimeMinutes
FROM   dbo.MenuItems mi
LEFT JOIN dbo.Recipes r ON r.MenuItemId = mi.Id
WHERE  mi.Id = @MenuItemId AND mi.BranchId = @BranchId", conn))
            {
                cmd.Parameters.AddWithValue("@MenuItemId", req.MenuItemId);
                cmd.Parameters.AddWithValue("@BranchId",   sourceBranchId.Value);
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return Json(new { success = false, message = "Source menu item not found." });
                pluCode  = r["PLUCode"] == DBNull.Value ? null : r["PLUCode"]?.ToString();
                yield    = r.GetInt32(r.GetOrdinal("Yield"));
                yieldPct = r.GetDecimal(r.GetOrdinal("YieldPct"));
                prepTime = r["PreparationTimeMinutes"] == DBNull.Value
                    ? null : (int?)r.GetInt32(r.GetOrdinal("PreparationTimeMinutes"));
            }

            if (string.IsNullOrWhiteSpace(pluCode))
                return Json(new { success = false, message = "Source menu item has no PLU code — cannot match across branches." });

            // 2. Get source ingredient lines (actual schema: MenuItemId, IngredientId, Quantity, IsOptional, Instructions)
            var lines = new List<(int IngredientId, decimal Qty, bool IsOptional, string? Instructions)>();
            using (var cmd = new SqlCommand(@"
SELECT IngredientId, Quantity, ISNULL(IsOptional, 0) AS IsOptional, Instructions
FROM   dbo.MenuItemIngredients
WHERE  MenuItemId = @MenuItemId", conn))
            {
                cmd.Parameters.AddWithValue("@MenuItemId", req.MenuItemId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    lines.Add((
                        r.GetInt32(r.GetOrdinal("IngredientId")),
                        r.GetDecimal(r.GetOrdinal("Quantity")),
                        r.GetBoolean(r.GetOrdinal("IsOptional")),
                        r["Instructions"] == DBNull.Value ? null : r["Instructions"]?.ToString()
                    ));
            }

            // 2a. Pre-load target branch names
            var branchNames = new Dictionary<int, string>();
            using (var cmd = new SqlCommand(
                "SELECT BranchId, BranchName FROM dbo.Branches WHERE BranchId IN (" +
                string.Join(",", req.TargetBranchIds) + ")", conn))
            {
                using var br = cmd.ExecuteReader();
                while (br.Read())
                    branchNames[br.GetInt32(0)] = br["BranchName"]?.ToString() ?? "";
            }

            // 3. Copy to each target branch
            var results = new List<CopyBOMResult>();
            foreach (var targetBranchId in req.TargetBranchIds)
            {
                var bName = branchNames.TryGetValue(targetBranchId, out var bdn) ? bdn : targetBranchId.ToString();
                try
                {
                    // Find target menu item by PLU code — skip gracefully if not found
                    int targetMenuItemId;
                    string targetMenuItemName = "";
                    using (var cmd = new SqlCommand(
                        "SELECT TOP 1 Id, ISNULL(Name,'') AS Name FROM dbo.MenuItems WHERE PLUCode = @PLU AND BranchId = @BranchId", conn))
                    {
                        cmd.Parameters.AddWithValue("@PLU",      pluCode);
                        cmd.Parameters.AddWithValue("@BranchId", targetBranchId);
                        using var rdr = cmd.ExecuteReader();
                        if (!rdr.Read())
                        {
                            results.Add(new CopyBOMResult { BranchId = targetBranchId, Success = true, Skipped = true,
                                Message = $"{bName}: Skipped — no menu item with PLU '{pluCode}'." });
                            continue;
                        }
                        targetMenuItemId   = rdr.GetInt32(0);
                        targetMenuItemName = rdr["Name"]?.ToString() ?? "";
                    }

                    // Upsert Recipes header — Title is NOT NULL so always supply it
                    using (var chk = new SqlCommand("SELECT Id FROM dbo.Recipes WHERE MenuItemId = @Mid", conn))
                    {
                        chk.Parameters.AddWithValue("@Mid", targetMenuItemId);
                        var existingRecipeId = chk.ExecuteScalar();
                        if (existingRecipeId != null && existingRecipeId != DBNull.Value)
                        {
                            using var upd = new SqlCommand(@"
UPDATE dbo.Recipes
SET    Title = @Title, Yield = @Yield, YieldPercentage = @YieldPct, PreparationTimeMinutes = @PrepTime
WHERE  Id = @Id", conn);
                            upd.Parameters.AddWithValue("@Title",   targetMenuItemName);
                            upd.Parameters.AddWithValue("@Yield",   yield);
                            upd.Parameters.AddWithValue("@YieldPct", yieldPct);
                            upd.Parameters.AddWithValue("@PrepTime", (object?)prepTime ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@Id",       Convert.ToInt32(existingRecipeId));
                            upd.ExecuteNonQuery();
                        }
                        else
                        {
                            using var ins = new SqlCommand(@"
INSERT INTO dbo.Recipes
    (MenuItemId, Title, Yield, YieldPercentage, PreparationTimeMinutes,
     PreparationInstructions, CookingInstructions)
VALUES
    (@Mid, @Title, @Yield, @YieldPct, @PrepTime, '', '')", conn);
                            ins.Parameters.AddWithValue("@Mid",     targetMenuItemId);
                            ins.Parameters.AddWithValue("@Title",   targetMenuItemName);
                            ins.Parameters.AddWithValue("@Yield",   yield);
                            ins.Parameters.AddWithValue("@YieldPct", yieldPct);
                            ins.Parameters.AddWithValue("@PrepTime", (object?)prepTime ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                    }

                    // Merge ingredient lines — upsert by (MenuItemId, IngredientId)
                    int merged = 0;
                    foreach (var line in lines)
                    {
                        using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM dbo.MenuItemIngredients WHERE MenuItemId = @Mid AND IngredientId = @IId)
    UPDATE dbo.MenuItemIngredients
    SET    Quantity     = @Qty,
           IsOptional   = @IsOpt,
           Instructions = @Notes
    WHERE  MenuItemId = @Mid AND IngredientId = @IId
ELSE
    INSERT INTO dbo.MenuItemIngredients (MenuItemId, IngredientId, Quantity, IsOptional, Instructions, Unit)
    VALUES (@Mid, @IId, @Qty, @IsOpt, @Notes, '')", conn);
                        cmd.Parameters.AddWithValue("@Mid",   targetMenuItemId);
                        cmd.Parameters.AddWithValue("@IId",   line.IngredientId);
                        cmd.Parameters.AddWithValue("@Qty",   line.Qty);
                        cmd.Parameters.AddWithValue("@IsOpt", line.IsOptional);
                        cmd.Parameters.AddWithValue("@Notes", (object?)line.Instructions ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                        merged++;
                    }

                    // Recalculate BOM cost using target branch's own stock (branch-wise weighted avg cost)
                    RecalcBOMCost(targetMenuItemId, targetBranchId);
                    var recalcedCost = GetComputedCost(targetMenuItemId);
                    string costInfo = recalcedCost.HasValue
                        ? $" | Cost: ₹{recalcedCost.Value:N2}"
                        : " | Cost: ₹0 (no stock yet)";

                    results.Add(new CopyBOMResult { BranchId = targetBranchId, Success = true,
                        Message = $"{bName}: {merged} ingredient(s) merged & cost recalculated.{costInfo}" });
                }
                catch (Exception ex)
                {
                    results.Add(new CopyBOMResult { BranchId = targetBranchId, Success = false,
                        Message = $"{bName}: {ex.Message}" });
                }
            }

            return Json(new { success = true, results });
        }

        // ═══════════════════════════════════════════════════════════════
        //  BOM CONFIGURE  –  edit BOM lines for one menu item
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult BOMConfigure(int id)
        {
            var branchId = GetActiveBranchId();
            if (!branchId.HasValue) return NoBranchRedirect();

            EnsureBOMTablesReady();

            var vm = LoadBOMConfigure(id, branchId.Value);
            if (vm == null)
            {
                TempData["ErrorMessage"] = "Menu item not found in the active branch.";
                return RedirectToAction(nameof(BOMList));
            }

            ViewBag.IngredientDropdown = LoadIngredientDropdown(branchId.Value);
            ViewBag.ActiveBranchId = branchId.Value;
            return View(vm);
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Get ingredient details for cost preview
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult GetIngredientDetails(int ingredientId)
        {
            var branchId = GetActiveBranchId() ?? 0;
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT i.Id, i.IngredientsName, i.ItemCategory,
       i.RecipeUOMId,   ru.UOMCode AS RecipeUOMCode,   ru.UOMName AS RecipeUOMName,
       i.PurchaseUOMId, pu.UOMCode AS PurchaseUOMCode, pu.UOMName AS PurchaseUOMName,
       ISNULL(i.PurchaseToRecipeFactor, 1) AS ConversionFactor,
       ISNULL((
           SELECT CASE WHEN SUM(cs.BalanceQty) > 0
                       THEN SUM(cs.BalanceQty * cs.AverageCost) / SUM(cs.BalanceQty)
                       ELSE 0 END
           FROM dbo.CurrentStock cs
           JOIN dbo.Godowns g ON g.Id = cs.GodownId
           WHERE cs.ItemId = i.Id AND g.BranchId = @BranchId AND cs.BalanceQty > 0
       ), 0)                               AS LiveAvgCost
FROM   dbo.Ingredients i
LEFT JOIN dbo.UomMaster ru ON ru.UOMId = i.RecipeUOMId
LEFT JOIN dbo.UomMaster pu ON pu.UOMId = i.PurchaseUOMId
WHERE  i.Id = @Id AND ISNULL(i.IsActive, 1) = 1", conn);
            cmd.Parameters.AddWithValue("@Id", ingredientId);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Json(new { success = false, message = "Ingredient not found." });

            decimal liveAvgCost = reader.GetDecimal(reader.GetOrdinal("LiveAvgCost"));
            return Json(new
            {
                success          = true,
                ingredientId     = reader.GetInt32(reader.GetOrdinal("Id")),
                ingredientName   = reader["IngredientsName"]?.ToString() ?? "",
                itemCategory     = reader["ItemCategory"]?.ToString() ?? "",
                consumptionUOMId   = reader["RecipeUOMId"]   == DBNull.Value ? (int?)null : reader.GetInt32(reader.GetOrdinal("RecipeUOMId")),
                consumptionUOMCode = reader["RecipeUOMCode"]?.ToString() ?? "",
                purchaseUOMId    = reader["PurchaseUOMId"]   == DBNull.Value ? (int?)null : reader.GetInt32(reader.GetOrdinal("PurchaseUOMId")),
                purchaseUOMCode  = reader["PurchaseUOMCode"]?.ToString() ?? "",
                conversionFactor = reader.GetDecimal(reader.GetOrdinal("ConversionFactor")),
                standardCost     = liveAvgCost,   // branch-wise weighted avg from CurrentStock only
                hasStock         = liveAvgCost > 0
            });
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Save header (Yield / YieldPct / PrepTime)
        // ═══════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveBOMHeader([FromBody] SaveBOMHeaderRequest req)
        {
            if (req == null || req.MenuItemId == 0)
                return Json(new { success = false, message = "Invalid request." });

            var branchId = GetActiveBranchId();
            if (!branchId.HasValue)
                return Json(new { success = false, message = "No active branch selected." });
            if (!MenuItemBelongsToBranch(req.MenuItemId, branchId.Value))
                return Json(new { success = false, message = "Access denied: item not in active branch." });

            if (req.Yield < 1 || req.Yield > 100)
                return Json(new { success = false, message = "Portions served must be 1–100." });

            if (req.YieldPercentage < 1 || req.YieldPercentage > 100)
                return Json(new { success = false, message = "Yield % must be 1–100." });

            try
            {
                EnsureBOMTablesReady();
                UpsertRecipeHeader(req.MenuItemId, req.Yield, req.YieldPercentage, req.PrepTimeMinutes);
                RecalcBOMCost(req.MenuItemId, branchId.Value);

                var newCost = GetComputedCost(req.MenuItemId);
                var (baseP, takeoutP, deliveryP, roomP) = GetAllPrices(req.MenuItemId);

                return Json(new
                {
                    success              = true,
                    computedCost         = newCost,
                    sellingPrice         = baseP,
                    grossMarginPct       = CalcMargin(baseP, newCost),
                    takeoutMarginPct     = CalcMargin(takeoutP, newCost),
                    deliveryMarginPct    = CalcMargin(deliveryP, newCost),
                    roomServiceMarginPct = CalcMargin(roomP, newCost),
                    message = "BOM header saved."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Save a BOM line (add or update)
        // ═══════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveBOMLine([FromBody] SaveBOMLineRequest req)
        {
            if (req == null || req.MenuItemId == 0 || req.IngredientId == 0)
                return Json(new { success = false, message = "Invalid request – missing MenuItemId or IngredientId." });

            if (req.Quantity <= 0)
                return Json(new { success = false, message = "Quantity must be greater than 0." });

            var branchId = GetActiveBranchId();
            if (!branchId.HasValue)
                return Json(new { success = false, message = "No active branch selected." });
            if (!MenuItemBelongsToBranch(req.MenuItemId, branchId.Value))
                return Json(new { success = false, message = "Access denied: item not in active branch." });

            // Ingredients with no stock are allowed — they contribute ₹0 line cost until stock is received.

            try
            {
                EnsureBOMTablesReady();

                // Ensure the recipe header exists before adding lines
                EnsureRecipeHeaderExists(req.MenuItemId);

                int savedLineId = UpsertBOMLine(req);
                RecalcBOMCost(req.MenuItemId, branchId.Value);

                var newCost = GetComputedCost(req.MenuItemId);
                var (baseP2, takeoutP2, deliveryP2, roomP2) = GetAllPrices(req.MenuItemId);

                return Json(new
                {
                    success              = true,
                    lineId               = savedLineId,
                    computedCost         = newCost,
                    sellingPrice         = baseP2,
                    grossMarginPct       = CalcMargin(baseP2, newCost),
                    takeoutMarginPct     = CalcMargin(takeoutP2, newCost),
                    deliveryMarginPct    = CalcMargin(deliveryP2, newCost),
                    roomServiceMarginPct = CalcMargin(roomP2, newCost),
                    message              = req.LineId == 0 ? "Ingredient added to BOM." : "BOM line updated."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Delete a BOM line
        // ═══════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteBOMLine([FromBody] DeleteBOMLineRequest req)
        {
            if (req == null || req.LineId == 0)
                return Json(new { success = false, message = "Invalid line ID." });

            var branchId = GetActiveBranchId();
            if (!branchId.HasValue)
                return Json(new { success = false, message = "No active branch selected." });

            try
            {
                int menuItemId = GetMenuItemIdForLine(req.LineId);
                if (menuItemId == 0)
                    return Json(new { success = false, message = "BOM line not found." });

                if (!MenuItemBelongsToBranch(menuItemId, branchId.Value))
                    return Json(new { success = false, message = "Access denied: item not in active branch." });

                DeleteLine(req.LineId);
                RecalcBOMCost(menuItemId, branchId.Value);

                var newCost = GetComputedCost(menuItemId);
                var (baseP3, takeoutP3, deliveryP3, roomP3) = GetAllPrices(menuItemId);

                return Json(new
                {
                    success              = true,
                    computedCost         = newCost,
                    sellingPrice         = baseP3,
                    grossMarginPct       = CalcMargin(baseP3, newCost),
                    takeoutMarginPct     = CalcMargin(takeoutP3, newCost),
                    deliveryMarginPct    = CalcMargin(deliveryP3, newCost),
                    roomServiceMarginPct = CalcMargin(roomP3, newCost),
                    message              = "Ingredient removed from BOM."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  AJAX – Recalculate cost for a menu item
        // ═══════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RecalculateCost([FromBody] RecalcCostRequest req)
        {
            if (req == null || req.MenuItemId == 0)
                return Json(new { success = false, message = "Invalid request." });

            var branchId = GetActiveBranchId();
            if (!branchId.HasValue)
                return Json(new { success = false, message = "No active branch selected." });
            if (!MenuItemBelongsToBranch(req.MenuItemId, branchId.Value))
                return Json(new { success = false, message = "Access denied: item not in active branch." });

            try
            {
                EnsureBOMTablesReady();
                RecalcBOMCost(req.MenuItemId, branchId.Value);

                var newCost = GetComputedCost(req.MenuItemId);
                var (baseP4, takeoutP4, deliveryP4, roomP4) = GetAllPrices(req.MenuItemId);

                return Json(new
                {
                    success              = true,
                    computedCost         = newCost,
                    sellingPrice         = baseP4,
                    grossMarginPct       = CalcMargin(baseP4, newCost),
                    takeoutMarginPct     = CalcMargin(takeoutP4, newCost),
                    deliveryMarginPct    = CalcMargin(deliveryP4, newCost),
                    roomServiceMarginPct = CalcMargin(roomP4, newCost),
                    message              = "Cost recalculated."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private List<BOMListItemViewModel> LoadBOMList(int branchId)
        {
            var list = new List<BOMListItemViewModel>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT
    mi.Id                                                    AS MenuItemId,
    mi.Name                                                  AS MenuItemName,
    mi.PLUCode                                               AS PLUCode,
    c.Name                                                   AS CategoryName,
    ISNULL(mi.Price, 0)                                      AS SellingPrice,
    mi.TakeoutPrice,
    mi.DeliveryPrice,
    mi.RoomServicePrice,
    ISNULL(bomCount.LineCount, 0)                            AS LineCount,
    r.ComputedCost,
    r.LastCostCalculatedAt
FROM   dbo.MenuItems mi
LEFT JOIN dbo.Categories c    ON c.Id = mi.CategoryId
LEFT JOIN dbo.Recipes    r    ON r.MenuItemId = mi.Id
LEFT JOIN (
    SELECT MenuItemId, COUNT(*) AS LineCount
    FROM   dbo.MenuItemIngredients
    GROUP  BY MenuItemId
)                        bomCount ON bomCount.MenuItemId = mi.Id
WHERE  ISNULL(mi.IsAvailable, 1) = 1
  AND  mi.BranchId = @BranchId
ORDER  BY c.Name, mi.Name", conn);
            cmd.Parameters.AddWithValue("@BranchId", branchId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                decimal sellingPrice      = reader.GetDecimal(reader.GetOrdinal("SellingPrice"));
                decimal? takeoutPrice     = reader["TakeoutPrice"]     == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("TakeoutPrice"));
                decimal? deliveryPrice    = reader["DeliveryPrice"]    == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("DeliveryPrice"));
                decimal? roomServicePrice = reader["RoomServicePrice"] == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("RoomServicePrice"));
                decimal? bomCost          = reader["ComputedCost"]      == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("ComputedCost"));
                decimal? margin           = (bomCost.HasValue && sellingPrice > 0)
                    ? Math.Round((sellingPrice - bomCost.Value) / sellingPrice * 100, 2)
                    : null;

                list.Add(new BOMListItemViewModel
                {
                    MenuItemId       = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                    MenuItemName     = reader["MenuItemName"]?.ToString() ?? "",
                    PLUCode          = reader["PLUCode"] == DBNull.Value ? null : reader["PLUCode"]?.ToString(),
                    CategoryName     = reader["CategoryName"]?.ToString(),
                    SellingPrice     = sellingPrice,
                    TakeoutPrice     = takeoutPrice,
                    DeliveryPrice    = deliveryPrice,
                    RoomServicePrice = roomServicePrice,
                    LineCount        = reader.GetInt32(reader.GetOrdinal("LineCount")),
                    BOMCost          = bomCost,
                    GrossMarginPct   = margin,
                    LastCalculated   = reader["LastCostCalculatedAt"] == DBNull.Value
                        ? null : (DateTime?)reader.GetDateTime(reader.GetOrdinal("LastCostCalculatedAt"))
                });
            }
            return list;
        }

        private BOMConfigureViewModel? LoadBOMConfigure(int menuItemId, int branchId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // 1. Load MenuItem + Recipe header (branch-scoped)
            BOMConfigureViewModel? vm = null;
            using (var cmd = new SqlCommand(@"
SELECT
    mi.Id, mi.Name, c.Name AS CategoryName,
    ISNULL(mi.Price, 0) AS SellingPrice,
    mi.TakeoutPrice, mi.DeliveryPrice, mi.RoomServicePrice,
    r.Id AS RecipeId,
    ISNULL(r.Yield, 1)                AS Yield,
    ISNULL(r.YieldPercentage, 100)    AS YieldPercentage,
    r.PreparationTimeMinutes,
    r.ComputedCost,
    r.LastCostCalculatedAt
FROM  dbo.MenuItems mi
LEFT JOIN dbo.Categories c ON c.Id = mi.CategoryId
LEFT JOIN dbo.Recipes    r ON r.MenuItemId = mi.Id
WHERE mi.Id = @MenuItemId
  AND mi.BranchId = @BranchId", conn))
            {
                cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
                cmd.Parameters.AddWithValue("@BranchId", branchId);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;

                vm = new BOMConfigureViewModel
                {
                    MenuItemId       = reader.GetInt32(reader.GetOrdinal("Id")),
                    MenuItemName     = reader["Name"]?.ToString() ?? "",
                    CategoryName     = reader["CategoryName"]?.ToString(),
                    SellingPrice     = reader.GetDecimal(reader.GetOrdinal("SellingPrice")),
                    TakeoutPrice     = reader["TakeoutPrice"]     == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("TakeoutPrice")),
                    DeliveryPrice    = reader["DeliveryPrice"]    == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("DeliveryPrice")),
                    RoomServicePrice = reader["RoomServicePrice"] == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("RoomServicePrice")),
                    RecipeId         = reader["RecipeId"]     == DBNull.Value ? null : (int?)reader.GetInt32(reader.GetOrdinal("RecipeId")),
                    Yield            = reader.GetInt32(reader.GetOrdinal("Yield")),
                    YieldPercentage  = reader.GetDecimal(reader.GetOrdinal("YieldPercentage")),
                    PrepTimeMinutes  = reader["PreparationTimeMinutes"] == DBNull.Value ? null : (int?)reader.GetInt32(reader.GetOrdinal("PreparationTimeMinutes")),
                    ComputedCost     = reader["ComputedCost"]  == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("ComputedCost")),
                    LastCalculated   = reader["LastCostCalculatedAt"] == DBNull.Value ? null : (DateTime?)reader.GetDateTime(reader.GetOrdinal("LastCostCalculatedAt"))
                };
            }

            // 2. Load BOM lines
            using (var cmd = new SqlCommand(@"
SELECT
    mii.Id, mii.MenuItemId,
    mii.IngredientId,   i.IngredientsName, i.ItemCategory,
    mii.Quantity,
    i.RecipeUOMId,   ru.UOMCode AS RecipeUOMCode,
    i.PurchaseUOMId, pu.UOMCode AS PurchaseUOMCode,
    ISNULL(i.PurchaseToRecipeFactor, 1) AS ConversionFactor,
    ISNULL((
        SELECT CASE WHEN SUM(cs.BalanceQty) > 0
                    THEN SUM(cs.BalanceQty * cs.AverageCost) / SUM(cs.BalanceQty)
                    ELSE 0 END
        FROM dbo.CurrentStock cs
        JOIN dbo.Godowns g ON g.Id = cs.GodownId
        WHERE cs.ItemId = i.Id AND g.BranchId = @BranchId AND cs.BalanceQty > 0
    ), 0)                               AS StandardCost,
    ISNULL(mii.IsOptional, 0)           AS IsOptional,
    mii.Instructions
FROM  dbo.MenuItemIngredients mii
JOIN  dbo.Ingredients i  ON i.Id = mii.IngredientId
LEFT JOIN dbo.UomMaster ru ON ru.UOMId = i.RecipeUOMId
LEFT JOIN dbo.UomMaster pu ON pu.UOMId = i.PurchaseUOMId
WHERE mii.MenuItemId = @MenuItemId
ORDER BY i.IngredientsName", conn))
            {
                cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
                cmd.Parameters.AddWithValue("@BranchId", branchId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    decimal qty              = reader.GetDecimal(reader.GetOrdinal("Quantity"));
                    decimal convFactor       = reader.GetDecimal(reader.GetOrdinal("ConversionFactor"));
                    decimal stdCost          = reader.GetDecimal(reader.GetOrdinal("StandardCost"));

                    vm!.Lines.Add(new BOMLineViewModel
                    {
                        Id               = reader.GetInt32(reader.GetOrdinal("Id")),
                        MenuItemId       = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                        IngredientId     = reader.GetInt32(reader.GetOrdinal("IngredientId")),
                        IngredientName   = reader["IngredientsName"]?.ToString() ?? "",
                        ItemCategory     = reader["ItemCategory"]?.ToString(),
                        Quantity         = qty,
                        ConsumptionUOMId   = reader["RecipeUOMId"]   == DBNull.Value ? null : (int?)reader.GetInt32(reader.GetOrdinal("RecipeUOMId")),
                        ConsumptionUOMCode = reader["RecipeUOMCode"]?.ToString(),
                        PurchaseUOMId    = reader["PurchaseUOMId"]   == DBNull.Value ? null : (int?)reader.GetInt32(reader.GetOrdinal("PurchaseUOMId")),
                        PurchaseUOMCode  = reader["PurchaseUOMCode"]?.ToString(),
                        ConversionFactor = convFactor,
                        StandardCost     = stdCost,
                        IsOptional       = reader.GetBoolean(reader.GetOrdinal("IsOptional")),
                        Instructions     = reader["Instructions"]?.ToString()
                    });
                }
            }

            return vm;
        }

        private List<dynamic> LoadIngredientDropdown(int branchId)
        {
            var items = new List<dynamic>();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT i.Id, i.IngredientsName, i.ItemCategory,
       ru.UOMCode AS RecipeUOMCode,
       ISNULL(i.PurchaseToRecipeFactor, 1) AS ConversionFactor,
       ISNULL((
           SELECT CASE WHEN SUM(cs.BalanceQty) > 0
                       THEN SUM(cs.BalanceQty * cs.AverageCost) / SUM(cs.BalanceQty)
                       ELSE 0 END
           FROM dbo.CurrentStock cs
           JOIN dbo.Godowns g ON g.Id = cs.GodownId
           WHERE cs.ItemId = i.Id AND g.BranchId = @BranchId AND cs.BalanceQty > 0
       ), 0)                               AS LiveAvgCost
FROM   dbo.Ingredients i
LEFT JOIN dbo.UomMaster ru ON ru.UOMId = i.RecipeUOMId
WHERE  ISNULL(i.IsActive, 1) = 1
ORDER  BY i.ItemCategory, i.IngredientsName", conn);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                decimal liveAvgCost = reader.GetDecimal(reader.GetOrdinal("LiveAvgCost"));
                items.Add(new
                {
                    id           = reader.GetInt32(0),
                    name         = reader["IngredientsName"]?.ToString() ?? "",
                    category     = reader["ItemCategory"]?.ToString() ?? "Other",
                    uomCode      = reader["RecipeUOMCode"]?.ToString() ?? "",
                    convFactor   = reader.GetDecimal(reader.GetOrdinal("ConversionFactor")),
                    standardCost = liveAvgCost,   // branch-wise weighted avg from CurrentStock only
                    hasLiveCost  = liveAvgCost > 0 // true only when branch has stock for this item
                });
            }
            return items;
        }

        private void UpsertRecipeHeader(int menuItemId, int yield, decimal yieldPct, int? prepTime)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM dbo.Recipes WHERE MenuItemId = @MenuItemId)
BEGIN
    UPDATE dbo.Recipes
    SET    Yield = @Yield,
           YieldPercentage = @YieldPct,
           PreparationTimeMinutes = @PrepTime
    WHERE  MenuItemId = @MenuItemId
END
ELSE
BEGIN
    INSERT INTO dbo.Recipes
        (MenuItemId, Title, Yield, YieldPercentage, PreparationTimeMinutes,
         PreparationInstructions, CookingInstructions)
    VALUES
        (@MenuItemId,
         (SELECT TOP 1 Name FROM dbo.MenuItems WHERE Id = @MenuItemId),
         @Yield, @YieldPct, @PrepTime,
         '', '')
END", conn);
            cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
            cmd.Parameters.AddWithValue("@Yield",      yield);
            cmd.Parameters.AddWithValue("@YieldPct",   yieldPct);
            cmd.Parameters.AddWithValue("@PrepTime",   (object?)prepTime ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        private void EnsureRecipeHeaderExists(int menuItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM dbo.Recipes WHERE MenuItemId = @MenuItemId)
BEGIN
    INSERT INTO dbo.Recipes
        (MenuItemId, Title, Yield, YieldPercentage,
         PreparationInstructions, CookingInstructions)
    VALUES
        (@MenuItemId,
         (SELECT TOP 1 Name FROM dbo.MenuItems WHERE Id = @MenuItemId),
         1, 100, '', '')
END", conn);
            cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
            cmd.ExecuteNonQuery();
        }

        private int UpsertBOMLine(SaveBOMLineRequest req)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            if (req.LineId == 0)
            {
                // Check duplicate ingredient
                using var chk = new SqlCommand(@"
SELECT COUNT(1) FROM dbo.MenuItemIngredients
WHERE MenuItemId = @M AND IngredientId = @I", conn);
                chk.Parameters.AddWithValue("@M", req.MenuItemId);
                chk.Parameters.AddWithValue("@I", req.IngredientId);
                if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
                    throw new InvalidOperationException("This ingredient is already in the BOM. Edit the existing line instead.");

                // Insert new
                using var ins = new SqlCommand(@"
INSERT INTO dbo.MenuItemIngredients
    (MenuItemId, IngredientId, Quantity, IsOptional, Instructions, Unit)
OUTPUT INSERTED.Id
VALUES
    (@MenuItemId, @IngredientId, @Qty, @IsOptional, @Instructions, '')", conn);
                ins.Parameters.AddWithValue("@MenuItemId",   req.MenuItemId);
                ins.Parameters.AddWithValue("@IngredientId", req.IngredientId);
                ins.Parameters.AddWithValue("@Qty",          req.Quantity);
                ins.Parameters.AddWithValue("@IsOptional",   req.IsOptional);
                ins.Parameters.AddWithValue("@Instructions", (object?)req.Instructions ?? DBNull.Value);
                return (int)ins.ExecuteScalar();
            }
            else
            {
                // Update existing line
                using var upd = new SqlCommand(@"
UPDATE dbo.MenuItemIngredients
SET    Quantity     = @Qty,
       IsOptional   = @IsOptional,
       Instructions = @Instructions
WHERE  Id = @Id AND MenuItemId = @MenuItemId", conn);
                upd.Parameters.AddWithValue("@Id",          req.LineId);
                upd.Parameters.AddWithValue("@MenuItemId",  req.MenuItemId);
                upd.Parameters.AddWithValue("@Qty",         req.Quantity);
                upd.Parameters.AddWithValue("@IsOptional",  req.IsOptional);
                upd.Parameters.AddWithValue("@Instructions",(object?)req.Instructions ?? DBNull.Value);
                upd.ExecuteNonQuery();
                return req.LineId;
            }
        }

        private void DeleteLine(int lineId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "DELETE FROM dbo.MenuItemIngredients WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", lineId);
            cmd.ExecuteNonQuery();
        }

        private int GetMenuItemIdForLine(int lineId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 MenuItemId FROM dbo.MenuItemIngredients WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", lineId);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : (int)result;
        }

        /// <summary>
        /// Returns true when the ingredient has any positive BalanceQty in the branch.
        /// </summary>
        private bool IngredientHasStockInBranch(int ingredientId, int branchId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
SELECT TOP 1 1
FROM   dbo.CurrentStock cs
JOIN   dbo.Godowns g ON g.Id = cs.GodownId
WHERE  cs.ItemId = @ItemId AND g.BranchId = @BranchId AND cs.BalanceQty > 0", conn);
            cmd.Parameters.AddWithValue("@ItemId",   ingredientId);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            return cmd.ExecuteScalar() != null;
        }

        private string GetIngredientNameForValidation(int ingredientId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 IngredientsName FROM dbo.Ingredients WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", ingredientId);
            return cmd.ExecuteScalar()?.ToString() ?? $"Ingredient #{ingredientId}";
        }

        /// <summary>
        /// Recalculates ComputedCost using the branch-wise weighted average cost
        /// from CurrentStock (SUM(Qty*AvgCost)/SUM(Qty) per ingredient per branch).
        /// No fallback — ingredients with no stock contribute ₹0 to the BOM cost.
        /// </summary>
        private void RecalcBOMCost(int menuItemId, int branchId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            // SQL Server cannot use an aggregate subquery inside SUM().
            // Fix: pre-compute branch-wise weighted avg cost per item in a CTE,
            // then LEFT JOIN it so we can reference a plain column inside SUM().
            using var cmd = new SqlCommand(@"
DECLARE @RawCost    DECIMAL(18,4);
DECLARE @YieldPct   DECIMAL(18,4);
DECLARE @FinalCost  DECIMAL(18,4);

-- Step 1: weighted average cost per item for this branch.
;WITH ItemAvgCost AS (
    SELECT cs.ItemId,
           SUM(cs.BalanceQty * cs.AverageCost) / NULLIF(SUM(cs.BalanceQty), 0) AS AvgCost
    FROM   dbo.CurrentStock cs
    JOIN   dbo.Godowns g ON g.Id = cs.GodownId
    WHERE  g.BranchId = @BranchId
      AND  cs.BalanceQty > 0
    GROUP BY cs.ItemId
)
-- Step 2: roll-up BOM lines using branch stock avg only (0 if no stock — no master fallback).
SELECT @RawCost = SUM(
    mii.Quantity
    / NULLIF(ISNULL(i.PurchaseToRecipeFactor, 1), 0)
    * ISNULL(iac.AvgCost, 0)
)
FROM  dbo.MenuItemIngredients mii
JOIN  dbo.Ingredients i  ON i.Id = mii.IngredientId
LEFT JOIN ItemAvgCost iac ON iac.ItemId = i.Id
WHERE mii.MenuItemId = @MenuItemId;

SELECT @YieldPct = ISNULL(YieldPercentage, 100)
FROM   dbo.Recipes
WHERE  MenuItemId = @MenuItemId;

SET @FinalCost = @RawCost / NULLIF(@YieldPct / 100.0, 0);

UPDATE dbo.Recipes
SET    ComputedCost         = @FinalCost,
       LastCostCalculatedAt = SYSUTCDATETIME()
WHERE  MenuItemId = @MenuItemId;
", conn);
            cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
            cmd.Parameters.AddWithValue("@BranchId",   branchId);
            cmd.ExecuteNonQuery();
        }

        private decimal? GetComputedCost(int menuItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 ComputedCost FROM dbo.Recipes WHERE MenuItemId = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", menuItemId);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : (decimal?)result;
        }

        private decimal GetSellingPrice(int menuItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 ISNULL(Price, 0) FROM dbo.MenuItems WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", menuItemId);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : (decimal)result;
        }

        private (decimal Base, decimal? Takeout, decimal? Delivery, decimal? RoomService) GetAllPrices(int menuItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 ISNULL(Price,0) AS BasePrice, TakeoutPrice, DeliveryPrice, RoomServicePrice FROM dbo.MenuItems WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", menuItemId);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return (0, null, null, null);
            return (
                rdr.GetDecimal(rdr.GetOrdinal("BasePrice")),
                rdr["TakeoutPrice"]     == DBNull.Value ? null : (decimal?)rdr.GetDecimal(rdr.GetOrdinal("TakeoutPrice")),
                rdr["DeliveryPrice"]    == DBNull.Value ? null : (decimal?)rdr.GetDecimal(rdr.GetOrdinal("DeliveryPrice")),
                rdr["RoomServicePrice"] == DBNull.Value ? null : (decimal?)rdr.GetDecimal(rdr.GetOrdinal("RoomServicePrice"))
            );
        }

        private static decimal? CalcMargin(decimal? price, decimal? cost) =>
            (cost.HasValue && price.HasValue && price > 0)
                ? Math.Round((price.Value - cost.Value) / price.Value * 100, 2)
                : null;

        private bool MenuItemBelongsToBranch(int menuItemId, int branchId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.MenuItems WHERE Id = @Id AND BranchId = @BranchId", conn);
            cmd.Parameters.AddWithValue("@Id", menuItemId);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>
        /// Ensures the Recipes and MenuItemIngredients tables exist with all BOM columns.
        /// Creates them if missing, then adds any new columns idempotently.
        /// </summary>
        private void EnsureBOMTablesReady()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
-- ── CREATE Recipes table if it does not exist ──────────────────────────
IF OBJECT_ID('dbo.Recipes') IS NULL
BEGIN
    CREATE TABLE dbo.Recipes (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Recipes PRIMARY KEY,
        MenuItemId             INT           NOT NULL,
        Title                  NVARCHAR(100) NOT NULL DEFAULT '',
        PreparationInstructions NVARCHAR(MAX) NOT NULL DEFAULT '',
        CookingInstructions    NVARCHAR(MAX) NOT NULL DEFAULT '',
        PlatingInstructions    NVARCHAR(MAX) NULL,
        Yield                  INT           NOT NULL DEFAULT 1,
        YieldPercentage        DECIMAL(5,2)  NOT NULL DEFAULT 100.00,
        PreparationTimeMinutes INT           NULL,
        CookingTimeMinutes     INT           NULL,
        Notes                  NVARCHAR(MAX) NULL,
        IsArchived             BIT           NOT NULL DEFAULT 0,
        Version                INT           NOT NULL DEFAULT 1,
        LastUpdated            DATETIME      NOT NULL DEFAULT GETDATE(),
        ComputedCost           DECIMAL(18,4) NULL,
        LastCostCalculatedAt   DATETIME2     NULL,
        CONSTRAINT FK_Recipes_MenuItems FOREIGN KEY (MenuItemId)
            REFERENCES dbo.MenuItems (Id)
    );
END

-- ── ADD BOM cost columns to Recipes if they don't exist ────────────────
IF COL_LENGTH('dbo.Recipes', 'ComputedCost') IS NULL
    ALTER TABLE dbo.Recipes ADD ComputedCost DECIMAL(18,4) NULL;

IF COL_LENGTH('dbo.Recipes', 'LastCostCalculatedAt') IS NULL
    ALTER TABLE dbo.Recipes ADD LastCostCalculatedAt DATETIME2 NULL;

IF COL_LENGTH('dbo.Recipes', 'PreparationTimeMinutes') IS NULL
    ALTER TABLE dbo.Recipes ADD PreparationTimeMinutes INT NULL;

-- ── CREATE MenuItemIngredients table if it does not exist ──────────────
IF OBJECT_ID('dbo.MenuItemIngredients') IS NULL
BEGIN
    CREATE TABLE dbo.MenuItemIngredients (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MenuItemIngredients PRIMARY KEY,
        MenuItemId   INT              NOT NULL,
        IngredientId INT              NOT NULL,
        Quantity     DECIMAL(18,4)    NOT NULL DEFAULT 0,
        Unit         NVARCHAR(20)     NULL,
        IsOptional   BIT              NOT NULL DEFAULT 0,
        Instructions NVARCHAR(200)    NULL,
        CONSTRAINT FK_MenuItemIngredients_MenuItem
            FOREIGN KEY (MenuItemId) REFERENCES dbo.MenuItems (Id),
        CONSTRAINT FK_MenuItemIngredients_Ingredient
            FOREIGN KEY (IngredientId) REFERENCES dbo.Ingredients (Id)
    );
END

-- ── ADD columns to MenuItemIngredients if they don't exist ─────────────
IF COL_LENGTH('dbo.MenuItemIngredients', 'IsOptional') IS NULL
    ALTER TABLE dbo.MenuItemIngredients ADD IsOptional BIT NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.MenuItemIngredients', 'Unit') IS NULL
    ALTER TABLE dbo.MenuItemIngredients ADD Unit NVARCHAR(20) NULL;

IF COL_LENGTH('dbo.MenuItemIngredients', 'Instructions') IS NULL
    ALTER TABLE dbo.MenuItemIngredients ADD Instructions NVARCHAR(200) NULL;
", conn);
            cmd.ExecuteNonQuery();
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Auxiliary AJAX request models
    // ─────────────────────────────────────────────────────────
    public class DeleteBOMLineRequest
    {
        public int LineId { get; set; }
    }

    public class RecalcCostRequest
    {
        public int MenuItemId { get; set; }
    }

    public class CopyBOMRequest
    {
        public int MenuItemId { get; set; }
        public List<int> TargetBranchIds { get; set; } = new();
    }

    public class CopyBOMResult
    {
        public int    BranchId { get; set; }
        public bool   Success  { get; set; }
        public bool   Skipped  { get; set; }
        public string Message  { get; set; } = "";
    }
}
