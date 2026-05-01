using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;

namespace RestaurantManagementSystem.Controllers
{
    /// <summary>
    /// UOM (Unit of Measurement) Master controller.
    /// Manages creation, editing, and deletion of UOM records used in Bill of Material (BOM).
    /// The controller auto-creates the dbo.UomMaster table on first use if it is absent.
    /// </summary>
    public class UomController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public UomController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Authorization helper
        // Only the user with username 'Admin' may create / edit / delete /
        // toggle UOM records. All other users have read-only access.
        // ──────────────────────────────────────────────────────────────────────
        private bool IsAdminUser() =>
            User?.Identity?.IsAuthenticated == true &&
            string.Equals(User.Identity!.Name, "Admin", StringComparison.OrdinalIgnoreCase);

        private IActionResult AdminOnlyDenied()
        {
            TempData["ErrorMessage"] = "You do not have permission to modify UOM records. Please contact the Admin user.";
            return RedirectToAction(nameof(Index));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Table bootstrapping
        // ──────────────────────────────────────────────────────────────────────

        private void EnsureUomMasterTableExists()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                const string checkSql = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME = 'UomMaster' AND TABLE_SCHEMA = 'dbo'";

                using var checkCmd = new SqlCommand(checkSql, connection);
                bool tableExists = (int)checkCmd.ExecuteScalar() > 0;

                if (!tableExists)
                {
                    const string createSql = @"
                        CREATE TABLE [dbo].[UomMaster] (
                            [UOMId]            INT            IDENTITY(1,1) NOT NULL,
                            [UOMCode]          NVARCHAR(15)   NOT NULL,
                            [UOMName]          NVARCHAR(100)  NOT NULL,
                            [UOMType]          NVARCHAR(20)   NOT NULL DEFAULT 'Count',
                            [BaseUOMId]        INT            NULL,
                            [ConversionFactor] DECIMAL(18,6)  NOT NULL DEFAULT 1,
                            [PackSize]         DECIMAL(18,3)  NULL,
                            [DecimalPlaces]    INT            NOT NULL DEFAULT 3,
                            [Description]      NVARCHAR(300)  NULL,
                            [IsActive]         BIT            NOT NULL DEFAULT 1,
                            [CreatedAt]        DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
                            [UpdatedAt]        DATETIME2(3)   NULL,
                            CONSTRAINT [PK_UomMaster] PRIMARY KEY ([UOMId]),
                            CONSTRAINT [UQ_UomMaster_UOMCode] UNIQUE ([UOMCode]),
                            CONSTRAINT [CHK_UomMaster_Type]
                                CHECK ([UOMType] IN ('Weight','Volume','Count','Other')),
                            CONSTRAINT [CHK_UomMaster_ConversionFactor]
                                CHECK ([ConversionFactor] > 0),
                            CONSTRAINT [FK_UomMaster_BaseUOM]
                                FOREIGN KEY ([BaseUOMId]) REFERENCES [dbo].[UomMaster]([UOMId])
                        );

                        CREATE INDEX [IX_UomMaster_UOMType]   ON [dbo].[UomMaster] ([UOMType]);
                        CREATE INDEX [IX_UomMaster_IsActive]  ON [dbo].[UomMaster] ([IsActive]);
                        CREATE INDEX [IX_UomMaster_BaseUOMId] ON [dbo].[UomMaster] ([BaseUOMId]);";

                    using var createCmd = new SqlCommand(createSql, connection);
                    createCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UomController] EnsureUomMasterTableExists error: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Read helpers
        // ──────────────────────────────────────────────────────────────────────

        private List<UomMaster> LoadAllUoms()
        {
            var list = new List<UomMaster>();
            const string sql = @"
                SELECT u.UOMId, u.UOMCode, u.UOMName, u.UOMType,
                       u.BaseUOMId, b.UOMCode AS BaseUOMCode, b.UOMName AS BaseUOMName,
                       u.ConversionFactor, u.PackSize, u.DecimalPlaces,
                       u.Description, u.IsActive, u.CreatedAt, u.UpdatedAt
                FROM [dbo].[UomMaster] u
                LEFT JOIN [dbo].[UomMaster] b ON u.BaseUOMId = b.UOMId
                ORDER BY u.UOMType, u.UOMCode";

            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapRow(reader));
            }
            return list;
        }

        private UomMaster? LoadUomById(int id)
        {
            const string sql = @"
                SELECT u.UOMId, u.UOMCode, u.UOMName, u.UOMType,
                       u.BaseUOMId, b.UOMCode AS BaseUOMCode, b.UOMName AS BaseUOMName,
                       u.ConversionFactor, u.PackSize, u.DecimalPlaces,
                       u.Description, u.IsActive, u.CreatedAt, u.UpdatedAt
                FROM [dbo].[UomMaster] u
                LEFT JOIN [dbo].[UomMaster] b ON u.BaseUOMId = b.UOMId
                WHERE u.UOMId = @Id";

            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapRow(reader) : null;
        }

        private static UomMaster MapRow(SqlDataReader r) => new()
        {
            UOMId            = r.GetInt32(r.GetOrdinal("UOMId")),
            UOMCode          = r.GetString(r.GetOrdinal("UOMCode")),
            UOMName          = r.GetString(r.GetOrdinal("UOMName")),
            UOMType          = r.GetString(r.GetOrdinal("UOMType")),
            BaseUOMId        = r["BaseUOMId"] is DBNull ? null : (int?)r.GetInt32(r.GetOrdinal("BaseUOMId")),
            BaseUOMCode      = r["BaseUOMCode"] as string,
            BaseUOMName      = r["BaseUOMName"] as string,
            ConversionFactor = r.GetDecimal(r.GetOrdinal("ConversionFactor")),
            PackSize         = r["PackSize"] is DBNull ? null : (decimal?)r.GetDecimal(r.GetOrdinal("PackSize")),
            DecimalPlaces    = r.GetInt32(r.GetOrdinal("DecimalPlaces")),
            Description      = r["Description"] as string,
            IsActive         = r.GetBoolean(r.GetOrdinal("IsActive")),
            CreatedAt        = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            UpdatedAt        = r["UpdatedAt"] is DBNull ? null : (DateTime?)r.GetDateTime(r.GetOrdinal("UpdatedAt"))
        };

        private void PopulateViewBag(int? excludeId = null)
        {
            // Base UOM dropdown (exclude self when editing to prevent circular reference)
            const string sql = @"
                SELECT UOMId, UOMCode, UOMName, UOMType
                FROM [dbo].[UomMaster]
                WHERE IsActive = 1
                  AND (BaseUOMId IS NULL)     -- only show base-eligible UOMs in dropdown
                ORDER BY UOMType, UOMCode";

            var baseUoms = new List<(int Id, string Code, string Name, string Type)>();
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                if (excludeId.HasValue && id == excludeId.Value) continue;
                baseUoms.Add((id,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }

            ViewBag.BaseUoms = baseUoms;
            ViewBag.UOMTypes = new[] { "Weight", "Volume", "Count", "Other" };
        }

        // ──────────────────────────────────────────────────────────────────────
        // GET: Uom/Index
        // ──────────────────────────────────────────────────────────────────────

        public IActionResult Index()
        {
            EnsureUomMasterTableExists();
            PopulateViewBag();
            var uoms = LoadAllUoms();
            return View(uoms);
        }

        // ──────────────────────────────────────────────────────────────────────
        // GET: Uom/Form  (Create / Edit / View)
        // ──────────────────────────────────────────────────────────────────────

        public IActionResult Form(int? id, bool isView = false)
        {
            EnsureUomMasterTableExists();

            // Non-admin users can only View. Any attempt to load the form in
            // create/edit mode is forced into read-only View mode.
            if (!IsAdminUser())
            {
                if (!id.HasValue || id.Value <= 0)
                {
                    return AdminOnlyDenied();
                }
                isView = true;
            }

            UomMaster model;
            if (id.HasValue && id.Value > 0)
            {
                model = LoadUomById(id.Value) ?? new UomMaster { IsActive = true, ConversionFactor = 1m, DecimalPlaces = 3 };
            }
            else
            {
                model = new UomMaster { IsActive = true, ConversionFactor = 1m, DecimalPlaces = 3 };
            }

            ViewBag.IsView = isView;
            PopulateViewBag(excludeId: id);
            return View("Form", model);
        }

        // ──────────────────────────────────────────────────────────────────────
        // POST: Uom/Save  (Create + Update)
        // ──────────────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            int UOMId,
            string UOMCode, string UOMName, string UOMType,
            int? BaseUOMId, decimal ConversionFactor,
            decimal? PackSize, int DecimalPlaces,
            string? Description, bool IsActive)
        {
            EnsureUomMasterTableExists();

            if (!IsAdminUser())
            {
                return AdminOnlyDenied();
            }

            // ── Validation ──────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(UOMCode))
            { TempData["ErrorMessage"] = "UOM Code is required."; return RedirectToAction(nameof(Index)); }
            if (string.IsNullOrWhiteSpace(UOMName))
            { TempData["ErrorMessage"] = "UOM Name is required."; return RedirectToAction(nameof(Index)); }
            if (string.IsNullOrWhiteSpace(UOMType) || !new[] { "Weight", "Volume", "Count", "Other" }.Contains(UOMType))
            { TempData["ErrorMessage"] = "Invalid UOM Type."; return RedirectToAction(nameof(Index)); }
            if (ConversionFactor <= 0)
            { TempData["ErrorMessage"] = "Conversion factor must be greater than zero."; return RedirectToAction(nameof(Index)); }

            UOMCode = UOMCode.Trim().ToUpperInvariant();
            UOMName = UOMName.Trim();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();
                SqlAuditContext.Apply(connection, User, HttpContext, User.GetActiveBranchId(), "UOM");

                if (UOMId == 0)
                {
                    // ── INSERT ────────────────────────────────────────────
                    const string insertSql = @"
                        INSERT INTO [dbo].[UomMaster]
                               (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor,
                                PackSize, DecimalPlaces, Description, IsActive, CreatedAt)
                        VALUES (@UOMCode, @UOMName, @UOMType, @BaseUOMId, @CF,
                                @PackSize, @DP, @Desc, @IsActive, SYSUTCDATETIME())";

                    using var cmd = new SqlCommand(insertSql, connection);
                    BindParams(cmd, UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor,
                               PackSize, DecimalPlaces, Description, IsActive);
                    cmd.ExecuteNonQuery();
                    TempData["SuccessMessage"] = $"UOM '{UOMCode} – {UOMName}' created successfully.";
                    try { await AuditTrailController.LogSystemAuditAsync(
                        _connectionString, "UOM", "Create",
                        null, $"{UOMCode} – {UOMName}", null,
                        null, $"{UOMCode} – {UOMName}, Type:{UOMType}",
                        User.GetActiveBranchId(),
                        User.GetUserId() ?? 0, User.Identity?.Name ?? "Unknown",
                        HttpContext.Connection.RemoteIpAddress?.ToString()); } catch { }
                }
                else
                {
                    // ── UPDATE ────────────────────────────────────────────
                    const string updateSql = @"
                        UPDATE [dbo].[UomMaster] SET
                            UOMCode = @UOMCode, UOMName = @UOMName, UOMType = @UOMType,
                            BaseUOMId = @BaseUOMId, ConversionFactor = @CF,
                            PackSize = @PackSize, DecimalPlaces = @DP,
                            Description = @Desc, IsActive = @IsActive,
                            UpdatedAt = SYSUTCDATETIME()
                        WHERE UOMId = @UOMId";

                    using var cmd = new SqlCommand(updateSql, connection);
                    cmd.Parameters.AddWithValue("@UOMId", UOMId);
                    BindParams(cmd, UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor,
                               PackSize, DecimalPlaces, Description, IsActive);
                    cmd.ExecuteNonQuery();
                    TempData["SuccessMessage"] = $"UOM '{UOMCode} – {UOMName}' updated successfully.";
                    try { await AuditTrailController.LogSystemAuditAsync(
                        _connectionString, "UOM", "Update",
                        UOMId, $"{UOMCode} – {UOMName}", null,
                        null, $"{UOMCode} – {UOMName}, Type:{UOMType}, CF:{ConversionFactor}",
                        User.GetActiveBranchId(),
                        User.GetUserId() ?? 0, User.Identity?.Name ?? "Unknown",
                        HttpContext.Connection.RemoteIpAddress?.ToString()); } catch { }
                }
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // Unique constraint violation
                TempData["ErrorMessage"] = $"UOM Code '{UOMCode}' already exists. Please use a different code.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private static void BindParams(SqlCommand cmd,
            string uomCode, string uomName, string uomType,
            int? baseUOMId, decimal cf,
            decimal? packSize, int dp, string? desc, bool isActive)
        {
            cmd.Parameters.AddWithValue("@UOMCode",   uomCode);
            cmd.Parameters.AddWithValue("@UOMName",   uomName);
            cmd.Parameters.AddWithValue("@UOMType",   uomType);
            cmd.Parameters.AddWithValue("@BaseUOMId", (object?)baseUOMId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CF",        cf);
            cmd.Parameters.AddWithValue("@PackSize",  (object?)packSize ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DP",        dp);
            cmd.Parameters.AddWithValue("@Desc",      (object?)desc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive",  isActive ? 1 : 0);
        }

        // ──────────────────────────────────────────────────────────────────────
        // POST: Uom/ToggleActive
        // ──────────────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleActive(int id)
        {
            if (!IsAdminUser())
            {
                return AdminOnlyDenied();
            }
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();
                SqlAuditContext.Apply(connection, User, HttpContext, User.GetActiveBranchId(), "UOM");
                const string sql = @"
                    UPDATE [dbo].[UomMaster]
                    SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE UOMId = @Id";
                using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "UOM status updated.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Toggle failed: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // ──────────────────────────────────────────────────────────────────────
        // POST: Uom/Delete
        // ──────────────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            if (!IsAdminUser())
            {
                return AdminOnlyDenied();
            }
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();
                SqlAuditContext.Apply(connection, User, HttpContext, User.GetActiveBranchId(), "UOM");

                // Check if this UOM is referenced as a base by another UOM
                const string checkSql = @"
                    SELECT COUNT(*) FROM [dbo].[UomMaster]
                    WHERE BaseUOMId = @Id";
                using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@Id", id);
                int refCount = (int)checkCmd.ExecuteScalar();

                if (refCount > 0)
                {
                    TempData["ErrorMessage"] = "Cannot delete: this UOM is used as a Base UOM by other records. Deactivate it instead.";
                    return RedirectToAction(nameof(Index));
                }

                const string deleteSql = "DELETE FROM [dbo].[UomMaster] WHERE UOMId = @Id";
                using var deleteCmd = new SqlCommand(deleteSql, connection);
                deleteCmd.Parameters.AddWithValue("@Id", id);
                deleteCmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "UOM deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Delete failed: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // ──────────────────────────────────────────────────────────────────────
        // GET: Uom/GetUomJson  (AJAX endpoint for dynamic forms)
        // ──────────────────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult GetUomJson()
        {
            EnsureUomMasterTableExists();
            const string sql = @"
                SELECT UOMId, UOMCode, UOMName, UOMType, ConversionFactor
                FROM [dbo].[UomMaster]
                WHERE IsActive = 1
                ORDER BY UOMType, UOMCode";

            var list = new List<object>();
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    id   = reader.GetInt32(0),
                    code = reader.GetString(1),
                    name = reader.GetString(2),
                    type = reader.GetString(3),
                    cf   = reader.GetDecimal(4)
                });
            }
            return Json(list);
        }
    }
}
