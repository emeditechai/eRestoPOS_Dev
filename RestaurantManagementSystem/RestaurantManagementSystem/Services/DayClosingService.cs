using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RestaurantManagementSystem.Models;

namespace RestaurantManagementSystem.Services
{
    public interface IDayClosingService
    {
        Task<List<CashierOption>> GetAvailableCashiersAsync(DateTime businessDate, int? branchId = null);
        Task<(bool Success, string Message)> InitializeDayOpeningAsync(DateTime businessDate, int cashierId, decimal openingFloat, string createdBy, int? branchId = null);
        Task<decimal> GetCashierSystemAmountAsync(DateTime businessDate, int cashierId, int? branchId = null);
        Task<List<CashierDayCloseViewModel>> GetDayClosingSummaryAsync(DateTime businessDate, int? branchId = null);
        Task<DayLockStatus?> GetDayLockStatusAsync(DateTime businessDate, int? branchId = null);
        Task<(bool Success, string Message, decimal Variance)> SaveDeclaredCashAsync(DateTime businessDate, int cashierId, decimal declaredAmount, string updatedBy, int? branchId = null);
        Task<(bool Success, string Message)> ApproveVarianceAsync(int closeId, string approvedBy, string comment, bool approved, int? branchId = null);
        Task<(bool Success, string Message, int IssueCount)> LockDayAsync(DateTime businessDate, string lockedBy, string? remarks, int? branchId = null);
        Task<EODReportViewModel> GenerateEODReportAsync(DateTime businessDate, string generatedBy, int? branchId = null);
        Task<bool> UpdateCashierSystemAmountsAsync(DateTime businessDate, int? branchId = null);
        Task<CashClosingReportViewModel> GenerateCashClosingReportAsync(DateTime startDate, DateTime endDate, int? cashierId = null, int? branchId = null);
    }

    public class DayClosingService : IDayClosingService
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public DayClosingService(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        private async Task<bool> StoredProcedureHasParameterAsync(SqlConnection connection, string procedureName, string parameterName)
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(1)
                    FROM sys.parameters
                    WHERE object_id = OBJECT_ID(@ProcedureName)
                      AND name = @ParameterName", connection);
                cmd.Parameters.AddWithValue("@ProcedureName", procedureName);
                cmd.Parameters.AddWithValue("@ParameterName", parameterName);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                return false;
            }
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

        /// <summary>
        /// Get list of cashiers available for day opening
        /// </summary>
        public async Task<List<CashierOption>> GetAvailableCashiersAsync(DateTime businessDate, int? branchId = null)
        {
            var cashiers = new List<CashierOption>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasUsersBranch = await HasColumnAsync(connection, "Users", "BranchId");
                var hasOpeningBranch = await HasColumnAsync(connection, "CashierDayOpening", "BranchId");

                var openingJoin = hasOpeningBranch && branchId.HasValue
                    ? "LEFT JOIN CashierDayOpening cdo ON u.Id = cdo.CashierId AND cdo.BusinessDate = @BusinessDate AND cdo.BranchId = @BranchId"
                    : "LEFT JOIN CashierDayOpening cdo ON u.Id = cdo.CashierId AND cdo.BusinessDate = @BusinessDate";

                var branchPredicate = hasUsersBranch && branchId.HasValue
                    ? "AND u.BranchId = @BranchId"
                    : string.Empty;

                var query = $@"
                    SELECT DISTINCT 
                        u.Id, 
                        u.Username, 
                        u.FullName,
                        CASE WHEN cdo.Id IS NOT NULL THEN 1 ELSE 0 END AS AlreadyInitialized
                    FROM Users u
                    INNER JOIN UserRoles ur ON u.Id = ur.UserId
                    INNER JOIN Roles r ON ur.RoleId = r.Id
                    {openingJoin}
                    WHERE r.Name IN ('Cashier', 'Manager', 'Administrator')
                      AND u.IsActive = 1
                      {branchPredicate}
                    ORDER BY u.Username";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    if (branchId.HasValue && (hasUsersBranch || hasOpeningBranch))
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            cashiers.Add(new CashierOption
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.IsDBNull(2) ? reader.GetString(1) : reader.GetString(2),
                                Username = reader.GetString(1),
                                AlreadyInitialized = reader.GetInt32(3) == 1
                            });
                        }
                    }
                }
            }

            return cashiers;
        }

        /// <summary>
        /// Initialize day opening for a cashier
        /// </summary>
        public async Task<(bool Success, string Message)> InitializeDayOpeningAsync(
            DateTime businessDate, int cashierId, decimal openingFloat, string createdBy, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasOpeningBranch = await HasColumnAsync(connection, "CashierDayOpening", "BranchId");
                var hasCloseBranch = await HasColumnAsync(connection, "CashierDayClose", "BranchId");
                var spHasBranch = await StoredProcedureHasParameterAsync(connection, "usp_InitializeDayOpening", "@BranchId");

                using (var command = new SqlCommand("usp_InitializeDayOpening", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    command.Parameters.AddWithValue("@CashierId", cashierId);
                    command.Parameters.AddWithValue("@OpeningFloat", openingFloat);
                    command.Parameters.AddWithValue("@CreatedBy", createdBy);
                    if (branchId.HasValue && spHasBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    try
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var message = reader["Message"].ToString() ?? "Success";
                                return (true, message);
                            }
                        }

                        if (branchId.HasValue && !spHasBranch)
                        {
                            if (hasOpeningBranch)
                            {
                                using var openingCmd = new SqlCommand(@"
                                    UPDATE CashierDayOpening
                                    SET BranchId = @BranchId
                                    WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId", connection);
                                openingCmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                                openingCmd.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                                openingCmd.Parameters.AddWithValue("@CashierId", cashierId);
                                await openingCmd.ExecuteNonQueryAsync();
                            }

                            if (hasCloseBranch)
                            {
                                using var closeCmd = new SqlCommand(@"
                                    UPDATE CashierDayClose
                                    SET BranchId = @BranchId
                                    WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId", connection);
                                closeCmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                                closeCmd.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                                closeCmd.Parameters.AddWithValue("@CashierId", cashierId);
                                await closeCmd.ExecuteNonQueryAsync();
                            }
                        }

                        return (true, "Opening float initialized successfully");
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Error: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Get system cash amount for a cashier
        /// </summary>
        public async Task<decimal> GetCashierSystemAmountAsync(DateTime businessDate, int cashierId, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var spHasBranch = await StoredProcedureHasParameterAsync(connection, "usp_GetCashierSystemAmount", "@BranchId")
                    || await StoredProcedureHasParameterAsync(connection, "usp_GetCashierSystemAmount", "@BranchID");

                using (var command = new SqlCommand("usp_GetCashierSystemAmount", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    command.Parameters.AddWithValue("@CashierId", cashierId);
                    if (branchId.HasValue)
                    {
                        if (await StoredProcedureHasParameterAsync(connection, "usp_GetCashierSystemAmount", "@BranchId"))
                        {
                            command.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }
                        else if (await StoredProcedureHasParameterAsync(connection, "usp_GetCashierSystemAmount", "@BranchID"))
                        {
                            command.Parameters.AddWithValue("@BranchID", branchId.Value);
                        }
                    }

                    if (spHasBranch || !branchId.HasValue)
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return reader.GetDecimal(0);
                            }
                        }
                    }
                }

                if (branchId.HasValue)
                {
                    var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");
                    var branchFilter = hasOrdersBranch ? "AND o.BranchId = @BranchId" : string.Empty;

                    using var fallbackCommand = new SqlCommand($@"
                        SELECT ISNULL(SUM(p.Amount + ISNULL(p.RoundoffAdjustmentAmt, 0)), 0) AS SystemAmount
                        FROM Orders o
                        INNER JOIN Payments p ON p.OrderId = o.Id
                        INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
                        WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
                          AND o.CashierId = @CashierId
                          AND pm.Name = 'CASH'
                          AND p.Status = 1
                          AND o.Status IN (2, 3)
                          {branchFilter}", connection);

                    fallbackCommand.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                    fallbackCommand.Parameters.AddWithValue("@CashierId", cashierId);
                    if (hasOrdersBranch)
                    {
                        fallbackCommand.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    var amount = await fallbackCommand.ExecuteScalarAsync();
                    return amount == DBNull.Value || amount == null ? 0 : Convert.ToDecimal(amount);
                }
            }

            return 0;
        }

        /// <summary>
        /// Update system amounts for all cashiers on a given date
        /// </summary>
        public async Task<bool> UpdateCashierSystemAmountsAsync(DateTime businessDate, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasCloseBranch = await HasColumnAsync(connection, "CashierDayClose", "BranchId");
                var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");

                var orderBranchFilter = hasOrdersBranch && branchId.HasValue ? "AND o.BranchId = @BranchId" : string.Empty;
                var closeBranchFilter = hasCloseBranch && branchId.HasValue ? "AND cdc.BranchId = @BranchId" : string.Empty;

                // Query using Payments table (actual schema)
                var query = $@"
                    -- Refresh SystemAmount per cashier for the business date
                    -- Prefer Orders.CashierId; fallback to Payments.ProcessedBy when Orders.CashierId is NULL
                    UPDATE cdc
                    SET cdc.SystemAmount = ISNULL(ROUND(cashSummary.CashAmount, 0), 0) -- adjust to whole rupees including roundoff
                    FROM CashierDayClose cdc
                    LEFT JOIN (
                        SELECT 
                            COALESCE(o.CashierId, p.ProcessedBy) AS CashierId,
                            SUM(p.Amount + ISNULL(p.RoundoffAdjustmentAmt, 0)) AS CashAmount -- include per-payment roundoff adjustments
                        FROM Orders o
                        INNER JOIN Payments p ON p.OrderId = o.Id
                        INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
                        WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
                          AND pm.Name = 'CASH'
                          AND p.Status = 1
                          AND o.Status IN (2, 3)
                          {orderBranchFilter}
                        GROUP BY COALESCE(o.CashierId, p.ProcessedBy)
                    ) cashSummary ON cdc.CashierId = cashSummary.CashierId
                    WHERE cdc.BusinessDate = @BusinessDate
                      {closeBranchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    if (branchId.HasValue && (hasCloseBranch || hasOrdersBranch))
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                    await command.ExecuteNonQueryAsync();
                    return true;
                }
            }
        }

        /// <summary>
        /// Get day closing summary for all cashiers
        /// </summary>
        public async Task<List<CashierDayCloseViewModel>> GetDayClosingSummaryAsync(DateTime businessDate, int? branchId = null)
        {
            // First update system amounts
            await UpdateCashierSystemAmountsAsync(businessDate, branchId);

            var closings = new List<CashierDayCloseViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasCloseBranch = await HasColumnAsync(connection, "CashierDayClose", "BranchId");
                var spHasBranch = await StoredProcedureHasParameterAsync(connection, "usp_GetDayClosingSummary", "@BranchId")
                    || await StoredProcedureHasParameterAsync(connection, "usp_GetDayClosingSummary", "@BranchID");

                using (var command = new SqlCommand("usp_GetDayClosingSummary", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    if (branchId.HasValue)
                    {
                        if (await StoredProcedureHasParameterAsync(connection, "usp_GetDayClosingSummary", "@BranchId"))
                        {
                            command.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }
                        else if (await StoredProcedureHasParameterAsync(connection, "usp_GetDayClosingSummary", "@BranchID"))
                        {
                            command.Parameters.AddWithValue("@BranchID", branchId.Value);
                        }
                    }

                    if (!branchId.HasValue || spHasBranch)
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var status = reader.GetString(reader.GetOrdinal("Status"));
                                var variance = reader.IsDBNull(reader.GetOrdinal("Variance"))
                                    ? (decimal?)null
                                    : reader.GetDecimal(reader.GetOrdinal("Variance"));

                                closings.Add(new CashierDayCloseViewModel
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                    CashierId = reader.GetInt32(reader.GetOrdinal("CashierId")),
                                    CashierName = reader.GetString(reader.GetOrdinal("CashierName")),
                                    OpeningFloat = reader.GetDecimal(reader.GetOrdinal("OpeningFloat")),
                                    SystemAmount = reader.GetDecimal(reader.GetOrdinal("SystemAmount")),
                                    DeclaredAmount = reader.IsDBNull(reader.GetOrdinal("DeclaredAmount"))
                                        ? (decimal?)null
                                        : reader.GetDecimal(reader.GetOrdinal("DeclaredAmount")),
                                    ExpectedCash = reader.GetDecimal(reader.GetOrdinal("ExpectedCash")),
                                    Variance = variance,
                                    Status = status,
                                    ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("ApprovedBy")),
                                    ApprovalComment = reader.IsDBNull(reader.GetOrdinal("ApprovalComment"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("ApprovalComment")),
                                    LockedFlag = reader.GetBoolean(reader.GetOrdinal("LockedFlag")),
                                    LockedAt = reader.IsDBNull(reader.GetOrdinal("LockedAt"))
                                        ? (DateTime?)null
                                        : reader.GetDateTime(reader.GetOrdinal("LockedAt")),
                                    LockedBy = reader.IsDBNull(reader.GetOrdinal("LockedBy"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("LockedBy")),
                                    StatusBadgeClass = GetStatusBadgeClass(status),
                                    StatusIcon = GetStatusIcon(status),
                                    RequiresApproval = status == "CHECK"
                                });
                            }
                        }

                        return closings;
                    }

                    var summarySql = hasCloseBranch
                        ? @"SELECT 
                                cdc.Id,
                                cdc.CashierId,
                                cdc.CashierName,
                                cdc.OpeningFloat,
                                cdc.SystemAmount,
                                cdc.DeclaredAmount,
                                cdc.Variance,
                                cdc.Status,
                                cdc.ApprovedBy,
                                cdc.ApprovalComment,
                                cdc.LockedFlag,
                                cdc.LockedAt,
                                cdc.LockedBy,
                                (cdc.SystemAmount + cdc.OpeningFloat) AS ExpectedCash
                           FROM CashierDayClose cdc
                           WHERE cdc.BusinessDate = @BusinessDate
                             AND cdc.BranchId = @BranchId
                           ORDER BY cdc.CashierName"
                        : @"SELECT 
                                cdc.Id,
                                cdc.CashierId,
                                cdc.CashierName,
                                cdc.OpeningFloat,
                                cdc.SystemAmount,
                                cdc.DeclaredAmount,
                                cdc.Variance,
                                cdc.Status,
                                cdc.ApprovedBy,
                                cdc.ApprovalComment,
                                cdc.LockedFlag,
                                cdc.LockedAt,
                                cdc.LockedBy,
                                (cdc.SystemAmount + cdc.OpeningFloat) AS ExpectedCash
                           FROM CashierDayClose cdc
                           WHERE cdc.BusinessDate = @BusinessDate
                           ORDER BY cdc.CashierName";

                    using var fallbackCommand = new SqlCommand(summarySql, connection);
                    fallbackCommand.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                    if (hasCloseBranch)
                    {
                        fallbackCommand.Parameters.AddWithValue("@BranchId", branchId!.Value);
                    }

                    using (var reader = await fallbackCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var status = reader.GetString(reader.GetOrdinal("Status"));
                            var variance = reader.IsDBNull(reader.GetOrdinal("Variance"))
                                ? (decimal?)null
                                : reader.GetDecimal(reader.GetOrdinal("Variance"));

                            closings.Add(new CashierDayCloseViewModel
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                CashierId = reader.GetInt32(reader.GetOrdinal("CashierId")),
                                CashierName = reader.GetString(reader.GetOrdinal("CashierName")),
                                OpeningFloat = reader.GetDecimal(reader.GetOrdinal("OpeningFloat")),
                                SystemAmount = reader.GetDecimal(reader.GetOrdinal("SystemAmount")),
                                DeclaredAmount = reader.IsDBNull(reader.GetOrdinal("DeclaredAmount"))
                                    ? (decimal?)null
                                    : reader.GetDecimal(reader.GetOrdinal("DeclaredAmount")),
                                ExpectedCash = reader.GetDecimal(reader.GetOrdinal("ExpectedCash")),
                                Variance = variance,
                                Status = status,
                                ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("ApprovedBy")),
                                ApprovalComment = reader.IsDBNull(reader.GetOrdinal("ApprovalComment"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("ApprovalComment")),
                                LockedFlag = reader.GetBoolean(reader.GetOrdinal("LockedFlag")),
                                LockedAt = reader.IsDBNull(reader.GetOrdinal("LockedAt"))
                                    ? (DateTime?)null
                                    : reader.GetDateTime(reader.GetOrdinal("LockedAt")),
                                LockedBy = reader.IsDBNull(reader.GetOrdinal("LockedBy"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("LockedBy")),
                                StatusBadgeClass = GetStatusBadgeClass(status),
                                StatusIcon = GetStatusIcon(status),
                                RequiresApproval = status == "CHECK"
                            });
                        }
                    }
                }
            }

            return closings;
        }

        /// <summary>
        /// Get day lock status
        /// </summary>
        public async Task<DayLockStatus?> GetDayLockStatusAsync(DateTime businessDate, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasAuditBranch = await HasColumnAsync(connection, "DayLockAudit", "BranchId");
                var branchFilter = hasAuditBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT TOP 1
                        LockId, BusinessDate, LockedBy, LockTime, Remarks, Status
                    FROM DayLockAudit
                    WHERE BusinessDate = @BusinessDate
                      {branchFilter}
                    ORDER BY LockTime DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    if (hasAuditBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new DayLockStatus
                            {
                                LockId = reader.GetInt32(0),
                                BusinessDate = reader.GetDateTime(1),
                                LockedBy = reader.GetString(2),
                                LockTime = reader.GetDateTime(3),
                                Remarks = reader.IsDBNull(4) ? null : reader.GetString(4),
                                Status = reader.GetString(5)
                            };
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Save declared cash amount for a cashier
        /// </summary>
        public async Task<(bool Success, string Message, decimal Variance)> SaveDeclaredCashAsync(
            DateTime businessDate, int cashierId, decimal declaredAmount, string updatedBy, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasCloseBranch = await HasColumnAsync(connection, "CashierDayClose", "BranchId");
                var spHasBranch = await StoredProcedureHasParameterAsync(connection, "usp_SaveDeclaredCash", "@BranchId")
                    || await StoredProcedureHasParameterAsync(connection, "usp_SaveDeclaredCash", "@BranchID");

                if (branchId.HasValue && hasCloseBranch && !spHasBranch)
                {
                    using var tx = connection.BeginTransaction();
                    try
                    {
                        decimal openingFloat;
                        decimal systemAmount;

                        using (var fetchCommand = new SqlCommand(@"
                            SELECT TOP 1 OpeningFloat, SystemAmount
                            FROM CashierDayClose
                            WHERE BusinessDate = @BusinessDate
                              AND CashierId = @CashierId
                              AND BranchId = @BranchId", connection, tx))
                        {
                            fetchCommand.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                            fetchCommand.Parameters.AddWithValue("@CashierId", cashierId);
                            fetchCommand.Parameters.AddWithValue("@BranchId", branchId.Value);

                            using var reader = await fetchCommand.ExecuteReaderAsync();
                            if (!await reader.ReadAsync())
                            {
                                await reader.CloseAsync();
                                tx.Rollback();
                                return (false, "Cashier closing record not found for selected branch", 0);
                            }

                            openingFloat = reader.GetDecimal(0);
                            systemAmount = reader.GetDecimal(1);
                        }

                        var variance = (declaredAmount + openingFloat) - systemAmount;
                        var status = Math.Abs(variance) > 100 ? "CHECK" : "OK";

                        using (var updateCommand = new SqlCommand(@"
                            UPDATE CashierDayClose
                            SET DeclaredAmount = @DeclaredAmount,
                                Variance = @Variance,
                                Status = @Status,
                                UpdatedBy = @UpdatedBy,
                                UpdatedAt = GETDATE()
                            WHERE BusinessDate = @BusinessDate
                              AND CashierId = @CashierId
                              AND BranchId = @BranchId", connection, tx))
                        {
                            updateCommand.Parameters.AddWithValue("@DeclaredAmount", declaredAmount);
                            updateCommand.Parameters.AddWithValue("@Variance", variance);
                            updateCommand.Parameters.AddWithValue("@Status", status);
                            updateCommand.Parameters.AddWithValue("@UpdatedBy", updatedBy);
                            updateCommand.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                            updateCommand.Parameters.AddWithValue("@CashierId", cashierId);
                            updateCommand.Parameters.AddWithValue("@BranchId", branchId.Value);

                            var rows = await updateCommand.ExecuteNonQueryAsync();
                            if (rows <= 0)
                            {
                                tx.Rollback();
                                return (false, "No record updated for selected branch", 0);
                            }
                        }

                        tx.Commit();
                        return (true, "Cash declaration saved successfully", variance);
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return (false, $"Error: {ex.Message}", 0);
                    }
                }

                using (var command = new SqlCommand("usp_SaveDeclaredCash", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    command.Parameters.AddWithValue("@CashierId", cashierId);
                    command.Parameters.AddWithValue("@DeclaredAmount", declaredAmount);
                    command.Parameters.AddWithValue("@UpdatedBy", updatedBy);
                    if (branchId.HasValue)
                    {
                        if (await StoredProcedureHasParameterAsync(connection, "usp_SaveDeclaredCash", "@BranchId"))
                        {
                            command.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }
                        else if (await StoredProcedureHasParameterAsync(connection, "usp_SaveDeclaredCash", "@BranchID"))
                        {
                            command.Parameters.AddWithValue("@BranchID", branchId.Value);
                        }
                    }

                    try
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var result = reader["Result"].ToString();
                                var variance = reader.GetDecimal(reader.GetOrdinal("Variance"));
                                var message = reader["Message"].ToString() ?? "Success";

                                return (true, message, variance);
                            }
                        }
                        return (false, "No response from database", 0);
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Error: {ex.Message}", 0);
                    }
                }
            }
        }

        /// <summary>
        /// Approve or reject variance
        /// </summary>
        public async Task<(bool Success, string Message)> ApproveVarianceAsync(
            int closeId, string approvedBy, string comment, bool approved, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasCloseBranch = await HasColumnAsync(connection, "CashierDayClose", "BranchId");
                var branchFilter = hasCloseBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    UPDATE CashierDayClose
                    SET Status = @Status,
                        ApprovedBy = @ApprovedBy,
                        ApprovalComment = @Comment,
                        UpdatedBy = @ApprovedBy,
                        UpdatedAt = GETDATE()
                    WHERE Id = @CloseId
                      {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CloseId", closeId);
                    command.Parameters.AddWithValue("@Status", approved ? "OK" : "CHECK");
                    command.Parameters.AddWithValue("@ApprovedBy", approvedBy);
                    command.Parameters.AddWithValue("@Comment", comment);
                    if (hasCloseBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        return (true, approved ? "Variance approved successfully" : "Variance requires re-verification");
                    }
                    return (false, "Failed to update approval status");
                }
            }
        }

        /// <summary>
        /// Lock the business day
        /// </summary>
        public async Task<(bool Success, string Message, int IssueCount)> LockDayAsync(
            DateTime businessDate, string lockedBy, string? remarks, int? branchId = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasCloseBranch = await HasColumnAsync(connection, "CashierDayClose", "BranchId");
                var hasAuditBranch = await HasColumnAsync(connection, "DayLockAudit", "BranchId");
                var spHasBranch = await StoredProcedureHasParameterAsync(connection, "usp_LockDay", "@BranchId")
                    || await StoredProcedureHasParameterAsync(connection, "usp_LockDay", "@BranchID");

                if (branchId.HasValue && hasCloseBranch && !spHasBranch)
                {
                    using var tx = connection.BeginTransaction();
                    try
                    {
                        int issueCount;
                        using (var issueCmd = new SqlCommand(@"
                            SELECT COUNT(*)
                            FROM CashierDayClose
                            WHERE BusinessDate = @BusinessDate
                              AND Status = 'CHECK'
                              AND LockedFlag = 0
                              AND BranchId = @BranchId", connection, tx))
                        {
                            issueCmd.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                            issueCmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                            issueCount = Convert.ToInt32(await issueCmd.ExecuteScalarAsync());
                        }

                        if (issueCount > 0)
                        {
                            tx.Rollback();
                            return (false, $"Cannot lock day: {issueCount} cashier(s) have unresolved variances", issueCount);
                        }

                        using (var lockCmd = new SqlCommand(@"
                            UPDATE CashierDayClose
                            SET LockedFlag = 1,
                                LockedAt = GETDATE(),
                                LockedBy = @LockedBy,
                                Status = 'LOCKED',
                                UpdatedBy = @LockedBy,
                                UpdatedAt = GETDATE()
                            WHERE BusinessDate = @BusinessDate
                              AND BranchId = @BranchId", connection, tx))
                        {
                            lockCmd.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                            lockCmd.Parameters.AddWithValue("@LockedBy", lockedBy);
                            lockCmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                            await lockCmd.ExecuteNonQueryAsync();
                        }

                        var auditSql = hasAuditBranch
                            ? @"INSERT INTO DayLockAudit (BusinessDate, LockedBy, LockTime, Remarks, Status, BranchId)
                                VALUES (@BusinessDate, @LockedBy, GETDATE(), @Remarks, 'LOCKED', @BranchId)"
                            : @"INSERT INTO DayLockAudit (BusinessDate, LockedBy, LockTime, Remarks, Status)
                                VALUES (@BusinessDate, @LockedBy, GETDATE(), @Remarks, 'LOCKED')";

                        using (var auditCmd = new SqlCommand(auditSql, connection, tx))
                        {
                            auditCmd.Parameters.AddWithValue("@BusinessDate", businessDate.Date);
                            auditCmd.Parameters.AddWithValue("@LockedBy", lockedBy);
                            auditCmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
                            if (hasAuditBranch)
                            {
                                auditCmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                            }
                            await auditCmd.ExecuteNonQueryAsync();
                        }

                        tx.Commit();
                        return (true, "Day locked successfully", 0);
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return (false, $"Error: {ex.Message}", 0);
                    }
                }

                using (var command = new SqlCommand("usp_LockDay", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                    command.Parameters.AddWithValue("@LockedBy", lockedBy);
                    command.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
                    if (branchId.HasValue)
                    {
                        if (await StoredProcedureHasParameterAsync(connection, "usp_LockDay", "@BranchId"))
                        {
                            command.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }
                        else if (await StoredProcedureHasParameterAsync(connection, "usp_LockDay", "@BranchID"))
                        {
                            command.Parameters.AddWithValue("@BranchID", branchId.Value);
                        }
                    }

                    try
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var result = reader["Result"].ToString();
                                var message = reader["Message"].ToString() ?? "Unknown result";

                                if (result == "Success")
                                {
                                    return (true, message, 0);
                                }
                                else
                                {
                                    var issueCount = reader.IsDBNull(reader.GetOrdinal("IssueCount")) 
                                        ? 0 
                                        : reader.GetInt32(reader.GetOrdinal("IssueCount"));
                                    return (false, message, issueCount);
                                }
                            }
                        }
                        return (false, "No response from database", 0);
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Error: {ex.Message}", 0);
                    }
                }
            }
        }

        /// <summary>
        /// Generate EOD Report
        /// </summary>
        public async Task<EODReportViewModel> GenerateEODReportAsync(DateTime businessDate, string generatedBy, int? branchId = null)
        {
            var report = new EODReportViewModel
            {
                BusinessDate = businessDate,
                GeneratedBy = generatedBy,
                GeneratedAt = DateTime.Now
            };

            // Get cashier details
            report.CashierDetails = await GetDayClosingSummaryAsync(businessDate, branchId);

            // Get lock status
            report.LockStatus = await GetDayLockStatusAsync(businessDate, branchId);

            // Calculate summary
            report.Summary = new DaySummary
            {
                TotalCashiers = report.CashierDetails.Count,
                PendingCount = report.CashierDetails.Count(c => c.Status == "PENDING"),
                OkCount = report.CashierDetails.Count(c => c.Status == "OK"),
                CheckCount = report.CashierDetails.Count(c => c.Status == "CHECK"),
                LockedCount = report.CashierDetails.Count(c => c.Status == "LOCKED"),
                TotalOpeningFloat = report.CashierDetails.Sum(c => c.OpeningFloat),
                TotalSystemAmount = report.CashierDetails.Sum(c => c.SystemAmount),
                TotalDeclaredAmount = report.CashierDetails.Sum(c => c.DeclaredAmount ?? 0),
                TotalVariance = report.CashierDetails.Sum(c => c.Variance ?? 0),
                TotalExpectedCash = report.CashierDetails.Sum(c => c.ExpectedCash)
            };

            // Get restaurant settings
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasSettingsBranch = await HasColumnAsync(connection, "RestaurantSettings", "BranchId");
                var hasOrdersBranch = await HasColumnAsync(connection, "Orders", "BranchId");

                var query = hasSettingsBranch && branchId.HasValue
                    ? "SELECT TOP 1 RestaurantName FROM RestaurantSettings WHERE BranchId = @BranchId ORDER BY Id DESC"
                    : "SELECT TOP 1 RestaurantName FROM RestaurantSettings ORDER BY Id DESC";
                using (var command = new SqlCommand(query, connection))
                {
                    if (hasSettingsBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                    var name = await command.ExecuteScalarAsync();
                    report.RestaurantName = name?.ToString() ?? "Restaurant";
                }

                // Get sales summary
                var orderBranchFilter = hasOrdersBranch && branchId.HasValue ? "AND o.BranchId = @BranchId" : string.Empty;

                var salesQuery = $@"
                    SELECT 
                        COUNT(DISTINCT o.Id) AS TotalOrders,
                        ISNULL(SUM(o.TotalAmount), 0) AS TotalSales,
                        ISNULL(SUM(CASE WHEN pm.Name = 'CASH' THEN p.Amount ELSE 0 END), 0) AS CashSales,
                        ISNULL(SUM(CASE WHEN pm.Name IN ('CREDIT_CARD', 'DEBIT_CARD') THEN p.Amount ELSE 0 END), 0) AS CardSales,
                        ISNULL(SUM(CASE WHEN pm.Name NOT IN ('CASH', 'CREDIT_CARD', 'DEBIT_CARD') THEN p.Amount ELSE 0 END), 0) AS OtherSales,
                        COUNT(DISTINCT CASE WHEN o.CustomerName IS NOT NULL THEN o.CustomerName ELSE NULL END) AS TotalCustomers
                    FROM Orders o
                    LEFT JOIN Payments p ON p.OrderId = o.Id AND p.Status = 1
                    LEFT JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
                    WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
                                            AND o.Status IN (2, 3)
                                            {orderBranchFilter}";

                using (var command = new SqlCommand(salesQuery, connection))
                {
                    command.Parameters.AddWithValue("@BusinessDate", businessDate);
                                        if (hasOrdersBranch && branchId.HasValue)
                                        {
                                                command.Parameters.AddWithValue("@BranchId", branchId.Value);
                                        }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            report.TotalOrders = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                            report.TotalSales = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                            report.CashSales = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                            report.CardSales = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                            report.UPISales = 0; // No separate UPI tracking, included in OtherSales
                            report.OtherSales = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
                            report.TotalCustomers = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                        }
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// Generate comprehensive cash closing report for date range
        /// </summary>
        public async Task<CashClosingReportViewModel> GenerateCashClosingReportAsync(DateTime startDate, DateTime endDate, int? cashierId = null, int? branchId = null)
        {
            var report = new CashClosingReportViewModel();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand("usp_GetCashClosingReport", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@StartDate", startDate);
                    command.Parameters.AddWithValue("@EndDate", endDate);
                    command.Parameters.AddWithValue("@CashierId", cashierId.HasValue ? (object)cashierId.Value : DBNull.Value);

                    if (branchId.HasValue && branchId.Value > 0)
                    {
                        if (await StoredProcedureHasParameterAsync(connection, "usp_GetCashClosingReport", "@BranchId"))
                        {
                            command.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }
                        else if (await StoredProcedureHasParameterAsync(connection, "usp_GetCashClosingReport", "@BranchID"))
                        {
                            command.Parameters.AddWithValue("@BranchID", branchId.Value);
                        }
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        // Result Set 1: Summary Statistics
                        if (await reader.ReadAsync())
                        {
                            int ordTotalDays = reader.GetOrdinal("TotalDays");
                            int ordTotalCashiers = reader.GetOrdinal("TotalCashiers");
                            int ordOpening = reader.GetOrdinal("TotalOpeningFloat");
                            int ordSystem = reader.GetOrdinal("TotalSystemAmount");
                            int ordDeclared = reader.GetOrdinal("TotalDeclaredAmount");
                            int ordVariance = reader.GetOrdinal("TotalVariance");
                            int ordCashOver = reader.GetOrdinal("TotalCashOver");
                            int ordCashShort = reader.GetOrdinal("TotalCashShort");
                            int ordApproved = reader.GetOrdinal("ApprovedCount");
                            int ordPending = reader.GetOrdinal("PendingApprovalCount");
                            int ordLocked = reader.GetOrdinal("LockedCount");

                            report.Summary = new CashClosingReportSummary
                            {
                                TotalDays = reader.IsDBNull(ordTotalDays) ? 0 : reader.GetInt32(ordTotalDays),
                                TotalCashiers = reader.IsDBNull(ordTotalCashiers) ? 0 : reader.GetInt32(ordTotalCashiers),
                                TotalOpeningFloat = reader.IsDBNull(ordOpening) ? 0 : reader.GetDecimal(ordOpening),
                                TotalSystemAmount = reader.IsDBNull(ordSystem) ? 0 : reader.GetDecimal(ordSystem),
                                TotalDeclaredAmount = reader.IsDBNull(ordDeclared) ? 0 : reader.GetDecimal(ordDeclared),
                                TotalVariance = reader.IsDBNull(ordVariance) ? 0 : reader.GetDecimal(ordVariance),
                                TotalCashOver = reader.IsDBNull(ordCashOver) ? 0 : reader.GetDecimal(ordCashOver),
                                TotalCashShort = reader.IsDBNull(ordCashShort) ? 0 : reader.GetDecimal(ordCashShort),
                                ApprovedCount = reader.IsDBNull(ordApproved) ? 0 : reader.GetInt32(ordApproved),
                                PendingApprovalCount = reader.IsDBNull(ordPending) ? 0 : reader.GetInt32(ordPending),
                                LockedCount = reader.IsDBNull(ordLocked) ? 0 : reader.GetInt32(ordLocked)
                            };
                        }

                        // Result Set 2: Daily Summary
                        if (await reader.NextResultAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                int ordBizDate = reader.GetOrdinal("BusinessDate");
                                int ordCashierCount = reader.GetOrdinal("CashierCount");
                                int ordDayOpening = reader.GetOrdinal("DayOpeningFloat");
                                int ordDaySystem = reader.GetOrdinal("DaySystemAmount");
                                int ordDayDeclared = reader.GetOrdinal("DayDeclaredAmount");
                                int ordDayVariance = reader.GetOrdinal("DayVariance");
                                int ordDayCashOver = reader.GetOrdinal("DayCashOver");
                                int ordDayCashShort = reader.GetOrdinal("DayCashShort");
                                int ordIsLocked = reader.GetOrdinal("IsDayLocked");

                                report.DailySummaries.Add(new CashClosingDailySummary
                                {
                                    BusinessDate = reader.GetDateTime(ordBizDate),
                                    CashierCount = reader.IsDBNull(ordCashierCount) ? 0 : reader.GetInt32(ordCashierCount),
                                    DayOpeningFloat = reader.IsDBNull(ordDayOpening) ? 0 : reader.GetDecimal(ordDayOpening),
                                    DaySystemAmount = reader.IsDBNull(ordDaySystem) ? 0 : reader.GetDecimal(ordDaySystem),
                                    DayDeclaredAmount = reader.IsDBNull(ordDayDeclared) ? 0 : reader.GetDecimal(ordDayDeclared),
                                    DayVariance = reader.IsDBNull(ordDayVariance) ? 0 : reader.GetDecimal(ordDayVariance),
                                    DayCashOver = reader.IsDBNull(ordDayCashOver) ? 0 : reader.GetDecimal(ordDayCashOver),
                                    DayCashShort = reader.IsDBNull(ordDayCashShort) ? 0 : reader.GetDecimal(ordDayCashShort),
                                    IsDayLocked = reader.IsDBNull(ordIsLocked) ? "No" : reader.GetString(ordIsLocked)
                                });
                            }
                        }

                        // Result Set 3: Detailed Records
                        if (await reader.NextResultAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                report.DetailRecords.Add(new CashClosingDetailRecord
                                {
                                    BusinessDate = reader.GetDateTime(reader.GetOrdinal("BusinessDate")),
                                    CashierId = reader.GetInt32(reader.GetOrdinal("CashierId")),
                                    CashierName = reader.GetString(reader.GetOrdinal("CashierName")),
                                    OpeningFloat = reader.GetDecimal(reader.GetOrdinal("OpeningFloat")),
                                    SystemAmount = reader.GetDecimal(reader.GetOrdinal("SystemAmount")),
                                    ExpectedCash = reader.GetDecimal(reader.GetOrdinal("ExpectedCash")),
                                    DeclaredAmount = reader.IsDBNull(reader.GetOrdinal("DeclaredAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DeclaredAmount")),
                                    Variance = reader.IsDBNull(reader.GetOrdinal("Variance")) ? 0 : reader.GetDecimal(reader.GetOrdinal("Variance")),
                                    VarianceType = reader.GetString(reader.GetOrdinal("VarianceType")),
                                    Status = reader.GetString(reader.GetOrdinal("Status")),
                                    ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy")) ? null : reader.GetString(reader.GetOrdinal("ApprovedBy")),
                                    ApprovalComment = reader.IsDBNull(reader.GetOrdinal("ApprovalComment")) ? null : reader.GetString(reader.GetOrdinal("ApprovalComment")),
                                    LockedFlag = reader.GetBoolean(reader.GetOrdinal("LockedFlag")),
                                    LockedAt = reader.IsDBNull(reader.GetOrdinal("LockedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LockedAt")),
                                    LockedBy = reader.IsDBNull(reader.GetOrdinal("LockedBy")) ? null : reader.GetString(reader.GetOrdinal("LockedBy")),
                                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                                    UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                                });
                            }
                        }

                        // Result Set 4: Cashier Performance
                        if (await reader.NextResultAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                int ordCashierId = reader.GetOrdinal("CashierId");
                                int ordCashierName = reader.GetOrdinal("CashierName");
                                int ordTotalDaysWorked = reader.GetOrdinal("TotalDaysWorked");
                                int ordTotalCashCollected = reader.GetOrdinal("TotalCashCollected");
                                int ordAverageVariance = reader.GetOrdinal("AverageVariance");
                                int ordBestVariance = reader.GetOrdinal("BestVariance");
                                int ordWorstVariance = reader.GetOrdinal("WorstVariance");
                                int ordDaysWithinTol = reader.GetOrdinal("DaysWithinTolerance");
                                int ordDaysAboveTol = reader.GetOrdinal("DaysAboveTolerance");
                                int ordApprovedDays = reader.GetOrdinal("ApprovedDays");
                                int ordPendingDays = reader.GetOrdinal("PendingDays");

                                report.CashierPerformance.Add(new CashClosingCashierPerformance
                                {
                                    CashierId = reader.IsDBNull(ordCashierId) ? 0 : reader.GetInt32(ordCashierId),
                                    CashierName = reader.IsDBNull(ordCashierName) ? string.Empty : reader.GetString(ordCashierName),
                                    TotalDaysWorked = reader.IsDBNull(ordTotalDaysWorked) ? 0 : reader.GetInt32(ordTotalDaysWorked),
                                    TotalCashCollected = reader.IsDBNull(ordTotalCashCollected) ? 0 : reader.GetDecimal(ordTotalCashCollected),
                                    AverageVariance = reader.IsDBNull(ordAverageVariance) ? 0 : reader.GetDecimal(ordAverageVariance),
                                    BestVariance = reader.IsDBNull(ordBestVariance) ? 0 : reader.GetDecimal(ordBestVariance),
                                    WorstVariance = reader.IsDBNull(ordWorstVariance) ? 0 : reader.GetDecimal(ordWorstVariance),
                                    DaysWithinTolerance = reader.IsDBNull(ordDaysWithinTol) ? 0 : reader.GetInt32(ordDaysWithinTol),
                                    DaysAboveTolerance = reader.IsDBNull(ordDaysAboveTol) ? 0 : reader.GetInt32(ordDaysAboveTol),
                                    ApprovedDays = reader.IsDBNull(ordApprovedDays) ? 0 : reader.GetInt32(ordApprovedDays),
                                    PendingDays = reader.IsDBNull(ordPendingDays) ? 0 : reader.GetInt32(ordPendingDays)
                                });
                            }
                        }

                        // Result Set 5: Day Lock Audit
                        if (await reader.NextResultAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                report.DayLockAudits.Add(new CashClosingDayLockAudit
                                {
                                    BusinessDate = reader.GetDateTime(reader.GetOrdinal("BusinessDate")),
                                    LockedBy = reader.GetString(reader.GetOrdinal("LockedBy")),
                                    LockTime = reader.GetDateTime(reader.GetOrdinal("LockTime")),
                                    Remarks = reader.IsDBNull(reader.GetOrdinal("Remarks")) ? null : reader.GetString(reader.GetOrdinal("Remarks")),
                                    Status = reader.GetString(reader.GetOrdinal("Status")),
                                    ReopenedBy = reader.IsDBNull(reader.GetOrdinal("ReopenedBy")) ? null : reader.GetString(reader.GetOrdinal("ReopenedBy")),
                                    ReopenedAt = reader.IsDBNull(reader.GetOrdinal("ReopenedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ReopenedAt")),
                                    ReopenReason = reader.IsDBNull(reader.GetOrdinal("ReopenReason")) ? null : reader.GetString(reader.GetOrdinal("ReopenReason"))
                                });
                            }
                        }
                    }
                }
            }

            return report;
        }

        // Helper methods
        private string GetStatusBadgeClass(string status) => status switch
        {
            "PENDING" => "bg-warning text-dark",
            "OK" => "bg-success",
            "CHECK" => "bg-danger",
            "LOCKED" => "bg-secondary",
            _ => "bg-secondary"
        };

        private string GetStatusIcon(string status) => status switch
        {
            "PENDING" => "fa-clock",
            "OK" => "fa-check-circle",
            "CHECK" => "fa-exclamation-triangle",
            "LOCKED" => "fa-lock",
            _ => "fa-question-circle"
        };
    }
}
