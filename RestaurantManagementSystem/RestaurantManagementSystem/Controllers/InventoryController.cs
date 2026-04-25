using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

        private async Task<bool> IsMainBranchAdminAsync(int? branchId)
        {
            bool isAdmin = User.GetActiveRoleName() == "Administrator" || User.IsSuperAdminUser();
            if (!isAdmin || !branchId.HasValue) return false;
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                using var cmd = new SqlCommand("SELECT Is_MainBranch FROM dbo.Branches WHERE BranchId = @BranchId", con);
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                var result = await cmd.ExecuteScalarAsync();
                return result != null && result != DBNull.Value && Convert.ToBoolean(result);
            }
            catch { return false; }
        }

        private async Task<List<SelectListItem>> LoadAllBranchesAsync()
        {
            var list = new List<SelectListItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                using var cmd = new SqlCommand("SELECT BranchId, BranchName FROM dbo.Branches WHERE IsActive = 1 ORDER BY BranchName", con);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    list.Add(new SelectListItem(rdr["BranchName"].ToString(), rdr["BranchId"].ToString()));
            }
            catch { }
            return list;
        }

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
                // Result set 3 – low-stock alerts (with avg daily consumption + days remaining)
                if (rdr.NextResult())
                {
                    while (rdr.Read())
                        vm.LowStockAlerts.Add(new LowStockAlert
                        {
                            ItemName             = GetStr(rdr, "ItemName"),
                            ItemCode             = GetStr(rdr, "ItemCode"),
                            BalanceQty           = GetDecimal(rdr, "BalanceQty"),
                            ReorderLevel         = rdr.IsDBNull(rdr.GetOrdinal("ReorderLevel")) ? null : (decimal?)GetDecimal(rdr, "ReorderLevel"),
                            UOMCode              = GetStr(rdr, "UOMCode"),
                            GodownName           = GetStr(rdr, "GodownName"),
                            AvgDailyConsumption  = GetDecimal(rdr, "AvgDailyConsumption"),
                            DaysRemaining        = GetInt(rdr, "DaysRemaining")
                        });
                }
                // Result set 4 – reorder suggestions
                if (rdr.NextResult())
                {
                    while (rdr.Read())
                        vm.ReorderSuggestions.Add(new ReorderSuggestion
                        {
                            ItemName            = GetStr(rdr, "ItemName"),
                            ItemCode            = GetStr(rdr, "ItemCode"),
                            BalanceQty          = GetDecimal(rdr, "BalanceQty"),
                            ReorderLevel        = GetDecimal(rdr, "ReorderLevel"),
                            UOMCode             = GetStr(rdr, "UOMCode"),
                            GodownName          = GetStr(rdr, "GodownName"),
                            AvgDailyConsumption = GetDecimal(rdr, "AvgDailyConsumption"),
                            DaysRemaining       = GetInt(rdr, "DaysRemaining"),
                            SuggestedOrderQty   = GetDecimal(rdr, "SuggestedOrderQty"),
                            LastPurchasePrice   = GetDecimal(rdr, "LastPurchasePrice")
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

        // ── Batch (multi-item) Opening Stock Entry ──────────────────────────

        [HttpGet]
        public IActionResult OpeningStockBatch()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var model = new OpeningStockBatchViewModel
            {
                BranchId  = branchId.Value,
                StockDate = DateTime.Today,
                Lines     = new List<OpeningStockLine> { new OpeningStockLine() }
            };
            LoadDropdowns();
            ViewBag.ActiveBranchId = branchId.Value;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult OpeningStockBatch(OpeningStockBatchViewModel model)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            // Keep only lines with an item and quantity
            var validLines = (model.Lines ?? new List<OpeningStockLine>())
                             .Where(l => l.ItemId > 0 && l.Quantity > 0).ToList();

            if (model.GodownId <= 0)
                ModelState.AddModelError("GodownId", "Please select a Godown.");

            if (!validLines.Any())
                ModelState.AddModelError("", "Please add at least one item with Quantity > 0.");

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
                int saved = 0;
                foreach (var line in validLines)
                {
                    using var cmd = new SqlCommand("usp_SaveOpeningStock", con)
                        { CommandType = CommandType.StoredProcedure };
                    cmd.Parameters.AddWithValue("@OpeningStockId", 0);
                    cmd.Parameters.AddWithValue("@BranchId",       model.BranchId);
                    cmd.Parameters.AddWithValue("@GodownId",       model.GodownId);
                    cmd.Parameters.AddWithValue("@ItemId",         line.ItemId);
                    cmd.Parameters.AddWithValue("@StockDate",      model.StockDate.Date);
                    cmd.Parameters.AddWithValue("@Quantity",       line.Quantity);
                    cmd.Parameters.AddWithValue("@UOMId",          line.UOMId);
                    cmd.Parameters.AddWithValue("@CostPrice",      line.CostPrice);
                    cmd.Parameters.AddWithValue("@Remarks",        (object?)model.Remarks ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserId",         DBNull.Value);
                    cmd.ExecuteScalar();
                    saved++;
                }
                TempData["SuccessMessage"] = $"{saved} opening stock {(saved == 1 ? "entry" : "entries")} saved successfully.";
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

        // ── AJAX: check if item already has opening stock in a godown ────────
        [HttpGet]
        public IActionResult CheckItemStock(int itemId, int godownId, int branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                // Query CurrentStock (the live running balance), NOT OpeningStock.
                // GodownId alone uniquely identifies the storage location so no BranchId filter needed.
                using var cmd = new SqlCommand(@"
                    SELECT ISNULL(SUM(cs.BalanceQty), 0)
                    FROM   dbo.CurrentStock cs
                    WHERE  cs.ItemId   = @ItemId
                      AND  cs.GodownId = @GodownId
                      AND  cs.BalanceQty <> 0", con);
                cmd.Parameters.AddWithValue("@ItemId",   itemId);
                cmd.Parameters.AddWithValue("@GodownId", godownId);
                var result = cmd.ExecuteScalar();
                decimal currentStock = result != DBNull.Value ? Convert.ToDecimal(result) : 0M;
                return Json(new { hasStock = currentStock != 0, currentStock });
            }
            catch
            {
                return Json(new { hasStock = false, currentStock = 0 });
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

            // Default: last 30 days when no dates provided
            fromDate ??= DateTime.Today.AddDays(-30);
            toDate   ??= DateTime.Today;

            // 0 means "All" from the dropdown — pass as NULL to SP so no filter is applied
            int? effectiveGodownId = (godownId.HasValue && godownId.Value > 0) ? godownId : null;
            int? effectiveItemId   = (itemId.HasValue   && itemId.Value   > 0) ? itemId   : null;

            var list = new List<StockLedgerEntry>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockLedger", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId",  branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId",  effectiveGodownId.HasValue ? (object)effectiveGodownId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemId",    effectiveItemId.HasValue   ? (object)effectiveItemId.Value   : DBNull.Value);
                cmd.Parameters.AddWithValue("@TxnType",   string.IsNullOrEmpty(txnType) ? (object)DBNull.Value : txnType);
                cmd.Parameters.AddWithValue("@FromDate",  fromDate.Value.Date);
                // Use end-of-day so records from ToDate (any time) are included
                cmd.Parameters.AddWithValue("@ToDate",    toDate.Value.Date.AddDays(1).AddSeconds(-1));
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapStockLedger(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.GodownId       = godownId ?? 0;
            ViewBag.ItemId         = itemId   ?? 0;
            ViewBag.TxnType        = txnType  ?? "";
            ViewBag.FromDate       = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate         = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ── Stock Ledger → Export PDF ─────────────────────────────────────────
        [HttpGet]
        public IActionResult StockLedgerPdf(int? godownId, int? itemId, string? txnType,
            DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= DateTime.Today.AddDays(-30);
            toDate   ??= DateTime.Today;
            int? effGodown = (godownId.HasValue && godownId.Value > 0) ? godownId : null;
            int? effItem   = (itemId.HasValue   && itemId.Value   > 0) ? itemId   : null;

            var list = new List<StockLedgerEntry>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockLedger", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId",  branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId",  effGodown.HasValue ? (object)effGodown.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemId",    effItem.HasValue   ? (object)effItem.Value   : DBNull.Value);
                cmd.Parameters.AddWithValue("@TxnType",   string.IsNullOrEmpty(txnType) ? (object)DBNull.Value : txnType);
                cmd.Parameters.AddWithValue("@FromDate",  fromDate.Value.Date);
                cmd.Parameters.AddWithValue("@ToDate",    toDate.Value.Date.AddDays(1).AddSeconds(-1));
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read()) list.Add(MapStockLedger(rdr));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            var sortedList = list
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => x.LedgerId)
                .ToList();

            string subtitle = $"Period: {fromDate.Value:dd-MMM-yyyy} to {toDate.Value:dd-MMM-yyyy}";

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("Stock Ledger")
                            .Bold().FontSize(14).FontColor("#1e3a5f");
                        col.Item().Text(subtitle)
                            .FontSize(8).FontColor("#6b7280");
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#93c5fd");
                    });

                    page.Content().PaddingTop(8).Table(tbl =>
                    {
                        tbl.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(60); // Date
                            c.RelativeColumn(3);  // Particulars
                            c.RelativeColumn();   // In Qty
                            c.RelativeColumn();   // In Val
                            c.RelativeColumn();   // Out Qty
                            c.RelativeColumn();   // Out Val
                            c.RelativeColumn();   // Bal Qty
                            c.RelativeColumn();   // Bal Val
                        });

                        tbl.Header(h =>
                        {
                            h.Cell().RowSpan(2).Background("#1e3a5f").Padding(4).AlignCenter()
                                .Text("Date").Bold().FontColor("#ffffff").FontSize(7);
                            h.Cell().RowSpan(2).Background("#1e3a5f").Padding(4).AlignCenter()
                                .Text("Particulars").Bold().FontColor("#ffffff").FontSize(7);

                            h.Cell().ColumnSpan(2).Background("#166534").Padding(3).AlignCenter()
                                .Text("Inwards").Bold().FontColor("#ffffff").FontSize(7);
                            h.Cell().ColumnSpan(2).Background("#7f1d1d").Padding(3).AlignCenter()
                                .Text("Outwards").Bold().FontColor("#ffffff").FontSize(7);
                            h.Cell().ColumnSpan(2).Background("#1e3a5f").Padding(3).AlignCenter()
                                .Text("Closing Balance").Bold().FontColor("#ffffff").FontSize(7);

                            foreach (var lbl in new[] { "Quantity", "Value (₹)", "Quantity", "Value (₹)", "Quantity", "Value (₹)" })
                            {
                                h.Cell().Background("#2d4e78").Padding(3).AlignCenter()
                                    .Text(lbl).FontColor("#ffffff").FontSize(6.5f);
                            }
                        });

                        // Data rows grouped by date
                        var grouped = sortedList
                            .GroupBy(x => x.TransactionDate.Date)
                            .ToList();

                        uint rowNum = 0;
                        foreach (var dayGroup in grouped)
                        {
                            var dayList   = dayGroup.OrderBy(x => x.LedgerId).ToList();
                            var dayLast   = dayList.Last();
                            decimal dayInQty  = dayList.Sum(x => x.InQuantity);
                            decimal dayInVal  = dayList.Sum(x => x.InQuantity  * x.UnitCost);
                            decimal dayOutQty = dayList.Sum(x => x.OutQuantity);
                            decimal dayOutVal = dayList.Sum(x => x.OutQuantity * x.UnitCost);
                            decimal dayBalQty = dayLast.BalanceQty;
                            decimal dayBalVal = dayLast.BalanceQty * dayLast.AverageCost;
                            string  dayUom    = dayLast.UOMCode ?? "";

                            // Date group header
                            tbl.Cell().ColumnSpan(6).Background("#eff6ff").BorderBottom(1).BorderColor("#bfdbfe")
                                .Padding(4)
                                .Text($"For {dayGroup.Key:d-MMM-yyyy}  ({dayList.Count} {(dayList.Count == 1 ? "entry" : "entries")})")
                                .Bold().FontSize(7.5f).FontColor("#1e3a5f");
                            tbl.Cell().Background("#eff6ff").BorderBottom(1).BorderColor("#bfdbfe")
                                .Padding(4).AlignRight()
                                .Text($"{dayBalQty:0.###} {dayUom}").Bold().FontSize(7).FontColor("#1d4ed8");
                            tbl.Cell().Background("#eff6ff").BorderBottom(1).BorderColor("#bfdbfe")
                                .Padding(4).AlignRight()
                                .Text($"{dayBalVal:N2}").Bold().FontSize(7).FontColor("#1d4ed8");

                            // Transaction rows
                            foreach (var row in dayList)
                            {
                                rowNum++;
                                string bg = rowNum % 2 == 0 ? "#f9fafb" : "#ffffff";
                                bool hasIn  = row.InQuantity  > 0;
                                bool hasOut = row.OutQuantity > 0;
                                string uom  = row.UOMCode ?? "";
                                decimal inVal  = row.InQuantity  * row.UnitCost;
                                decimal outVal = row.OutQuantity * row.UnitCost;
                                decimal balVal = row.BalanceQty  * row.AverageCost;
                                string txnLabel = row.TransactionType switch
                                {
                                    "GRN"                    => "GRN/Purchase",
                                    "OPENING"                => "Opening Stock",
                                    "TRANSFER_IN"            => "Transfer In",
                                    "TRANSFER_OUT"           => "Transfer Out",
                                    "DAMAGE"                 => "Damage",
                                    "SaleConsumption"        => "Sale Consumption",
                                    "PRODUCTION_CONSUMPTION" => "BOM Consumption",
                                    _                        => row.TransactionType
                                };

                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3)
                                    .Text(row.TransactionDate.ToString("dd MMM")).FontSize(7).FontColor("#6b7280");

                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).Column(c =>
                                    {
                                        c.Item().Text(txnLabel).Bold().FontSize(7).FontColor("#374151");
                                        if (!string.IsNullOrEmpty(row.ItemName))
                                            c.Item().Text(row.ItemName).FontSize(6.5f).FontColor("#1e3a5f");
                                        if (!string.IsNullOrEmpty(row.ReferenceNumber))
                                            c.Item().Text(row.ReferenceNumber).FontSize(6f).FontColor("#9ca3af");
                                    });

                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).AlignRight()
                                    .Text(hasIn ? $"{row.InQuantity:0.###} {uom}" : "—")
                                    .FontSize(7).FontColor(hasIn ? "#15803d" : "#d1d5db");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).AlignRight()
                                    .Text(hasIn ? $"{inVal:N2}" : "—")
                                    .FontSize(7).FontColor(hasIn ? "#15803d" : "#d1d5db");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).AlignRight()
                                    .Text(hasOut ? $"{row.OutQuantity:0.###} {uom}" : "—")
                                    .FontSize(7).FontColor(hasOut ? "#b91c1c" : "#d1d5db");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).AlignRight()
                                    .Text(hasOut ? $"{outVal:N2}" : "—")
                                    .FontSize(7).FontColor(hasOut ? "#b91c1c" : "#d1d5db");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).AlignRight()
                                    .Text($"{row.BalanceQty:0.###} {uom}").Bold()
                                    .FontSize(7).FontColor(row.BalanceQty < 0 ? "#b91c1c" : "#1e3a5f");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(3).AlignRight()
                                    .Text($"{balVal:N2}").FontSize(7).FontColor("#374151");
                            }

                            // Day total row
                            if (grouped.Count > 1 || dayList.Count > 1)
                            {
                                tbl.Cell().ColumnSpan(2).Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text($"Day Total — {dayGroup.Key:d-MMM-yyyy}")
                                    .Bold().FontSize(7).FontColor("#78350f");
                                tbl.Cell().Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text(dayInQty > 0 ? $"{dayInQty:0.###} {dayUom}" : "—")
                                    .Bold().FontSize(7).FontColor("#78350f");
                                tbl.Cell().Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text(dayInQty > 0 ? $"{dayInVal:N2}" : "—")
                                    .Bold().FontSize(7).FontColor("#78350f");
                                tbl.Cell().Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text(dayOutQty > 0 ? $"{dayOutQty:0.###} {dayUom}" : "—")
                                    .Bold().FontSize(7).FontColor("#78350f");
                                tbl.Cell().Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text(dayOutQty > 0 ? $"{dayOutVal:N2}" : "—")
                                    .Bold().FontSize(7).FontColor("#78350f");
                                tbl.Cell().Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text($"{dayBalQty:0.###} {dayUom}").Bold().FontSize(7).FontColor("#78350f");
                                tbl.Cell().Background("#fffbeb").BorderTop(1).BorderColor("#fbbf24")
                                    .Padding(3).AlignRight()
                                    .Text($"{dayBalVal:N2}").Bold().FontSize(7).FontColor("#78350f");
                            }
                        }

                        // Grand total
                        decimal gtInQty  = sortedList.Sum(x => x.InQuantity);
                        decimal gtInVal  = sortedList.Sum(x => x.InQuantity  * x.UnitCost);
                        decimal gtOutQty = sortedList.Sum(x => x.OutQuantity);
                        decimal gtOutVal = sortedList.Sum(x => x.OutQuantity * x.UnitCost);
                        var     gtLast   = sortedList.LastOrDefault();
                        decimal gtBalQty = gtLast?.BalanceQty ?? 0;
                        decimal gtBalVal = gtLast != null ? gtLast.BalanceQty * gtLast.AverageCost : 0;
                        string  gtUom    = gtLast?.UOMCode ?? "";

                        tbl.Cell().ColumnSpan(2).Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text("GRAND TOTAL").Bold().FontSize(7.5f).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text(gtInQty > 0 ? $"{gtInQty:0.###} {gtUom}" : "—").Bold().FontSize(7).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text(gtInQty > 0 ? $"₹{gtInVal:N2}" : "—").Bold().FontSize(7).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text(gtOutQty > 0 ? $"{gtOutQty:0.###} {gtUom}" : "—").Bold().FontSize(7).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text(gtOutQty > 0 ? $"₹{gtOutVal:N2}" : "—").Bold().FontSize(7).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"{gtBalQty:0.###} {gtUom}").Bold().FontSize(7).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"₹{gtBalVal:N2}").Bold().FontSize(7).FontColor("#ffffff");
                    });

                    page.Footer().AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ").FontSize(7).FontColor("#9ca3af");
                            x.CurrentPageNumber().FontSize(7).FontColor("#9ca3af");
                            x.Span(" of ").FontSize(7).FontColor("#9ca3af");
                            x.TotalPages().FontSize(7).FontColor("#9ca3af");
                        });
                });
            });

            var bytes = pdf.GeneratePdf();
            string fileName = $"StockLedger_{fromDate.Value:yyyyMMdd}_{toDate.Value:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", fileName);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CURRENT STOCK SUMMARY
        // ═══════════════════════════════════════════════════════════════

        public IActionResult StockSummary(int? godownId, bool resetFilter = false)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            bool isMain = IsMainBranchById(branchId.Value);

            // On first load (no godownId in URL), default to the login branch's main godown
            if (!resetFilter && !godownId.HasValue)
            {
                godownId = GetMainGodownId(branchId.Value);
            }

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

            // Load godown filter dropdown:
            //   Main branch    → main godowns from all branches
            //   Non-main branch→ only own-branch godowns
            var godownItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            try
            {
                using var con2 = new SqlConnection(_connectionString);
                con2.Open();
                using var cmd2 = new SqlCommand("usp_GetGodownsWithStock", con2)
                    { CommandType = CommandType.StoredProcedure };
                cmd2.Parameters.AddWithValue("@BranchId",     branchId.Value);
                cmd2.Parameters.AddWithValue("@IsMainBranch", isMain ? 1 : 0);
                using var rdr2 = cmd2.ExecuteReader();
                while (rdr2.Read())
                {
                    var gId    = GetInt(rdr2, "GodownId");
                    var gName  = GetStr(rdr2, "GodownName") ?? "";
                    var bName  = GetStr(rdr2, "BranchName") ?? "";
                    var label  = isMain ? $"{bName} – {gName}" : gName;
                    godownItems.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                    {
                        Value    = gId.ToString(),
                        Text     = label,
                        Selected = godownId.HasValue && godownId.Value == gId
                    });
                }
            }
            catch { /* filter dropdown failure is non-critical */ }

            ViewBag.GodownList     = godownItems;
            ViewBag.SelGodown      = godownId ?? 0;
            ViewBag.ActiveBranchId = branchId.Value;
            ViewBag.IsMainBranch   = isMain;
            return View(list);
        }

        // ── Current Stock Summary → Export PDF ────────────────────────────────
        [HttpGet]
        public IActionResult StockSummaryPdf(int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            bool isMain = IsMainBranchById(branchId.Value);

            var list = new List<CurrentStockItem>();
            string godownLabel = "All Godowns";
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetCurrentStockSummary", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId", godownId.HasValue && godownId.Value > 0
                    ? (object)godownId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read()) list.Add(MapCurrentStock(rdr));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            // Resolve godown label for the header
            if (godownId.HasValue && godownId.Value > 0 && list.Count > 0)
                godownLabel = list[0].GodownName ?? "Selected Godown";

            var grouped = list
                .OrderBy(x => x.ItemCategory ?? "Uncategorised")
                .ThenBy(x => x.ItemName)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.ItemCategory) ? "Uncategorised" : x.ItemCategory)
                .ToList();

            decimal totalVal  = list.Sum(x => x.BalanceQty * x.AverageCost);
            int     totalItems = list.Count;
            int     lowStock   = list.Count(x => x.BalanceQty > 0 && x.BalanceQty <= x.ReorderLevel && x.ReorderLevel > 0);
            int     zeroStock  = list.Count(x => x.BalanceQty <= 0);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Current Stock Summary")
                                    .Bold().FontSize(14).FontColor("#1e3a5f");
                                c.Item().Text($"Godown: {godownLabel}   |   As at {DateTime.Today:dd-MMM-yyyy}")
                                    .FontSize(8).FontColor("#6b7280");
                            });
                            r.ConstantItem(160).Column(c =>
                            {
                                c.Item().Row(sr =>
                                {
                                    sr.RelativeItem().Background("#dbeafe").Padding(5).AlignCenter().Column(sc =>
                                    {
                                        sc.Item().Text("Total Items").FontSize(6.5f).FontColor("#1e40af");
                                        sc.Item().Text(totalItems.ToString()).Bold().FontSize(10).FontColor("#1d4ed8");
                                    });
                                    sr.RelativeItem().Background("#fef9c3").Padding(5).AlignCenter().Column(sc =>
                                    {
                                        sc.Item().Text("Low Stock").FontSize(6.5f).FontColor("#92400e");
                                        sc.Item().Text(lowStock.ToString()).Bold().FontSize(10).FontColor("#a16207");
                                    });
                                    sr.RelativeItem().Background("#fee2e2").Padding(5).AlignCenter().Column(sc =>
                                    {
                                        sc.Item().Text("Zero Stock").FontSize(6.5f).FontColor("#991b1b");
                                        sc.Item().Text(zeroStock.ToString()).Bold().FontSize(10).FontColor("#b91c1c");
                                    });
                                });
                                c.Item().Background("#dcfce7").Padding(4).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("Total Value").FontSize(6.5f).FontColor("#166534");
                                    sc.Item().Text($"₹{totalVal:N2}").Bold().FontSize(10).FontColor("#15803d");
                                });
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#93c5fd");
                    });

                    page.Content().PaddingTop(8).Table(tbl =>
                    {
                        tbl.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3); // Item
                            c.RelativeColumn(2); // Godown
                            c.RelativeColumn();  // Qty
                            c.ConstantColumn(40);   // UOM
                            c.RelativeColumn();  // Avg Cost
                            c.RelativeColumn();  // Stock Value
                            c.RelativeColumn();  // Reorder
                            c.ConstantColumn(45);   // Status
                        });

                        tbl.Header(h =>
                        {
                            foreach (var lbl in new[] { "Item", "Godown", "Current Qty", "UOM", "Avg Cost (₹)", "Stock Value (₹)", "Reorder", "Status" })
                            {
                                h.Cell().Background("#1e3a5f").Padding(5)
                                    .Text(lbl).Bold().FontSize(7).FontColor("#ffffff");
                            }
                        });

                        uint rowNum = 0;
                        foreach (var cat in grouped)
                        {
                            decimal catVal = cat.Sum(x => x.BalanceQty * x.AverageCost);

                            // Category header
                            tbl.Cell().ColumnSpan(6).Background("#eff6ff").BorderTop(1).BorderColor("#93c5fd")
                                .Padding(4)
                                .Text($"{cat.Key}  ({cat.Count()} {(cat.Count() == 1 ? "item" : "items")})")
                                .Bold().FontSize(7.5f).FontColor("#1e3a5f");
                            tbl.Cell().ColumnSpan(2).Background("#eff6ff").BorderTop(1).BorderColor("#93c5fd")
                                .Padding(4).AlignRight()
                                .Text($"Value: ₹{catVal:N0}").Bold().FontSize(7).FontColor("#1d4ed8");

                            foreach (var s in cat.OrderBy(x => x.ItemName))
                            {
                                rowNum++;
                                bool isZero = s.BalanceQty <= 0;
                                bool isLow  = !isZero && s.BalanceQty <= s.ReorderLevel && s.ReorderLevel > 0;
                                string bg   = isZero ? "#fff1f2" : isLow ? "#fffbeb" : (rowNum % 2 == 0 ? "#f9fafb" : "#ffffff");
                                decimal sv  = s.BalanceQty * s.AverageCost;
                                string  status = isZero ? "OUT" : isLow ? "LOW" : "OK";
                                string  statusColor = isZero ? "#b91c1c" : isLow ? "#a16207" : "#15803d";

                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb").Padding(4).Column(c =>
                                {
                                    c.Item().Text(s.ItemName ?? "").Bold().FontSize(7.5f).FontColor("#1e3a5f");
                                    if (!string.IsNullOrEmpty(s.ItemCode))
                                        c.Item().Text(s.ItemCode).FontSize(6f).FontColor("#9ca3af");
                                });
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).Text(s.GodownName ?? "").FontSize(7).FontColor("#0369a1");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.BalanceQty:0.###}").Bold().FontSize(7.5f)
                                    .FontColor(isZero ? "#b91c1c" : isLow ? "#a16207" : "#1e3a5f");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text(s.BaseUOMCode ?? "").FontSize(7).FontColor("#6b7280");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.AverageCost:N2}").FontSize(7).FontColor("#374151");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{sv:N2}").Bold().FontSize(7).FontColor("#374151");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text(s.ReorderLevel.HasValue && s.ReorderLevel > 0 ? $"{s.ReorderLevel:0.###}" : "—")
                                    .FontSize(7).FontColor("#6b7280");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignCenter()
                                    .Text(status).Bold().FontSize(7).FontColor(statusColor);
                            }
                        }

                        // Grand total
                        tbl.Cell().ColumnSpan(5).Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"GRAND TOTAL — {totalItems} Items").Bold().FontSize(7.5f).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"₹{totalVal:N2}").Bold().FontSize(7.5f).FontColor("#ffffff");
                        tbl.Cell().ColumnSpan(2).Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4);
                    });

                    page.Footer().AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ").FontSize(7).FontColor("#9ca3af");
                            x.CurrentPageNumber().FontSize(7).FontColor("#9ca3af");
                            x.Span(" of ").FontSize(7).FontColor("#9ca3af");
                            x.TotalPages().FontSize(7).FontColor("#9ca3af");
                        });
                });
            });

            var bytes = pdf.GeneratePdf();
            string fileName = $"CurrentStock_{DateTime.Today:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", fileName);
        }

        /// <summary>Returns the Id of the IsMainGodown godown for the given branch, or null if none.</summary>
        private int? GetMainGodownId(int branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT TOP 1 Id FROM dbo.Godowns WHERE BranchId=@bid AND IsMainGodown=1 AND IsActive=1", con);
                cmd.Parameters.AddWithValue("@bid", branchId);
                var val = cmd.ExecuteScalar();
                return val != null && val != DBNull.Value ? (int?)Convert.ToInt32(val) : null;
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLOSING STOCK REPORT
        // ═══════════════════════════════════════════════════════════════

        public IActionResult ClosingStock(DateTime? asOfDate, int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            asOfDate ??= DateTime.Today;
            bool isMain = IsMainBranchById(branchId.Value);
            var list = new List<ClosingStockReportItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetClosingStockReport", con)
                    { CommandType = CommandType.StoredProcedure };
                // Main-branch admin: pass NULL to see all branches; non-main: own branch only
                cmd.Parameters.AddWithValue("@BranchId", isMain ? DBNull.Value : (object)branchId.Value);
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
            ViewBag.AsOfDate       = asOfDate.Value.ToString("yyyy-MM-dd");
            ViewBag.GodownId       = godownId ?? 0;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ── Closing Stock → Export PDF ────────────────────────────────────────
        [HttpGet]
        public IActionResult ClosingStockPdf(DateTime? asOfDate, int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            asOfDate ??= DateTime.Today;
            bool isMain = IsMainBranchById(branchId.Value);
            var list = new List<ClosingStockReportItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetClosingStockReport", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", isMain ? DBNull.Value : (object)branchId.Value);
                cmd.Parameters.AddWithValue("@AsOfDate", asOfDate.Value.Date);
                cmd.Parameters.AddWithValue("@GodownId", godownId.HasValue && godownId.Value > 0
                    ? (object)godownId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read()) list.Add(MapClosingStock(rdr));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            string godownLabel = "All Godowns";
            if (godownId.HasValue && godownId.Value > 0 && list.Count > 0)
                godownLabel = list[0].GodownName ?? "Selected Godown";

            var grouped = list
                .OrderBy(x => x.ItemCategory ?? "Uncategorised")
                .ThenBy(x => x.ItemName)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.ItemCategory) ? "Uncategorised" : x.ItemCategory)
                .ToList();

            decimal totalVal  = list.Sum(x => x.ClosingValue);
            int     zeroStock = list.Count(x => x.ClosingQty <= 0);
            int     negStock  = list.Count(x => x.ClosingQty < 0);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Closing Stock Report")
                                    .Bold().FontSize(14).FontColor("#1e3a5f");
                                c.Item().Text($"As at {asOfDate.Value:dd-MMM-yyyy}   |   Godown: {godownLabel}")
                                    .FontSize(8).FontColor("#6b7280");
                            });
                            r.ConstantItem(180).Row(sr =>
                            {
                                sr.RelativeItem().Background("#dbeafe").Padding(5).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("Total Items").FontSize(6.5f).FontColor("#1e40af");
                                    sc.Item().Text(list.Count.ToString()).Bold().FontSize(10).FontColor("#1d4ed8");
                                });
                                sr.RelativeItem().Background("#fee2e2").Padding(5).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("Zero/Neg").FontSize(6.5f).FontColor("#991b1b");
                                    sc.Item().Text(zeroStock.ToString()).Bold().FontSize(10).FontColor("#b91c1c");
                                });
                                sr.RelativeItem().Background("#dcfce7").Padding(5).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("Total Value").FontSize(6.5f).FontColor("#166534");
                                    sc.Item().Text($"₹{totalVal:N0}").Bold().FontSize(9).FontColor("#15803d");
                                });
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#93c5fd");
                    });

                    page.Content().PaddingTop(8).Table(tbl =>
                    {
                        tbl.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3); // Item
                            c.RelativeColumn(2); // Godown
                            c.RelativeColumn();  // Opening
                            c.RelativeColumn();  // In Qty
                            c.RelativeColumn();  // Out Qty
                            c.RelativeColumn();  // Closing Qty
                            c.RelativeColumn();  // Avg Cost
                            c.RelativeColumn();  // Closing Value
                        });

                        tbl.Header(h =>
                        {
                            foreach (var lbl in new[] { "Item", "Godown", "Opening Qty", "In Qty", "Out Qty", "Closing Qty", "Avg Cost (₹)", "Closing Value (₹)" })
                            {
                                h.Cell().Background("#1e3a5f").Padding(5)
                                    .Text(lbl).Bold().FontSize(7).FontColor("#ffffff");
                            }
                        });

                        uint rowNum = 0;
                        foreach (var cat in grouped)
                        {
                            decimal catVal = cat.Sum(x => x.ClosingValue);

                            // Category header
                            tbl.Cell().ColumnSpan(6).Background("#eff6ff").BorderTop(1).BorderColor("#93c5fd")
                                .Padding(4)
                                .Text($"{cat.Key}  ({cat.Count()} {(cat.Count() == 1 ? "item" : "items")})")
                                .Bold().FontSize(7.5f).FontColor("#1e3a5f");
                            tbl.Cell().ColumnSpan(2).Background("#eff6ff").BorderTop(1).BorderColor("#93c5fd")
                                .Padding(4).AlignRight()
                                .Text($"Value: ₹{catVal:N2}").Bold().FontSize(7).FontColor("#1d4ed8");

                            foreach (var s in cat.OrderBy(x => x.ItemName))
                            {
                                rowNum++;
                                bool isNeg  = s.ClosingQty < 0;
                                bool isZero = s.ClosingQty == 0;
                                string bg   = isNeg ? "#fff1f2" : isZero ? "#fef9c3" : (rowNum % 2 == 0 ? "#f9fafb" : "#ffffff");
                                decimal inQty  = s.PurchaseQty + s.TransferInQty;
                                decimal outQty = s.TransferOutQty + s.DamageQty + s.SaleQty;

                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).Column(c =>
                                    {
                                        c.Item().Text(s.ItemName ?? "").Bold().FontSize(7.5f).FontColor("#1e3a5f");
                                        if (!string.IsNullOrEmpty(s.ItemCode))
                                            c.Item().Text(s.ItemCode).FontSize(6f).FontColor("#9ca3af");
                                    });
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).Text(s.GodownName ?? "").FontSize(7).FontColor("#0369a1");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.OpeningQty:0.###}").FontSize(7).FontColor("#6b7280");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text(inQty > 0 ? $"+{inQty:0.###}" : "—")
                                    .FontSize(7).FontColor(inQty > 0 ? "#15803d" : "#d1d5db");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text(outQty > 0 ? $"-{outQty:0.###}" : "—")
                                    .FontSize(7).FontColor(outQty > 0 ? "#b91c1c" : "#d1d5db");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.ClosingQty:0.###}").Bold().FontSize(7.5f)
                                    .FontColor(isNeg ? "#b91c1c" : isZero ? "#a16207" : "#1e3a5f");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.AverageCost:N2}").FontSize(7).FontColor("#374151");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.ClosingValue:N2}").Bold().FontSize(7).FontColor("#374151");
                            }
                        }

                        // Grand total
                        tbl.Cell().ColumnSpan(7).Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"GRAND TOTAL — {list.Count} Items").Bold().FontSize(7.5f).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"₹{totalVal:N2}").Bold().FontSize(7.5f).FontColor("#ffffff");
                    });

                    page.Footer().AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ").FontSize(7).FontColor("#9ca3af");
                            x.CurrentPageNumber().FontSize(7).FontColor("#9ca3af");
                            x.Span(" of ").FontSize(7).FontColor("#9ca3af");
                            x.TotalPages().FontSize(7).FontColor("#9ca3af");
                        });
                });
            });

            var bytes = pdf.GeneratePdf();
            string fileName = $"ClosingStock_{asOfDate.Value:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", fileName);
        }

        // ═══════════════════════════════════════════════════════════════
        //  STOCK VALUATION
        // ═══════════════════════════════════════════════════════════════

        public IActionResult StockValuation(int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            bool isMain = IsMainBranchById(branchId.Value);
            var list = new List<StockValuationItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockValuationReport", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", isMain ? DBNull.Value : (object)branchId.Value);
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
            ViewBag.GodownId       = godownId ?? 0;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ── Stock Valuation → Export PDF ──────────────────────────────────────
        [HttpGet]
        public IActionResult StockValuationPdf(int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            bool isMain = IsMainBranchById(branchId.Value);
            var list = new List<StockValuationItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockValuationReport", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", isMain ? DBNull.Value : (object)branchId.Value);
                cmd.Parameters.AddWithValue("@GodownId", godownId.HasValue && godownId.Value > 0
                    ? (object)godownId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read()) list.Add(MapValuation(rdr));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            string godownLabel = "All Godowns";
            if (godownId.HasValue && godownId.Value > 0 && list.Count > 0)
                godownLabel = list[0].GodownName ?? "Selected Godown";

            var grouped = list
                .OrderBy(x => x.ItemCategory ?? "Uncategorised")
                .ThenBy(x => x.ItemName)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.ItemCategory) ? "Uncategorised" : x.ItemCategory)
                .ToList();

            decimal totalVal    = list.Sum(x => x.StockValue ?? 0);
            int     positiveQty = list.Count(x => x.BalanceQty > 0);
            int     zeroQty     = list.Count(x => x.BalanceQty <= 0);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Stock Valuation Report")
                                    .Bold().FontSize(14).FontColor("#1e3a5f");
                                c.Item().Text($"Godown: {godownLabel}   |   As at {DateTime.Today:dd-MMM-yyyy}   |   Method: Weighted Average")
                                    .FontSize(8).FontColor("#6b7280");
                            });
                            r.ConstantItem(180).Row(sr =>
                            {
                                sr.RelativeItem().Background("#dbeafe").Padding(5).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("Total Items").FontSize(6.5f).FontColor("#1e40af");
                                    sc.Item().Text(list.Count.ToString()).Bold().FontSize(10).FontColor("#1d4ed8");
                                });
                                sr.RelativeItem().Background("#dcfce7").Padding(5).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("With Stock").FontSize(6.5f).FontColor("#166534");
                                    sc.Item().Text(positiveQty.ToString()).Bold().FontSize(10).FontColor("#15803d");
                                });
                                sr.RelativeItem().Background("#f0fdf4").Padding(5).AlignCenter().Column(sc =>
                                {
                                    sc.Item().Text("Total Value").FontSize(6.5f).FontColor("#166534");
                                    sc.Item().Text($"₹{totalVal:N0}").Bold().FontSize(9).FontColor("#15803d");
                                });
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#93c5fd");
                    });

                    page.Content().PaddingTop(8).Table(tbl =>
                    {
                        tbl.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3); // Item
                            c.RelativeColumn(2); // Godown
                            c.RelativeColumn();  // Current Qty
                            c.ConstantColumn(40);// UOM
                            c.RelativeColumn();  // Avg Cost
                            c.RelativeColumn();  // Stock Value
                        });

                        tbl.Header(h =>
                        {
                            foreach (var lbl in new[] { "Item", "Godown", "Current Qty", "UOM", "Wtd Avg Cost (₹)", "Stock Value (₹)" })
                            {
                                h.Cell().Background("#1e3a5f").Padding(5)
                                    .Text(lbl).Bold().FontSize(7).FontColor("#ffffff");
                            }
                        });

                        uint rowNum = 0;
                        foreach (var cat in grouped)
                        {
                            decimal catVal = cat.Sum(x => x.StockValue ?? 0);

                            tbl.Cell().ColumnSpan(4).Background("#eff6ff").BorderTop(1).BorderColor("#93c5fd")
                                .Padding(4)
                                .Text($"{cat.Key}  ({cat.Count()} {(cat.Count() == 1 ? "item" : "items")})") 
                                .Bold().FontSize(7.5f).FontColor("#1e3a5f");
                            tbl.Cell().ColumnSpan(2).Background("#eff6ff").BorderTop(1).BorderColor("#93c5fd")
                                .Padding(4).AlignRight()
                                .Text($"Value: ₹{catVal:N2}").Bold().FontSize(7).FontColor("#1d4ed8");

                            foreach (var s in cat.OrderBy(x => x.ItemName))
                            {
                                rowNum++;
                                bool isZero = s.BalanceQty <= 0;
                                string bg   = isZero ? "#fef9c3" : (rowNum % 2 == 0 ? "#f9fafb" : "#ffffff");
                                decimal sv  = s.StockValue ?? 0;

                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).Column(c =>
                                    {
                                        c.Item().Text(s.ItemName ?? "").Bold().FontSize(7.5f).FontColor("#1e3a5f");
                                        if (!string.IsNullOrEmpty(s.ItemCode))
                                            c.Item().Text(s.ItemCode).FontSize(6f).FontColor("#9ca3af");
                                    });
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).Text(s.GodownName ?? "").FontSize(7).FontColor("#0369a1");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.BalanceQty:0.###}").Bold().FontSize(7.5f)
                                    .FontColor(isZero ? "#b91c1c" : "#1e3a5f");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text(s.UOMCode ?? "").FontSize(7).FontColor("#6b7280");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{s.AverageCost:N4}").FontSize(7).FontColor("#374151");
                                tbl.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e5e7eb")
                                    .Padding(4).AlignRight()
                                    .Text($"{sv:N2}").Bold().FontSize(7)
                                    .FontColor(isZero ? "#9ca3af" : "#374151");
                            }
                        }

                        // Grand total
                        tbl.Cell().ColumnSpan(5).Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"GRAND TOTAL — {list.Count} Items").Bold().FontSize(7.5f).FontColor("#ffffff");
                        tbl.Cell().Background("#1e3a5f").BorderTop(2).BorderColor("#60a5fa")
                            .Padding(4).AlignRight()
                            .Text($"₹{totalVal:N2}").Bold().FontSize(7.5f).FontColor("#ffffff");
                    });

                    page.Footer().AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ").FontSize(7).FontColor("#9ca3af");
                            x.CurrentPageNumber().FontSize(7).FontColor("#9ca3af");
                            x.Span(" of ").FontSize(7).FontColor("#9ca3af");
                            x.TotalPages().FontSize(7).FontColor("#9ca3af");
                        });
                });
            });

            var bytes = pdf.GeneratePdf();
            string fileName = $"StockValuation_{DateTime.Today:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", fileName);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PURCHASE REGISTER
        // ═══════════════════════════════════════════════════════════════

        public async Task<IActionResult> PurchaseRegister(DateTime? fromDate, DateTime? toDate, int? supplierId, string? branchIds = null)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            toDate   ??= DateTime.Today;

            bool isMainBranchAdmin = await IsMainBranchAdminAsync(branchId);
            List<SelectListItem> allBranches = isMainBranchAdmin ? await LoadAllBranchesAsync() : new();

            // Parse selected branch ids from comma-separated or repeated query param
            List<int> selectedBranchIds = new();
            if (isMainBranchAdmin && !string.IsNullOrWhiteSpace(branchIds))
            {
                foreach (var part in branchIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out var bid)) selectedBranchIds.Add(bid);
            }

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
                cmd.Parameters.AddWithValue("@SupplierId", supplierId.HasValue && supplierId.Value > 0 ? (object)supplierId.Value : DBNull.Value);
                if (isMainBranchAdmin && selectedBranchIds.Count > 0)
                    cmd.Parameters.AddWithValue("@BranchIds", string.Join(",", selectedBranchIds));
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapPurchaseRegister(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            LoadDropdowns();
            ViewBag.FromDate          = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate            = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.SupplierId        = supplierId;
            ViewBag.ActiveBranchId    = branchId.Value;
            ViewBag.IsMainBranchAdmin = isMainBranchAdmin;
            ViewBag.AllBranches       = allBranches;
            ViewBag.SelectedBranchIds = selectedBranchIds;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PURCHASE REGISTER DETAILS (line-item drill-down)
        // ═══════════════════════════════════════════════════════════════

        public IActionResult PurchaseRegisterDetails(DateTime? fromDate, DateTime? toDate, int? supplierId, int? grnId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            toDate   ??= DateTime.Today;

            var list = new List<PurchaseRegisterDetailItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetPurchaseRegisterDetails", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId",   branchId.Value);
                cmd.Parameters.AddWithValue("@FromDate",   fromDate.Value.Date);
                cmd.Parameters.AddWithValue("@ToDate",     toDate.Value.Date);
                cmd.Parameters.AddWithValue("@SupplierId", supplierId.HasValue && supplierId.Value > 0 ? (object)supplierId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@GrnId",      grnId.HasValue && grnId.Value > 0 ? (object)grnId.Value : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapPurchaseRegisterDetail(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            // Supplier dropdown
            var suppliers = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id, PartyName FROM dbo.Parties WHERE IsActive=1 AND PartyType='Supplier' ORDER BY PartyName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    suppliers.Add(new SelectListItem { Value = rdr.GetInt32(0).ToString(), Text = rdr.GetString(1) });
            }

            ViewBag.Suppliers  = suppliers;
            ViewBag.FromDate   = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate     = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.SupplierId = supplierId ?? 0;
            ViewBag.GrnId      = grnId ?? 0;
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  TRANSFER REGISTER
        // ═══════════════════════════════════════════════════════════════

        public IActionResult TransferRegister(DateTime? fromDate, DateTime? toDate, int? godownId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            fromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            toDate   ??= DateTime.Today;
            godownId ??= 0;

            var list = new List<TransferRegisterItem>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();

                // Load godown dropdown (all godowns involved in transfers for this branch)
                using (var gdCmd = new SqlCommand(
                    @"SELECT DISTINCT g.Id, g.GodownName + ' (' + b.BranchName + ')' AS Label
                      FROM dbo.Godowns g
                      JOIN dbo.Branches b ON b.BranchId = g.BranchId
                      WHERE g.IsActive = 1
                      ORDER BY Label", con))
                using (var gdRdr = gdCmd.ExecuteReader())
                {
                    var items = new List<SelectListItem>();
                    while (gdRdr.Read())
                        items.Add(new SelectListItem(gdRdr.GetString(1), gdRdr.GetInt32(0).ToString()));
                    ViewBag.Godowns  = new SelectList(items, "Value", "Text");
                    ViewBag.SelGodown = godownId.Value;
                }

                using var cmd = new SqlCommand("usp_GetTransferRegister", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@FromDate", fromDate.Value.Date);
                cmd.Parameters.AddWithValue("@ToDate",   toDate.Value.Date);
                cmd.Parameters.AddWithValue("@GodownId", godownId.Value == 0 ? DBNull.Value : (object)godownId.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapTransferRegister(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            ViewBag.DateFrom = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.DateTo   = toDate.Value.ToString("yyyy-MM-dd");
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
        private bool IsMainBranchById(int branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT TOP 1 ISNULL(Is_MainBranch,0) FROM dbo.Branches WHERE BranchId=@bid", con);
                cmd.Parameters.AddWithValue("@bid", branchId);
                var val = cmd.ExecuteScalar();
                return val != null && val != DBNull.Value && Convert.ToBoolean(val);
            }
            catch { return false; }
        }

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

        [HttpGet]
        public IActionResult QuickStockCheck(string? q)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue || string.IsNullOrWhiteSpace(q)) return Json(new List<object>());

            var result = new List<object>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(@"
                    SELECT TOP 8
                        i.Id            AS ItemId,
                        i.IngredientsName AS ItemName,
                        ISNULL(i.Code,'') AS ItemCode,
                        ISNULL(cs.BalanceQty, 0) AS BalanceQty,
                        ISNULL(cs.AverageCost, 0) AS AverageCost,
                        ISNULL(u.UOMCode,'') AS UOMCode,
                        ISNULL(g.GodownName,'') AS GodownName,
                        ISNULL(i.ReorderLevel, 0) AS ReorderLevel,
                        -- Days remaining based on last 30-day avg consumption
                        CASE WHEN ISNULL((SELECT SUM(sl.OutQuantity)/30.0 FROM dbo.StockLedger sl
                                          WHERE sl.BranchId=@BranchId AND sl.ItemId=i.Id
                                            AND sl.TransactionType='SaleConsumption'
                                            AND sl.TransactionDate >= DATEADD(DAY,-30,CAST(GETDATE() AS DATE))),0) > 0
                             THEN CAST(ISNULL(cs.BalanceQty,0) /
                                  (SELECT SUM(sl.OutQuantity)/30.0 FROM dbo.StockLedger sl
                                   WHERE sl.BranchId=@BranchId AND sl.ItemId=i.Id
                                     AND sl.TransactionType='SaleConsumption'
                                     AND sl.TransactionDate >= DATEADD(DAY,-30,CAST(GETDATE() AS DATE))) AS INT)
                             ELSE NULL END AS DaysRemaining,
                        (SELECT TOP 1 gm.GRNDate FROM dbo.GRNDetails gd
                         INNER JOIN dbo.GRNMaster gm ON gm.GRNId=gd.GRNId
                         WHERE gd.ItemId=i.Id AND gm.BranchId=@BranchId AND gm.Status='Posted'
                         ORDER BY gm.GRNDate DESC) AS LastPurchaseDate
                    FROM dbo.Ingredients i
                    LEFT JOIN dbo.CurrentStock cs ON cs.ItemId=i.Id AND cs.BranchId=@BranchId
                    LEFT JOIN dbo.Godowns g ON g.Id=cs.GodownId
                    LEFT JOIN dbo.UomMaster u ON u.UOMId=i.PurchaseUOMId
                    WHERE i.IsActive=1 AND i.IngredientsName LIKE '%' + @q + '%'
                    ORDER BY i.IngredientsName", con);
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@q", q.Trim());
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    result.Add(new {
                        itemId         = GetInt(rdr, "ItemId"),
                        itemName       = GetStr(rdr, "ItemName"),
                        itemCode       = GetStr(rdr, "ItemCode"),
                        balanceQty     = GetDecimal(rdr, "BalanceQty"),
                        averageCost    = GetDecimal(rdr, "AverageCost"),
                        uomCode        = GetStr(rdr, "UOMCode"),
                        godownName     = GetStr(rdr, "GodownName"),
                        reorderLevel   = GetDecimal(rdr, "ReorderLevel"),
                        daysRemaining  = rdr.IsDBNull(rdr.GetOrdinal("DaysRemaining")) ? (int?)null : (int?)GetInt(rdr, "DaysRemaining"),
                        lastPurchaseDate = rdr.IsDBNull(rdr.GetOrdinal("LastPurchaseDate")) ? null : rdr.GetDateTime(rdr.GetOrdinal("LastPurchaseDate")).ToString("dd MMM yyyy")
                    });
            }
            catch { }
            return Json(result);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void LoadDropdowns()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return;

            bool isMain = IsMainBranchById(branchId.Value);

            // ── Godowns ─────────────────────────────────────────────────────
            // Main branch  → all active godowns across all branches (prefixed with branch name)
            // Other branch → only this branch's godowns
            var godowns = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                string godownSql = isMain
                    ? @"SELECT g.Id, b.BranchName, g.GodownName, g.IsMainGodown
                          FROM dbo.Godowns g
                          INNER JOIN dbo.Branches b ON b.BranchId = g.BranchId
                         WHERE g.IsActive = 1
                         ORDER BY b.BranchName, g.IsMainGodown DESC, g.GodownName"
                    : @"SELECT g.Id, b.BranchName, g.GodownName, g.IsMainGodown
                          FROM dbo.Godowns g
                          INNER JOIN dbo.Branches b ON b.BranchId = g.BranchId
                         WHERE g.BranchId = @bid AND g.IsActive = 1
                         ORDER BY g.IsMainGodown DESC, g.GodownName";

                using var cmd = new SqlCommand(godownSql, con);
                if (!isMain) cmd.Parameters.AddWithValue("@bid", branchId.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var bName = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                    var gName = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
                    var label = isMain ? $"{bName} – {gName}" : gName;
                    godowns.Add(new SelectListItem { Value = rdr.GetInt32(0).ToString(), Text = label });
                }
            }
            ViewBag.Godowns = godowns;

            // ── Items (Ingredients) ─────────────────────────────────────────
            var items = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT i.Id, i.IngredientsName + ISNULL(' [' + i.Code + ']','') AS DisplayName " +
                    "FROM dbo.Ingredients i WHERE i.IsActive = 1 ORDER BY i.IngredientsName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    items.Add(new SelectListItem
                    {
                        Value = rdr.GetInt32(0).ToString(),
                        Text  = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
                    });
            }
            ViewBag.Items = items;

            // ── UOMs ────────────────────────────────────────────────────────
            var uoms = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT UOMId, UOMCode + ' - ' + UOMName FROM dbo.UomMaster WHERE IsActive = 1 ORDER BY UOMName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    uoms.Add(new SelectListItem { Value = rdr.GetInt32(0).ToString(), Text = rdr.GetString(1) });
            }
            ViewBag.UOMs = uoms;

            // ── Suppliers ───────────────────────────────────────────────────
            var suppliers = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id, PartyName FROM dbo.Parties WHERE IsActive=1 AND PartyType='Supplier' ORDER BY PartyName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    suppliers.Add(new SelectListItem { Value = rdr.GetInt32(0).ToString(), Text = rdr.GetString(1) });
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
            ItemCategory    = GetStr(rdr, "ItemCategory"),
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
            ItemCategory  = GetStr(rdr, "ItemCategory"),
            UOMCode       = GetStr(rdr, "UOMCode"),
            BalanceQty    = GetDecimal(rdr, "BalanceQty"),
            AverageCost   = GetDecimal(rdr, "AverageCost"),
            StockValue    = GetDecimal(rdr, "StockValue")
        };

        private static PurchaseRegisterItem MapPurchaseRegister(SqlDataReader rdr) => new()
        {
            GRNId          = GetInt(rdr, "GRNId"),
            GRNNumber      = GetStr(rdr, "GRNNumber"),
            GRNDate        = rdr.GetDateTime(rdr.GetOrdinal("GRNDate")),
            InvoiceNo      = GetStr(rdr, "InvoiceNo"),
            SupplierName   = GetStr(rdr, "SupplierName"),
            GodownName     = GetStr(rdr, "GodownName"),
            BranchName     = GetStr(rdr, "BranchName"),
            SubTotal       = GetDecimal(rdr, "SubTotal"),
            TotalGSTAmount = GetDecimal(rdr, "TotalGSTAmount"),
            TotalAmount    = GetDecimal(rdr, "TotalAmount"),
            PONumber       = GetStr(rdr, "PONumber")
        };

        private static PurchaseRegisterDetailItem MapPurchaseRegisterDetail(SqlDataReader rdr) => new()
        {
            GRNId          = GetInt(rdr, "GRNId"),
            GRNNumber      = GetStr(rdr, "GRNNumber"),
            GRNDate        = rdr.GetDateTime(rdr.GetOrdinal("GRNDate")),
            InvoiceNo      = GetStr(rdr, "InvoiceNo"),
            InvoiceDate    = rdr.IsDBNull(rdr.GetOrdinal("InvoiceDate")) ? (DateTime?)null : rdr.GetDateTime(rdr.GetOrdinal("InvoiceDate")),
            SupplierName   = GetStr(rdr, "SupplierName"),
            GodownName     = GetStr(rdr, "GodownName"),
            GSTType        = GetStr(rdr, "GSTType"),
            PONumber       = GetStr(rdr, "PONumber"),
            GRNDetailId    = GetInt(rdr, "GRNDetailId"),
            ItemName       = GetStr(rdr, "ItemName"),
            ItemCode       = GetStr(rdr, "ItemCode"),
            UOMCode        = GetStr(rdr, "UOMCode"),
            ReceivedQty    = GetDecimal(rdr, "ReceivedQty"),
            AcceptedQty    = GetDecimal(rdr, "AcceptedQty"),
            UnitRate       = GetDecimal(rdr, "UnitRate"),
            TaxableAmount  = GetDecimal(rdr, "TaxableAmount"),
            GSTPercent     = GetDecimal(rdr, "GSTPercent"),
            IGSTPercent    = GetDecimal(rdr, "IGSTPercent"),
            IGSTAmount     = GetDecimal(rdr, "IGSTAmount"),
            CGSTPercent    = GetDecimal(rdr, "CGSTPercent"),
            CGSTAmount     = GetDecimal(rdr, "CGSTAmount"),
            SGSTPercent    = GetDecimal(rdr, "SGSTPercent"),
            SGSTAmount     = GetDecimal(rdr, "SGSTAmount"),
            TotalGSTAmount = GetDecimal(rdr, "TotalGSTAmount"),
            LineAmount     = GetDecimal(rdr, "LineAmount"),
            LineRemarks    = GetStr(rdr, "LineRemarks")
        };

        private static TransferRegisterItem MapTransferRegister(SqlDataReader rdr) => new()
        {
            Direction       = rdr["Direction"]       as string,
            FromBranchName  = rdr["FromBranchName"]  as string,
            ToBranchName    = rdr["ToBranchName"]    as string,
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
