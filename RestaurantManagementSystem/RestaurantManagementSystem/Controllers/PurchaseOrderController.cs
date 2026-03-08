using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    /// Purchase Order Controller.
    /// If PurchaseOnlyFromMainGodown flag = true, GodownId is auto-set to main godown.
    /// All data via stored procedures.
    /// </summary>
    public class PurchaseOrderController : Controller
    {
        private readonly string _connectionString;

        public PurchaseOrderController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        private int? ActiveBranchId() => User.GetActiveBranchId();

        private IActionResult NoBranch()
        {
            TempData["ErrorMessage"] = "Please select an active branch first.";
            return RedirectToAction("Index", "Home");
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIST
        // ═══════════════════════════════════════════════════════════════

        public IActionResult Index(string? status, DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<PurchaseOrderHeader>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetPurchaseOrderList", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@Status",   string.IsNullOrEmpty(status) ? (object)DBNull.Value : status);
                cmd.Parameters.AddWithValue("@FromDate", fromDate.HasValue ? (object)fromDate.Value.Date : DBNull.Value);
                cmd.Parameters.AddWithValue("@ToDate",   toDate.HasValue   ? (object)toDate.Value.Date   : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapPOHeader(rdr));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            ViewBag.Status   = status;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate   = toDate?.ToString("yyyy-MM-dd");
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CREATE / EDIT FORM
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult Form(int? id)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            PurchaseOrderHeader model;
            if (id.HasValue && id.Value > 0)
            {
                model = LoadPOById(id.Value) ?? new PurchaseOrderHeader { BranchId = branchId.Value };
                // Serialize existing lines for edit-mode pre-population
                ViewBag.ExistingLinesJson = JsonSerializer.Serialize(model.Lines.Select(l => new {
                    itemId     = l.ItemId,
                    itemName   = l.ItemName ?? "",
                    uomId      = l.UOMId,
                    uomName    = l.UOMName ?? "",
                    orderedQty = l.OrderedQty,
                    unitRate   = l.UnitRate,
                    gstPercent = l.GSTPercent,
                    remarks    = l.Remarks ?? ""
                }));
            }
            else
            {
                model = new PurchaseOrderHeader { BranchId = branchId.Value, PODate = DateTime.Today, GSTType = "Exclusive" };
            }

            LoadViewBag(branchId.Value);
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Form(PurchaseOrderHeader model, string linesJson)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            // Enforce main godown if flag is set
            if (GetPurchaseOnlyFromMainFlag(branchId.Value))
            {
                var mainId = GetMainGodownId(branchId.Value);
                if (mainId.HasValue)
                {
                    model.GodownId = mainId.Value;
                }
                else
                {
                    TempData["ErrorMessage"] = "No main godown configured for this branch. Please set up a Main Godown first.";
                    LoadViewBag(branchId.Value);
                    return View(model);
                }
            }

            if (model.GodownId == 0)
            {
                ModelState.AddModelError("GodownId", "Please select a godown.");
                LoadViewBag(branchId.Value);
                return View(model);
            }
            if (model.SupplierId == 0)
            {
                ModelState.AddModelError("SupplierId", "Please select a supplier.");
                LoadViewBag(branchId.Value);
                return View(model);
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_SavePurchaseOrder", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@POId",           model.POId);
                cmd.Parameters.AddWithValue("@BranchId",       model.BranchId);
                cmd.Parameters.AddWithValue("@GodownId",       model.GodownId);
                cmd.Parameters.AddWithValue("@SupplierId",     model.SupplierId);
                cmd.Parameters.AddWithValue("@PODate",         model.PODate.Date);
                cmd.Parameters.AddWithValue("@ExpectedDate",   model.ExpectedDate.HasValue ? (object)model.ExpectedDate.Value.Date : DBNull.Value);
                cmd.Parameters.AddWithValue("@GSTType",        model.GSTType ?? "Exclusive");
                cmd.Parameters.AddWithValue("@PaymentTerms",   (object?)model.PaymentTerms ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Remarks",        (object?)model.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SubTotal",       model.SubTotal);
                cmd.Parameters.AddWithValue("@TotalGSTAmount", model.TotalGSTAmount);
                cmd.Parameters.AddWithValue("@TotalAmount",    model.TotalAmount);
                cmd.Parameters.AddWithValue("@UserId",         DBNull.Value);
                cmd.Parameters.AddWithValue("@DetailsJson",    string.IsNullOrWhiteSpace(linesJson) ? (object)DBNull.Value : linesJson);

                var newId = cmd.ExecuteScalar();
                TempData["SuccessMessage"] = "Purchase Order saved successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                LoadViewBag(branchId.Value);
                return View(model);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  DETAILS VIEW
        // ═══════════════════════════════════════════════════════════════

        public IActionResult Details(int id)
        {
            var model = LoadPOById(id);
            if (model == null) return RedirectToAction(nameof(Index));

            var branchId = ActiveBranchId();
            LoadViewBag(branchId ?? model.BranchId);
            return View(model);
        }

        // ═══════════════════════════════════════════════════════════════
        //  APPROVE / CANCEL
        // ═══════════════════════════════════════════════════════════════

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Approve(int id)
        {
            CallSP("usp_ApprovePurchaseOrder", ("@POId", id), ("@UserId", DBNull.Value));
            TempData["SuccessMessage"] = "Purchase Order approved.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Cancel(int id)
        {
            CallSP("usp_CancelPurchaseOrder", ("@POId", id), ("@UserId", DBNull.Value));
            TempData["SuccessMessage"] = "Purchase Order cancelled.";
            return RedirectToAction(nameof(Index));
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private bool GetPurchaseOnlyFromMainFlag(int branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetInventoryParameters", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId);
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    var ord = rdr.GetOrdinal("PurchaseOnlyFromMainGodown");
                    return !rdr.IsDBNull(ord) && rdr.GetBoolean(ord);
                }
            }
            catch { }
            return false;
        }

        private int? GetMainGodownId(int branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT TOP 1 Id AS GodownId FROM dbo.Godowns WHERE BranchId = @BranchId AND IsMainGodown = 1 AND IsActive = 1",
                    con);
                cmd.Parameters.AddWithValue("@BranchId", branchId);
                var val = cmd.ExecuteScalar();
                if (val != null && val != DBNull.Value) return Convert.ToInt32(val);
            }
            catch { }
            return null;
        }

        private PurchaseOrderHeader? LoadPOById(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetPurchaseOrderById", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@POId", id);
                using var rdr = cmd.ExecuteReader();

                PurchaseOrderHeader? h = null;
                if (rdr.Read()) h = MapPOHeader(rdr);

                if (h != null && rdr.NextResult())
                    while (rdr.Read())
                        h.Lines.Add(MapPOLine(rdr));

                return h;
            }
            catch { return null; }
        }

        private bool IsMainBranch(int branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Is_MainBranch FROM dbo.Branches WHERE BranchId = @bid", con);
                cmd.Parameters.AddWithValue("@bid", branchId);
                var val = cmd.ExecuteScalar();
                return val != null && val != DBNull.Value && Convert.ToBoolean(val);
            }
            catch { return false; }
        }

        private void LoadViewBag(int branchId)
        {
            bool purchaseOnlyMain = GetPurchaseOnlyFromMainFlag(branchId);
            bool isMainBranch     = IsMainBranch(branchId);

            // ── Godowns ──────────────────────────────────────────────────────
            // Main branch: all godowns across all branches (display: BranchName - GodownName)
            // Non-main branch: only own branch godowns (display: BranchName - GodownName)
            var godownItems = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                string sql = isMainBranch
                    ? @"SELECT g.Id, b.BranchName, g.GodownName, g.IsMainGodown
                          FROM dbo.Godowns g
                          INNER JOIN dbo.Branches b ON b.BranchId = g.BranchId
                         WHERE g.IsActive = 1
                         ORDER BY b.BranchName, g.GodownName"
                    : @"SELECT g.Id, b.BranchName, g.GodownName, g.IsMainGodown
                          FROM dbo.Godowns g
                          INNER JOIN dbo.Branches b ON b.BranchId = g.BranchId
                         WHERE g.BranchId = @bid AND g.IsActive = 1
                         ORDER BY g.GodownName";

                using var cmd = new SqlCommand(sql, con);
                if (!isMainBranch) cmd.Parameters.AddWithValue("@bid", branchId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var isMain     = !rdr.IsDBNull(3) && rdr.GetBoolean(3);
                    if (purchaseOnlyMain && !isMain) continue;
                    var branchName = rdr.GetString(1);
                    var godownName = rdr.GetString(2);
                    godownItems.Add(new SelectListItem
                    {
                        Value = rdr.GetInt32(0).ToString(),
                        Text  = $"{branchName} - {godownName}"
                    });
                }
            }
            ViewBag.Godowns          = new SelectList(godownItems, "Value", "Text");
            ViewBag.PurchaseOnlyMain = purchaseOnlyMain;
            ViewBag.MainGodownId     = GetMainGodownId(branchId);

            // ── Suppliers ────────────────────────────────────────────────────
            var supplierItems = new List<SelectListItem>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id, PartyName FROM dbo.Parties WHERE IsActive = 1 AND PartyType IN ('Supplier','Vendor') ORDER BY PartyName", con);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    supplierItems.Add(new SelectListItem
                    {
                        Value = rdr.GetInt32(0).ToString(),
                        Text  = rdr.GetString(1)
                    });
            }
            ViewBag.Suppliers      = new SelectList(supplierItems, "Value", "Text");
            ViewBag.ActiveBranchId = branchId;
        }

        private void CallSP(string spName, params (string name, object value)[] parameters)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(spName, con) { CommandType = CommandType.StoredProcedure };
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        private static PurchaseOrderHeader MapPOHeader(SqlDataReader r) => new()
        {
            POId           = GetInt(r, "POId"),
            PONumber       = GetStr(r, "PONumber"),
            BranchId       = GetInt(r, "BranchId"),
            GodownId       = GetInt(r, "GodownId"),
            SupplierId     = GetInt(r, "SupplierId"),
            PODate         = r.GetDateTime(r.GetOrdinal("PODate")),
            GSTType        = GetStr(r, "GSTType") ?? "Exclusive",
            PaymentTerms   = GetStr(r, "PaymentTerms"),
            Remarks        = GetStr(r, "Remarks"),
            Status         = GetStr(r, "Status") ?? "Draft",
            SubTotal       = GetDecimal(r, "SubTotal"),
            TotalGSTAmount = GetDecimal(r, "TotalGSTAmount"),
            TotalAmount    = GetDecimal(r, "TotalAmount"),
            GodownName     = GetStr(r, "GodownName"),
            SupplierName   = GetStr(r, "SupplierName"),
            LineCount      = TryGetInt(r, "LineCount"),
            CreatedAt      = TryGetDate(r, "CreatedAt")
        };

        private static PurchaseOrderLine MapPOLine(SqlDataReader r) => new()
        {
            PODetailId    = GetInt(r, "PODetailId"),
            POId          = GetInt(r, "POId"),
            ItemId        = GetInt(r, "ItemId"),
            UOMId         = GetInt(r, "UOMId"),
            OrderedQty    = GetDecimal(r, "OrderedQty"),
            ReceivedQty   = GetDecimal(r, "ReceivedQty"),
            UnitRate      = GetDecimal(r, "UnitRate"),
            GSTPercent    = GetDecimal(r, "GSTPercent"),
            CGSTPercent   = GetDecimal(r, "CGSTPercent"),
            SGSTPercent   = GetDecimal(r, "SGSTPercent"),
            IGSTPercent   = GetDecimal(r, "IGSTPercent"),
            TaxableAmount = GetDecimal(r, "TaxableAmount"),
            CGSTAmount    = GetDecimal(r, "CGSTAmount"),
            SGSTAmount    = GetDecimal(r, "SGSTAmount"),
            IGSTAmount    = GetDecimal(r, "IGSTAmount"),
            Remarks       = GetStr(r, "Remarks"),
            ItemName      = GetStr(r, "ItemName"),
            ItemCode      = GetStr(r, "ItemCode"),
            UOMCode       = GetStr(r, "UOMCode"),
            UOMName       = GetStr(r, "UOMName")
        };

        private static int GetInt(SqlDataReader r, string col)
        {
            try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; }
        }
        private static int TryGetInt(SqlDataReader r, string col)
        {
            try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; }
        }
        private static decimal GetDecimal(SqlDataReader r, string col)
        {
            try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0m : Convert.ToDecimal(r.GetValue(o)); } catch { return 0m; }
        }
        private static string? GetStr(SqlDataReader r, string col)
        {
            try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetString(o); } catch { return null; }
        }
        private static DateTime? TryGetDate(SqlDataReader r, string col)
        {
            try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetDateTime(o); } catch { return null; }
        }
    }
}
