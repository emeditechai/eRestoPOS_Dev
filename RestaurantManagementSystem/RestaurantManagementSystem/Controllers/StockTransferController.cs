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
    /// Stock Transfer Controller – handles both Main→Branch and Inter-Godown transfers.
    /// All data via stored procedures.
    /// </summary>
    public class StockTransferController : Controller
    {
        private readonly string _connectionString;

        public StockTransferController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        private int? ActiveBranchId() => User.GetActiveBranchId();
        private IActionResult NoBranch()
        {
            TempData["ErrorMessage"] = "Please select an active branch first.";
            return RedirectToAction("Index", "Home");
        }

        // ── LIST ─────────────────────────────────────────────────────────────

        public IActionResult Index(string? status, DateTime? fromDate, DateTime? toDate)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            var list = new List<StockTransferHeader>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockTransferList", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                cmd.Parameters.AddWithValue("@Status",   string.IsNullOrEmpty(status) ? (object)DBNull.Value : status);
                cmd.Parameters.AddWithValue("@FromDate", fromDate.HasValue ? (object)fromDate.Value.Date : DBNull.Value);
                cmd.Parameters.AddWithValue("@ToDate",   toDate.HasValue   ? (object)toDate.Value.Date   : DBNull.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(MapHeader(rdr));
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
        public IActionResult Form(int? id)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();

            StockTransferHeader model;
            if (id.HasValue && id.Value > 0)
                model = LoadById(id.Value) ?? new StockTransferHeader { BranchId = branchId.Value };
            else
                model = new StockTransferHeader { BranchId = branchId.Value, TransferDate = DateTime.Today, PriceMode = "AverageCost" };

            LoadViewBag(branchId.Value);
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Form(StockTransferHeader model, string linesJson)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            if (model.FromGodownId == 0 || model.ToGodownId == 0)
            {
                ModelState.AddModelError("", "Please select both source and destination godowns.");
                LoadViewBag(branchId.Value);
                return View(model);
            }
            if (model.FromGodownId == model.ToGodownId)
            {
                ModelState.AddModelError("", "Source and destination godowns cannot be the same.");
                LoadViewBag(branchId.Value);
                return View(model);
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_SaveStockTransfer", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@TransferId",    model.TransferId);
                cmd.Parameters.AddWithValue("@BranchId",      model.BranchId);
                cmd.Parameters.AddWithValue("@FromGodownId",  model.FromGodownId);
                cmd.Parameters.AddWithValue("@ToGodownId",    model.ToGodownId);
                cmd.Parameters.AddWithValue("@TransferDate",  model.TransferDate.Date);
                cmd.Parameters.AddWithValue("@TransferType",  model.TransferType ?? "Internal");
                cmd.Parameters.AddWithValue("@PriceMode",     model.PriceMode ?? "AverageCost");
                cmd.Parameters.AddWithValue("@Remarks",       (object?)model.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserId",        DBNull.Value);
                cmd.Parameters.AddWithValue("@DetailsJson",   string.IsNullOrWhiteSpace(linesJson) ? (object)DBNull.Value : linesJson);
                cmd.ExecuteScalar();
                TempData["SuccessMessage"] = "Stock transfer saved.";
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
                using var cmd = new SqlCommand("usp_PostStockTransfer", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@TransferId", id);
                cmd.Parameters.AddWithValue("@UserId",     DBNull.Value);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Transfer posted – stock updated.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Post failed: " + ex.Message;
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        public IActionResult Details(int id)
        {
            var model = LoadById(id);
            if (model == null) return RedirectToAction(nameof(Index));
            LoadViewBag(model.BranchId);
            return View(model);
        }

        /// <summary>Returns items that have stock > 0 in the given godown, used by the line-item dropdown.</summary>
        [HttpGet]
        public IActionResult GetItemsWithStockJson(int godownId)
        {
            if (godownId <= 0) return Json(new List<object>());
            var result = new List<object>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetItemsWithStockByGodown", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@GodownId", godownId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    result.Add(new
                    {
                        id          = GetInt(rdr, "ItemId"),
                        name        = GetStr(rdr, "ItemName") ?? "",
                        code        = GetStr(rdr, "ItemCode") ?? "",
                        balanceQty  = GetDecimal(rdr, "BalanceQty"),
                        avgCost     = GetDecimal(rdr, "AverageCost"),
                        uomId       = GetInt(rdr, "UOMId"),
                        uomCode     = GetStr(rdr, "UOMCode") ?? "",
                        uomName     = GetStr(rdr, "UOMName") ?? ""
                    });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
            return Json(result);
        }

        // ── HELPERS ──────────────────────────────────────────────────────────

        private StockTransferHeader? LoadById(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetStockTransferById", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@TransferId", id);
                using var rdr = cmd.ExecuteReader();
                StockTransferHeader? h = null;
                if (rdr.Read()) h = MapHeader(rdr);
                if (h != null && rdr.NextResult())
                    while (rdr.Read())
                        h.Lines.Add(MapLine(rdr));
                return h;
            }
            catch { return null; }
        }

        private void LoadViewBag(int branchId)
        {
            bool isMainBranch = false;
            int  selfGodownId = 0;
            var  fromGodowns  = new List<RestaurantManagementSystem.Models.GodownDropdownItem>();
            var  toGodowns    = new List<RestaurantManagementSystem.Models.GodownDropdownItem>();

            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();

                // ── From Godowns ──────────────────────────────────────────
                using (var cmd = new SqlCommand("usp_GetTransferFromGodowns", con)
                    { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var item = new RestaurantManagementSystem.Models.GodownDropdownItem
                        {
                            GodownId   = GetInt(rdr, "GodownId"),
                            GodownName = GetStr(rdr, "GodownName") ?? "",
                            BranchName = GetStr(rdr, "BranchName") ?? "",
                            BranchId   = GetInt(rdr, "BranchId"),
                            IsDisabled = GetBool(rdr, "IsDisabled")
                        };
                        isMainBranch = GetBool(rdr, "IsLoginBranchMain");
                        if (item.IsDisabled) selfGodownId = item.GodownId;
                        fromGodowns.Add(item);
                    }
                }

                // ── To Godowns ────────────────────────────────────────────
                using (var cmd2 = new SqlCommand("usp_GetTransferToGodowns", con)
                    { CommandType = CommandType.StoredProcedure })
                {
                    cmd2.Parameters.AddWithValue("@BranchId", branchId);
                    using var rdr2 = cmd2.ExecuteReader();
                    while (rdr2.Read())
                        toGodowns.Add(new RestaurantManagementSystem.Models.GodownDropdownItem
                        {
                            GodownId   = GetInt(rdr2, "GodownId"),
                            GodownName = GetStr(rdr2, "GodownName") ?? "",
                            BranchName = GetStr(rdr2, "BranchName") ?? "",
                            BranchId   = GetInt(rdr2, "BranchId"),
                            IsDisabled = GetBool(rdr2, "IsDisabled")
                        });
                }
            }

            ViewBag.FromGodowns    = fromGodowns;
            ViewBag.ToGodowns      = toGodowns;
            ViewBag.IsMainBranch   = isMainBranch;
            ViewBag.SelfGodownId   = selfGodownId;   // pre-selected & disabled for non-main
            ViewBag.ActiveBranchId = branchId;
        }

        private static StockTransferHeader MapHeader(SqlDataReader r) => new()
        {
            TransferId      = GetInt(r, "TransferId"),
            TransferNumber  = GetStr(r, "TransferNumber"),
            BranchId        = GetInt(r, "BranchId"),
            FromGodownId    = GetInt(r, "FromGodownId"),
            ToGodownId      = GetInt(r, "ToGodownId"),
            TransferDate    = r.GetDateTime(r.GetOrdinal("TransferDate")),
            TransferType    = GetStr(r, "TransferType") ?? "Internal",
            PriceMode       = GetStr(r, "PriceMode") ?? "AverageCost",
            Remarks         = GetStr(r, "Remarks"),
            Status          = GetStr(r, "Status") ?? "Draft",
            TotalQty        = GetDecimal(r, "TotalQty"),
            TotalValue      = GetDecimal(r, "TotalValue"),
            FromGodownName  = GetStr(r, "FromGodownName"),
            ToGodownName    = GetStr(r, "ToGodownName"),
            LineCount       = TryGetInt(r, "LineCount"),
            CreatedAt       = TryGetDate(r, "CreatedAt")
        };

        private static StockTransferLine MapLine(SqlDataReader r) => new()
        {
            TransferDetailId = GetInt(r, "TransferDetailId"),
            TransferId       = GetInt(r, "TransferId"),
            ItemId           = GetInt(r, "ItemId"),
            UOMId            = GetInt(r, "UOMId"),
            Quantity         = GetDecimal(r, "Quantity"),
            UnitCost         = GetDecimal(r, "UnitCost"),
            Remarks          = GetStr(r, "Remarks"),
            ItemName         = GetStr(r, "ItemName"),
            UOMCode          = GetStr(r, "UOMCode"),
            UOMName          = GetStr(r, "UOMName")
        };

        private static int GetInt(SqlDataReader r, string col)       { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; } }
        private static int TryGetInt(SqlDataReader r, string col)    { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; } }
        private static decimal GetDecimal(SqlDataReader r, string col){ try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0m : Convert.ToDecimal(r.GetValue(o)); } catch { return 0m; } }
        private static bool GetBool(SqlDataReader r, string col)     { try { var o = r.GetOrdinal(col); return !r.IsDBNull(o) && Convert.ToBoolean(r.GetValue(o)); } catch { return false; } }
        private static string? GetStr(SqlDataReader r, string col)   { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetString(o); } catch { return null; } }
        private static DateTime? TryGetDate(SqlDataReader r, string col){ try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetDateTime(o); } catch { return null; } }
    }
}
