USE [dev_Restaurant];
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Fix: Add ItemCategory, ReorderLevel; compute IsLowStock properly;
-- filter includes cross-branch godowns (BranchId filter only).
CREATE OR ALTER PROCEDURE dbo.usp_GetCurrentStockSummary
    @BranchId INT,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        cs.StockId,
        cs.BranchId,
        cs.GodownId,
        cs.ItemId,
        cs.BalanceQty,
        cs.AverageCost,
        cs.BalanceQty * cs.AverageCost          AS StockValue,
        i.IngredientsName                        AS ItemName,
        ISNULL(i.Code,        '')                AS ItemCode,
        ISNULL(i.ItemCategory,'')                AS ItemCategory,
        ISNULL(i.ReorderLevel, 0)                AS ReorderLevel,
        ISNULL(u.UOMCode,     '')                AS BaseUOMCode,
        ISNULL(u.UOMName,     '')                AS BaseUOMName,
        g.GodownName,
        b.BranchName,
        CASE WHEN g.IsMainGodown = 1 THEN 'Main' ELSE 'Sub' END AS GodownType,
        CASE WHEN i.ReorderLevel > 0 AND cs.BalanceQty <= i.ReorderLevel
             THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END        AS IsLowStock
    FROM  dbo.CurrentStock cs
    JOIN  dbo.Ingredients  i  ON i.Id     = cs.ItemId
    JOIN  dbo.Godowns      g  ON g.Id     = cs.GodownId
    JOIN  dbo.Branches     b  ON b.BranchId = g.BranchId
    LEFT  JOIN dbo.UomMaster u ON u.UOMId = i.PurchaseUOMId
    WHERE cs.BranchId = @BranchId
      AND (@GodownId IS NULL OR cs.GodownId = @GodownId)
      AND cs.BalanceQty <> 0
      AND i.IsActive = 1
    ORDER BY g.GodownName, i.IngredientsName;
END;
GO

-- Returns distinct godowns that have stock for a given BranchId.
-- Used to populate the filter dropdown on the StockSummary page.
CREATE OR ALTER PROCEDURE dbo.usp_GetGodownsWithStock
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT
        g.Id          AS GodownId,
        g.GodownName,
        b.BranchName,
        g.BranchId    AS GodownBranchId,
        g.IsMainGodown
    FROM  dbo.CurrentStock cs
    JOIN  dbo.Godowns  g ON g.Id       = cs.GodownId
    JOIN  dbo.Branches b ON b.BranchId = g.BranchId
    WHERE cs.BranchId  = @BranchId
      AND cs.BalanceQty <> 0
      AND g.IsActive    = 1
    ORDER BY b.BranchName, g.GodownName;
END;
GO
