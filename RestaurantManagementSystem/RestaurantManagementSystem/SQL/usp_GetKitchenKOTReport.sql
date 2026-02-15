--EXEC [dbo].[usp_GetKitchenKOTReport] ''
create or alter PROCEDURE [dbo].[usp_GetKitchenKOTReport]
    @FromDate DATE = NULL,
    @ToDate DATE = NULL,
    @Station NVARCHAR(100) = NULL,
    @BranchId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Start DATETIME = COALESCE(CAST(@FromDate AS DATETIME), DATEADD(day, -1, CAST(GETDATE() AS DATE)));
    DECLARE @End DATETIME = DATEADD(day, 1, COALESCE(CAST(@ToDate AS DATETIME), CAST(GETDATE() AS DATE)));
    DECLARE @HasOrdersBranch BIT = CASE WHEN COL_LENGTH('dbo.Orders', 'BranchId') IS NULL THEN 0 ELSE 1 END;

    ;WITH KOTData AS
    (
        SELECT
            o.Id AS OrderId,
            o.OrderNumber,
            kt.TicketNumber AS KOTNumber,
            ISNULL(t.TableName, CONCAT('Table ', o.TableTurnoverId)) AS TableName,
            i.Name AS ItemName,
            oi.Quantity,
            COALESCE(
                ksTicket.Name,
                NULLIF(LTRIM(RTRIM(kti.StationName)), ''),
                ksMenu.Name,
                NULLIF(LTRIM(RTRIM(kt.StationName)), ''),
                ''
            ) AS Station,
            CASE WHEN kti.CompletionTime IS NOT NULL THEN 'Completed' ELSE 'Pending' END AS Status,
            COALESCE(kti.StartTime, kt.CreatedAt) AS RequestedAt,
            o.BranchId
        FROM OrderItems oi
        INNER JOIN dbo.Orders o ON oi.OrderId = o.Id
        INNER JOIN [dbo].[KitchenTicketItems] kti on kti.OrderItemId = oi.Id
        INNER JOIN [dbo].[KitchenTickets] kt ON kti.KitchenTicketId = kt.Id
        LEFT JOIN dbo.MenuItems i ON oi.MenuItemId = i.Id
        LEFT JOIN [dbo].[KitchenStations] ksMenu ON i.KitchenStationId = ksMenu.Id
        LEFT JOIN [dbo].[KitchenStations] ksTicket ON kti.KitchenStationId = ksTicket.Id
        LEFT JOIN Tables t ON o.TableTurnoverId = t.Id
        WHERE kt.KitchenStation = 'KITCHEN'
          AND (
              (kt.CreatedAt >= @Start AND kt.CreatedAt < @End)
              OR (kti.CompletionTime >= @Start AND kti.CompletionTime < @End)
          )
          AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId)
    )
    SELECT
        OrderId,
        OrderNumber,
        KOTNumber,
        TableName,
        ItemName,
        Quantity,
        Station,
        Status,
        RequestedAt
    FROM KOTData
    WHERE (@Station IS NULL OR @Station = '' OR Station = @Station)
      AND (Station IS NULL OR Station <> 'Bar')
    ORDER BY RequestedAt DESC, KOTNumber DESC;
END
GO
