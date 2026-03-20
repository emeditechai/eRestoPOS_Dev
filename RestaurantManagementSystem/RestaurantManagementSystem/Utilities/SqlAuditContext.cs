using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace RestaurantManagementSystem.Utilities
{
    public static class SqlAuditContext
    {
        private const string ApplyAuditContextSql = @"
EXEC sys.sp_set_session_context @key=N'AuditUserId',    @value=@AuditUserId;
EXEC sys.sp_set_session_context @key=N'AuditUserName',  @value=@AuditUserName;
EXEC sys.sp_set_session_context @key=N'AuditBranchId',  @value=@AuditBranchId;
EXEC sys.sp_set_session_context @key=N'AuditIpAddress', @value=@AuditIpAddress;
EXEC sys.sp_set_session_context @key=N'AuditModule',    @value=@AuditModule;";

        public static void Apply(
            SqlConnection connection,
            ClaimsPrincipal? user,
            HttpContext? httpContext,
            int? branchId = null,
            string? module = null)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using var cmd = new SqlCommand(ApplyAuditContextSql, connection);
            cmd.Parameters.AddWithValue("@AuditUserId", (object?)(user.GetUserId() ?? 0) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AuditUserName", (object?)(user?.Identity?.Name ?? "Unknown") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AuditBranchId", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AuditIpAddress", (object?)httpContext?.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AuditModule", string.IsNullOrWhiteSpace(module) ? (object)DBNull.Value : module!);
            cmd.ExecuteNonQuery();
        }
    }
}