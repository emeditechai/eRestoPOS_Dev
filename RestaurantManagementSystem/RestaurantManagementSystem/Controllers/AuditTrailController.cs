using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize]
    public class AuditTrailController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public AuditTrailController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // GET: AuditTrail
        public async Task<IActionResult> Index(int? orderId, string? orderNumber, DateTime? startDate, 
            DateTime? endDate, int? userId, string? entityType, string? searchTerm, int page = 1, int pageSize = 50)
        {
            var activeBranchId = User.GetActiveBranchId();

            if (!activeBranchId.HasValue)
            {
                TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                return RedirectToAction("Index", "Home");
            }

            // If dates are not provided, default to last 90 days
            if (!startDate.HasValue)
            {
                startDate = DateTime.Now.AddDays(-90);
            }
            if (!endDate.HasValue)
            {
                endDate = DateTime.Now.AddDays(1); // Include today's records
            }
            
            // Handle empty string for entityType (when "All Types" is selected)
            if (string.IsNullOrWhiteSpace(entityType))
            {
                entityType = null;
            }
            
            var model = new AuditTrailViewModel
            {
                OrderId = orderId,
                OrderNumber = orderNumber,
                StartDate = startDate,
                EndDate = endDate,
                UserId = userId,
                EntityType = entityType,
                SearchTerm = searchTerm,
                CurrentPage = page,
                PageSize = pageSize
            };

            try
            {
                await LoadAuditDataAsync(model, activeBranchId.Value);
                await LoadFilterOptionsAsync(model, activeBranchId.Value);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error loading audit trail: {ex.Message}";
            }

            return View(model);
        }

        // GET: Audit Trail for a specific order
        public async Task<IActionResult> OrderAudit(int id)
        {
            var activeBranchId = User.GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                return RedirectToAction("Index", "Home");
            }

            var model = new AuditTrailViewModel
            {
                OrderId = id,
                CurrentPage = 1,
                PageSize = 100,
                StartDate = DateTime.Now.AddYears(-1),
                EndDate = DateTime.Now
            };

            try
            {
                await LoadAuditDataAsync(model, activeBranchId.Value);
                var orderFound = await LoadOrderDetailsAsync(model, id, activeBranchId.Value);
                if (!orderFound)
                {
                    TempData["ErrorMessage"] = "Order not found in active branch.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error loading order audit trail: {ex.Message}";
            }

            return View(model);
        }

        // GET: Audit Trail Statistics
        public async Task<IActionResult> Statistics()
        {
            var activeBranchId = User.GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                return RedirectToAction("Index", "Home");
            }

            var stats = new AuditTrailStatistics();

            try
            {
                await LoadStatisticsAsync(stats, activeBranchId.Value);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error loading statistics: {ex.Message}";
            }

            return View(stats);
        }

        private async Task LoadAuditDataAsync(AuditTrailViewModel model, int activeBranchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");

                var whereClauses = new List<string>
                {
                    "(@OrderId IS NULL OR a.OrderId = @OrderId)",
                    "(@StartDate IS NULL OR a.ChangedDate >= @StartDate)",
                    "(@EndDate IS NULL OR a.ChangedDate <= @EndDate)",
                    "(@UserId IS NULL OR a.ChangedBy = @UserId)",
                    "(@EntityType IS NULL OR a.EntityType = @EntityType)",
                    "(@OrderNumber IS NULL OR a.OrderNumber LIKE '%' + @OrderNumber + '%')"
                };

                if (!string.IsNullOrWhiteSpace(model.SearchTerm))
                {
                    whereClauses.Add(@"(
                        a.OrderNumber LIKE '%' + @SearchTerm + '%' OR
                        a.Action LIKE '%' + @SearchTerm + '%' OR
                        a.EntityType LIKE '%' + @SearchTerm + '%' OR
                        a.ChangedByName LIKE '%' + @SearchTerm + '%' OR
                        a.FieldName LIKE '%' + @SearchTerm + '%' OR
                        a.OldValue LIKE '%' + @SearchTerm + '%' OR
                        a.NewValue LIKE '%' + @SearchTerm + '%' OR
                        a.AdditionalInfo LIKE '%' + @SearchTerm + '%'
                    )");
                }

                if (hasOrdersBranch)
                {
                    whereClauses.Add("EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.Id = a.OrderId AND o.BranchId = @BranchId)");
                }

                var whereClause = string.Join(" AND ", whereClauses);
                var offset = (model.CurrentPage - 1) * model.PageSize;

                var countSql = $@"
                    SELECT COUNT(*)
                    FROM dbo.OrderAuditTrail a
                    WHERE {whereClause};";

                using (var countCommand = new SqlCommand(countSql, connection))
                {
                    AddAuditFilterParameters(countCommand, model, hasOrdersBranch ? activeBranchId : null);
                    model.TotalRecords = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                }

                var dataSql = $@"
                    SELECT
                        a.Id,
                        a.OrderId,
                        a.OrderNumber,
                        a.Action,
                        a.EntityType,
                        a.EntityId,
                        a.FieldName,
                        a.OldValue,
                        a.NewValue,
                        a.ChangedBy,
                        a.ChangedByName,
                        a.ChangedDate,
                        a.IPAddress,
                        a.UserAgent,
                        a.AdditionalInfo
                    FROM dbo.OrderAuditTrail a
                    WHERE {whereClause}
                    ORDER BY a.ChangedDate DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                using (var dataCommand = new SqlCommand(dataSql, connection))
                {
                    AddAuditFilterParameters(dataCommand, model, hasOrdersBranch ? activeBranchId : null);
                    dataCommand.Parameters.AddWithValue("@Offset", offset);
                    dataCommand.Parameters.AddWithValue("@PageSize", model.PageSize);

                    using (var reader = await dataCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            model.AuditRecords.Add(new OrderAuditTrail
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                OrderId = reader.GetInt32(reader.GetOrdinal("OrderId")),
                                OrderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber")) ? null : reader.GetString(reader.GetOrdinal("OrderNumber")),
                                Action = reader.GetString(reader.GetOrdinal("Action")),
                                EntityType = reader.GetString(reader.GetOrdinal("EntityType")),
                                EntityId = reader.IsDBNull(reader.GetOrdinal("EntityId")) ? null : reader.GetInt32(reader.GetOrdinal("EntityId")),
                                FieldName = reader.IsDBNull(reader.GetOrdinal("FieldName")) ? null : reader.GetString(reader.GetOrdinal("FieldName")),
                                OldValue = reader.IsDBNull(reader.GetOrdinal("OldValue")) ? null : reader.GetString(reader.GetOrdinal("OldValue")),
                                NewValue = reader.IsDBNull(reader.GetOrdinal("NewValue")) ? null : reader.GetString(reader.GetOrdinal("NewValue")),
                                ChangedBy = reader.GetInt32(reader.GetOrdinal("ChangedBy")),
                                ChangedByName = reader.IsDBNull(reader.GetOrdinal("ChangedByName")) ? string.Empty : reader.GetString(reader.GetOrdinal("ChangedByName")),
                                ChangedDate = reader.GetDateTime(reader.GetOrdinal("ChangedDate")),
                                IPAddress = reader.IsDBNull(reader.GetOrdinal("IPAddress")) ? null : reader.GetString(reader.GetOrdinal("IPAddress")),
                                UserAgent = reader.IsDBNull(reader.GetOrdinal("UserAgent")) ? null : reader.GetString(reader.GetOrdinal("UserAgent")),
                                AdditionalInfo = reader.IsDBNull(reader.GetOrdinal("AdditionalInfo")) ? null : reader.GetString(reader.GetOrdinal("AdditionalInfo"))
                            });
                        }
                    }
                }
            }
        }

        private void AddAuditFilterParameters(SqlCommand command, AuditTrailViewModel model, int? branchId)
        {
            command.Parameters.AddWithValue("@OrderId", model.OrderId.HasValue ? model.OrderId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@StartDate", model.StartDate.HasValue ? model.StartDate.Value : DBNull.Value);
            command.Parameters.AddWithValue("@EndDate", model.EndDate.HasValue ? model.EndDate.Value : DBNull.Value);
            command.Parameters.AddWithValue("@UserId", model.UserId.HasValue ? model.UserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@EntityType", !string.IsNullOrEmpty(model.EntityType) ? model.EntityType : DBNull.Value);
            command.Parameters.AddWithValue("@OrderNumber", !string.IsNullOrWhiteSpace(model.OrderNumber) ? model.OrderNumber! : DBNull.Value);
            command.Parameters.AddWithValue("@SearchTerm", !string.IsNullOrWhiteSpace(model.SearchTerm) ? model.SearchTerm! : DBNull.Value);
            if (branchId.HasValue)
            {
                command.Parameters.AddWithValue("@BranchId", branchId.Value);
            }
        }

        private async Task LoadFilterOptionsAsync(AuditTrailViewModel model, int activeBranchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");
                var branchFilter = hasOrdersBranch
                    ? "AND EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.Id = a.OrderId AND o.BranchId = @BranchId)"
                    : string.Empty;

                // Load entity types
                using (var command = new SqlCommand($"SELECT DISTINCT a.EntityType FROM dbo.OrderAuditTrail a WHERE 1=1 {branchFilter} ORDER BY a.EntityType", connection))
                {
                    if (hasOrdersBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", activeBranchId);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        model.EntityTypes.Add(("", "All Types"));
                        while (await reader.ReadAsync())
                        {
                            var entityType = reader.GetString(0);
                            model.EntityTypes.Add((entityType, entityType));
                        }
                    }
                }

                // Load users
                using (var command = new SqlCommand($"SELECT DISTINCT a.ChangedBy, a.ChangedByName FROM dbo.OrderAuditTrail a WHERE 1=1 {branchFilter} ORDER BY a.ChangedByName", connection))
                {
                    if (hasOrdersBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", activeBranchId);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        model.Users.Add(("", "All Users"));
                        while (await reader.ReadAsync())
                        {
                            var userId = reader.GetInt32(0).ToString();
                            var userName = reader.GetString(1);
                            model.Users.Add((userId, userName));
                        }
                    }
                }
            }
        }

        private async Task<bool> LoadOrderDetailsAsync(AuditTrailViewModel model, int orderId, int activeBranchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");

                var query = hasOrdersBranch
                    ? "SELECT OrderNumber FROM Orders WHERE Id = @OrderId AND BranchId = @BranchId"
                    : "SELECT OrderNumber FROM Orders WHERE Id = @OrderId";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@OrderId", orderId);
                    if (hasOrdersBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", activeBranchId);
                    }

                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        model.OrderNumber = result.ToString();
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task LoadStatisticsAsync(AuditTrailStatistics stats, int activeBranchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");
                var joinClause = hasOrdersBranch ? "INNER JOIN dbo.Orders o ON o.Id = a.OrderId" : string.Empty;
                var branchWhere = hasOrdersBranch ? "AND o.BranchId = @BranchId" : string.Empty;

                async Task<int> ExecuteCountAsync(string sql)
                {
                    using (var command = new SqlCommand(sql, connection))
                    {
                        if (hasOrdersBranch)
                        {
                            command.Parameters.AddWithValue("@BranchId", activeBranchId);
                        }

                        return Convert.ToInt32(await command.ExecuteScalarAsync());
                    }
                }

                // Total records
                stats.TotalAuditRecords = await ExecuteCountAsync($@"
                    SELECT COUNT(*)
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE 1=1 {branchWhere}");

                // Today's modifications
                stats.OrdersModifiedToday = await ExecuteCountAsync($@"
                    SELECT COUNT(DISTINCT a.OrderId)
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE CAST(a.ChangedDate AS DATE) = CAST(GETDATE() AS DATE)
                      {branchWhere}");

                // This week's modifications
                stats.OrdersModifiedThisWeek = await ExecuteCountAsync($@"
                    SELECT COUNT(DISTINCT a.OrderId)
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE a.ChangedDate >= DATEADD(DAY, -7, GETDATE())
                      {branchWhere}");

                // This month's modifications
                stats.OrdersModifiedThisMonth = await ExecuteCountAsync($@"
                    SELECT COUNT(DISTINCT a.OrderId)
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE a.ChangedDate >= DATEADD(MONTH, -1, GETDATE())
                      {branchWhere}");

                // Action breakdown
                using (var command = new SqlCommand($@"
                    SELECT a.Action, COUNT(*) as Count
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE 1=1 {branchWhere}
                    GROUP BY a.Action
                    ORDER BY Count DESC", connection))
                {
                    if (hasOrdersBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", activeBranchId);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            stats.ActionBreakdown[reader.GetString(0)] = reader.GetInt32(1);
                        }
                    }
                }

                // Entity type breakdown
                using (var command = new SqlCommand($@"
                    SELECT a.EntityType, COUNT(*) as Count
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE 1=1 {branchWhere}
                    GROUP BY a.EntityType
                    ORDER BY Count DESC", connection))
                {
                    if (hasOrdersBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", activeBranchId);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            stats.EntityTypeBreakdown[reader.GetString(0)] = reader.GetInt32(1);
                        }
                    }
                }

                // Top users
                using (var command = new SqlCommand($@"
                    SELECT TOP 10 a.ChangedByName, COUNT(*) as Count
                    FROM dbo.OrderAuditTrail a
                    {joinClause}
                    WHERE 1=1 {branchWhere}
                    GROUP BY a.ChangedByName
                    ORDER BY Count DESC", connection))
                {
                    if (hasOrdersBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", activeBranchId);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            stats.TopUsers.Add(new TopUserActivity
                            {
                                UserName = reader.GetString(0),
                                ActivityCount = reader.GetInt32(1)
                            });
                        }
                    }
                }
            }
        }

        private async Task<bool> HasColumnAsync(SqlConnection connection, string tableName, string columnName)
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

        // Helper method to log audit entries (to be called from other controllers)
        public static async Task LogAuditAsync(string connectionString, int orderId, string orderNumber, string action,
            string entityType, int? entityId, string? fieldName, string? oldValue, string? newValue,
            int changedBy, string changedByName, string? ipAddress, string? userAgent, string? additionalInfo)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand("usp_LogOrderAudit", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@OrderId", orderId);
                    command.Parameters.AddWithValue("@OrderNumber", orderNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Action", action);
                    command.Parameters.AddWithValue("@EntityType", entityType);
                    command.Parameters.AddWithValue("@EntityId", entityId.HasValue ? entityId.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@FieldName", fieldName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@OldValue", oldValue ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@NewValue", newValue ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ChangedBy", changedBy);
                    command.Parameters.AddWithValue("@ChangedByName", changedByName);
                    command.Parameters.AddWithValue("@IPAddress", ipAddress ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@UserAgent", userAgent ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@AdditionalInfo", additionalInfo ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        // ── Static helper: log a system (non-order) audit entry ──────────────
        public static async Task LogSystemAuditAsync(
            string connectionString,
            string module,
            string action,
            int? entityId,
            string? entityName,
            string? fieldName,
            string? oldValue,
            string? newValue,
            int? branchId,
            int changedBy,
            string changedByName,
            string? ipAddress = null,
            string? additionalInfo = null)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // When trigger-based audit capture is installed for this module,
                // let SQL Server triggers own the log entry to avoid duplicates.
                if (SystemAuditBootstrapper.HasTriggerBasedAuditForModule(connection, module))
                {
                    return;
                }

                // Ensure table exists (idempotent inline DDL)
                using (var ddl = new SqlCommand(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SystemAuditLog')
                    BEGIN
                        CREATE TABLE dbo.SystemAuditLog
                        (
                            Id            INT           IDENTITY(1,1) PRIMARY KEY,
                            Module        NVARCHAR(100) NOT NULL,
                            Action        NVARCHAR(50)  NOT NULL,
                            EntityId      INT           NULL,
                            EntityName    NVARCHAR(500) NULL,
                            FieldName     NVARCHAR(200) NULL,
                            OldValue      NVARCHAR(MAX) NULL,
                            NewValue      NVARCHAR(MAX) NULL,
                            BranchId      INT           NULL,
                            ChangedBy     INT           NOT NULL,
                            ChangedByName NVARCHAR(200) NOT NULL,
                            ChangedDate   DATETIME      NOT NULL DEFAULT GETDATE(),
                            IPAddress     NVARCHAR(50)  NULL,
                            AdditionalInfo NVARCHAR(MAX) NULL
                        );
                        CREATE INDEX IX_SystemAuditLog_Module      ON dbo.SystemAuditLog(Module);
                        CREATE INDEX IX_SystemAuditLog_ChangedDate ON dbo.SystemAuditLog(ChangedDate DESC);
                        CREATE INDEX IX_SystemAuditLog_ChangedBy   ON dbo.SystemAuditLog(ChangedBy);
                        CREATE INDEX IX_SystemAuditLog_BranchId    ON dbo.SystemAuditLog(BranchId);
                    END", connection))
                {
                    await ddl.ExecuteNonQueryAsync();
                }

                using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.SystemAuditLog
                        (Module, Action, EntityId, EntityName, FieldName,
                         OldValue, NewValue, BranchId,
                         ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
                    VALUES
                        (@Module, @Action, @EntityId, @EntityName, @FieldName,
                         @OldValue, @NewValue, @BranchId,
                         @ChangedBy, @ChangedByName, GETDATE(), @IPAddress, @AdditionalInfo)", connection);

                cmd.Parameters.AddWithValue("@Module",         module);
                cmd.Parameters.AddWithValue("@Action",         action);
                cmd.Parameters.AddWithValue("@EntityId",       (object?)entityId    ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EntityName",     (object?)entityName  ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FieldName",      (object?)fieldName   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OldValue",       (object?)oldValue    ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NewValue",       (object?)newValue    ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BranchId",       (object?)branchId    ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ChangedBy",      changedBy);
                cmd.Parameters.AddWithValue("@ChangedByName",  changedByName);
                cmd.Parameters.AddWithValue("@IPAddress",      (object?)ipAddress   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AdditionalInfo", (object?)additionalInfo ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Audit logging must never break the main flow — log to console for diagnostics
                System.Diagnostics.Debug.WriteLine($"[SystemAudit] Failed to log: {module}/{action} — {ex.Message}");
                Console.WriteLine($"[SystemAudit ERROR] {module}/{action} entity={entityName}: {ex.Message}");
            }
        }

        // GET: AuditTrail/SystemLogsDiag  — quick JSON diagnostic (no view needed)
        [HttpGet]
        public async Task<IActionResult> SystemLogsDiag()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var tblExists = Convert.ToInt32(await new SqlCommand(
                    "SELECT COUNT(1) FROM sys.tables WHERE name='SystemAuditLog'", conn).ExecuteScalarAsync()) > 0;
                if (!tblExists) return Json(new { tableExists = false, rowCount = 0 });

                var rowCount = Convert.ToInt32(await new SqlCommand(
                    "SELECT COUNT(*) FROM dbo.SystemAuditLog", conn).ExecuteScalarAsync());

                var recentRows = new List<object>();
                using var cmd = new SqlCommand(
                    "SELECT TOP 5 Id, Module, Action, EntityName, BranchId, ChangedByName, ChangedDate FROM dbo.SystemAuditLog ORDER BY ChangedDate DESC", conn);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    recentRows.Add(new
                    {
                        Id         = rdr.GetInt32(0),
                        Module     = rdr.GetString(1),
                        Action     = rdr.GetString(2),
                        EntityName = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                        BranchId   = rdr.IsDBNull(4) ? (int?)null : rdr.GetInt32(4),
                        ChangedBy  = rdr.GetString(5),
                        ChangedDate= rdr.GetDateTime(6).ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }

                return Json(new { tableExists = true, rowCount, recentRows });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // GET: AuditTrail/SystemLogs  — view system (non-order) audit log
        public async Task<IActionResult> SystemLogs(
            string? module, string? auditAction, int? userId,
            DateTime? startDate, DateTime? endDate,
            string? searchTerm, int page = 1, int pageSize = 50)
        {
            var activeBranchId = User.GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                TempData["ErrorMessage"] = "No active branch selected.";
                return RedirectToAction("Index", "Home");
            }

            if (!startDate.HasValue) startDate = DateTime.Now.AddDays(-30);
            if (!endDate.HasValue)   endDate   = DateTime.Now.AddDays(1);

            var vm = new SystemAuditLogViewModel
            {
                Module      = module,
                Action      = auditAction,
                UserId      = userId,
                StartDate   = startDate,
                EndDate     = endDate,
                SearchTerm  = searchTerm,
                CurrentPage = page,
                PageSize    = pageSize
            };

            try
            {
                await LoadSystemAuditDataAsync(vm, activeBranchId.Value);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error loading system audit log: {ex.Message}";
            }

            return View(vm);
        }

        private async Task LoadSystemAuditDataAsync(SystemAuditLogViewModel vm, int branchId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check table exists
            var tblExists = Convert.ToInt32(await new SqlCommand(
                "SELECT COUNT(1) FROM sys.tables WHERE name='SystemAuditLog'", connection).ExecuteScalarAsync()) > 0;
            if (!tblExists) { vm.TotalRecords = 0; return; }

            var where = new List<string>
            {
                "(@StartDate IS NULL OR ChangedDate >= @StartDate)",
                "(@EndDate   IS NULL OR ChangedDate <= @EndDate)",
                "(@UserId    IS NULL OR ChangedBy   = @UserId)",
                "(@Module    IS NULL OR Module       = @Module)",
                "(@Action    IS NULL OR Action       = @Action)"
            };
            if (!string.IsNullOrWhiteSpace(vm.SearchTerm))
            {
                where.Add(@"(Module LIKE '%'+@Search+'%' OR Action LIKE '%'+@Search+'%'
                    OR EntityName LIKE '%'+@Search+'%' OR ChangedByName LIKE '%'+@Search+'%'
                    OR OldValue LIKE '%'+@Search+'%' OR NewValue LIKE '%'+@Search+'%')");
            }
            var whereClause = string.Join(" AND ", where);
            var offset = (vm.CurrentPage - 1) * vm.PageSize;

            void AddParams(SqlCommand c)
            {
                c.Parameters.AddWithValue("@StartDate", (object?)vm.StartDate ?? DBNull.Value);
                c.Parameters.AddWithValue("@EndDate",   (object?)vm.EndDate   ?? DBNull.Value);
                c.Parameters.AddWithValue("@UserId",    (object?)vm.UserId    ?? DBNull.Value);
                c.Parameters.AddWithValue("@Module",    string.IsNullOrWhiteSpace(vm.Module) ? (object)DBNull.Value : vm.Module);
                c.Parameters.AddWithValue("@Action",    string.IsNullOrWhiteSpace(vm.Action) ? (object)DBNull.Value : vm.Action);
                if (!string.IsNullOrWhiteSpace(vm.SearchTerm))
                    c.Parameters.AddWithValue("@Search", vm.SearchTerm);
            }

            using (var cnt = new SqlCommand($"SELECT COUNT(*) FROM dbo.SystemAuditLog WHERE {whereClause}", connection))
            {
                AddParams(cnt);
                vm.TotalRecords = Convert.ToInt32(await cnt.ExecuteScalarAsync());
            }

            using (var dat = new SqlCommand($@"
                SELECT Id, Module, Action, EntityId, EntityName, FieldName,
                       OldValue, NewValue, BranchId, ChangedBy, ChangedByName,
                       ChangedDate, IPAddress, AdditionalInfo
                FROM dbo.SystemAuditLog
                WHERE {whereClause}
                ORDER BY ChangedDate DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", connection))
            {
                AddParams(dat);
                dat.Parameters.AddWithValue("@Offset",   offset);
                dat.Parameters.AddWithValue("@PageSize",  vm.PageSize);

                using var reader = await dat.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    vm.Records.Add(new SystemAuditLogEntry
                    {
                        Id            = reader.GetInt32(0),
                        Module        = reader.GetString(1),
                        Action        = reader.GetString(2),
                        EntityId      = reader.IsDBNull(3)  ? null : reader.GetInt32(3),
                        EntityName    = reader.IsDBNull(4)  ? null : reader.GetString(4),
                        FieldName     = reader.IsDBNull(5)  ? null : reader.GetString(5),
                        OldValue      = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        NewValue      = reader.IsDBNull(7)  ? null : reader.GetString(7),
                        BranchId      = reader.IsDBNull(8)  ? null : reader.GetInt32(8),
                        ChangedBy     = reader.GetInt32(9),
                        ChangedByName = reader.GetString(10),
                        ChangedDate   = reader.GetDateTime(11),
                        IPAddress     = reader.IsDBNull(12) ? null : reader.GetString(12),
                        AdditionalInfo= reader.IsDBNull(13) ? null : reader.GetString(13)
                    });
                }
            }

            // Populate filter drop-down options
            using (var modCmd = new SqlCommand("SELECT DISTINCT Module FROM dbo.SystemAuditLog ORDER BY Module", connection))
            using (var modReader = await modCmd.ExecuteReaderAsync())
            {
                vm.Modules.Add(("", "All Modules"));
                while (await modReader.ReadAsync())
                    vm.Modules.Add((modReader.GetString(0), modReader.GetString(0)));
            }

            using (var usrCmd = new SqlCommand("SELECT DISTINCT ChangedBy, ChangedByName FROM dbo.SystemAuditLog ORDER BY ChangedByName", connection))
            using (var usrReader = await usrCmd.ExecuteReaderAsync())
            {
                vm.Users.Add(("", "All Users"));
                while (await usrReader.ReadAsync())
                    vm.Users.Add((usrReader.GetInt32(0).ToString(), usrReader.GetString(1)));
            }
        }
    }
}
