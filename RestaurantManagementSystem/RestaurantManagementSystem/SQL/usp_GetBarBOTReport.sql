--EXEC [dbo].[usp_GetBarBOTReport] ''
CREATE OR ALTER PROCEDURE [dbo].[usp_GetBarBOTReport]
    @FromDate DATE = NULL,
    @ToDate DATE = NULL,
    @Station NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Start DATETIME = COALESCE(CAST(@FromDate AS DATETIME), DATEADD(day, -1, CAST(GETDATE() AS DATE)));
    DECLARE @End DATETIME = DATEADD(day, 1, COALESCE(CAST(@ToDate AS DATETIME), CAST(GETDATE() AS DATE)));
    DECLARE @StationFilter NVARCHAR(100) = NULLIF(LTRIM(RTRIM(@Station)), '');

    ;WITH FilteredOrders AS (
        SELECT o.Id,
               o.OrderNumber,
               o.TableTurnoverId,
               o.CreatedAt
        FROM dbo.Orders o
        WHERE o.CreatedAt >= @Start
          AND o.CreatedAt < @End
    ),
    BarMenuItems AS (
        SELECT i.Id AS MenuItemId,
               i.Name AS ItemName,
               i.UOM_Id,
               i.KitchenStationId
        FROM dbo.MenuItems i
        INNER JOIN dbo.menuitemgroup mig ON i.menuitemgroupID = mig.ID
        WHERE mig.itemgroup = 'BAR'
    ),
    OrderLines AS (
        SELECT oi.OrderId,
               oi.MenuItemId,
               oi.Quantity
        FROM dbo.OrderItems oi
        INNER JOIN FilteredOrders fo ON fo.Id = oi.OrderId
        INNER JOIN BarMenuItems bmi ON bmi.MenuItemId = oi.MenuItemId
    ),
    LatestKitchenStatus AS (
        SELECT kt.OrderId,
               CASE 
                   WHEN kt.Status = 0 THEN 'New'
                   WHEN kt.Status = 1 THEN 'In Progress'
                   WHEN kt.Status = 2 THEN 'Ready'
                   WHEN kt.Status = 3 THEN 'Completed'
                   ELSE 'Pending'
               END AS StatusLabel,
               ROW_NUMBER() OVER (PARTITION BY kt.OrderId ORDER BY kt.CreatedAt DESC, kt.Id DESC) AS rn
        FROM dbo.KitchenTickets kt
        WHERE kt.KitchenStation = 'BAR'
          AND kt.TicketNumber LIKE 'BOT-%'
    )
    SELECT
        fo.Id AS OrderId,
        fo.OrderNumber,
        ISNULL(t.TableName, CONCAT('Table ', fo.TableTurnoverId)) AS TableName,
        bmi.ItemName,
        u.UOM_Name AS UOMName,
        u.UOM_Type AS UOMType,
        u.Base_Quantity_ML AS UOMQuantityML,
        ol.Quantity,
        ISNULL(s.Name, 'Bar') AS Station,
        COALESCE(lks.StatusLabel, 'Pending') AS Status,
        fo.CreatedAt AS RequestedAt
    FROM OrderLines ol
    INNER JOIN FilteredOrders fo ON fo.Id = ol.OrderId
    INNER JOIN BarMenuItems bmi ON bmi.MenuItemId = ol.MenuItemId
    LEFT JOIN dbo.tbl_mst_uom u ON bmi.UOM_Id = u.UOM_Id
    LEFT JOIN dbo.KitchenStations s ON bmi.KitchenStationId = s.Id
    LEFT JOIN dbo.Tables t ON fo.TableTurnoverId = t.Id
    LEFT JOIN LatestKitchenStatus lks ON lks.OrderId = fo.Id AND lks.rn = 1
    WHERE (@StationFilter IS NULL OR s.Name = @StationFilter)
    ORDER BY fo.CreatedAt DESC, fo.Id DESC
    OPTION (RECOMPILE);

    /*
        Suggested supporting indexes (run once, outside this procedure):
            CREATE INDEX IX_Orders_CreatedAt ON dbo.Orders (CreatedAt) INCLUDE (Id, TableTurnoverId, OrderNumber);
            CREATE INDEX IX_OrderItems_Order_Menu ON dbo.OrderItems (OrderId, MenuItemId) INCLUDE (Quantity);
            CREATE INDEX IX_KitchenTickets_OrderStation ON dbo.KitchenTickets (OrderId, KitchenStation, TicketNumber) INCLUDE (Status, CreatedAt);
    */
END
GO
