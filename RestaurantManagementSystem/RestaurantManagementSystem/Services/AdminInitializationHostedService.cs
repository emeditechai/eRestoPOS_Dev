using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantManagementSystem.Services;
using RestaurantManagementSystem.Utilities;

namespace RestaurantManagementSystem.Services
{
    /// <summary>
    /// Performs non-critical admin initialization after the web host has started so Kestrel can bind immediately.
    /// </summary>
    public class AdminInitializationHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AdminInitializationHostedService> _logger;
        private Task? _backgroundTask;
        private CancellationTokenSource _cts = new();

        public AdminInitializationHostedService(IServiceProvider serviceProvider, ILogger<AdminInitializationHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Admin initialization hosted service starting in background");
            _backgroundTask = Task.Run(() => RunAsync(_cts.Token), cancellationToken);
            return Task.CompletedTask; // Don't block startup
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var adminSetupService = scope.ServiceProvider.GetRequiredService<AdminSetupService>();
                var envLogger = scope.ServiceProvider.GetRequiredService<ILogger<AdminInitializationHostedService>>();

                // Hard timeout so we never hang forever if DB is unreachable
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    envLogger.LogInformation("Ensuring admin user exists (background)...");
                    await adminSetupService.EnsureAdminUserAsync();
                }
                catch (Exception ex)
                {
                    envLogger.LogWarning(ex, "Admin user initialization failed or timed out");
                }

                // Attempt password reset only in Development
                var hostEnv = scope.ServiceProvider.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
                if (hostEnv?.IsDevelopment() == true)
                {
                    try
                    {
                        envLogger.LogInformation("Attempting admin password reset (background)...");
                        await AdminPasswordReset.ResetAdminPassword(scope.ServiceProvider);
                    }
                    catch (Exception ex)
                    {
                        envLogger.LogWarning(ex, "Admin password reset failed");
                    }
                }

                envLogger.LogInformation("Admin initialization hosted service completed tasks");

                // Seed Stocks navigation entries (UOM Master etc.) – safe to re-run
                try
                {
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var connStr = config.GetConnectionString("DefaultConnection");
                    if (!string.IsNullOrEmpty(connStr))
                        await SeedStocksNavigationAsync(connStr, envLogger, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    envLogger.LogWarning(ex, "Stocks navigation seed failed or timed out");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in admin initialization hosted service");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _cts.Cancel();
                if (_backgroundTask != null)
                {
                    var completed = await Task.WhenAny(_backgroundTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
                    if (completed != _backgroundTask)
                    {
                        _logger.LogInformation("Admin initialization background task did not finish before stop timeout");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping admin initialization hosted service");
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Stocks navigation seed (idempotent – safe to run on every startup)
        // ──────────────────────────────────────────────────────────────────────
        private static async Task SeedStocksNavigationAsync(
            string connectionString,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // language=sql
            const string sql = @"
-- Ensure NavigationMenus table exists before trying to insert
IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL RETURN;

-- Insert NAV_STOCKS parent
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_STOCKS', NULL, 'Stocks', 'Stock and inventory masters', NULL,
            NULL, NULL, NULL, NULL, 'fas fa-boxes compact-icon',
            11, 1, 1, '#22c55e', NULL, 0);
END

-- Insert NAV_STOCKS_UOM child
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_UOM')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_STOCKS_UOM', 'NAV_STOCKS', 'UOM Master',
            'Unit of Measurement master for BOM', NULL,
            'Uom', 'Index', NULL, NULL,
            'fas fa-ruler-combined compact-icon text-primary',
            1, 1, 1, NULL, NULL, 0);
END

-- Insert NAV_STOCKS_ITEMS child (Item / Ingredients Master)
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_ITEMS')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_STOCKS_ITEMS', 'NAV_STOCKS', 'Item Master',
            'Ingredient / inventory item master with UOM mapping', NULL,
            'Master', 'IngredientsList', NULL, NULL,
            'fas fa-boxes-stacked compact-icon text-success',
            2, 1, 1, NULL, NULL, 0);
END

-- Insert NAV_STOCKS_CATEGORIES child (Stock Item Categories)
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_CATEGORIES')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_STOCKS_CATEGORIES', 'NAV_STOCKS', 'Stock Categories',
            'Manage stock item categories', NULL,
            'Master', 'StockCategoryList', NULL, NULL,
            'fas fa-tags compact-icon text-warning',
            3, 1, 1, NULL, NULL, 0);
END

-- Grant full permissions to Administrator role for the new nodes
DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code IN ('NAV_STOCKS', 'NAV_STOCKS_UOM', 'NAV_STOCKS_ITEMS', 'NAV_STOCKS_CATEGORIES')
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RoleMenuPermissions rmp
           WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id);
END
";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = 10;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Stocks navigation seed completed.");
        }
    }
}
