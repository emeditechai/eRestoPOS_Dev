using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RestaurantManagementSystem.Middleware
{
    /// <summary>
    /// Middleware that checks database connectivity and redirects to a maintenance page
    /// when the database server is unreachable.
    /// Uses a lightweight cached probe so the check does not run on every single request.
    /// </summary>
    public class MaintenanceMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _connectionString;
        private readonly ILogger<MaintenanceMiddleware> _logger;

        // Cached health-check result to avoid hitting the DB on every request.
        private static bool _lastCheckHealthy = true;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static readonly object _lock = new();

        // Re-probe every 15 seconds when healthy, every 5 seconds when unhealthy (faster recovery).
        private static readonly TimeSpan HealthyInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan UnhealthyInterval = TimeSpan.FromSeconds(5);

        public MaintenanceMiddleware(RequestDelegate next, IConfiguration configuration,
            ILogger<MaintenanceMiddleware> logger)
        {
            _next = next;
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Always let through: static files, the maintenance page itself, and health-check endpoints.
            if (ShouldBypass(path))
            {
                await _next(context);
                return;
            }

            bool isHealthy = CheckDatabaseHealth();

            if (!isHealthy)
            {
                // If this is already a request for the maintenance page action, serve it.
                if (path.StartsWith("/Home/Maintenance", StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }

                _logger.LogWarning("Database connectivity check failed — redirecting to maintenance page.");
                context.Response.Redirect("/Home/Maintenance");
                return;
            }

            // DB is reachable — if user is on the maintenance page, send them back.
            if (path.StartsWith("/Home/Maintenance", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/");
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Performs a lightweight, cached database connectivity probe.
        /// </summary>
        private bool CheckDatabaseHealth()
        {
            var now = DateTime.UtcNow;
            var interval = _lastCheckHealthy ? HealthyInterval : UnhealthyInterval;

            if ((now - _lastCheckTime) < interval)
            {
                return _lastCheckHealthy;
            }

            lock (_lock)
            {
                // Double-check inside the lock to avoid thundering-herd re-checks.
                if ((DateTime.UtcNow - _lastCheckTime) < interval)
                {
                    return _lastCheckHealthy;
                }

                bool healthy;
                try
                {
                    using var connection = new SqlConnection(_connectionString);
                    connection.Open();
                    using var cmd = new SqlCommand("SELECT 1", connection);
                    cmd.CommandTimeout = 5; // seconds
                    cmd.ExecuteScalar();
                    healthy = true;
                }
                catch (SqlException ex)
                {
                    _logger.LogError(ex, "Database health check failed (SqlException).");
                    healthy = false;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Database health check failed (InvalidOperationException).");
                    healthy = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Database health check failed (unexpected).");
                    healthy = false;
                }

                _lastCheckHealthy = healthy;
                _lastCheckTime = DateTime.UtcNow;
                return healthy;
            }
        }

        private static bool ShouldBypass(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            return path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/images", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Home/Maintenance", StringComparison.OrdinalIgnoreCase);
        }
    }
}
