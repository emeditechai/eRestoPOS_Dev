using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    public class FeedbackController : Controller
    {
        private readonly string _connectionString;
        public FeedbackController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        private int? ResolveBranchId(int? branchId)
        {
            if (branchId.HasValue && branchId.Value > 0)
            {
                return branchId.Value;
            }

            return User.GetActiveBranchId();
        }

        private async Task<bool> HasColumnAsync(SqlConnection connection, string tableName, string columnName)
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(1)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName", connection);
                cmd.Parameters.AddWithValue("@TableName", tableName);
                cmd.Parameters.AddWithValue("@ColumnName", columnName);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task LoadRestaurantHeaderAsync(int? branchId)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                var hasSettingsBranch = await HasColumnAsync(con, "RestaurantSettings", "BranchId");
                var sql = hasSettingsBranch && branchId.HasValue
                    ? "SELECT TOP 1 RestaurantName, StreetAddress, City, State, Pincode, Email, Website FROM RestaurantSettings WHERE BranchId = @BranchId ORDER BY Id DESC"
                    : "SELECT TOP 1 RestaurantName, StreetAddress, City, State, Pincode, Email, Website FROM RestaurantSettings ORDER BY Id DESC";

                using var cmd = new SqlCommand(sql, con);
                if (hasSettingsBranch && branchId.HasValue)
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    ViewBag.Restaurant = new
                    {
                        Name = reader["RestaurantName"] as string,
                        Address = string.Join(", ", new[]
                        {
                            reader["StreetAddress"] as string,
                            reader["City"] as string,
                            reader["State"] as string,
                            reader["Pincode"] as string
                        }.Where(s => !string.IsNullOrWhiteSpace(s))),
                        Email = reader["Email"] as string,
                        Website = reader["Website"] as string
                    };
                }
            }
            catch
            {
            }
        }

        private async Task<string> GetBranchDisplayNameAsync(int? branchId)
        {
            var claimBranchName = User.GetActiveBranchName();
            if (branchId.HasValue && User.GetActiveBranchId() == branchId && !string.IsNullOrWhiteSpace(claimBranchName))
            {
                return claimBranchName;
            }

            if (!branchId.HasValue)
            {
                return claimBranchName ?? string.Empty;
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                var hasBranchName = await HasColumnAsync(con, "Branches", "BranchName");
                if (!hasBranchName)
                {
                    return claimBranchName ?? string.Empty;
                }

                var hasBranchLocationId = await HasColumnAsync(con, "Branches", "BranchLocationId");
                var hasLocationName = await HasColumnAsync(con, "BranchLocations", "LocationName");

                var sql = hasBranchLocationId && hasLocationName
                    ? @"SELECT TOP 1
                            CASE
                                WHEN ISNULL(bl.LocationName, '') <> '' THEN ISNULL(b.BranchName, '') + ' - ' + bl.LocationName
                                ELSE ISNULL(b.BranchName, '')
                            END
                        FROM dbo.Branches b
                        LEFT JOIN dbo.BranchLocations bl ON bl.LocationId = b.BranchLocationId
                        WHERE b.BranchId = @BranchId"
                    : "SELECT TOP 1 ISNULL(BranchName, '') FROM dbo.Branches WHERE BranchId = @BranchId";

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@BranchId", branchId.Value);

                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? claimBranchName ?? string.Empty;
            }
            catch
            {
                return claimBranchName ?? string.Empty;
            }
        }

        // GET: /Feedback/Form
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Form(int? branchId = null)
        {
            var activeBranchId = ResolveBranchId(branchId);
            var activeBranchName = await GetBranchDisplayNameAsync(activeBranchId);
            var model = new GuestFeedback
            {
                VisitDate = DateTime.Today,
                OverallRating = 5,
                Location = activeBranchName
            };

            ViewBag.ActiveBranchId = activeBranchId;
            ViewBag.ActiveBranchName = activeBranchName;
            await LoadRestaurantHeaderAsync(activeBranchId);
            return View(model);
        }

        // POST: /Feedback/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Submit(GuestFeedback model, int? branchId = null)
        {
            var activeBranchId = ResolveBranchId(branchId);
            var activeBranchName = await GetBranchDisplayNameAsync(activeBranchId);
            ViewBag.ActiveBranchId = activeBranchId;
            ViewBag.ActiveBranchName = activeBranchName;

            if (string.IsNullOrWhiteSpace(model.Location))
            {
                model.Location = activeBranchName;
            }

            if (!activeBranchId.HasValue)
            {
                ModelState.AddModelError(string.Empty, "No active branch selected. Please submit feedback from a valid branch link.");
            }

            if (model.OverallRating < 1 || model.OverallRating > 5)
            {
                ModelState.AddModelError("OverallRating", "Overall rating must be between 1 and 5.");
            }
            if (!ModelState.IsValid)
            {
                await LoadRestaurantHeaderAsync(activeBranchId);
                return View("Form", model);
            }

            try
            {
                // Log received data for debugging
                Console.WriteLine($"=== Feedback Submission Debug ===");
                Console.WriteLine($"VisitDate: {model.VisitDate}");
                Console.WriteLine($"OverallRating: {model.OverallRating}");
                Console.WriteLine($"Location: {model.Location}");
                Console.WriteLine($"FirstVisit: {model.FirstVisit}");
                Console.WriteLine($"SurveyJson length: {model.SurveyJson?.Length ?? 0}");
                Console.WriteLine($"SurveyJson: {model.SurveyJson}");
                Console.WriteLine($"Tags: {model.Tags}");
                Console.WriteLine($"Comments length: {model.Comments?.Length ?? 0}");
                Console.WriteLine($"Guest Birth Date: {model.GuestBirthDate?.ToShortDateString() ?? "(none)"}");
                Console.WriteLine($"Anniversary Date: {model.AnniversaryDate?.ToShortDateString() ?? "(none)"}");

                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                var hasFeedbackBranchColumn = await HasColumnAsync(con, "GuestFeedback", "BranchId");

                // Discover available parameters on the SP to maintain backward compatibility
                var availableParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var pCmd = new SqlCommand("SELECT REPLACE(name,'@','') as name FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.usp_SubmitGuestFeedback')", con))
                using (var pReader = await pCmd.ExecuteReaderAsync())
                {
                    while (await pReader.ReadAsync())
                    {
                        availableParams.Add(pReader.GetString(0));
                    }
                }
                Console.WriteLine($"SP has {availableParams.Count} parameters: {string.Join(", ", availableParams)}");

                using var cmd = new SqlCommand("usp_SubmitGuestFeedback", con)
                {
                    CommandType = CommandType.StoredProcedure
                };
                if (availableParams.Contains("VisitDate")) cmd.Parameters.AddWithValue("@VisitDate", model.VisitDate.Date);
                if (availableParams.Contains("OverallRating")) cmd.Parameters.AddWithValue("@OverallRating", model.OverallRating);
                if (availableParams.Contains("FoodRating")) cmd.Parameters.AddWithValue("@FoodRating", (object?)model.FoodRating ?? DBNull.Value);
                if (availableParams.Contains("ServiceRating")) cmd.Parameters.AddWithValue("@ServiceRating", (object?)model.ServiceRating ?? DBNull.Value);
                if (availableParams.Contains("CleanlinessRating")) cmd.Parameters.AddWithValue("@CleanlinessRating", (object?)model.CleanlinessRating ?? DBNull.Value);
                if (availableParams.Contains("StaffRating")) cmd.Parameters.AddWithValue("@StaffRating", (object?)model.StaffRating ?? DBNull.Value);
                // New detailed rating params (nullable)
                if (availableParams.Contains("AmbienceRating")) cmd.Parameters.AddWithValue("@AmbienceRating", (object?)model.AmbienceRating ?? DBNull.Value);
                if (availableParams.Contains("ValueRating")) cmd.Parameters.AddWithValue("@ValueRating", (object?)model.ValueRating ?? DBNull.Value);
                if (availableParams.Contains("SpeedRating")) cmd.Parameters.AddWithValue("@SpeedRating", (object?)model.SpeedRating ?? DBNull.Value);
                if (availableParams.Contains("Location")) cmd.Parameters.AddWithValue("@Location", (object?)model.Location ?? DBNull.Value);
                if (availableParams.Contains("IsFirstVisit")) cmd.Parameters.AddWithValue("@IsFirstVisit", (object?)model.FirstVisit ?? DBNull.Value);
                if (availableParams.Contains("SurveyJson")) 
                {
                    cmd.Parameters.AddWithValue("@SurveyJson", (object?)model.SurveyJson ?? DBNull.Value);
                    Console.WriteLine($"✓ SurveyJson parameter ADDED to SP call");
                }
                else
                {
                    Console.WriteLine($"✗ SurveyJson parameter NOT supported by SP");
                }
                if (availableParams.Contains("Tags")) cmd.Parameters.AddWithValue("@Tags", (object?)model.Tags ?? DBNull.Value);
                if (availableParams.Contains("Comments")) cmd.Parameters.AddWithValue("@Comments", (object?)model.Comments ?? DBNull.Value);
                if (availableParams.Contains("GuestName")) cmd.Parameters.AddWithValue("@GuestName", (object?)model.GuestName ?? DBNull.Value);
                if (availableParams.Contains("Email")) cmd.Parameters.AddWithValue("@Email", (object?)model.Email ?? DBNull.Value);
                if (availableParams.Contains("Phone")) cmd.Parameters.AddWithValue("@Phone", (object?)model.Phone ?? DBNull.Value);
                if (availableParams.Contains("GuestBirthDate")) cmd.Parameters.AddWithValue("@GuestBirthDate", (object?)model.GuestBirthDate ?? DBNull.Value);
                if (availableParams.Contains("AnniversaryDate")) cmd.Parameters.AddWithValue("@AnniversaryDate", (object?)model.AnniversaryDate ?? DBNull.Value);
                if (activeBranchId.HasValue && availableParams.Contains("BranchId")) cmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);

                Console.WriteLine($"Executing SP with {cmd.Parameters.Count} parameters");
                var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (activeBranchId.HasValue && hasFeedbackBranchColumn && !availableParams.Contains("BranchId"))
                {
                    using var updateCmd = new SqlCommand("UPDATE GuestFeedback SET BranchId = @BranchId WHERE Id = @Id", con);
                    updateCmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                    updateCmd.Parameters.AddWithValue("@Id", newId);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                Console.WriteLine($"✓ Feedback saved successfully with ID: {newId}");
                TempData["FeedbackSuccess"] = "Thank you! Your feedback has been submitted.";
                return RedirectToAction("ThankYou", new { id = newId, branchId = activeBranchId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error submitting feedback: {ex.Message}");
                await LoadRestaurantHeaderAsync(activeBranchId);
                return View("Form", model);
            }
        }

        // GET: /Feedback/ThankYou
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ThankYou(int id, int? branchId = null)
        {
            ViewBag.FeedbackId = id;
            ViewBag.ActiveBranchId = ResolveBranchId(branchId);
            return View();
        }

        // GET: /Feedback/Summary
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Summary(DateTime? from, DateTime? to, int? branchId = null)
        {
            var activeBranchId = ResolveBranchId(branchId);
            if (!activeBranchId.HasValue)
            {
                TempData["FeedbackError"] = "No active branch selected. Please select a branch first.";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.ActiveBranchId = activeBranchId;
            ViewBag.ActiveBranchName = User.GetActiveBranchName();

            var summary = new GuestFeedbackSummary();
            var latest = new List<GuestFeedback>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                var hasFeedbackBranchColumn = await HasColumnAsync(con, "GuestFeedback", "BranchId");

                using var cmd = new SqlCommand("usp_GetGuestFeedbackSummary", con)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@FromDate", (object?)from?.Date ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToDate", (object?)to?.Date ?? DBNull.Value);

                var summarySpHasBranchParam = false;
                using (var pCmd = new SqlCommand("SELECT COUNT(1) FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.usp_GetGuestFeedbackSummary') AND name = '@BranchId'", con))
                {
                    summarySpHasBranchParam = Convert.ToInt32(await pCmd.ExecuteScalarAsync()) > 0;
                }
                if (summarySpHasBranchParam)
                {
                    cmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    // helper local function to safely read numeric averages (float/decimal)
                    decimal ReadNumeric(string col)
                    {
                        try
                        {
                            var val = reader[col];
                            if (val == DBNull.Value) return 0m;
                            return Convert.ToDecimal(val);
                        }
                        catch { return 0m; }
                    }

                    summary.TotalFeedback = reader.ColumnExists("TotalFeedback") && !reader.IsDBNull(reader.GetOrdinal("TotalFeedback")) ? reader.GetInt32(reader.GetOrdinal("TotalFeedback")) : 0;
                    summary.AvgOverall = ReadNumeric("AvgOverall");
                    summary.AvgFood = ReadNumeric("AvgFood");
                    summary.AvgService = ReadNumeric("AvgService");
                    summary.AvgCleanliness = ReadNumeric("AvgCleanliness");
                    summary.AvgStaff = ReadNumeric("AvgStaff");
                    // New averages (optional columns)
                    summary.AvgAmbience = ReadNumeric("AvgAmbience");
                    summary.AvgValue = ReadNumeric("AvgValue");
                    summary.AvgSpeed = ReadNumeric("AvgSpeed");
                }
                if (await reader.NextResultAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        latest.Add(new GuestFeedback
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            VisitDate = reader.GetDateTime(reader.GetOrdinal("VisitDate")),
                            OverallRating = (byte)reader.GetByte(reader.GetOrdinal("OverallRating")),
                            FoodRating = reader.IsDBNull(reader.GetOrdinal("FoodRating")) ? null : (byte?)reader.GetByte(reader.GetOrdinal("FoodRating")),
                            ServiceRating = reader.IsDBNull(reader.GetOrdinal("ServiceRating")) ? null : (byte?)reader.GetByte(reader.GetOrdinal("ServiceRating")),
                            CleanlinessRating = reader.IsDBNull(reader.GetOrdinal("CleanlinessRating")) ? null : (byte?)reader.GetByte(reader.GetOrdinal("CleanlinessRating")),
                            StaffRating = reader.IsDBNull(reader.GetOrdinal("StaffRating")) ? null : (byte?)reader.GetByte(reader.GetOrdinal("StaffRating")),
                            AmbienceRating = reader.ColumnExists("AmbienceRating") && !reader.IsDBNull(reader.GetOrdinal("AmbienceRating")) ? (byte?)reader.GetByte(reader.GetOrdinal("AmbienceRating")) : null,
                            ValueRating = reader.ColumnExists("ValueRating") && !reader.IsDBNull(reader.GetOrdinal("ValueRating")) ? (byte?)reader.GetByte(reader.GetOrdinal("ValueRating")) : null,
                            SpeedRating = reader.ColumnExists("SpeedRating") && !reader.IsDBNull(reader.GetOrdinal("SpeedRating")) ? (byte?)reader.GetByte(reader.GetOrdinal("SpeedRating")) : null,
                            Tags = reader.IsDBNull(reader.GetOrdinal("Tags")) ? null : reader.GetString(reader.GetOrdinal("Tags")),
                            Comments = reader.IsDBNull(reader.GetOrdinal("Comments")) ? null : reader.GetString(reader.GetOrdinal("Comments")),
                            GuestName = reader.IsDBNull(reader.GetOrdinal("GuestName")) ? null : reader.GetString(reader.GetOrdinal("GuestName")),
                            GuestBirthDate = reader.ColumnExists("GuestBirthDate") && !reader.IsDBNull(reader.GetOrdinal("GuestBirthDate")) ? (DateTime?)reader.GetDateTime(reader.GetOrdinal("GuestBirthDate")) : null,
                            AnniversaryDate = reader.ColumnExists("AnniversaryDate") && !reader.IsDBNull(reader.GetOrdinal("AnniversaryDate")) ? (DateTime?)reader.GetDateTime(reader.GetOrdinal("AnniversaryDate")) : null,
                            // Optional extras
                            Location = reader.ColumnExists("Location") && !reader.IsDBNull(reader.GetOrdinal("Location")) ? reader.GetString(reader.GetOrdinal("Location")) : null,
                            FirstVisit = reader.ColumnExists("IsFirstVisit") && !reader.IsDBNull(reader.GetOrdinal("IsFirstVisit")) ? (bool?)reader.GetBoolean(reader.GetOrdinal("IsFirstVisit")) : null,
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                        });
                    }
                }
                else
                {
                    // Fallback for older DBs where the SP returns only a single result set (aggregates)
                    var latestSql = hasFeedbackBranchColumn
                        ? "SELECT TOP 50 Id, VisitDate, OverallRating, FoodRating, ServiceRating, CleanlinessRating, StaffRating, Tags, Comments, GuestName, GuestBirthDate, AnniversaryDate, CreatedAt FROM GuestFeedback WHERE BranchId = @BranchId ORDER BY CreatedAt DESC"
                        : "SELECT TOP 50 Id, VisitDate, OverallRating, FoodRating, ServiceRating, CleanlinessRating, StaffRating, Tags, Comments, GuestName, GuestBirthDate, AnniversaryDate, CreatedAt FROM GuestFeedback ORDER BY CreatedAt DESC";

                    using var cmdLatest = new SqlCommand(latestSql, con);
                    if (hasFeedbackBranchColumn)
                    {
                        cmdLatest.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                    }

                    using var r2 = await cmdLatest.ExecuteReaderAsync();
                    while (await r2.ReadAsync())
                    {
                        latest.Add(new GuestFeedback
                        {
                            Id = r2.GetInt32(r2.GetOrdinal("Id")),
                            VisitDate = r2.GetDateTime(r2.GetOrdinal("VisitDate")),
                            OverallRating = (byte)r2.GetByte(r2.GetOrdinal("OverallRating")),
                            FoodRating = r2.IsDBNull(r2.GetOrdinal("FoodRating")) ? null : (byte?)r2.GetByte(r2.GetOrdinal("FoodRating")),
                            ServiceRating = r2.IsDBNull(r2.GetOrdinal("ServiceRating")) ? null : (byte?)r2.GetByte(r2.GetOrdinal("ServiceRating")),
                            CleanlinessRating = r2.IsDBNull(r2.GetOrdinal("CleanlinessRating")) ? null : (byte?)r2.GetByte(r2.GetOrdinal("CleanlinessRating")),
                            StaffRating = r2.IsDBNull(r2.GetOrdinal("StaffRating")) ? null : (byte?)r2.GetByte(r2.GetOrdinal("StaffRating")),
                            Tags = r2.IsDBNull(r2.GetOrdinal("Tags")) ? null : r2.GetString(r2.GetOrdinal("Tags")),
                            Comments = r2.IsDBNull(r2.GetOrdinal("Comments")) ? null : r2.GetString(r2.GetOrdinal("Comments")),
                            GuestName = r2.IsDBNull(r2.GetOrdinal("GuestName")) ? null : r2.GetString(r2.GetOrdinal("GuestName")),
                            GuestBirthDate = r2.ColumnExists("GuestBirthDate") && !r2.IsDBNull(r2.GetOrdinal("GuestBirthDate")) ? (DateTime?)r2.GetDateTime(r2.GetOrdinal("GuestBirthDate")) : null,
                            AnniversaryDate = r2.ColumnExists("AnniversaryDate") && !r2.IsDBNull(r2.GetOrdinal("AnniversaryDate")) ? (DateTime?)r2.GetDateTime(r2.GetOrdinal("AnniversaryDate")) : null,
                            Location = r2.ColumnExists("Location") && !r2.IsDBNull(r2.GetOrdinal("Location")) ? r2.GetString(r2.GetOrdinal("Location")) : null,
                            FirstVisit = r2.ColumnExists("IsFirstVisit") && !r2.IsDBNull(r2.GetOrdinal("IsFirstVisit")) ? (bool?)r2.GetBoolean(r2.GetOrdinal("IsFirstVisit")) : null,
                            CreatedAt = r2.GetDateTime(r2.GetOrdinal("CreatedAt"))
                        });
                    }
                }
                // If aggregates indicate data but the latest list is empty (e.g., due to SP filter differences), load a best-effort latest list.
                if (latest.Count == 0 && summary.TotalFeedback > 0)
                {
                    var latestSql2 = hasFeedbackBranchColumn
                        ? "SELECT TOP 50 Id, VisitDate, OverallRating, FoodRating, ServiceRating, CleanlinessRating, StaffRating, AmbienceRating, ValueRating, SpeedRating, Tags, Comments, GuestName, GuestBirthDate, AnniversaryDate, Location, IsFirstVisit, CreatedAt FROM GuestFeedback WHERE BranchId = @BranchId ORDER BY CreatedAt DESC"
                        : "SELECT TOP 50 Id, VisitDate, OverallRating, FoodRating, ServiceRating, CleanlinessRating, StaffRating, AmbienceRating, ValueRating, SpeedRating, Tags, Comments, GuestName, GuestBirthDate, AnniversaryDate, Location, IsFirstVisit, CreatedAt FROM GuestFeedback ORDER BY CreatedAt DESC";

                    using var cmdLatest2 = new SqlCommand(latestSql2, con);
                    if (hasFeedbackBranchColumn)
                    {
                        cmdLatest2.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                    }

                    using var r3 = await cmdLatest2.ExecuteReaderAsync();
                    while (await r3.ReadAsync())
                    {
                        latest.Add(new GuestFeedback
                        {
                            Id = r3.GetInt32(r3.GetOrdinal("Id")),
                            VisitDate = r3.GetDateTime(r3.GetOrdinal("VisitDate")),
                            OverallRating = (byte)r3.GetByte(r3.GetOrdinal("OverallRating")),
                            FoodRating = r3.IsDBNull(r3.GetOrdinal("FoodRating")) ? null : (byte?)r3.GetByte(r3.GetOrdinal("FoodRating")),
                            ServiceRating = r3.IsDBNull(r3.GetOrdinal("ServiceRating")) ? null : (byte?)r3.GetByte(r3.GetOrdinal("ServiceRating")),
                            CleanlinessRating = r3.IsDBNull(r3.GetOrdinal("CleanlinessRating")) ? null : (byte?)r3.GetByte(r3.GetOrdinal("CleanlinessRating")),
                            StaffRating = r3.IsDBNull(r3.GetOrdinal("StaffRating")) ? null : (byte?)r3.GetByte(r3.GetOrdinal("StaffRating")),
                            AmbienceRating = r3.ColumnExists("AmbienceRating") && !r3.IsDBNull(r3.GetOrdinal("AmbienceRating")) ? (byte?)r3.GetByte(r3.GetOrdinal("AmbienceRating")) : null,
                            ValueRating = r3.ColumnExists("ValueRating") && !r3.IsDBNull(r3.GetOrdinal("ValueRating")) ? (byte?)r3.GetByte(r3.GetOrdinal("ValueRating")) : null,
                            SpeedRating = r3.ColumnExists("SpeedRating") && !r3.IsDBNull(r3.GetOrdinal("SpeedRating")) ? (byte?)r3.GetByte(r3.GetOrdinal("SpeedRating")) : null,
                            Tags = r3.IsDBNull(r3.GetOrdinal("Tags")) ? null : r3.GetString(r3.GetOrdinal("Tags")),
                            Comments = r3.IsDBNull(r3.GetOrdinal("Comments")) ? null : r3.GetString(r3.GetOrdinal("Comments")),
                            GuestName = r3.IsDBNull(r3.GetOrdinal("GuestName")) ? null : r3.GetString(r3.GetOrdinal("GuestName")),
                            GuestBirthDate = r3.ColumnExists("GuestBirthDate") && !r3.IsDBNull(r3.GetOrdinal("GuestBirthDate")) ? (DateTime?)r3.GetDateTime(r3.GetOrdinal("GuestBirthDate")) : null,
                            AnniversaryDate = r3.ColumnExists("AnniversaryDate") && !r3.IsDBNull(r3.GetOrdinal("AnniversaryDate")) ? (DateTime?)r3.GetDateTime(r3.GetOrdinal("AnniversaryDate")) : null,
                            Location = r3.ColumnExists("Location") && !r3.IsDBNull(r3.GetOrdinal("Location")) ? r3.GetString(r3.GetOrdinal("Location")) : null,
                            FirstVisit = r3.ColumnExists("IsFirstVisit") && !r3.IsDBNull(r3.GetOrdinal("IsFirstVisit")) ? (bool?)r3.GetBoolean(r3.GetOrdinal("IsFirstVisit")) : null,
                            CreatedAt = r3.GetDateTime(r3.GetOrdinal("CreatedAt"))
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["FeedbackError"] = $"Error loading feedback summary: {ex.Message}";
            }
            ViewBag.Summary = summary;
            return View(latest);
        }
    }
}