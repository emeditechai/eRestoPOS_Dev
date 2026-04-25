-- ═══════════════════════════════════════════════════════════════════════════════
-- Cross-Branch Stock Report Fix
-- Run this on the live database to allow main-branch admin to view all branches'
-- godown data in Closing Stock Report and Stock Valuation Report.
-- Changes:
--   1. usp_GetClosingStockReport  – @BranchId now nullable (NULL = all branches)
--                                  + adds ItemCategory to result set
--   2. usp_GetStockValuationReport – same two changes
-- ═══════════════════════════════════════════════════════════════════════════════

-- ─── 1. Closing Stock Report ─────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.usp_GetClosingStockReport', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetClosingStockReport;
GO
CREATE PROCEDURE dbo.usp_GetClosingStockReport
    @BranchId INT = NULL,   -- NULL = all branches (main-branch admin only)
    @AsOfDate DATE,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        i.IngredientsName               AS ItemName,
        ISNULL(i.Code,'')               AS ItemCode,
        ISNULL(i.ItemCategory,'')       AS ItemCategory,
        g.GodownName,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'OPENING' THEN sl.InQuantity ELSE 0 END), 0)        AS OpeningQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType IN ('GRN','PURCHASE')  THEN sl.InQuantity ELSE 0 END), 0) AS PurchaseQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'TRANSFER_IN'  THEN sl.InQuantity  ELSE 0 END), 0)  AS TransferInQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'TRANSFER_OUT' THEN sl.OutQuantity ELSE 0 END), 0)  AS TransferOutQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'DAMAGE'       THEN sl.OutQuantity ELSE 0 END), 0)  AS DamageQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'SALE'         THEN sl.OutQuantity ELSE 0 END), 0)  AS SaleQty,
        ISNULL(SUM(sl.InQuantity - sl.OutQuantity), 0)                                                AS ClosingQty,
        ISNULL(MAX(sl.AverageCost), 0)                                                                AS AverageCost,
        ISNULL(SUM(sl.InQuantity - sl.OutQuantity), 0)
            * ISNULL(MAX(sl.AverageCost), 0)                                                          AS ClosingValue
    FROM dbo.StockLedger sl
    INNER JOIN dbo.Ingredients i    ON i.Id  = sl.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id  = sl.GodownId
    WHERE (@BranchId IS NULL OR sl.BranchId = @BranchId)
      AND sl.TransactionDate <= @AsOfDate
      AND (@GodownId IS NULL  OR sl.GodownId = @GodownId)
    GROUP BY i.IngredientsName, i.Code, i.ItemCategory, g.GodownName
    HAVING ISNULL(SUM(sl.InQuantity - sl.OutQuantity), 0) <> 0
    ORDER BY g.GodownName, i.IngredientsName;
END
GO

-- ─── 2. Stock Valuation Report ───────────────────────────────────────────────
IF OBJECT_ID(N'dbo.usp_GetStockValuationReport', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetStockValuationReport;
GO
CREATE PROCEDURE dbo.usp_GetStockValuationReport
    @BranchId INT = NULL,   -- NULL = all branches (main-branch admin only)
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        cs.GodownId,
        g.GodownName,
        cs.ItemId,
        i.IngredientsName         AS ItemName,
        ISNULL(i.Code,'')         AS ItemCode,
        ISNULL(i.ItemCategory,'') AS ItemCategory,
        ISNULL(u.UOMCode,'')      AS UOMCode,
        cs.BalanceQty,
        cs.AverageCost,
        (cs.BalanceQty * cs.AverageCost) AS StockValue
    FROM dbo.CurrentStock cs
    INNER JOIN dbo.Ingredients i    ON i.Id    = cs.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id    = cs.GodownId
    LEFT  JOIN dbo.UomMaster u      ON u.UOMId = i.PurchaseUOMId
    WHERE (@BranchId IS NULL OR cs.BranchId = @BranchId)
      AND (@GodownId IS NULL OR cs.GodownId = @GodownId)
      AND cs.BalanceQty <> 0
    ORDER BY g.GodownName, i.IngredientsName;
END
GO
