IF OBJECT_ID('dbo.usp_GetGSTBreakupReport', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetGSTBreakupReport;
GO
CREATE PROCEDURE dbo.usp_GetGSTBreakupReport
    @StartDate DATE = NULL,
    @EndDate   DATE = NULL,
    @BranchId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Normalize dates: if only one provided treat both as same day
    IF @StartDate IS NULL AND @EndDate IS NULL
    BEGIN
        SET @StartDate = CAST(GETDATE() AS DATE);
        SET @EndDate = @StartDate;
    END
    ELSE IF @StartDate IS NULL SET @StartDate = @EndDate;
    ELSE IF @EndDate IS NULL SET @EndDate = @StartDate;

    -- Aggregate payments by order to handle split payments correctly
    -- Multiple payment rows per order should be treated as one invoice
    IF OBJECT_ID('tempdb..#OrderGST') IS NOT NULL DROP TABLE #OrderGST;
    
    SELECT 
        o.Id AS OrderId,
        o.OrderNumber,
        MIN(p.CreatedAt) AS PaymentDate,
        -- Use Orders table as the authoritative source for financial amounts.
        -- This correctly handles BAR inclusive-GST orders (where Orders.Subtotal is the
        -- after-discount taxable base and Orders.TaxAmount is the exact GST charged),
        -- as well as regular exclusive-GST orders. Stored Payments.CGSTAmount /
        -- Payments.SGSTAmount can be wrong due to the double-discount bug for BAR orders.
        ISNULL(MIN(o.Subtotal), 0) AS TaxableValue,
        ISNULL(MIN(o.DiscountAmount), 0) AS DiscountAmount,
        -- Percentages are stored correctly in Payments rows
        MAX(ISNULL(p.CGST_Perc,0)) AS CGSTPerc,
        ROUND(ISNULL(MIN(o.TaxAmount), 0) / 2.0, 2) AS CGSTAmount,
        MAX(ISNULL(p.SGST_Perc,0)) AS SGSTPerc,
        ISNULL(MIN(o.TaxAmount), 0) - ROUND(ISNULL(MIN(o.TaxAmount), 0) / 2.0, 2) AS SGSTAmount,
        ISNULL(MIN(o.TaxAmount), 0) AS TotalGST,
        ISNULL(MIN(o.Subtotal), 0) + ISNULL(MIN(o.TaxAmount), 0) AS InvoiceTotal
    INTO #OrderGST
    FROM Orders o
    INNER JOIN Payments p ON o.Id = p.OrderId
    WHERE CAST(p.CreatedAt AS DATE) BETWEEN @StartDate AND @EndDate
      AND p.Status = 1 -- Only approved/completed payments
            AND (@BranchId IS NULL OR COL_LENGTH('dbo.Orders', 'BranchId') IS NULL OR o.BranchId = @BranchId)
    GROUP BY o.Id, o.OrderNumber;

    -- Summary: One row per order (invoice)
    SELECT 
        COUNT(*) AS InvoiceCount,
        SUM(TaxableValue) AS TotalTaxableValue,
        SUM(DiscountAmount) AS TotalDiscount,
        SUM(CGSTAmount) AS TotalCGST,
        SUM(SGSTAmount) AS TotalSGST,
        SUM(InvoiceTotal) AS NetAmount,
        CASE WHEN COUNT(*) > 0 THEN SUM(TaxableValue) / COUNT(*) ELSE 0 END AS AverageTaxablePerInvoice,
        CASE WHEN COUNT(*) > 0 THEN (SUM(CGSTAmount)+SUM(SGSTAmount)) / COUNT(*) ELSE 0 END AS AverageGSTPerInvoice
    FROM #OrderGST;

    -- Detail rows: One row per order (already aggregated in temp table)
    SELECT 
        PaymentDate,
        OrderNumber,
        TaxableValue,
        DiscountAmount,
        CGSTPerc AS CGSTPercentage,
        CGSTAmount,
        SGSTPerc AS SGSTPercentage,
        SGSTAmount,
        TotalGST,
        InvoiceTotal
    FROM #OrderGST
    ORDER BY PaymentDate ASC, OrderNumber ASC;
    
    DROP TABLE #OrderGST;
END
GO
