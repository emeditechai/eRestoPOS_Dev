using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using System;
using System.Collections.Generic;

namespace RestaurantManagementSystem.Controllers
{
    public class BankController : Controller
    {
        private readonly string _connectionString;

        public BankController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // List
        public IActionResult Index()
        {
            var banks = new List<BankViewModel>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_GetAll", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var idObj = rdr.GetValue(0);
                                byte idVal = Convert.ToByte(idObj);
                                var name = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                                var status = !rdr.IsDBNull(2) && Convert.ToBoolean(rdr.GetValue(2));

                                banks.Add(new BankViewModel
                                {
                                    Id = idVal,
                                    BankName = name,
                                    Status = status
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Try fallback to direct table select if stored proc is missing or fails
                try
                {
                    using (var conn = new SqlConnection(_connectionString))
                    {
                        conn.Open();
                        using (var cmd = new SqlCommand("SELECT ID, bankname, Status FROM dbo.bank_Master ORDER BY bankname", conn))
                        {
                            using (var rdr = cmd.ExecuteReader())
                            {
                                while (rdr.Read())
                                {
                                    var idObj = rdr.GetValue(0);
                                    byte idVal = Convert.ToByte(idObj);
                                    var name = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                                    var status = !rdr.IsDBNull(2) && Convert.ToBoolean(rdr.GetValue(2));

                                    banks.Add(new BankViewModel
                                    {
                                        Id = idVal,
                                        BankName = name,
                                        Status = status
                                    });
                                }
                            }
                        }
                    }
                    TempData["WarningMessage"] = "Loaded banks using fallback SELECT (stored proc missing or failed).";
                }
                catch (Exception inner)
                {
                    TempData["ErrorMessage"] = "Error loading banks: " + ex.Message + " (fallback also failed: " + inner.Message + ")";
                }
            }

            TempData["InfoMessage"] = $"Loaded {banks.Count} banks.";
            return View(banks);
        }

        // Create GET
        public IActionResult Create()
        {
            return View(new BankViewModel { Status = true });
        }

        // Create POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(BankViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_Create", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@BankName", model.BankName ?? string.Empty);
                        cmd.Parameters.AddWithValue("@Status", model.Status);
                        var newId = cmd.ExecuteScalar();
                        TempData["SuccessMessage"] = "Bank created successfully.";
                        return RedirectToAction("Index");
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error creating bank: " + ex.Message);
                return View(model);
            }
        }

        // Edit GET
        public IActionResult Edit(byte id)
        {
            var model = new BankViewModel();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_GetById", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Id", id);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                // Use GetValue + Convert to handle tinyint/int differences
                                var idVal = rdr.GetValue(0);
                                model.Id = Convert.ToByte(idVal);
                                model.BankName = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                                model.Status = !rdr.IsDBNull(2) && Convert.ToBoolean(rdr.GetValue(2));

                                TempData["InfoMessage"] = $"Loaded bank id={model.Id}, name={model.BankName}";
                            }
                            else
                            {
                                return NotFound();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading bank: " + ex.Message;
                return RedirectToAction("Index");
            }

            return View(model);
        }

        // Edit POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(BankViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_Update", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Id", model.Id);
                        cmd.Parameters.AddWithValue("@BankName", model.BankName ?? string.Empty);
                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "Bank updated successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error updating bank: " + ex.Message);
                return View(model);
            }
        }

        // Delete POST (soft-deactivate)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(byte id)
        {
            // Deactivate (soft-delete) via status toggle
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_SetStatus", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.Parameters.AddWithValue("@Status", 0);
                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "Bank deactivated.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error updating bank status: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // Toggle status via AJAX (activate/deactivate)
        [HttpPost]
        public JsonResult ToggleStatus(byte id)
        {
            try
            {
                // Get current status
                bool currentStatus = false;
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_GetById", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Id", id);
                        using (var rdr = cmd.ExecuteReader())
                        {
                                if (rdr.Read())
                                {
                                    currentStatus = !rdr.IsDBNull(2) && Convert.ToBoolean(rdr.GetValue(2));
                                }
                            else
                            {
                                return Json(new { success = false, message = "Bank not found." });
                            }
                        }
                    }
                }

                // Toggle
                var newStatus = currentStatus ? 0 : 1;
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("usp_BankMaster_SetStatus", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.Parameters.AddWithValue("@Status", newStatus);
                        cmd.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Status updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error updating status: " + ex.Message });
            }
        }
    }
}
