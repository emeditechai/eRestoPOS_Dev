using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize]
    public class UPISettingsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public UPISettingsController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // GET: UPISettings
        public IActionResult Index()
        {
            var model = new UPISettingsViewModel();
            var activeBranchId = User.GetActiveBranchId();

            if (!activeBranchId.HasValue)
            {
                model.Message = "Please select an active branch to access UPI settings.";
                model.IsSuccess = false;
                return View(model);
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    EnsureUpiBranchColumnExists(connection);

                    bool hasBranchColumn = HasBranchColumn(connection, "UPISettings");
                    string query = hasBranchColumn
                        ? "SELECT TOP 1 UPIId, PayeeName, IsEnabled FROM UPISettings WHERE BranchId = @BranchId ORDER BY Id DESC"
                        : "SELECT TOP 1 UPIId, PayeeName, IsEnabled FROM UPISettings ORDER BY Id DESC";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        if (hasBranchColumn)
                        {
                            command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        }

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                        if (reader.Read())
                        {
                            model.UPIId = reader.GetString(0);
                            model.PayeeName = reader.GetString(1);
                            model.IsEnabled = reader.GetBoolean(2);
                        }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                model.Message = $"Error loading settings: {ex.Message}";
                model.IsSuccess = false;
            }

            return View(model);
        }

        // POST: UPISettings/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update(UPISettingsViewModel model)
        {
            var activeBranchId = User.GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                model.Message = "Please select an active branch to save UPI settings.";
                model.IsSuccess = false;
                return View("Index", model);
            }

            if (string.IsNullOrWhiteSpace(model.UPIId))
            {
                model.Message = "UPI ID is required";
                model.IsSuccess = false;
                return View("Index", model);
            }

            if (string.IsNullOrWhiteSpace(model.PayeeName))
            {
                model.Message = "Payee Name is required";
                model.IsSuccess = false;
                return View("Index", model);
            }

            // Validate UPI ID format (basic validation)
            if (!model.UPIId.Contains("@"))
            {
                model.Message = "Invalid UPI ID format. Should be like: username@bank";
                model.IsSuccess = false;
                return View("Index", model);
            }

            try
            {
                int? currentUserId = GetCurrentUserId();

                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    EnsureUpiBranchColumnExists(connection);

                    bool hasBranchColumn = HasBranchColumn(connection, "UPISettings");

                    // Check if record exists
                    string checkQuery = hasBranchColumn
                        ? "SELECT COUNT(*) FROM UPISettings WHERE BranchId = @BranchId"
                        : "SELECT COUNT(*) FROM UPISettings";
                    bool recordExists = false;

                    using (SqlCommand checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        if (hasBranchColumn)
                        {
                            checkCommand.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        }

                        recordExists = (int)checkCommand.ExecuteScalar() > 0;
                    }

                    string query;
                    if (recordExists)
                    {
                        query = hasBranchColumn
                            ? @"UPDATE UPISettings 
                                 SET UPIId = @UPIId, 
                                     PayeeName = @PayeeName, 
                                     IsEnabled = @IsEnabled, 
                                     UpdatedAt = GETDATE(),
                                     UpdatedBy = @UpdatedBy
                                 WHERE BranchId = @BranchId"
                            : @"UPDATE UPISettings 
                                 SET UPIId = @UPIId, 
                                     PayeeName = @PayeeName, 
                                     IsEnabled = @IsEnabled, 
                                     UpdatedAt = GETDATE(),
                                     UpdatedBy = @UpdatedBy
                                 WHERE Id = (SELECT TOP 1 Id FROM UPISettings ORDER BY Id DESC)";
                    }
                    else
                    {
                        query = hasBranchColumn
                            ? @"INSERT INTO UPISettings (BranchId, UPIId, PayeeName, IsEnabled, UpdatedBy) 
                                 VALUES (@BranchId, @UPIId, @PayeeName, @IsEnabled, @UpdatedBy)"
                            : @"INSERT INTO UPISettings (UPIId, PayeeName, IsEnabled, UpdatedBy) 
                                 VALUES (@UPIId, @PayeeName, @IsEnabled, @UpdatedBy)";
                    }

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        if (hasBranchColumn)
                        {
                            command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        }

                        command.Parameters.AddWithValue("@UPIId", model.UPIId.Trim());
                        command.Parameters.AddWithValue("@PayeeName", model.PayeeName.Trim());
                        command.Parameters.AddWithValue("@IsEnabled", model.IsEnabled);
                        command.Parameters.AddWithValue("@UpdatedBy", currentUserId.HasValue ? (object)currentUserId.Value : DBNull.Value);

                        command.ExecuteNonQuery();
                    }
                }

                model.Message = "UPI settings saved successfully!";
                model.IsSuccess = true;
            }
            catch (Exception ex)
            {
                model.Message = $"Error saving settings: {ex.Message}";
                model.IsSuccess = false;
            }

            return View("Index", model);
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
            return null;
        }

        private static bool HasBranchColumn(SqlConnection connection, string tableName)
        {
            using (var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME = @TableName
  AND COLUMN_NAME = 'BranchId'", connection))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static void EnsureUpiBranchColumnExists(SqlConnection connection)
        {
            using (var cmd = new SqlCommand(@"
IF COL_LENGTH('dbo.UPISettings', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.UPISettings ADD BranchId INT NULL;

    DECLARE @MainBranchId INT = (
        SELECT TOP 1 BranchId
        FROM dbo.Branches
        WHERE ISNULL(IsActive, 1) = 1 AND ISNULL(Is_MainBranch, 0) = 1
        ORDER BY BranchId
    );

    IF @MainBranchId IS NULL
    BEGIN
        SET @MainBranchId = (
            SELECT TOP 1 BranchId
            FROM dbo.Branches
            WHERE ISNULL(IsActive, 1) = 1
            ORDER BY BranchId
        );
    END

    IF @MainBranchId IS NOT NULL
    BEGIN
        UPDATE dbo.UPISettings
        SET BranchId = @MainBranchId
        WHERE BranchId IS NULL;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.UPISettings')
          AND name = 'IX_UPISettings_BranchId'
    )
    BEGIN
        CREATE UNIQUE INDEX IX_UPISettings_BranchId ON dbo.UPISettings(BranchId) WHERE BranchId IS NOT NULL;
    END
END", connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}
