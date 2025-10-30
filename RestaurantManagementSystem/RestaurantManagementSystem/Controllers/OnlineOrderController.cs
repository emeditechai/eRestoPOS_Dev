using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Dapper;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.ViewModels;
using Newtonsoft.Json;

namespace RestaurantManagementSystem.Controllers
{
    [AuthorizeAttribute]
    public class OnlineOrderController : Controller
    {
        private readonly string _connectionString;
        private static bool _onlineOrdersSchemaEnsured = false;
        private static readonly object _schemaInitLock = new object();
        
        public OnlineOrderController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }
        
        /// <summary>
        /// Ensures the OnlineOrderStatuses table exists with the expected seed data.
        /// This prevents runtime errors if the Online Order setup script has not yet been executed.
        /// </summary>
        /// <param name="connection">Open SQL connection</param>
        private void EnsureOnlineOrderStatusesTable(SqlConnection connection)
        {
            using (var command = new SqlCommand(@"IF OBJECT_ID('dbo.OnlineOrderStatuses','U') IS NULL
BEGIN
    CREATE TABLE OnlineOrderStatuses (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Name NVARCHAR(50) NOT NULL,
        Description NVARCHAR(200) NULL,
        Color NVARCHAR(20) NULL,
        IsActive BIT NOT NULL DEFAULT 1
    );

    SET IDENTITY_INSERT OnlineOrderStatuses ON;
    INSERT INTO OnlineOrderStatuses (Id, Name, Description, Color, IsActive) VALUES
        (1,'New','Newly received order','#007bff',1),
        (2,'Acknowledged','Acknowledged by staff','#17a2b8',1),
        (3,'In Preparation','Being prepared','#ffc107',1),
        (4,'Ready For Pickup','Ready for pickup','#28a745',1),
        (5,'Out For Delivery','Out for delivery','#6f42c1',1);
    SET IDENTITY_INSERT OnlineOrderStatuses OFF;
END", connection))
            {
                command.ExecuteNonQuery();
            }
        }
        
        /// <summary>
        /// Ensures the broader Online Orders schema exists to prevent invalid object errors.
        /// Creates minimal versions of OrderSources and OnlineOrders if they are missing.
        /// </summary>
        private void EnsureOnlineOrdersSchema(SqlConnection connection)
        {
            if (_onlineOrdersSchemaEnsured) return;
            lock (_schemaInitLock)
            {
                if (_onlineOrdersSchemaEnsured) return;

                // OrderSources
                using (var cmd = new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='OrderSources')
BEGIN
    CREATE TABLE dbo.OrderSources (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL,
        Description NVARCHAR(200) NULL,
        ApiKey NVARCHAR(100) NULL,
        ApiSecret NVARCHAR(100) NULL,
        WebhookUrl NVARCHAR(255) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
    INSERT INTO dbo.OrderSources (Name, Description) VALUES ('Default','Auto-created order source');
END", connection))
                { cmd.ExecuteNonQuery(); }

                // Statuses + seed
                using (var cmd = new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='OnlineOrderStatuses')
BEGIN
    CREATE TABLE dbo.OnlineOrderStatuses (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Name NVARCHAR(50) NOT NULL,
        Description NVARCHAR(200) NULL,
        Color NVARCHAR(20) NULL,
        IsActive BIT NOT NULL DEFAULT 1
    );
    SET IDENTITY_INSERT dbo.OnlineOrderStatuses ON;
    INSERT INTO dbo.OnlineOrderStatuses (Id, Name, Description, Color, IsActive) VALUES
        (1,'New','Newly received order','#007bff',1),
        (2,'Acknowledged','Acknowledged by staff','#17a2b8',1),
        (3,'In Preparation','Being prepared','#ffc107',1),
        (4,'Ready For Pickup','Ready for pickup','#28a745',1),
        (5,'Out For Delivery','Out for delivery','#6f42c1',1);
    SET IDENTITY_INSERT dbo.OnlineOrderStatuses OFF;
END", connection))
                { cmd.ExecuteNonQuery(); }

                // OnlineOrders (depends on sources & statuses)
                using (var cmd = new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='OnlineOrders')
BEGIN
    CREATE TABLE dbo.OnlineOrders (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OrderSourceId INT NOT NULL,
        ExternalOrderId NVARCHAR(50) NULL,
        OrderNumber NVARCHAR(20) NOT NULL DEFAULT ('ON-' + CONVERT(VARCHAR(8), GETDATE(), 112) + RIGHT('0000'+CAST(ABS(CHECKSUM(NEWID()))%10000 AS VARCHAR(4)),4)),
        CustomerName NVARCHAR(100) NULL,
        CustomerPhone NVARCHAR(20) NULL,
        OrderStatusId INT NOT NULL DEFAULT 1,
        OrderTotal DECIMAL(10,2) NOT NULL DEFAULT 0,
        TaxAmount DECIMAL(10,2) NOT NULL DEFAULT 0,
        IsDelivery BIT NOT NULL DEFAULT 0,
        RequestedDeliveryTime DATETIME NULL,
        SyncStatus INT NOT NULL DEFAULT 0,
        SyncedToLocalOrderId INT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END", connection))
                { cmd.ExecuteNonQuery(); }

                // WebhookEvents (depends on OrderSources)
                using (var cmd = new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='WebhookEvents')
BEGIN
    CREATE TABLE dbo.WebhookEvents (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OrderSourceId INT NOT NULL,
        EventType NVARCHAR(50) NOT NULL,
        Payload NVARCHAR(MAX) NOT NULL,
        ProcessStatus INT NOT NULL DEFAULT 0,
        ErrorDetails NVARCHAR(MAX) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        ProcessedAt DATETIME NULL
    );
END", connection))
                { cmd.ExecuteNonQuery(); }

                _onlineOrdersSchemaEnsured = true;
            }
        }
        
        /// <summary>
        /// Dashboard for online orders
        /// </summary>
        /// <returns>Dashboard view with statistics and recent orders</returns>
        public IActionResult Dashboard()
        {
            var viewModel = new OnlineOrderDashboardViewModel();
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                try
                {
                    EnsureOnlineOrdersSchema(connection);
                }
                catch (SqlException)
                {
                    // Retry once in case of race condition
                    System.Threading.Thread.Sleep(200);
                    EnsureOnlineOrdersSchema(connection);
                }
                
                var stats = new OnlineOrderStats(); // Get stats for dashboard
                
                // Count of orders by status
                var statusCounts = connection.Query<dynamic>(@"
                    SELECT os.Id, os.Name, COUNT(o.Id) AS OrderCount
                    FROM OnlineOrderStatuses os
                    LEFT JOIN OnlineOrders o ON os.Id = o.OrderStatusId
                    WHERE os.IsActive = 1
                    GROUP BY os.Id, os.Name
                ").ToList();
                
                // Map status counts to stats properties
                foreach (var status in statusCounts)
                {
                    if (status.Id == 1) stats.NewOrdersCount = status.OrderCount;
                    if (status.Id == 2) stats.AcknowledgedOrdersCount = status.OrderCount;
                    if (status.Id == 3) stats.InPreparationOrdersCount = status.OrderCount;
                    if (status.Id == 4) stats.ReadyForPickupOrdersCount = status.OrderCount;
                    if (status.Id == 5) stats.OutForDeliveryOrdersCount = status.OrderCount;
                }
                
                // Today's orders stats
                var todayStats = connection.QueryFirstOrDefault<dynamic>(@"
                    SELECT COUNT(Id) AS OrderCount, COALESCE(SUM(OrderTotal), 0) AS OrderTotal
                    FROM OnlineOrders
                    WHERE CAST(CreatedAt AS DATE) = CAST(GETDATE() AS DATE)
                ");
                
                if (todayStats != null)
                {
                    stats.TodayOrdersCount = todayStats.OrderCount;
                    stats.TodayOrdersTotal = todayStats.OrderTotal;
                }
                
                // Sync status counts
                var syncStats = connection.Query<dynamic>(@"
                    SELECT SyncStatus, COUNT(Id) AS Count
                    FROM OnlineOrders
                    GROUP BY SyncStatus
                ").ToList();
                
                foreach (var syncStat in syncStats)
                {
                    if (syncStat.SyncStatus == 0) stats.UnsyncedOrdersCount = syncStat.Count;
                    if (syncStat.SyncStatus == 2) stats.FailedSyncOrdersCount = syncStat.Count;
                }
                
                viewModel.Stats = stats;
                
                // Get recent orders
                viewModel.RecentOrders = connection.Query<OnlineOrderViewModel>(@"
                    SELECT TOP 10
                        oo.Id,
                        oo.OrderNumber,
                        oo.ExternalOrderId,
                        os.Name AS OrderSourceName,
                        oos.Name AS StatusName,
                        oos.Color AS StatusColor,
                        oo.CustomerName,
                        oo.CustomerPhone,
                        oo.OrderTotal,
                        oo.IsDelivery,
                        oo.CreatedAt AS OrderDate,
                        oo.RequestedDeliveryTime,
                        oo.SyncStatus,
                        oo.SyncedToLocalOrderId
                    FROM OnlineOrders oo
                    INNER JOIN OrderSources os ON oo.OrderSourceId = os.Id
                    INNER JOIN OnlineOrderStatuses oos ON oo.OrderStatusId = oos.Id
                    ORDER BY oo.CreatedAt DESC
                ").ToList();
                
                // Get recent webhook events
                viewModel.RecentWebhookEvents = connection.Query<WebhookEventViewModel>(@"
                    SELECT TOP 5
                        we.Id,
                        os.Name AS OrderSourceName,
                        we.EventType,
                        LEFT(we.Payload, 100) + CASE WHEN LEN(we.Payload) > 100 THEN '...' ELSE '' END AS Payload,
                        we.ProcessStatus,
                        we.ErrorDetails,
                        we.CreatedAt,
                        we.ProcessedAt
                    FROM WebhookEvents we
                    INNER JOIN OrderSources os ON we.OrderSourceId = os.Id
                    ORDER BY we.CreatedAt DESC
                ").ToList();
                
                // Get active order sources
                viewModel.OrderSources = connection.Query<OrderSourceViewModel>(@"
                    SELECT Id, Name, Description, IsActive
                    FROM OrderSources
                    WHERE IsActive = 1
                    ORDER BY Name
                ").ToList();
            }
            
            return View(viewModel);
        }
        
        /// <summary>
        /// List of online orders with filtering
        /// </summary>
        /// <param name="model">Filter parameters</param>
        /// <returns>List of orders</returns>
        public IActionResult Index(OnlineOrderListViewModel model)
        {
            if (model == null)
            {
                model = new OnlineOrderListViewModel();
            }
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                EnsureOnlineOrdersSchema(connection);
                
                // Get order statuses
                model.OrderStatuses = connection.Query<OnlineOrderStatusViewModel>(@"
                    SELECT Id, Name, Description, Color, IsActive
                    FROM OnlineOrderStatuses
                    ORDER BY Id
                ").ToList();
                
                // Get order sources
                model.OrderSources = connection.Query<OrderSourceViewModel>(@"
                    SELECT Id, Name, Description, IsActive
                    FROM OrderSources
                    ORDER BY Name
                ").ToList();
                
                // Get orders with filters
                var parameters = new DynamicParameters();
                parameters.Add("@StatusId", model.StatusId);
                parameters.Add("@OrderSourceId", model.OrderSourceId);
                parameters.Add("@SyncStatus", model.SyncStatus);
                parameters.Add("@StartDate", model.StartDate);
                parameters.Add("@EndDate", model.EndDate);
                parameters.Add("@SearchTerm", model.SearchTerm);
                
                model.Orders = connection.Query<OnlineOrderViewModel>("GetOnlineOrders", 
                    parameters, 
                    commandType: CommandType.StoredProcedure).ToList();
            }
            
            return View(model);
        }
        
        /// <summary>
        /// Details of a specific online order
        /// </summary>
        /// <param name="id">Order ID</param>
        /// <returns>Order details view</returns>
        public IActionResult Details(int id)
        {
            var viewModel = new OnlineOrderDetailsViewModel();
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                EnsureOnlineOrdersSchema(connection);
                
                var parameters = new DynamicParameters();
                parameters.Add("@OrderId", id);
                
                // Use multiple result sets to get order, items, and modifiers
                using (var multi = connection.QueryMultiple("GetOnlineOrderDetails", 
                    parameters, 
                    commandType: CommandType.StoredProcedure))
                {
                    var orderDetail = multi.Read<OnlineOrderDetailsViewModel>().FirstOrDefault();
                    if (orderDetail != null)
                    {
                        viewModel = orderDetail;
                        viewModel.Items = multi.Read<OnlineOrderItemViewModel>().ToList();
                        var modifiers = multi.Read<OnlineOrderItemModifierViewModel>().ToList();
                        
                        // Assign modifiers to their items
                        foreach (var modifier in modifiers)
                        {
                            var item = viewModel.Items.FirstOrDefault(i => i.Id == modifier.OnlineOrderItemId);
                            if (item != null)
                            {
                                item.Modifiers.Add(modifier);
                            }
                        }
                    }
                }
                
                // Get available statuses for dropdown
                viewModel.AvailableStatuses = connection.Query<OnlineOrderStatusViewModel>(@"
                    SELECT Id, Name, Description, Color, IsActive
                    FROM OnlineOrderStatuses
                    WHERE IsActive = 1
                    ORDER BY Id
                ").ToList();
            }
            
            return View(viewModel);
        }
        
        /// <summary>
        /// Update the status of an online order
        /// </summary>
        /// <param name="model">Status update data</param>
        /// <returns>Redirect to order details</returns>
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult UpdateStatus(UpdateOrderStatusViewModel model)
        {
            if (ModelState.IsValid)
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    var parameters = new DynamicParameters();
                    parameters.Add("@OrderId", model.OrderId);
                    parameters.Add("@StatusId", model.StatusId);
                    parameters.Add("@Notes", model.Notes);
                    
                    connection.Execute("UpdateOnlineOrderStatus", 
                        parameters, 
                        commandType: CommandType.StoredProcedure);
                }
                
                TempData["SuccessMessage"] = "Order status updated successfully.";
            }
            
            return RedirectToAction(nameof(Details), new { id = model.OrderId });
        }
        
        /// <summary>
        /// Sync an online order to the local order system
        /// </summary>
        /// <param name="id">Online order ID</param>
        /// <returns>Redirect to order details</returns>
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult SyncOrder(int id)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    var parameters = new DynamicParameters();
                    parameters.Add("@OnlineOrderId", id);
                    
                    var result = connection.QueryFirstOrDefault<dynamic>("SyncOnlineOrderToLocalOrder",
                        parameters,
                        commandType: CommandType.StoredProcedure);
                    
                    if (result != null && result.OrderId != null)
                    {
                        TempData["SuccessMessage"] = $"Order synced successfully. Local Order ID: {result.OrderId}";
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error syncing order: {ex.Message}";
            }
            
            return RedirectToAction(nameof(Details), new { id });
        }
        
        /// <summary>
        /// List of order sources
        /// </summary>
        /// <returns>Order sources list view</returns>
        public IActionResult OrderSources()
        {
            var orderSources = new List<OrderSourceViewModel>();
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                orderSources = connection.Query<OrderSourceViewModel>(@"
                    SELECT Id, Name, Description, IsActive
                    FROM OrderSources
                    ORDER BY Name
                ").ToList();
            }
            
            return View(orderSources);
        }
        
        /// <summary>
        /// Get details of an order source
        /// </summary>
        /// <param name="id">Order source ID</param>
        /// <returns>Order source details view</returns>
        public IActionResult OrderSourceDetails(int id)
        {
            var viewModel = new OrderSourceDetailsViewModel();
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                // Get order source details
                viewModel = connection.QueryFirstOrDefault<OrderSourceDetailsViewModel>(@"
                    SELECT Id, Name, Description, ApiKey, ApiSecret, WebhookUrl, IsActive
                    FROM OrderSources
                    WHERE Id = @Id
                ", new { Id = id });
                
                if (viewModel != null)
                {
                    // Get configurations
                    viewModel.Configurations = connection.Query<OrderSourceConfigurationViewModel>(@"
                        SELECT Id, OrderSourceId, ConfigKey, 
                            CASE WHEN IsEncrypted = 1 THEN '********' ELSE ConfigValue END AS ConfigValue,
                            IsEncrypted
                        FROM OrderSourceConfigurations
                        WHERE OrderSourceId = @OrderSourceId
                        ORDER BY ConfigKey
                    ", new { OrderSourceId = id }).ToList();
                    
                    // Get recent webhook events
                    viewModel.RecentEvents = connection.Query<WebhookEventViewModel>(@"
                        SELECT TOP 10
                            we.Id,
                            os.Name AS OrderSourceName,
                            we.EventType,
                            LEFT(we.Payload, 100) + CASE WHEN LEN(we.Payload) > 100 THEN '...' ELSE '' END AS Payload,
                            we.ProcessStatus,
                            we.ErrorDetails,
                            we.CreatedAt,
                            we.ProcessedAt
                        FROM WebhookEvents we
                        INNER JOIN OrderSources os ON we.OrderSourceId = os.Id
                        WHERE we.OrderSourceId = @OrderSourceId
                        ORDER BY we.CreatedAt DESC
                    ", new { OrderSourceId = id }).ToList();
                }
            }
            
            return View(viewModel);
        }
        
        /// <summary>
        /// Create or edit an order source
        /// </summary>
        /// <param name="id">Order source ID (null for create)</param>
        /// <returns>Order source form view</returns>
        public IActionResult EditOrderSource(int? id)
        {
            var viewModel = new OrderSourceDetailsViewModel();
            
            if (id.HasValue)
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    // Get order source details
                    viewModel = connection.QueryFirstOrDefault<OrderSourceDetailsViewModel>(@"
                        SELECT Id, Name, Description, ApiKey, ApiSecret, WebhookUrl, IsActive
                        FROM OrderSources
                        WHERE Id = @Id
                    ", new { Id = id });
                    
                    if (viewModel != null)
                    {
                        // Get configurations
                        viewModel.Configurations = connection.Query<OrderSourceConfigurationViewModel>(@"
                            SELECT Id, OrderSourceId, ConfigKey, 
                                CASE WHEN IsEncrypted = 1 THEN '********' ELSE ConfigValue END AS ConfigValue,
                                IsEncrypted
                            FROM OrderSourceConfigurations
                            WHERE OrderSourceId = @OrderSourceId
                            ORDER BY ConfigKey
                        ", new { OrderSourceId = id }).ToList();
                    }
                }
            }
            
            return View(viewModel);
        }
        
        /// <summary>
        /// Save an order source
        /// </summary>
        /// <param name="model">Order source data</param>
        /// <returns>Redirect to order sources list</returns>
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult SaveOrderSource(OrderSourceDetailsViewModel model)
        {
            if (ModelState.IsValid)
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            if (model.Id > 0)
                            {
                                // Update existing order source
                                connection.Execute(@"
                                    UPDATE OrderSources
                                    SET Name = @Name,
                                        Description = @Description,
                                        ApiKey = @ApiKey,
                                        ApiSecret = @ApiSecret,
                                        WebhookUrl = @WebhookUrl,
                                        IsActive = @IsActive,
                                        UpdatedAt = GETDATE()
                                    WHERE Id = @Id
                                ", model, transaction);
                            }
                            else
                            {
                                // Insert new order source
                                var id = connection.QuerySingle<int>(@"
                                    INSERT INTO OrderSources (
                                        Name, Description, ApiKey, ApiSecret, WebhookUrl, IsActive, CreatedAt, UpdatedAt
                                    )
                                    VALUES (
                                        @Name, @Description, @ApiKey, @ApiSecret, @WebhookUrl, @IsActive, GETDATE(), GETDATE()
                                    );
                                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                                ", model, transaction);
                                
                                model.Id = id;
                            }
                            
                            transaction.Commit();
                            
                            TempData["SuccessMessage"] = "Order source saved successfully.";
                            return RedirectToAction(nameof(OrderSources));
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            ModelState.AddModelError("", $"Error saving order source: {ex.Message}");
                        }
                    }
                }
            }
            
            return View("EditOrderSource", model);
        }
        
        /// <summary>
        /// List of menu item mappings for external platforms
        /// </summary>
        /// <param name="model">Filter parameters</param>
        /// <returns>Menu item mappings list view</returns>
        public IActionResult MenuItemMappings(MenuItemMappingsViewModel model)
        {
            if (model == null)
            {
                model = new MenuItemMappingsViewModel();
            }
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                // Get order sources for filter
                model.OrderSources = connection.Query<OrderSourceViewModel>(@"
                    SELECT Id, Name, Description, IsActive
                    FROM OrderSources
                    WHERE IsActive = 1
                    ORDER BY Name
                ").ToList();
                
                // Get mappings with filter
                var query = @"
                    SELECT 
                        eim.Id,
                        eim.MenuItemId,
                        mi.Name AS MenuItemName,
                        eim.OrderSourceId,
                        os.Name AS OrderSourceName,
                        eim.ExternalItemId,
                        eim.ExternalItemName,
                        eim.ExternalPrice,
                        eim.IsActive,
                        eim.LastSyncedAt
                    FROM ExternalMenuItemMappings eim
                    INNER JOIN MenuItems mi ON eim.MenuItemId = mi.Id
                    INNER JOIN OrderSources os ON eim.OrderSourceId = os.Id
                    WHERE (@OrderSourceId IS NULL OR eim.OrderSourceId = @OrderSourceId)
                    ORDER BY mi.Name, os.Name
                ";
                
                try
                {
                    model.Mappings = connection.Query<ExternalMenuItemMappingViewModel>(query,
                        new { OrderSourceId = model.OrderSourceId }).ToList();
                }
                catch (Microsoft.Data.SqlClient.SqlException sqlEx)
                {
                    // If the ExternalMenuItemMappings table is missing (error 208), fallback gracefully
                    if (sqlEx.Number == 208)
                    {
                        model.Mappings = new List<ExternalMenuItemMappingViewModel>();
                        TempData["WarningMessage"] = "External menu mappings table not found in DB; skipping mappings list.";
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            
            return View(model);
        }
        
        /// <summary>
        /// Create or edit a menu item mapping
        /// </summary>
        /// <param name="id">Mapping ID (null for create)</param>
        /// <returns>Menu item mapping form view</returns>
        public IActionResult EditMenuItemMapping(int? id)
        {
            var viewModel = new ExternalMenuItemMappingViewModel();
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                // Get menu items
                viewModel.MenuItems = connection.Query<MenuItem>(@"
                    SELECT Id, Name, Price
                    FROM MenuItems
                    WHERE IsActive = 1
                    ORDER BY Name
                ").ToList();
                
                // Get order sources
                viewModel.OrderSources = connection.Query<OrderSource>(@"
                    SELECT Id, Name, Description
                    FROM OrderSources
                    WHERE IsActive = 1
                    ORDER BY Name
                ").ToList();
                
                if (id.HasValue)
                {
                    // Get mapping details
                    var mapping = connection.QueryFirstOrDefault<ExternalMenuItemMappingViewModel>(@"
                        SELECT 
                            eim.Id,
                            eim.MenuItemId,
                            mi.Name AS MenuItemName,
                            eim.OrderSourceId,
                            os.Name AS OrderSourceName,
                            eim.ExternalItemId,
                            eim.ExternalItemName,
                            eim.ExternalPrice,
                            eim.IsActive,
                            eim.LastSyncedAt
                        FROM ExternalMenuItemMappings eim
                        INNER JOIN MenuItems mi ON eim.MenuItemId = mi.Id
                        INNER JOIN OrderSources os ON eim.OrderSourceId = os.Id
                        WHERE eim.Id = @Id
                    ", new { Id = id });
                    
                    if (mapping != null)
                    {
                        viewModel = mapping;
                    }
                }
            }
            
            return View(viewModel);
        }
        
        /// <summary>
        /// Save a menu item mapping
        /// </summary>
        /// <param name="model">Menu item mapping data</param>
        /// <returns>Redirect to menu item mappings list</returns>
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult SaveMenuItemMapping(ExternalMenuItemMappingViewModel model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                    {
                        connection.Open();
                        
                        var parameters = new DynamicParameters();
                        parameters.Add("@MenuItemId", model.MenuItemId);
                        parameters.Add("@OrderSourceId", model.OrderSourceId);
                        parameters.Add("@ExternalItemId", model.ExternalItemId);
                        parameters.Add("@ExternalItemName", model.ExternalItemName);
                        parameters.Add("@ExternalPrice", model.ExternalPrice);
                        parameters.Add("@IsActive", model.IsActive);
                        
                        connection.Execute("ManageExternalMenuItemMapping", 
                            parameters, 
                            commandType: CommandType.StoredProcedure);
                    }
                    
                    TempData["SuccessMessage"] = "Menu item mapping saved successfully.";
                    return RedirectToAction(nameof(MenuItemMappings));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error saving menu item mapping: {ex.Message}");
                }
            }
            
            // If there's an error, reload the dropdown data
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                // Get menu items
                model.MenuItems = connection.Query<MenuItem>(@"
                    SELECT Id, Name, Price
                    FROM MenuItems
                    WHERE IsActive = 1
                    ORDER BY Name
                ").ToList();
                
                // Get order sources
                model.OrderSources = connection.Query<OrderSource>(@"
                    SELECT Id, Name, Description
                    FROM OrderSources
                    WHERE IsActive = 1
                    ORDER BY Name
                ").ToList();
            }
            
            return View("EditMenuItemMapping", model);
        }
        
        /// <summary>
        /// Webhook endpoint for receiving events from external platforms
        /// </summary>
        /// <param name="source">Source identifier</param>
        /// <returns>Status code result</returns>
        [HttpPostAttribute]
        [AllowAnonymousAttribute]
        [RouteAttribute("api/webhook/{source}")]
        public async Task<IActionResult> ReceiveWebhook(string source)
        {
            try
            {
                // Read raw request body
                string payload;
                using (var reader = new System.IO.StreamReader(Request.Body))
                {
                    payload = await reader.ReadToEndAsync();
                }
                
                // Get order source by identifier
                int orderSourceId;
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    orderSourceId = connection.QueryFirstOrDefault<int>(@"
                        SELECT Id
                        FROM OrderSources
                        WHERE Name = @Source OR ApiKey = @Source
                    ", new { Source = source });
                }
                
                if (orderSourceId == 0)
                {
                    return NotFound("Order source not found");
                }
                
                // Determine event type from headers
                string eventType = "unknown";
                if (Request.Headers.ContainsKey("X-Event-Type"))
                {
                    eventType = Request.Headers["X-Event-Type"];
                }
                else if (Request.Headers.ContainsKey("X-Delivery-ID"))
                {
                    eventType = "delivery";
                }
                
                // Log webhook event
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    var webhookId = connection.ExecuteScalar<int>(@"
                        INSERT INTO WebhookEvents (
                            OrderSourceId, EventType, Payload, ProcessStatus, CreatedAt
                        )
                        VALUES (
                            @OrderSourceId, @EventType, @Payload, 0, GETDATE()
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                    ", new { OrderSourceId = orderSourceId, EventType = eventType, Payload = payload });
                }
                
                // Return immediate response, we'll process the webhook asynchronously
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error processing webhook: {ex.Message}");
            }
        }
    }
}
