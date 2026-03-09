using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;

namespace RestaurantManagementSystem.Controllers
{
    /// <summary>
    /// Inventory Controller – Dashboard, Parameters, Supplier Master,
    /// Opening Stock, Stock Ledger, Reports.
    /// All data access goes through stored procedures (no inline SQL).
    /// </summary>
    public class InventoryController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public InventoryController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        }

        private int? ActiveBranchId() => User.GetActiveBranchId();

        private IActionResult NoBranch()
        {
            TempData["ErrorMessage"] = "Please select an active branch first.";
            return RedirectToAction("Index", "Home");
        }

        // ═══════════════════════════════════════════════════════════════
        //  INVENTORY DASHBOARD
        // ═══════════════════════════════════════════════════════════════

        public IActionResult Index()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var vm = new InventoryDashboardViewModel();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetInventoryDashboardStats", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);

                using var rdr = cmd.ExecuteReader();
                // Result set 1 – scalar stats
                if (rdr.Read())
                {
                    vm.TotalStockValue   = GetDecimal(rdr, "TotalStockValue");
                    vm.LowStockItems     = GetInt(rdr, "LowStockItems");
                    vm.PendingGRN        = GetInt(rdr, "PendingGRN");
                    vm.TodayPurchase     = GetDecimal(rdr, "TodayPurchase");
                    vm.TodayConsumption  = GetDecimal(rdr, "TodayConsumption");
                    vm.ActiveGodowns     = GetInt(rdr, "ActiveGodowns");
                    vm.TodayDamageCount  = GetInt(rdr, "TodayDamageCount");
                }
                // Result set 2 – top consumed
                if (rdr.NextResult())
                {
                    while (rdr.Read())
                        vm.TopConsumedItems.Add(new TopConsumedItem
                        {
                            ItemName      = GetStr(rdr, "ItemName"),
                            ItemCode      = GetStr(rdr, "ItemCode"),
                            TotalConsumed = GetDecimal(rdr, "TotalConsumed"),
                            UOMCode       = GetStr(rdr, "UOMCode")
                        });
                }
                // Result set 3 – low-stock alerts
                if (rdr.NextResult())
                {
                    while (rdr.Read())
                        vm.LowStockAlerts.Add(new LowStockAlert
                        {
                            ItemName     = GetStr(rdr, "ItemName"),
                            ItemCode     = GetStr(rdr, "ItemCode"),
                            BalanceQty   = GetDecimal(rdr, "BalanceQty"),
                            ReorderLevel = rdr.IsDBNull(rdr.GetOrdinal("ReorderLevel")) ? null : (decimal?)GetDecimal(rdr, "ReorderLevel"),
                            UOMCode      = GetStr(rdr, "UOMCode"),
                            GodownName   = GetStr(rdr, "GodownName")
                        });
                }
            }
            catch (Exception ex)
            {
                ViewBag.DashboardError = ex.Message;
            }

            ViewBag.ActiveBranchId = branchId.Value;
            return View(vm);
        }

        // ═══════════════════════════════════════════════════════════════
        //  INVENTORY PARAMETERS
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult Parameters()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            InventoryParameters? model = null;
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetInventoryParameters", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    model = new InventoryParameters
                    {
                        ParamId                    = GetInt(rdr, "ParamId"),
                        BranchId                   = GetInt(rdr, "BranchId"),
                        PurchaseOnlyFromMainGodown = GetBool(rdr, "PurchaseOnlyFromMainGodown"),
                        GRNMandatory               = GetBool(rdr, "GRNMandatory"),
                        AllowDirectPurchase        = GetBool(rdr, "AllowDirectPurchase"),
                        TransferPriceMode          = GetStr(rdr, "TransferPriceMode") ?? "AverageCost",
                        NegativeStockAllowed       = GetBool(rdr, "NegativeStockAllowed"),
                        AutoConsumptionOnSale      = GetBool(rdr, "AutoConsumptionOnSale")
                    };
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to load parameters: " + ex.Message;
            }

            model ??= new InventoryParameters { BranchId = branchId.Value, GRNMandatory = true, AllowDirectPurchase = true, AutoConsumptionOnSale = true };
            ViewBag.ActiveBranchId = branchId.Value;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Parameters(InventoryParameters model)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_SaveInventoryParameters", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId",                   model.BranchId);
                cmd.Parameters.AddWithValue("@PurchaseOnlyFromMainGodown", model.PurchaseOnlyFromMainGodown);
                cmd.Parameters.AddWithValue("@GRNMandatory",               model.GRNMandatory);
                cmd.Parameters.AddWithValue("@AllowDirectPurchase",        model.AllowDirectPurchase);
                cmd.Parameters.AddWithValue("@TransferPriceMode",          model.TransferPriceMode);
                cmd.Parameters.AddWithValue("@NegativeStockAllowed",       model.NegativeStockAllowed);
                cmd.Parameters.AddWithValue("@AutoConsumptionOnSale",      model.AutoConsumptionOnSale);
                cmd.Parameters.AddWithValue("@UserId",                     DBNull.Value);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Inventory parameters saved successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Save failed: " + ex.Message;
            }

            return RedirectToAction(nameof(Parameters));
        }

        // ═══════════════════════════════════════════════════════════════
        //  OPENING STOCK
        // ═══════════════════════════════════════════════════════════════

        public IActionResult OpeningStock()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<OpeningStockItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetOpeningStockList", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId", DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapOpeningStockItem(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        [HttpGet]
        public IActionResult OpeningStockForm(int? id)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            OpeningStockItem model;
            if (id.HasValue && id.Value > 0)
            {
                model = LoadOpeningStockById(id.Value) ?? new OpeningStockItem();
            }
            else
            {
                model = new OpeningStockItem { BranchId = branchId.Value, StockDate = DateTime.Today };
            }
            LoadDropdowns();
            ViewBag.ActiveBranchId = branchId.Value;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult OpeningStockForm(OpeningStockItem model)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            if (!ModelState.IsValid)
            {
                LoadDropdowns();
                ViewBag.ActiveBranchId = branchId.Value;
                return View(model);
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_SaveOpeningStock", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@OpeningStockId", model.OpeningStockId);
                cmd.Parameters.AddWithValue("@BranchId",       model.BranchId);
                cmd.Parameters.AddWithValue("@GodownId",       model.GodownId);
                cmd.Parameters.AddWithValue("@ItemId",         model.ItemId);
                cmd.Parameters.AddWithValue("@StockDate",      model.StockDate.Date);
                cmd.Parameters.AddWithValue("@Quantity",       model.Quantity);
                cmd.Parameters.AddWithValue("@UOMId",          model.UOMId);
                cmd.Parameters.AddWithValue("@CostPrice",      model.CostPrice);
                cmd.Parameters.AddWithValue("@Remarks",        (object?)model.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserId",         DBNull.Value);
                cmd.ExecuteScalar();
                TempData["SuccessMessage"] = "Opening stock saved.";
                return RedirectToAction(nameof(OpeningStock));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                LoadDropdowns();
                ViewBag.ActiveBranchId = branchId.Value;
                return View(model);
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult OpeningStockPost(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_PostOpeningStock", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@OpeningStockId", id);
                cmd.Parameters.AddWithValue("@UserId",         DBNull.Value);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Opening stock posted to ledger.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Post failed: " + ex.Message;
            }
            return RedirectToAction(nameof(OpeningStock));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult OpeningStockDelete(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_DeleteOpeningStock", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@OpeningStockId", id);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Opening stock deleted.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Delete failed: " + ex.Message;
            }
            return RedirectToAction(nameof(OpeningStock));
        }

        // ═══════════════════════════════════════════════════════════════
        //  STOCK LEDGER
        // ═══════════════════════════════════════════════════════════════

        public IActionResult StockLedger(int? godownId, int? itemId, string? txnType,
            DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<StockLedgerEntry>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockLedger", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId",  branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId",  godownId.HasValue ? (object)godownId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemId",    itemId.HasValue   ? (object)itemId.Value   : DBNull.Value);
                cmd.Parameters.AddWithValue("@TxnType",   string.IsNullOrEmpty(txnType) ? (object)DBNull.Value : txnType);
                cmd.Parameters.AddWithValue("@FromDate",  fromDate.HasValue ? (object)fromDate.Value.Date : DBNull.Value);
                cmd.Parameters.AddWithValue("@ToDate",    toDate.HasValue   ? (object)toDate.Value.Date   : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapStockLedger(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.GodownId   = godownId;
            ViewBag.ItemId     = itemId;
            ViewBag.TxnType    = txnType;
            ViewBag.FromDate   = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate     = toDate?.ToString("yyyy-MM-dd");
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CURRENT STOCK SUMMARY
        // ═══════════════════════════════════════════════════════════════

        public IActionResult StockSummary(int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<CurrentStockItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetCurrentStockSummary", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId", godownId.HasValue ? (object)godownId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapCurrentStock(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            // Load godown filter list (only godowns that have actual stock for this branch)
            var godownItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            try
            {
                using var con2 = new SqlConnection(_connectionString);
                con2.Open();
                using var cmd2 = new SqlCommand("usp_GetGodownsWithStock", con2)
                    { CommandType = CommandType.StoredProcedure };
                cmd2.Parameters.AddWithValue("@BranchId", branchId.Value);
                using var rdr2 = cmd2.ExecuteReader();
                while (rdr2.Read())
                {
                    var gId    = GetInt(rdr2, "GodownId");
                    var gName  = GetStr(rdr2, "GodownName") ?? "";
                    var bName  = GetStr(rdr2, "BranchName") ?? "";
                    godownItems.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                    {
                        Value    = gId.ToString(),
                        Text     = $"{gName} ({bName})",
                        Selected = godownId.HasValue && godownId.Value == gId
                    });
                }
            }
            catch { /* filter dropdown failure is non-critical */ }

            ViewBag.GodownList     = godownItems;
            ViewBag.SelGodown      = godownId ?? 0;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLOSING STOCK REPORT
        // ═══════════════════════════════════════════════════════════════

        public IActionResult ClosingStock(DateTime? asOfDate, int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            asOfDate ??= DateTime.Today;
            var list = new List<ClosingStockReportItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetClosingStockReport", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@AsOfDate", asOfDate.Value.Date);
                cmd.Parameters.AddWithValue("@GodownId", godownId.HasValue ? (object)godownId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapClosingStock(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.AsOfDate = asOfDate.Value.ToString("yyyy-MM-dd");
            ViewBag.GodownId = godownId;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  STOCK VALUATION
        // ═══════════════════════════════════════════════════════════════

        public IActionResult StockValuation(int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<StockValuationItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockValuationReport", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId", godownId.HasValue ? (object)godownId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapValuation(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.GodownId = godownId;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PURCHASE REGISTER
        // ═══════════════════════════════════════════════════════════════

        public IActionResult PurchaseRegister(DateTime? fromDate, DateTime? toDate, int? supplierId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            toDate   ??= DateTime.Today;

            var list = new List<PurchaseRegisterItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetPurchaseRegister", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId",   branchId.Value);
                cmd.Parameters.AddWithValue("@FromDate",   fromDate.Value.Date);
                cmd.Parameters.AddWithValue("@ToDate",     toDate.Value.Date);
                cmd.Parameters.AddWithValue("@SupplierId", supplierId.HasValue ? (object)supplierId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapPurchaseRegister(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.FromDate   = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate     = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.SupplierId = supplierId;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  TRANSFER REGISTER
        // ═══════════════════════════════════════════════════════════════

        public IActionResult TransferRegister(DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            toDate   ??= DateTime.Today;

            var list = new List<TransferRegisterItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetTransferRegister", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@FromDate", fromDate.Value.Date);
                cmd.Parameters.AddWithValue("@ToDate",   toDate.Value.Date);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapTransferRegister(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate   = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  DAMAGE REGISTER
        // ═══════════════════════════════════════════════════════════════

        public IActionResult DamageRegister(DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            toDate   ??= DateTime.Today;

            var list = new List<DamageRegisterItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetDamageRegister", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@FromDate", fromDate.Value.Date);
                cmd.Parameters.AddWithValue("@ToDate",   toDate.Value.Date);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapDamageRegister(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate   = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  API – AJAX HELPERS
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult GetGodownsJson(int? branchId)
        {
            branchId ??= ActiveBranchId();
            if (!branchId.HasValue) return Json(new List<object>());

            var result = new List<object>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT Id AS GodownId, BranchId, Code AS GodownCode, GodownName, CASE WHEN IsMainGodown=1 THEN \'Main\' ELSE \'Sub\' END AS GodownType, IsMainGodown, IsActive FROM dbo.Godowns WHERE BranchId = @bid AND IsActive = 1 ORDER BY GodownName", con);
            cmd.Parameters.AddWithValue("@bid", branchId.Value);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                result.Add(new
                {
                    id   = GetInt(rdr, "GodownId"),
                    name = GetStr(rdr, "GodownName"),
                    code = GetStr(rdr, "GodownCode"),
                    type = GetStr(rdr, "GodownType"),
                    isMain = GetBool(rdr, "IsMainGodown")
                });
            return Json(result);
        }

        [HttpGet]
        public IActionResult GetMainGodownJson()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return Json(null);

            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 Id AS GodownId, GodownName, Code AS GodownCode FROM dbo.Godowns WHERE BranchId = @BranchId AND IsMainGodown = 1 AND IsActive = 1",
                con);
            cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read())
                return Json(new { id = rdr.GetInt32(0), name = rdr.GetString(1), code = rdr.GetString(2) });
            return Json(null);
        }

        [HttpGet]
        public IActionResult GetSuppliersJson()
        {
            var result = new List<object>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT Id, PartyName FROM dbo.Parties WHERE IsActive=1 AND PartyType='Supplier' ORDER BY PartyName", con);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                result.Add(new { id = rdr.GetInt32(0), name = rdr.GetString(1), gst = "" });
            return Json(result);

        }

        [HttpGet]
        public IActionResult GetItemsJson()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return Json(new List<object>());

            var result = new List<object>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                @"SELECT i.Id, i.IngredientsName, i.Code, ISNULL(u.UOMCode,'') AS UOMCode,
                         ISNULL(u.UOMId,0) AS PurchaseUOMId, ISNULL(u.UOMName,'') AS UOMName
                  FROM dbo.Ingredients i
                  LEFT JOIN dbo.UomMaster u ON u.UOMId = i.PurchaseUOMId
                  WHERE i.IsActive = 1
                  ORDER BY i.IngredientsName", con);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                result.Add(new {
                    id          = rdr.GetInt32(0),
                    name        = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    code        = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    uomCode     = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                    uomId       = rdr.GetInt32(4),
                    uomName     = rdr.IsDBNull(5) ? "" : rdr.GetString(5)
                });
            return Json(result);
        }

        [HttpGet]
        public IActionResult GetUOMsJson()
        {
            var result = new List<object>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT UOMId, UOMCode, UOMName FROM dbo.UomMaster WHERE IsActive = 1 ORDER BY UOMName", con);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                result.Add(new { id = rdr.GetInt32(0), code = rdr.GetString(1), name = rdr.GetString(2) });
            return Json(result);
        }

        [HttpGet]
        public IActionResult GetItemAverageCost(int itemId, int godownId)
        {
            decimal avgCost = 0;
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetItemAverageCost", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@ItemId",    itemId);
                cmd.Parameters.AddWithValue("@GodownId",  godownId);
                var val = cmd.ExecuteScalar();
                if (val != null && val != DBNull.Value) avgCost = Convert.ToDecimal(val);
            }
            catch { }
            return Json(avgCost);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void LoadDropdowns()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return;

            // Godowns
            var godowns = new List<object>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id AS GodownId, Code AS GodownCode, GodownName, IsMainGodown FROM dbo.Godowns WHERE BranchId = @bid AND IsActive = 1 ORDER BY GodownName", con);
                cmd.Parameters.AddWithValue("@bid", branchId.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    godowns.Add(new { value = GetInt(rdr, "GodownId"), text = GetStr(rdr, "GodownName"), isMain = GetBool(rdr, "IsMainGodown") });
            }
            ViewBag.Godowns = godowns;

            // Ingredients
            var items = new List<object>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id, IngredientsName, Code FROM dbo.Ingredients WHERE IsActive = 1 ORDER BY IngredientsName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    items.Add(new { value = rdr.GetInt32(0), text = rdr.IsDBNull(1) ? "" : rdr.GetString(1) });
            }
            ViewBag.Items = items;

            // UOMs
            var uoms = new List<object>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT UOMId, UOMCode, UOMName FROM dbo.UomMaster WHERE IsActive = 1 ORDER BY UOMName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    uoms.Add(new { value = rdr.GetInt32(0), text = rdr.GetString(1) + " - " + rdr.GetString(2) });
            }
            ViewBag.UOMs = uoms;

            // Suppliers — reuse existing Party Master (PartyType='Supplier')
            var suppliers = new List<object>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id, PartyName FROM dbo.Parties WHERE IsActive=1 AND PartyType='Supplier' ORDER BY PartyName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    suppliers.Add(new { value = rdr.GetInt32(0), text = rdr.GetString(1) });
            }
            ViewBag.Suppliers = suppliers;
        }

        private OpeningStockItem? LoadOpeningStockById(int id)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand("usp_GetOpeningStockById", con) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@OpeningStockId", id);
            using var rdr = cmd.ExecuteReader();
            return rdr.Read() ? MapOpeningStockItem(rdr) : null;
        }


        private static OpeningStockItem MapOpeningStockItem(SqlDataReader rdr) => new()
        {
            OpeningStockId = GetInt(rdr, "OpeningStockId"),
            BranchId       = GetInt(rdr, "BranchId"),
            GodownId       = GetInt(rdr, "GodownId"),
            ItemId         = GetInt(rdr, "ItemId"),
            StockDate      = rdr.GetDateTime(rdr.GetOrdinal("StockDate")),
            Quantity       = GetDecimal(rdr, "Quantity"),
            UOMId          = GetInt(rdr, "UOMId"),
            CostPrice      = GetDecimal(rdr, "CostPrice"),
            Remarks        = GetStr(rdr, "Remarks"),
            IsPosted       = GetBool(rdr, "IsPosted"),
            ItemName       = GetStr(rdr, "ItemName"),
            UOMCode        = GetStr(rdr, "UOMCode"),
            GodownName     = GetStr(rdr, "GodownName")
        };

        private static StockLedgerEntry MapStockLedger(SqlDataReader rdr) => new()
        {
            LedgerId         = GetInt(rdr, "LedgerId"),
            BranchId         = GetInt(rdr, "BranchId"),
            GodownId         = GetInt(rdr, "GodownId"),
            ItemId           = GetInt(rdr, "ItemId"),
            TransactionDate  = rdr.GetDateTime(rdr.GetOrdinal("TransactionDate")),
            TransactionType  = GetStr(rdr, "TransactionType") ?? "",
            ReferenceNumber  = GetStr(rdr, "ReferenceNumber"),
            InQuantity       = GetDecimal(rdr, "InQuantity"),
            OutQuantity      = GetDecimal(rdr, "OutQuantity"),
            UnitCost         = GetDecimal(rdr, "UnitCost"),
            BalanceQty       = GetDecimal(rdr, "BalanceQty"),
            AverageCost      = GetDecimal(rdr, "AverageCost"),
            Remarks          = GetStr(rdr, "Remarks"),
            ItemName         = GetStr(rdr, "ItemName"),
            UOMCode          = GetStr(rdr, "UOMCode"),
            GodownName       = GetStr(rdr, "GodownName")
        };

        private static CurrentStockItem MapCurrentStock(SqlDataReader rdr) => new()
        {
            StockId       = GetInt(rdr, "StockId"),
            BranchId      = GetInt(rdr, "BranchId"),
            GodownId      = GetInt(rdr, "GodownId"),
            ItemId        = GetInt(rdr, "ItemId"),
            BalanceQty    = GetDecimal(rdr, "BalanceQty"),
            AverageCost   = GetDecimal(rdr, "AverageCost"),
            StockValue    = GetDecimal(rdr, "StockValue"),
            ItemName      = GetStr(rdr, "ItemName"),
            ItemCode      = GetStr(rdr, "ItemCode"),
            ItemCategory  = GetStr(rdr, "ItemCategory"),
            ReorderLevel  = GetDecimal(rdr, "ReorderLevel"),
            BaseUOMCode   = GetStr(rdr, "BaseUOMCode"),
            BaseUOMName   = GetStr(rdr, "BaseUOMName"),
            GodownName    = GetStr(rdr, "GodownName"),
            GodownType    = GetStr(rdr, "GodownType"),
            IsLowStock    = GetBool(rdr, "IsLowStock")
        };

        private static ClosingStockReportItem MapClosingStock(SqlDataReader rdr) => new()
        {
            ItemName        = GetStr(rdr, "ItemName"),
            ItemCode        = GetStr(rdr, "ItemCode"),
            GodownName      = GetStr(rdr, "GodownName"),
            OpeningQty      = GetDecimal(rdr, "OpeningQty"),
            PurchaseQty     = GetDecimal(rdr, "PurchaseQty"),
            TransferInQty   = GetDecimal(rdr, "TransferInQty"),
            TransferOutQty  = GetDecimal(rdr, "TransferOutQty"),
            DamageQty       = GetDecimal(rdr, "DamageQty"),
            SaleQty         = GetDecimal(rdr, "SaleQty"),
            ClosingQty      = GetDecimal(rdr, "ClosingQty"),
            AverageCost     = GetDecimal(rdr, "AverageCost"),
            ClosingValue    = GetDecimal(rdr, "ClosingValue")
        };

        private static StockValuationItem MapValuation(SqlDataReader rdr) => new()
        {
            GodownId      = GetInt(rdr, "GodownId"),
            GodownName    = GetStr(rdr, "GodownName"),
            ItemId        = GetInt(rdr, "ItemId"),
            ItemName      = GetStr(rdr, "ItemName"),
            ItemCode      = GetStr(rdr, "ItemCode"),
            UOMCode       = GetStr(rdr, "UOMCode"),
            BalanceQty    = GetDecimal(rdr, "BalanceQty"),
            AverageCost   = GetDecimal(rdr, "AverageCost"),
            StockValue    = GetDecimal(rdr, "StockValue")
        };

        private static PurchaseRegisterItem MapPurchaseRegister(SqlDataReader rdr) => new()
        {
            GRNId         = GetInt(rdr, "GRNId"),
            GRNNumber     = GetStr(rdr, "GRNNumber"),
            GRNDate       = rdr.GetDateTime(rdr.GetOrdinal("GRNDate")),
            InvoiceNo     = GetStr(rdr, "InvoiceNo"),
            SupplierName  = GetStr(rdr, "SupplierName"),
            GodownName    = GetStr(rdr, "GodownName"),
            SubTotal      = GetDecimal(rdr, "SubTotal"),
            TotalGSTAmount= GetDecimal(rdr, "TotalGSTAmount"),
            TotalAmount   = GetDecimal(rdr, "TotalAmount"),
            PONumber      = GetStr(rdr, "PONumber")
        };

        private static TransferRegisterItem MapTransferRegister(SqlDataReader rdr) => new()
        {
            TransferId      = GetInt(rdr, "TransferId"),
            TransferNumber  = GetStr(rdr, "TransferNumber"),
            TransferDate    = rdr.GetDateTime(rdr.GetOrdinal("TransferDate")),
            TransferType    = GetStr(rdr, "TransferType"),
            FromGodownName  = GetStr(rdr, "FromGodownName"),
            ToGodownName    = GetStr(rdr, "ToGodownName"),
            TotalQty        = GetDecimal(rdr, "TotalQty"),
            TotalValue      = GetDecimal(rdr, "TotalValue"),
            Status          = GetStr(rdr, "Status"),
            Remarks         = GetStr(rdr, "Remarks")
        };

        private static DamageRegisterItem MapDamageRegister(SqlDataReader rdr) => new()
        {
            DamageId     = GetInt(rdr, "DamageId"),
            DamageNumber = GetStr(rdr, "DamageNumber"),
            DamageDate   = rdr.GetDateTime(rdr.GetOrdinal("DamageDate")),
            DamageType   = GetStr(rdr, "DamageType"),
            GodownName   = GetStr(rdr, "GodownName"),
            TotalQty     = GetDecimal(rdr, "TotalQty"),
            TotalValue   = GetDecimal(rdr, "TotalValue"),
            Remarks      = GetStr(rdr, "Remarks"),
            Status       = GetStr(rdr, "Status")
        };

        private static int GetInt(SqlDataReader r, string col)
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? 0 : Convert.ToInt32(r.GetValue(ord));
        }
        private static decimal GetDecimal(SqlDataReader r, string col)
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? 0m : Convert.ToDecimal(r.GetValue(ord));
        }
        private static bool GetBool(SqlDataReader r, string col)
        {
            var ord = r.GetOrdinal(col);
            return !r.IsDBNull(ord) && Convert.ToBoolean(r.GetValue(ord));
        }
        private static string? GetStr(SqlDataReader r, string col)
        {
            try
            {
                var ord = r.GetOrdinal(col);
                return r.IsDBNull(ord) ? null : r.GetString(ord);
            }
            catch { return null; }
        }
    }
}
