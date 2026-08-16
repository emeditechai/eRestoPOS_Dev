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

                // Seed Branch Location nav entry – safe to re-run
                try
                {
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var connStr = config.GetConnectionString("DefaultConnection");
                    if (!string.IsNullOrEmpty(connStr))
                        await SeedBranchLocationNavigationAsync(connStr, envLogger, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    envLogger.LogWarning(ex, "Branch Location navigation seed failed or timed out");
                }

                // Seed Profit & Loss report nav entry – safe to re-run
                try
                {
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var connStr = config.GetConnectionString("DefaultConnection");
                    if (!string.IsNullOrEmpty(connStr))
                        await SeedProfitLossNavigationAsync(connStr, envLogger, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    envLogger.LogWarning(ex, "Profit & Loss navigation seed failed or timed out");
                }

                // Seed Waitlist Guest report nav entry – safe to re-run
                try
                {
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var connStr = config.GetConnectionString("DefaultConnection");
                    if (!string.IsNullOrEmpty(connStr))
                        await SeedWaitlistGuestNavigationAsync(connStr, envLogger, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    envLogger.LogWarning(ex, "Waitlist Guest navigation seed failed or timed out");
                }

                // Seed Sign Up page nav entry under Settings – safe to re-run
                try
                {
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var connStr = config.GetConnectionString("DefaultConnection");
                    if (!string.IsNullOrEmpty(connStr))
                        await SeedSignUpNavigationAsync(connStr, envLogger, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    envLogger.LogWarning(ex, "Sign Up navigation seed failed or timed out");
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

-- Hide Ingredients from Menu nav (it lives under Stocks > Item Master now)
UPDATE dbo.NavigationMenus SET IsVisible = 0 WHERE Code = 'NAV_MENU_INGREDIENTS';

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

-- Insert NAV_STOCKS_GODOWN child (Godown Master)
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_GODOWN')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_STOCKS_GODOWN', 'NAV_STOCKS', 'Godown Master',
            'Manage branch-wise godowns / warehouses', NULL,
            'Master', 'GodownList', NULL, NULL,
            'fas fa-warehouse compact-icon text-info',
            4, 1, 1, NULL, NULL, 0);
END

-- Insert NAV_STOCKS_PARTY child (Party Master)
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_PARTY')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_STOCKS_PARTY', 'NAV_STOCKS', 'Party Master',
            'Manage vendors, suppliers and traders', NULL,
            'Master', 'PartyList', NULL, NULL,
            'fas fa-handshake compact-icon text-purple',
            5, 1, 1, NULL, NULL, 0);
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
    WHERE nm.Code IN ('NAV_STOCKS', 'NAV_STOCKS_UOM', 'NAV_STOCKS_ITEMS', 'NAV_STOCKS_CATEGORIES', 'NAV_STOCKS_GODOWN', 'NAV_STOCKS_PARTY')
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

        // ──────────────────────────────────────────────────────────────────────
        // Branch Location navigation seed (idempotent)
        // ──────────────────────────────────────────────────────────────────────
        private static async Task SeedBranchLocationNavigationAsync(
            string connectionString,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL RETURN;

-- Insert NAV_SETTINGS_BRANCH_LOCATION child under Settings
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS_BRANCH_LOCATION')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_SETTINGS_BRANCH_LOCATION', 'NAV_SETTINGS', 'Branch Location',
            'Manage branch location master', NULL,
            'Master', 'BranchLocationList', NULL, NULL,
            'fas fa-map-marker-alt compact-icon text-success',
            3, 1, 1, NULL, NULL, 0);
END

-- Grant full permissions to Administrator role
DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code = 'NAV_SETTINGS_BRANCH_LOCATION'
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
            logger.LogInformation("Branch Location navigation seed completed.");
        }

        private static async Task SeedProfitLossNavigationAsync(
            string connectionString,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL RETURN;

-- Insert NAV_REPORTS_PROFITLOSS child under Reports
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_REPORTS_PROFITLOSS')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_REPORTS_PROFITLOSS', 'NAV_REPORTS', 'P&L Analysis',
            'Profit & Loss Analysis Report with BOM-based ingredient costing', NULL,
            'Reports', 'ProfitLoss', NULL, NULL,
            'fas fa-chart-line compact-icon text-success',
            99, 1, 1, NULL, NULL, 0);
END

-- Grant full permissions to Administrator role
DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code = 'NAV_REPORTS_PROFITLOSS'
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
            logger.LogInformation("Profit & Loss navigation seed completed.");
        }

        private static async Task SeedWaitlistGuestNavigationAsync(
            string connectionString,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL RETURN;

IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_REPORTS_WAITLIST_GUESTS')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_REPORTS_WAITLIST_GUESTS', 'NAV_REPORTS', 'Waitlist Guest Report',
            'Waitlist and seated guest operational report by date range', NULL,
            'Reports', 'WaitlistGuestReport', NULL, NULL,
            'fas fa-chair compact-icon text-info',
            14, 1, 1, NULL, NULL, 0);
END

DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code = 'NAV_REPORTS_WAITLIST_GUESTS'
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
            logger.LogInformation("Waitlist Guest navigation seed completed.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Sign Up navigation seed – places 'Sign Up' under Settings NAV bar
        // This is a PUBLIC page link visible to all authenticated users.
        // ──────────────────────────────────────────────────────────────────────
        private static async Task SeedSignUpNavigationAsync(
            string connectionString,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL RETURN;

-- Ensure NAV_SETTINGS parent exists (it should already, but guard anyway)
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS')
    RETURN;

-- Insert Sign Up page under Settings
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS_SIGNUP')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_SETTINGS_SIGNUP', 'NAV_SETTINGS', 'Sign Up',
            'Public sign-up page – create a new restaurant branch and account', NULL,
            'SignUp', 'Index', NULL, NULL,
            'fas fa-rocket compact-icon text-warning',
            99, 1, 1, NULL, NULL, 1);
END
ELSE
BEGIN
    -- Ensure it is visible if it was previously hidden
    UPDATE dbo.NavigationMenus
    SET IsActive = 1, IsVisible = 1,
        ControllerName = 'SignUp', ActionName = 'Index',
        IconCss = 'fas fa-rocket compact-icon text-warning',
        OpenInNewTab = 1
    WHERE Code = 'NAV_SETTINGS_SIGNUP';
END

-- Grant Administrator role full permissions on the Sign Up menu entry
DECLARE @AdminRoleId2 INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId2 IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId2, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code = 'NAV_SETTINGS_SIGNUP'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RoleMenuPermissions rmp
           WHERE rmp.RoleId = @AdminRoleId2 AND rmp.MenuId = nm.Id);
END
";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = 10;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Sign Up navigation seed completed.");
        }
    }
}
