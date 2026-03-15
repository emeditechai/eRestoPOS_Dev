-- ============================================================
-- Stored Procedure : dbo.usp_GetProfitLossReport
-- Purpose          : Profit & Loss Analysis Report
--                    Returns 5 result sets in one call:
--                      #1  Summary    (TotalQtySold, TotalSales, TotalCost)
--                      #2  MenuItems  (per item – sorted by SalesValue DESC)
--                      #3  Categories (grouped by category)
--                      #4  Branches   (only when @IsMainBranchAdmin = 1)
--                      #5  Periods    (trend – grouped by @GroupBy)
-- Cost Method      : Weighted Average via CurrentStock.AverageCost × BOM qty
--                    Fallback : Ingredients.StandardCost
--                    No BOM   : Cost = 0
-- Parameters:
--   @StartDate          DATE          – Inclusive start of period
--   @EndDate            DATE          – Inclusive end of period (End-of-day)
--   @GroupBy            VARCHAR(20)   – daily | weekly | monthly | quarterly | yearly
--   @BranchIds          VARCHAR(500)  – Comma-separated BranchId list e.g. '1,2,5'
--   @CategoryId         INT NULL      – NULL = all categories
--   @IsMainBranchAdmin  BIT           – 1 = return branch breakdown result set
-- Safe   : CREATE OR ALTER – idempotent.
-- Created: 2026-03-15
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.usp_GetProfitLossReport
    @StartDate          DATE,
    @EndDate            DATE,
    @GroupBy            VARCHAR(20)  = 'monthly',
    @BranchIds          VARCHAR(500) = '',
    @CategoryId         INT          = NULL,
    @IsMainBranchAdmin  BIT          = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Exclusive end date (< next day) ─────────────────────────
    DECLARE @ExclusiveEnd DATETIME = DATEADD(DAY, 1, CAST(@EndDate AS DATETIME));

    -- ── Schema presence checks ───────────────────────────────────
    DECLARE @HasMIITable    BIT = 0;
    DECLARE @HasCurrentStock BIT = 0;
    DECLARE @HasBranchCol   BIT = 0;

    IF OBJECT_ID('dbo.MenuItemIngredients', 'U') IS NOT NULL
        AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'MenuItemIngredients' AND COLUMN_NAME = 'MenuItemId')
        SET @HasMIITable = 1;

    IF OBJECT_ID('dbo.CurrentStock', 'U') IS NOT NULL
        AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'CurrentStock' AND COLUMN_NAME = 'AverageCost')
        SET @HasCurrentStock = 1;

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'Orders' AND COLUMN_NAME = 'BranchId')
        SET @HasBranchCol = 1;

    -- ── Build dynamic fragments ──────────────────────────────────

    -- BOM CTE
    DECLARE @BomCte NVARCHAR(MAX);
    IF @HasMIITable = 1 AND @HasCurrentStock = 1
        SET @BomCte = N'
    IngredientAvgCost AS (
        SELECT ItemId, AVG(AverageCost) AS AvgCost
        FROM dbo.CurrentStock
        GROUP BY ItemId
    ),
    BOMCost AS (
        -- AverageCost / StandardCost is stored per PURCHASE unit (e.g. per kg).
        -- BOM Quantity is in RECIPE units (e.g. grams).
        -- PurchaseToRecipeFactor converts: cost per recipe unit = cost / factor.
        SELECT mii.MenuItemId,
               SUM(
                   mii.Quantity
                   * COALESCE(iac.AvgCost, ing.StandardCost, 0)
                   / NULLIF(ing.PurchaseToRecipeFactor, 0)
               ) AS CostPerUnit,
               1 AS HasBOM
        FROM dbo.MenuItemIngredients mii
        INNER JOIN dbo.Ingredients ing ON ing.Id = mii.IngredientId
        LEFT  JOIN IngredientAvgCost iac ON iac.ItemId = mii.IngredientId
        GROUP BY mii.MenuItemId
    )';
    ELSE
        SET @BomCte = N'
    BOMCost AS (SELECT NULL AS MenuItemId, 0.0 AS CostPerUnit, 0 AS HasBOM WHERE 1=0)';

    -- Branch filter
    DECLARE @BranchFilter NVARCHAR(200) = N'';
    IF @HasBranchCol = 1 AND LEN(LTRIM(RTRIM(@BranchIds))) > 0
        SET @BranchFilter = N' AND o.BranchId IN (' + @BranchIds + N')';

    -- Category filter
    DECLARE @CatFilter NVARCHAR(100) = N'';
    IF @CategoryId IS NOT NULL
        SET @CatFilter = N' AND mi.CategoryId = @pCategoryId';

    -- Period expression
    DECLARE @PeriodExpr NVARCHAR(200);
    DECLARE @PeriodSort NVARCHAR(200);

    SELECT
        @PeriodExpr = CASE @GroupBy
            WHEN 'daily'     THEN N'CONVERT(VARCHAR(10), o.CreatedAt, 23)'
            WHEN 'weekly'    THEN N'CONCAT(''W'', DATEPART(iso_week, o.CreatedAt), ''-'', YEAR(o.CreatedAt))'
            WHEN 'quarterly' THEN N'CONCAT(''Q'', DATEPART(QUARTER, o.CreatedAt), '' '', YEAR(o.CreatedAt))'
            WHEN 'yearly'    THEN N'CAST(YEAR(o.CreatedAt) AS VARCHAR(4))'
            ELSE                  N'CONCAT(YEAR(o.CreatedAt), ''-'', RIGHT(''0''+CAST(MONTH(o.CreatedAt) AS VARCHAR(2)),2))'
        END,
        @PeriodSort  = CASE @GroupBy
            WHEN 'daily'     THEN N'CAST(CONVERT(VARCHAR(10), o.CreatedAt, 23) AS VARCHAR(10))'
            WHEN 'weekly'    THEN N'CAST(YEAR(o.CreatedAt)*100 + DATEPART(iso_week, o.CreatedAt) AS VARCHAR(20))'
            WHEN 'quarterly' THEN N'CAST(YEAR(o.CreatedAt)*10  + DATEPART(QUARTER, o.CreatedAt) AS VARCHAR(20))'
            WHEN 'yearly'    THEN N'CAST(YEAR(o.CreatedAt) AS VARCHAR(4))'
            ELSE                  N'CAST(YEAR(o.CreatedAt)*100 + MONTH(o.CreatedAt) AS VARCHAR(20))'
        END;

    -- ── Shared params for sp_executesql ─────────────────────────
    DECLARE @ParamDef NVARCHAR(200) =
        N'@pStart DATETIME, @pEnd DATETIME, @pCategoryId INT';

    -- ============================================================
    -- RESULT SET 1 : Summary
    -- ============================================================
    DECLARE @SqlSummary NVARCHAR(MAX) = N'
WITH ' + @BomCte + N',
Summary AS (
    SELECT
        SUM(oi.Quantity)                              AS TotalQtySold,
        SUM(oi.Quantity * oi.UnitPrice)               AS TotalSales,
        SUM(oi.Quantity * ISNULL(bc.CostPerUnit, 0))  AS TotalCost,
        COUNT(DISTINCT o.Id)                          AS TotalOrders
    FROM dbo.OrderItems  oi
    INNER JOIN dbo.Orders    o   ON o.Id  = oi.OrderId
    INNER JOIN dbo.MenuItems mi  ON mi.Id = oi.MenuItemId
    LEFT  JOIN BOMCost        bc  ON bc.MenuItemId = oi.MenuItemId
    WHERE o.CreatedAt >= @pStart AND o.CreatedAt < @pEnd
      AND o.Status IN (1, 3)'
    + @BranchFilter + @CatFilter + N'
)
SELECT TotalQtySold, TotalSales, TotalCost, TotalOrders FROM Summary;';

    EXEC sp_executesql @SqlSummary, @ParamDef,
        @pStart = @StartDate, @pEnd = @ExclusiveEnd, @pCategoryId = @CategoryId;

    -- ============================================================
    -- RESULT SET 2 : Menu Item detail rows
    -- ============================================================
    DECLARE @SqlItems NVARCHAR(MAX) = N'
WITH ' + @BomCte + N'
SELECT
    oi.MenuItemId,
    mi.Name                                                   AS ItemName,
    ISNULL(cat.Name, ''Uncategorized'')                       AS CategoryName,
    CAST(SUM(oi.Quantity) AS INT)                             AS QtySold,
    SUM(oi.Quantity * oi.UnitPrice)                           AS SalesValue,
    SUM(oi.Quantity * ISNULL(bc.CostPerUnit, 0))              AS CostValue,
    ISNULL(MAX(bc.HasBOM), 0)                                 AS HasBOM
FROM dbo.OrderItems  oi
INNER JOIN dbo.Orders    o   ON o.Id  = oi.OrderId
INNER JOIN dbo.MenuItems mi  ON mi.Id = oi.MenuItemId
LEFT  JOIN dbo.Categories cat ON cat.Id = mi.CategoryId
LEFT  JOIN BOMCost        bc  ON bc.MenuItemId = oi.MenuItemId
WHERE o.CreatedAt >= @pStart AND o.CreatedAt < @pEnd
  AND o.Status IN (1, 3)'
    + @BranchFilter + @CatFilter + N'
GROUP BY oi.MenuItemId, mi.Name, ISNULL(cat.Name,''Uncategorized'')
ORDER BY SUM(oi.Quantity * oi.UnitPrice) DESC;';

    EXEC sp_executesql @SqlItems, @ParamDef,
        @pStart = @StartDate, @pEnd = @ExclusiveEnd, @pCategoryId = @CategoryId;

    -- ============================================================
    -- RESULT SET 3 : Category rows
    -- ============================================================
    DECLARE @SqlCat NVARCHAR(MAX) = N'
WITH ' + @BomCte + N'
SELECT
    ISNULL(cat.Name, ''Uncategorized'')                       AS CategoryName,
    CAST(SUM(oi.Quantity) AS INT)                             AS QtySold,
    SUM(oi.Quantity * oi.UnitPrice)                           AS SalesValue,
    SUM(oi.Quantity * ISNULL(bc.CostPerUnit, 0))              AS CostValue
FROM dbo.OrderItems  oi
INNER JOIN dbo.Orders    o   ON o.Id  = oi.OrderId
INNER JOIN dbo.MenuItems mi  ON mi.Id = oi.MenuItemId
LEFT  JOIN dbo.Categories cat ON cat.Id = mi.CategoryId
LEFT  JOIN BOMCost        bc  ON bc.MenuItemId = oi.MenuItemId
WHERE o.CreatedAt >= @pStart AND o.CreatedAt < @pEnd
  AND o.Status IN (1, 3)'
    + @BranchFilter + @CatFilter + N'
GROUP BY ISNULL(cat.Name,''Uncategorized'')
ORDER BY SUM(oi.Quantity * oi.UnitPrice) DESC;';

    EXEC sp_executesql @SqlCat, @ParamDef,
        @pStart = @StartDate, @pEnd = @ExclusiveEnd, @pCategoryId = @CategoryId;

    -- ============================================================
    -- RESULT SET 4 : Branch rows  (only when @IsMainBranchAdmin = 1)
    -- ============================================================
    IF @IsMainBranchAdmin = 1 AND @HasBranchCol = 1
    BEGIN
        DECLARE @SqlBranch NVARCHAR(MAX) = N'
WITH ' + @BomCte + N'
SELECT
    b.BranchId,
    ISNULL(b.BranchName, ''Unknown'')                         AS BranchName,
    SUM(oi.Quantity * oi.UnitPrice)                           AS SalesValue,
    SUM(oi.Quantity * ISNULL(bc.CostPerUnit, 0))              AS CostValue
FROM dbo.OrderItems  oi
INNER JOIN dbo.Orders    o   ON o.Id  = oi.OrderId
INNER JOIN dbo.MenuItems mi  ON mi.Id = oi.MenuItemId
INNER JOIN dbo.Branches  b   ON b.BranchId = o.BranchId
LEFT  JOIN BOMCost        bc  ON bc.MenuItemId = oi.MenuItemId
WHERE o.CreatedAt >= @pStart AND o.CreatedAt < @pEnd
  AND o.Status IN (1, 3)'
        + @BranchFilter + @CatFilter + N'
GROUP BY b.BranchId, b.BranchName
ORDER BY SUM(oi.Quantity * oi.UnitPrice) DESC;';

        EXEC sp_executesql @SqlBranch, @ParamDef,
            @pStart = @StartDate, @pEnd = @ExclusiveEnd, @pCategoryId = @CategoryId;
    END
    ELSE
    BEGIN
        -- Always return result set 4 (empty) so ordinal reading stays consistent
        SELECT
            CAST(0  AS INT)          AS BranchId,
            CAST('' AS VARCHAR(100)) AS BranchName,
            CAST(0  AS DECIMAL(18,4))AS SalesValue,
            CAST(0  AS DECIMAL(18,4))AS CostValue
        WHERE 1 = 0;
    END

    -- ============================================================
    -- RESULT SET 5 : Period trend rows
    -- ============================================================
    DECLARE @SqlPeriod NVARCHAR(MAX) = N'
WITH ' + @BomCte + N'
SELECT
    ' + @PeriodExpr + N'                                      AS PeriodLabel,
    ' + @PeriodSort + N'                                      AS SortKey,
    CAST(SUM(oi.Quantity) AS INT)                             AS QtySold,
    SUM(oi.Quantity * oi.UnitPrice)                           AS SalesValue,
    SUM(oi.Quantity * ISNULL(bc.CostPerUnit, 0))              AS CostValue
FROM dbo.OrderItems  oi
INNER JOIN dbo.Orders    o   ON o.Id  = oi.OrderId
INNER JOIN dbo.MenuItems mi  ON mi.Id = oi.MenuItemId
LEFT  JOIN BOMCost        bc  ON bc.MenuItemId = oi.MenuItemId
WHERE o.CreatedAt >= @pStart AND o.CreatedAt < @pEnd
  AND o.Status IN (1, 3)'
    + @BranchFilter + @CatFilter + N'
GROUP BY ' + @PeriodExpr + N', ' + @PeriodSort + N'
ORDER BY ' + @PeriodSort + N';';

    EXEC sp_executesql @SqlPeriod, @ParamDef,
        @pStart = @StartDate, @pEnd = @ExclusiveEnd, @pCategoryId = @CategoryId;

END
GO
