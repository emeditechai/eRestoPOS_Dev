IF OBJECT_ID('dbo.usp_GetGSTBreakupReport', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetGSTBreakupReport;
GO
CREATE PROCEDURE dbo.usp_GetGSTBreakupReport
    @StartDate DATE = NULL,
    @EndDate   DATE = NULL,
    @BranchId  INT  = NULL,
    @BranchIds NVARCHAR(MAX) = NULL   -- comma-separated branch IDs for multi-branch admin
AS
BEGIN
    SET NOCOUNT ON;

    -- Normalize dates
    IF @StartDate IS NULL AND @EndDate IS NULL
    BEGIN
        SET @StartDate = CAST(GETDATE() AS DATE);
        SET @EndDate = @StartDate;
    END
    ELSE IF @StartDate IS NULL SET @StartDate = @EndDate;
    ELSE IF @EndDate   IS NULL SET @EndDate   = @StartDate;

    -- ── Branch filter ────────────────────────────────────────────────────────
    IF OBJECT_ID('tempdb..#BranchFilter') IS NOT NULL DROP TABLE #BranchFilter;
    CREATE TABLE #BranchFilter (BranchId INT PRIMARY KEY);
    IF @BranchIds IS NOT NULL AND LTRIM(RTRIM(@BranchIds)) <> ''
    BEGIN
        INSERT INTO #BranchFilter (BranchId)
        SELECT CAST(value AS INT)
        FROM STRING_SPLIT(@BranchIds, ',')
        WHERE LTRIM(RTRIM(value)) <> '';
    END
    ELSE IF @BranchId IS NOT NULL
        INSERT INTO #BranchFilter VALUES (@BranchId);

    -- ── BAR order detection (precomputed to avoid COL_LENGTH inside aggregates) ──
    -- Using two separate passes so each can use its own safe check:
    --   Pass 1: OrderKitchenType column (dynamic SQL guards column existence at runtime)
    --   Pass 2: KitchenTickets BOT/BAR rows (always works)
    IF OBJECT_ID('tempdb..#BarOrders') IS NOT NULL DROP TABLE #BarOrders;
    CREATE TABLE #BarOrders (OrderId INT PRIMARY KEY);

    IF COL_LENGTH('dbo.Orders', 'OrderKitchenType') IS NOT NULL
        EXEC sp_executesql N'
            INSERT INTO #BarOrders (OrderId)
            SELECT DISTINCT Id FROM dbo.Orders
            WHERE ISNULL(CAST(OrderKitchenType AS NVARCHAR(50)), '''') = ''Bar''';

    -- BOT/BAR kitchen tickets (merge, ignore duplicates)
    INSERT INTO #BarOrders (OrderId)
        SELECT DISTINCT kt.OrderId
        FROM dbo.KitchenTickets kt
        WHERE (kt.KitchenStation = 'BAR' OR kt.TicketNumber LIKE 'BOT-%')
          AND NOT EXISTS (SELECT 1 FROM #BarOrders b WHERE b.OrderId = kt.OrderId);

    -- ── Main aggregation ─────────────────────────────────────────────────────
    IF OBJECT_ID('tempdb..#OrderGST') IS NOT NULL DROP TABLE #OrderGST;

    SELECT
        o.Id          AS OrderId,
        o.OrderNumber,
        CASE WHEN COL_LENGTH('dbo.Orders','GlobalBillNo') IS NOT NULL
             THEN ISNULL(CAST(o.GlobalBillNo AS NVARCHAR(100)), '')
             ELSE '' END                                          AS GlobalBillNo,
        ISNULL(o.BranchId, 0)                                    AS BranchId,
        MIN(p.CreatedAt)                                          AS PaymentDate,
        ISNULL(MIN(o.Subtotal),       0)                          AS TaxableValue,
        ISNULL(MIN(o.DiscountAmount), 0)                          AS DiscountAmount,
        ISNULL(
            NULLIF(MIN(CASE WHEN COL_LENGTH('dbo.Orders','GSTPercentage') IS NOT NULL
                            THEN o.GSTPercentage ELSE NULL END), 0),
            MAX(ISNULL(p.GST_Perc, 0))
        )                                                         AS GSTPerc,
        MAX(ISNULL(p.CGST_Perc, 0))                              AS CGSTPerc,
        ROUND(ISNULL(MIN(o.TaxAmount), 0) / 2.0, 2)              AS CGSTAmount,
        MAX(ISNULL(p.SGST_Perc, 0))                              AS SGSTPerc,
        ISNULL(MIN(o.TaxAmount),0) - ROUND(ISNULL(MIN(o.TaxAmount),0)/2.0,2) AS SGSTAmount,
        ISNULL(MIN(o.TaxAmount),       0)                         AS TotalGST,
        ISNULL(MIN(o.Subtotal),0) + ISNULL(MIN(o.TaxAmount),0)   AS InvoiceTotal,
        -- OrderType resolved via precomputed #BarOrders (no COL_LENGTH in CASE)
        CASE WHEN MIN(bo.OrderId) IS NOT NULL THEN 'BAR' ELSE 'Foods' END AS OrderType,
        ISNULL((
            SELECT TOP 1 t.TableName
            FROM dbo.OrderTables ot
            INNER JOIN dbo.Tables t ON t.Id = ot.TableId
            WHERE ot.OrderId = o.Id
        ), '')                                                    AS TableNumber
    INTO #OrderGST
    FROM dbo.Orders o
    INNER JOIN dbo.Payments p ON o.Id = p.OrderId
    LEFT  JOIN #BarOrders   bo ON bo.OrderId = o.Id
    WHERE CAST(p.CreatedAt AS DATE) BETWEEN @StartDate AND @EndDate
      AND p.Status = 1
      AND (
            (SELECT COUNT(*) FROM #BranchFilter) = 0
            OR COL_LENGTH('dbo.Orders','BranchId') IS NULL
            OR o.BranchId IN (SELECT BranchId FROM #BranchFilter)
          )
    GROUP BY
        o.Id,
        o.OrderNumber,
        CASE WHEN COL_LENGTH('dbo.Orders','GlobalBillNo') IS NOT NULL
             THEN ISNULL(CAST(o.GlobalBillNo AS NVARCHAR(100)), '')
             ELSE '' END,
        ISNULL(o.BranchId, 0);

    -- ── Summary row ───────────────────────────────────────────────────────────
    SELECT
        COUNT(*)                                                    AS InvoiceCount,
        SUM(TaxableValue)                                           AS TotalTaxableValue,
        SUM(DiscountAmount)                                         AS TotalDiscount,
        SUM(CGSTAmount)                                             AS TotalCGST,
        SUM(SGSTAmount)                                             AS TotalSGST,
        SUM(InvoiceTotal)                                           AS NetAmount,
        CASE WHEN COUNT(*) > 0 THEN SUM(TaxableValue) / COUNT(*) ELSE 0 END AS AverageTaxablePerInvoice,
        CASE WHEN COUNT(*) > 0 THEN (SUM(CGSTAmount)+SUM(SGSTAmount))/COUNT(*) ELSE 0 END AS AverageGSTPerInvoice
    FROM #OrderGST;

    -- ── Detail rows ───────────────────────────────────────────────────────────
    SELECT
        og.PaymentDate,
        og.OrderNumber,
        og.GlobalBillNo      AS BillNo,
        ISNULL(b.BranchName, '') AS BranchName,
        og.OrderType,
        og.TableNumber,
        og.TaxableValue,
        og.DiscountAmount,
        og.GSTPerc           AS GSTPercentage,
        og.CGSTPerc          AS CGSTPercentage,
        og.CGSTAmount,
        og.SGSTPerc          AS SGSTPercentage,
        og.SGSTAmount,
        og.TotalGST,
        og.InvoiceTotal
    FROM #OrderGST og
    LEFT JOIN dbo.Branches b
        ON COL_LENGTH('dbo.Orders','BranchId') IS NOT NULL
       AND b.BranchId = og.BranchId
    ORDER BY og.PaymentDate ASC, og.OrderNumber ASC;

    DROP TABLE #OrderGST;
    DROP TABLE #BarOrders;
    DROP TABLE #BranchFilter;
END
GO
