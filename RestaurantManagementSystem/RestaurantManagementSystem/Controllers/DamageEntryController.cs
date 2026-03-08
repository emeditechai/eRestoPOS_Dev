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
    /// Damage / Wastage Entry Controller.
    /// All data via stored procedures.
    /// </summary>
    public class DamageEntryController : Controller
    {
        private readonly string _connectionString;

        public DamageEntryController(IConfiguration configuration)
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

            var list = new List<DamageEntryHeader>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetDamageEntryList", con)
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

            DamageEntryHeader model;
            if (id.HasValue && id.Value > 0)
                model = LoadById(id.Value) ?? new DamageEntryHeader { BranchId = branchId.Value };
            else
                model = new DamageEntryHeader { BranchId = branchId.Value, DamageDate = DateTime.Today, DamageType = "Damage" };

            LoadViewBag(branchId.Value);
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Form(DamageEntryHeader model, string linesJson)
        {
            var branchId = ActiveBranchId();
            if (!branchId.HasValue) return NoBranch();
            model.BranchId = branchId.Value;

            if (model.GodownId == 0)
            {
                ModelState.AddModelError("GodownId", "Please select a godown.");
                LoadViewBag(branchId.Value);
                return View(model);
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_SaveDamageEntry", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@DamageId",    model.DamageId);
                cmd.Parameters.AddWithValue("@BranchId",    model.BranchId);
                cmd.Parameters.AddWithValue("@GodownId",    model.GodownId);
                cmd.Parameters.AddWithValue("@DamageDate",  model.DamageDate.Date);
                cmd.Parameters.AddWithValue("@DamageType",  model.DamageType ?? "Damage");
                cmd.Parameters.AddWithValue("@Remarks",     (object?)model.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserId",      DBNull.Value);
                cmd.Parameters.AddWithValue("@DetailsJson", string.IsNullOrWhiteSpace(linesJson) ? (object)DBNull.Value : linesJson);
                cmd.ExecuteScalar();
                TempData["SuccessMessage"] = "Damage entry saved.";
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
                using var cmd = new SqlCommand("usp_PostDamageEntry", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@DamageId", id);
                cmd.Parameters.AddWithValue("@UserId",   DBNull.Value);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Damage entry posted – stock updated.";
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

        // ── HELPERS ──────────────────────────────────────────────────────────

        private DamageEntryHeader? LoadById(int id)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand("usp_GetDamageEntryById", con)
                    { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@DamageId", id);
                using var rdr = cmd.ExecuteReader();
                DamageEntryHeader? h = null;
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
            var godowns = new List<object>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                using var cmd = new SqlCommand(
                    "SELECT Id AS GodownId, GodownName FROM dbo.Godowns WHERE BranchId = @bid AND IsActive = 1 ORDER BY GodownName", con);
                cmd.Parameters.AddWithValue("@bid", branchId);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    godowns.Add(new { value = GetInt(rdr, "GodownId"), text = GetStr(rdr, "GodownName") });
            }
            ViewBag.Godowns = godowns;
            ViewBag.ActiveBranchId = branchId;
        }

        private static DamageEntryHeader MapHeader(SqlDataReader r) => new()
        {
            DamageId     = GetInt(r, "DamageId"),
            DamageNumber = GetStr(r, "DamageNumber"),
            BranchId     = GetInt(r, "BranchId"),
            GodownId     = GetInt(r, "GodownId"),
            DamageDate   = r.GetDateTime(r.GetOrdinal("DamageDate")),
            DamageType   = GetStr(r, "DamageType") ?? "Damage",
            Remarks      = GetStr(r, "Remarks"),
            Status       = GetStr(r, "Status") ?? "Draft",
            TotalQty     = GetDecimal(r, "TotalQty"),
            TotalValue   = GetDecimal(r, "TotalValue"),
            GodownName   = GetStr(r, "GodownName"),
            CreatedAt    = TryGetDate(r, "CreatedAt")
        };

        private static DamageEntryLine MapLine(SqlDataReader r) => new()
        {
            DamageDetailId = GetInt(r, "DamageDetailId"),
            DamageId       = GetInt(r, "DamageId"),
            ItemId         = GetInt(r, "ItemId"),
            UOMId          = GetInt(r, "UOMId"),
            Quantity       = GetDecimal(r, "Quantity"),
            UnitCost       = GetDecimal(r, "UnitCost"),
            Reason         = GetStr(r, "Reason"),
            ItemName       = GetStr(r, "ItemName"),
            UOMCode        = GetStr(r, "UOMCode"),
            UOMName        = GetStr(r, "UOMName")
        };

        private static int GetInt(SqlDataReader r, string col)       { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); } catch { return 0; } }
        private static decimal GetDecimal(SqlDataReader r, string col){ try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0m : Convert.ToDecimal(r.GetValue(o)); } catch { return 0m; } }
        private static string? GetStr(SqlDataReader r, string col)   { try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetString(o); } catch { return null; } }
        private static DateTime? TryGetDate(SqlDataReader r, string col){ try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetDateTime(o); } catch { return null; } }
    }
}
