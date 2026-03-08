using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Data;

namespace RestaurantManagementSystem.Controllers
{
    /// <summary>
    /// GRN (Goods Receipt Note) Controller.
    /// GRN links to an approved Purchase Order and adds stock to the ledger when posted.
    /// All data via stored procedures.
    /// </summary>
    public class GRNController : Controller
    {
        private readonly string _connectionString;

        public GRNController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        private int? ActiveBranchId() => User.GetActiveBranchId();
        private IActionResult NoBranch()
        {
            TempData["ErrorMessage"] = "Please select an active branch first.";
            return RedirectToAction("Index", "Home");
        }

        // ── LIST ────────────────────────────────────────────────────────────

        public IActionResult Index(string? status, DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<GRNHeader>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetGRNList", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@Status",   string.IsNullOrEmpty(status) ? (object)DBNull.Value : status);
                cmd.Parameters.AddWithValue("@FromDate", fromDate.HasValue ? (object)fromDate.Value.Date : DBNull.Value);
                cmd.Parameters.AddWithValue("@ToDate",   toDate.HasValue   ? (object)toDate.Value.Date   : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapGRNHeader(rdr));
            }
            catch (Exception ex) { TempData["ErrorMessage"] = ex.Message; }

            ViewBag.Status   = status;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate   = toDate?.ToString("yyyy-MM-dd");
            ViewBag.ActiveBranchId = branchId.Value;
            return View(list);
        }

        // ── FORM ─────────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Form(int? id, int? poId)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            GRNHeader model;
            if (id.HasValue && id.Value > 0)
            {
                model = LoadGRNById(id.Value) ?? new GRNHeader { BranchId = branchId.Value };
            }
            else
            {
                model = new GRNHeader { BranchId = branchId.Value, GRNDate = DateTime.Today, GSTType = "Exclusive" };
                if (poId.HasValue && poId.Value > 0)
                {
                    model.POId = poId.Value;
                    model.Lines = LoadPODetailsForGRN(poId.Value);
                    // Load PO godown / supplier
                    var poHdr  = LoadPOForGRN(poId.Value);
                    if (poHdr != null)
                    {
                        model.GodownId    = poHdr.GodownId;
                        model.SupplierId  = poHdr.SupplierId;
                        model.GodownName  = poHdr.GodownName;
                        model.SupplierName= poHdr.SupplierName;
                        model.PONumber    = poHdr.PONumber;
                    }
                }
            }

            LoadViewBag(branchId.Value);

            // Embed line data as inline JSON so the view renders instantly — no AJAX on load
            bool isEdit = id.HasValue && id.Value > 0;
            if (isEdit && model.Lines.Count > 0)
            {
                ViewBag.ExistingLinesJson = System.Text.Json.JsonSerializer.Serialize(
                    model.Lines.Select(l => new {
                        grnDetailId = l.GRNDetailId,
                        poDetailId  = l.PODetailId ?? 0,
                        itemId      = l.ItemId,
                        uomId       = l.UOMId,
                        itemName    = l.ItemName ?? "",
                        uomCode     = l.UOMCode ?? "",
                        orderedQty  = l.OrderedQty,
                        pendingQty  = l.AcceptedQty,
                        receivedQty = l.ReceivedQty,
                        rejectedQty = l.RejectedQty,
                        acceptedQty = l.AcceptedQty,
                        unitRate    = l.UnitRate,
                        gstPercent  = l.GSTPercent
                    }));
            }
            else if (!isEdit && model.Lines.Count > 0)
            {
                // New GRN pre-populated from a PO
                ViewBag.PreloadedLinesJson = System.Text.Json.JsonSerializer.Serialize(
                    model.Lines.Select(l => new {
                        poDetailId  = l.PODetailId ?? 0,
                        itemId      = l.ItemId,
                        uomId       = l.UOMId,
                        itemName    = l.ItemName ?? "",
                        uomCode     = l.UOMCode ?? "",
                        orderedQty  = l.OrderedQty,
                        pendingQty  = l.AcceptedQty,
                        receivedQty = (decimal)0,
                        rejectedQty = (decimal)0,
                        acceptedQty = l.AcceptedQty,
                        unitRate    = l.UnitRate,
                        gstPercent  = l.GSTPercent
                    }));
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult GetPOList()
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return Json(new List<object>());

            var result = new List<object>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand("usp_GetPOForGRN", con)
                { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                result.Add(new
                {
                    id          = GetInt(rdr, "POId"),
                    poNumber    = GetStr(rdr, "PONumber"),
                    supplierName= GetStr(rdr, "SupplierName"),
                    godownName  = GetStr(rdr, "GodownName"),
                    godownId    = GetInt(rdr, "GodownId"),
                    supplierId  = GetInt(rdr, "SupplierId")
                });
            return Json(result);
        }

        [HttpGet]
        public IActionResult GetPODetails(int poId)
        {
            var lines = LoadPODetailsForGRN(poId);
            return Json(lines);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Form(GRNHeader model, string linesJson)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            if (model.POId == 0)
            {
                ModelState.AddModelError("POId", "Please select a Purchase Order.");
                LoadViewBag(branchId.Value);
                return View(model);
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_SaveGRN", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@GRNId",           model.GRNId);
                cmd.Parameters.AddWithValue("@BranchId",        model.BranchId);
                cmd.Parameters.AddWithValue("@POId",            model.POId);
                cmd.Parameters.AddWithValue("@GodownId",        model.GodownId);
                cmd.Parameters.AddWithValue("@SupplierId",      model.SupplierId);
                cmd.Parameters.AddWithValue("@GRNDate",         model.GRNDate.Date);
                cmd.Parameters.AddWithValue("@InvoiceNo",       (object?)model.InvoiceNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InvoiceDate",     model.InvoiceDate.HasValue ? (object)model.InvoiceDate.Value.Date : DBNull.Value);
                cmd.Parameters.AddWithValue("@GSTType",         model.GSTType ?? "Exclusive");
                cmd.Parameters.AddWithValue("@Remarks",         (object?)model.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SubTotal",        model.SubTotal);
                cmd.Parameters.AddWithValue("@TotalGSTAmount",  model.TotalGSTAmount);
                cmd.Parameters.AddWithValue("@TotalAmount",     model.TotalAmount);
                cmd.Parameters.AddWithValue("@UserId",          DBNull.Value);
                cmd.Parameters.AddWithValue("@DetailsJson",     string.IsNullOrWhiteSpace(linesJson) ? (object)DBNull.Value : linesJson);
                cmd.ExecuteScalar();
                TempData["SuccessMessage"] = "GRN saved successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                LoadViewBag(branchId.Value);
                return View(model);
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Post(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_PostGRN", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@GRNId",  id);
                cmd.Parameters.AddWithValue("@UserId", DBNull.Value);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "GRN posted – stock updated.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Post failed: " + ex.Message;
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        public IActionResult Details(int id)
        {
            var model = LoadGRNById(id);
            if (model == null) return RedirectToAction(nameof(Index));
            LoadViewBag(model.BranchId);
            return View(model);
        }

        // ── HELPERS ──────────────────────────────────────────────────────────

        private GRNHeader? LoadGRNById(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetGRNById", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@GRNId", id);
                using var rdr = cmd.ExecuteReader();
                GRNHeader? h = null;
                if (rdr.Read()) h = MapGRNHeader(rdr);
                if (h != null && rdr.NextResult())
                    while (rdr.Read())
                        h.Lines.Add(MapGRNLine(rdr));
                return h;
            }
            catch { return null; }
        }

        private PurchaseOrderHeader? LoadPOForGRN(int poId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetPOForGRN", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", ActiveBranchId() ?? 0);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    if (GetInt(rdr, "POId") == poId)
                        return new PurchaseOrderHeader
                        {
                            POId         = poId,
                            PONumber     = GetStr(rdr, "PONumber"),
                            GodownId     = GetInt(rdr, "GodownId"),
                            GodownName   = GetStr(rdr, "GodownName"),
                            SupplierId   = GetInt(rdr, "SupplierId"),
                            SupplierName = GetStr(rdr, "SupplierName")
                        };
                }
                return null;
            }
            catch { return null; }
        }

        private List<GRNLine> LoadPODetailsForGRN(int poId)
        {
            var lines = new List<GRNLine>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetPODetailsForGRN", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@POId", poId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    lines.Add(new GRNLine
                    {
                        PODetailId   = GetInt(rdr, "PODetailId"),
                        ItemId       = GetInt(rdr, "ItemId"),
                        UOMId        = GetInt(rdr, "UOMId"),
                        OrderedQty   = GetDecimal(rdr, "OrderedQty"),
                        ReceivedQty  = GetDecimal(rdr, "ReceivedQty"),
                        AcceptedQty  = GetDecimal(rdr, "PendingQty"),
                        UnitRate     = GetDecimal(rdr, "UnitRate"),
                        GSTPercent   = GetDecimal(rdr, "GSTPercent"),
                        ItemName     = GetStr(rdr, "ItemName"),
                        ItemCode     = GetStr(rdr, "ItemCode"),
                        UOMCode      = GetStr(rdr, "UOMCode"),
                        UOMName      = GetStr(rdr, "UOMName")
                    });
            }
            catch { }
            return lines;
        }

        private void LoadViewBag(int branchId)
        {
            var pos = new List<object>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand("usp_GetPOForGRN", con) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    pos.Add(new {
                        id           = GetInt(rdr, "POId"),
                        poNumber     = GetStr(rdr, "PONumber") ?? "",
                        supplierName = GetStr(rdr, "SupplierName") ?? "",
                        godownName   = GetStr(rdr, "GodownName") ?? "",
                        godownId     = GetInt(rdr, "GodownId"),
                        supplierId   = GetInt(rdr, "SupplierId")
                    });
            }
            // Serialised inline so the view needs ZERO AJAX calls on initial load
            ViewBag.POListJson = System.Text.Json.JsonSerializer.Serialize(pos);
            ViewBag.ActiveBranchId = branchId;
        }

        private static GRNHeader MapGRNHeader(SqlDataReader r) => new()
        {
            GRNId          = GetInt(r, "GRNId"),
            GRNNumber      = GetStr(r, "GRNNumber"),
            BranchId       = GetInt(r, "BranchId"),
            POId           = GetInt(r, "POId"),
            GodownId       = GetInt(r, "GodownId"),
            SupplierId     = GetInt(r, "SupplierId"),
            GRNDate        = r.GetDateTime(r.GetOrdinal("GRNDate")),
            InvoiceNo      = GetStr(r, "InvoiceNo"),
            GSTType        = GetStr(r, "GSTType") ?? "Exclusive",
            Remarks        = GetStr(r, "Remarks"),
            SubTotal       = GetDecimal(r, "SubTotal"),
            TotalGSTAmount = GetDecimal(r, "TotalGSTAmount"),
            TotalAmount    = GetDecimal(r, "TotalAmount"),
            Status         = GetStr(r, "Status") ?? "Draft",
            GodownName     = GetStr(r, "GodownName"),
            SupplierName   = GetStr(r, "SupplierName"),
            PONumber       = GetStr(r, "PONumber"),
            LineCount      = TryGetInt(r, "LineCount"),
            CreatedAt      = TryGetDate(r, "CreatedAt")
        };

        private static GRNLine MapGRNLine(SqlDataReader r) => new()
        {
            GRNDetailId  = GetInt(r, "GRNDetailId"),
            GRNId        = GetInt(r, "GRNId"),
            PODetailId   = TryGetInt(r, "PODetailId"),
            ItemId       = GetInt(r, "ItemId"),
            UOMId        = GetInt(r, "UOMId"),
            OrderedQty   = GetDecimal(r, "OrderedQty"),
            ReceivedQty  = GetDecimal(r, "ReceivedQty"),
            RejectedQty  = GetDecimal(r, "RejectedQty"),
            AcceptedQty  = GetDecimal(r, "AcceptedQty"),
            UnitRate     = GetDecimal(r, "UnitRate"),
            GSTPercent   = GetDecimal(r, "GSTPercent"),
            Remarks      = GetStr(r, "Remarks"),
            ItemName     = GetStr(r, "ItemName"),
            UOMCode      = GetStr(r, "UOMCode"),
            UOMName      = GetStr(r, "UOMName")
        };

        private static int GetInt(SqlDataReader r, string col)  { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; } }
        private static int TryGetInt(SqlDataReader r, string col) { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; } }
        private static decimal GetDecimal(SqlDataReader r, string col) { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0m : Convert.ToDecimal(r.GetValue(o)); } catch { return 0m; } }
        private static string? GetStr(SqlDataReader r, string col) { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetString(o); } catch { return null; } }
        private static DateTime? TryGetDate(SqlDataReader r, string col) { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetDateTime(o); } catch { return null; } }
    }
}
