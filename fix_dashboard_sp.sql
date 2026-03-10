USE [dev_Restaurant];
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetInventoryDashboardStats
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    -- RS1: Scalar KPI stats
    SELECT
        ISNULL(SUM(cs.BalanceQty * cs.AverageCost), 0)                          AS TotalStockValue,
        -- Low stock = items at or below reorder level (or qty <= 0 if no reorder set)
        ISNULL((
            SELECT COUNT(*) FROM dbo.CurrentStock cs2
            JOIN dbo.Ingredients i2 ON i2.Id = cs2.ItemId
            WHERE cs2.BranchId = @BranchId
              AND (
                    (i2.ReorderLevel > 0 AND cs2.BalanceQty <= i2.ReorderLevel)
                    OR cs2.BalanceQty <= 0
                  )
        ), 0)                                                                    AS LowStockItems,
        ISNULL((SELECT COUNT(*) FROM dbo.GRNMaster
                WHERE BranchId = @BranchId AND Status = 'Draft'), 0)            AS PendingGRN,
        ISNULL((SELECT SUM(TotalAmount) FROM dbo.GRNMaster
                WHERE BranchId = @BranchId AND Status = 'Posted'
                  AND GRNDate = @Today), 0)                                      AS TodayPurchase,
        CAST(0 AS DECIMAL(18,2))                                                 AS TodayConsumption,
        ISNULL((SELECT COUNT(*) FROM dbo.Godowns
                WHERE BranchId = @BranchId AND IsActive = 1), 0)               AS ActiveGodowns,
        ISNULL((SELECT COUNT(*) FROM dbo.DamageEntry
                WHERE BranchId = @BranchId
                  AND DamageDate = @Today), 0)                                   AS TodayDamageCount
    FROM dbo.CurrentStock cs
    WHERE cs.BranchId = @BranchId;

    -- RS2: Top 10 consumed items TODAY (SALE / CONSUMPTION transactions only)
    --      These are populated when POS is integrated.
    --      DAMAGE and TRANSFER_OUT are intentionally excluded — they are
    --      not "consumption from sale"; they have their own registers.
    SELECT TOP 10
        i.IngredientsName    AS ItemName,
        ISNULL(i.Code, '')   AS ItemCode,
        ISNULL(SUM(sl.OutQuantity), 0) AS TotalConsumed,
        ISNULL(u.UOMCode, '') AS UOMCode
    FROM dbo.StockLedger sl
    INNER JOIN dbo.Ingredients i ON i.Id    = sl.ItemId
    LEFT  JOIN dbo.UomMaster   u ON u.UOMId = i.PurchaseUOMId
    WHERE sl.BranchId        = @BranchId
      AND sl.TransactionDate = @Today
      AND sl.TransactionType IN ('SALE', 'CONSUMPTION')   -- only actual sales/kitchen consumption
    GROUP BY i.IngredientsName, i.Code, u.UOMCode
    HAVING SUM(sl.OutQuantity) > 0
    ORDER BY SUM(sl.OutQuantity) DESC;

    -- RS3: Low stock alerts
    SELECT
        i.IngredientsName    AS ItemName,
        ISNULL(i.Code, '')   AS ItemCode,
        cs.BalanceQty,
        ISNULL(i.ReorderLevel, 0) AS ReorderLevel,
        ISNULL(u.UOMCode, '') AS UOMCode,
        g.GodownName
    FROM dbo.CurrentStock cs
    INNER JOIN dbo.Ingredients i ON i.Id    = cs.ItemId
    INNER JOIN dbo.Godowns     g ON g.Id    = cs.GodownId
    LEFT  JOIN dbo.UomMaster   u ON u.UOMId = i.PurchaseUOMId
    WHERE cs.BranchId = @BranchId
      AND (
            (i.ReorderLevel > 0 AND cs.BalanceQty <= i.ReorderLevel)
            OR cs.BalanceQty <= 0
          )
    ORDER BY cs.BalanceQty ASC, i.IngredientsName;
END;
GO

PRINT 'usp_GetInventoryDashboardStats updated.';
GO
