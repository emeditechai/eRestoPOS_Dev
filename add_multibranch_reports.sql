-- ============================================================
-- Multi-Branch Consolidated Report Support
-- Adds @BranchIds (comma-separated) parameter + BranchName column
-- to Collection Register, GST Breakup, and Purchase Register SPs
-- ============================================================

-- ============================================================
-- 1. usp_GetGSTBreakupReport
-- ============================================================
ALTER PROCEDURE dbo.usp_GetGSTBreakupReport
    @StartDate DATE = NULL,
    @EndDate   DATE = NULL,
    @BranchId  INT = NULL,
    @BranchIds NVARCHAR(MAX) = NULL   -- NEW: comma-separated for multi-branch
AS
BEGIN
    SET NOCOUNT ON;

    IF @StartDate IS NULL AND @EndDate IS NULL
    BEGIN
        SET @StartDate = CAST(GETDATE() AS DATE);
        SET @EndDate = @StartDate;
    END
    ELSE IF @StartDate IS NULL SET @StartDate = @EndDate;
    ELSE IF @EndDate IS NULL SET @EndDate = @StartDate;

    IF OBJECT_ID('tempdb..#OrderGST') IS NOT NULL DROP TABLE #OrderGST;

    SELECT
        o.Id AS OrderId,
        o.OrderNumber,
        ISNULL(o.GlobalBillNo, N'') AS GlobalBillNo,
        ISNULL(o.BranchId, 0) AS BranchId,
        MIN(p.CreatedAt) AS PaymentDate,
        SUM(p.Amount_ExclGST) - SUM(ISNULL(p.DiscAmount,0)) AS TaxableValue,
        SUM(ISNULL(p.DiscAmount,0)) AS DiscountAmount,
        MAX(ISNULL(p.CGST_Perc,0)) AS CGSTPerc,
        SUM(ISNULL(p.CGSTAmount,0)) AS CGSTAmount,
        MAX(ISNULL(p.SGST_Perc,0)) AS SGSTPerc,
        SUM(ISNULL(p.SGSTAmount,0)) AS SGSTAmount,
        SUM(ISNULL(p.CGSTAmount,0)) + SUM(ISNULL(p.SGSTAmount,0)) AS TotalGST,
        SUM(p.Amount_ExclGST) - SUM(ISNULL(p.DiscAmount,0)) +
            (SUM(ISNULL(p.CGSTAmount,0)) + SUM(ISNULL(p.SGSTAmount,0))) AS InvoiceTotal
    INTO #OrderGST
    FROM Orders o
    INNER JOIN Payments p ON o.Id = p.OrderId
    WHERE CAST(p.CreatedAt AS DATE) BETWEEN @StartDate AND @EndDate
      AND p.Status = 1
      AND (
            (@BranchIds IS NOT NULL
             AND COL_LENGTH('dbo.Orders','BranchId') IS NOT NULL
             AND o.BranchId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@BranchIds, ',')))
          OR
            (@BranchIds IS NULL
             AND (@BranchId IS NULL OR COL_LENGTH('dbo.Orders','BranchId') IS NULL OR o.BranchId = @BranchId))
          )
    GROUP BY o.Id, o.OrderNumber, o.GlobalBillNo, o.BranchId;

    -- Summary result set
    SELECT
        COUNT(*)                                                               AS InvoiceCount,
        SUM(TaxableValue)                                                      AS TotalTaxableValue,
        SUM(DiscountAmount)                                                    AS TotalDiscount,
        SUM(CGSTAmount)                                                        AS TotalCGST,
        SUM(SGSTAmount)                                                        AS TotalSGST,
        SUM(InvoiceTotal)                                                      AS NetAmount,
        CASE WHEN COUNT(*) > 0 THEN SUM(TaxableValue) / COUNT(*) ELSE 0 END   AS AverageTaxablePerInvoice,
        CASE WHEN COUNT(*) > 0 THEN (SUM(CGSTAmount)+SUM(SGSTAmount))/COUNT(*) ELSE 0 END AS AverageGSTPerInvoice
    FROM #OrderGST;

    -- Detail result set (with BranchName)
    SELECT
        og.PaymentDate,
        og.OrderNumber,
        og.GlobalBillNo  AS BillNo,
        ISNULL(b.BranchName, 'N/A') AS BranchName,
        og.TaxableValue,
        og.DiscountAmount,
        og.CGSTPerc      AS CGSTPercentage,
        og.CGSTAmount,
        og.SGSTPerc      AS SGSTPercentage,
        og.SGSTAmount,
        og.TotalGST,
        og.InvoiceTotal
    FROM #OrderGST og
    LEFT JOIN dbo.Branches b ON b.BranchId = og.BranchId
    ORDER BY og.PaymentDate ASC, og.OrderNumber ASC;

    DROP TABLE #OrderGST;
END
GO

-- ============================================================
-- 2. usp_GetCollectionRegister
-- ============================================================
ALTER PROCEDURE dbo.usp_GetCollectionRegister
    @FromDate        DATE = NULL,
    @ToDate          DATE = NULL,
    @PaymentMethodId INT = NULL,
    @UserId          INT = NULL,
    @CounterId       INT = NULL,
    @BranchId        INT = NULL,
    @BranchIds       NVARCHAR(MAX) = NULL   -- NEW: comma-separated for multi-branch
AS
BEGIN
    SET NOCOUNT ON;

    IF @FromDate IS NULL SET @FromDate = CAST(GETDATE() AS DATE);
    IF @ToDate IS NULL SET @ToDate = CAST(GETDATE() AS DATE);

    IF @FromDate > @ToDate
    BEGIN
        DECLARE @Temp DATE = @FromDate;
        SET @FromDate = @ToDate;
        SET @ToDate = @Temp;
    END;

    DECLARE @HasVoidReason BIT = 0;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'VoidReason')
        SET @HasVoidReason = 1;

    DECLARE @CounterColumn SYSNAME = NULL;
    IF COL_LENGTH('dbo.Orders', 'CounterId') IS NOT NULL
       SET @CounterColumn = 'CounterId';
    ELSE IF COL_LENGTH('dbo.Orders', 'CounterID') IS NOT NULL
       SET @CounterColumn = 'CounterID';

    DECLARE @HasCountersTable BIT = 0;
    IF OBJECT_ID('dbo.Counters', 'U') IS NOT NULL
        SET @HasCountersTable = 1;

    DECLARE @HasBranchesTable BIT = 0;
    IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL AND COL_LENGTH('dbo.Orders','BranchId') IS NOT NULL
        SET @HasBranchesTable = 1;

    DECLARE @CounterSelect NVARCHAR(MAX) = N'CAST(NULL AS INT) AS CounterId, CAST('''' AS NVARCHAR(200)) AS CounterName,';
    DECLARE @CounterJoin NVARCHAR(MAX) = N'';
    IF @CounterColumn IS NOT NULL
    BEGIN
       SET @CounterSelect = N'o.' + QUOTENAME(@CounterColumn) + N' AS CounterId, ';
       IF @HasCountersTable = 1
       BEGIN
          SET @CounterSelect += N'NULLIF(LTRIM(RTRIM(COALESCE(NULLIF(c.CounterCode, '''') + '' - '' + NULLIF(c.CounterName, ''''), NULLIF(c.CounterName, ''''), NULLIF(c.CounterCode, ''''), ''''))), '''') AS CounterName,';
          SET @CounterJoin = N'LEFT JOIN dbo.Counters c WITH (NOLOCK) ON c.Id = o.' + QUOTENAME(@CounterColumn) + CHAR(10);
       END
       ELSE
       BEGIN
          SET @CounterSelect += N'CAST('''' AS NVARCHAR(200)) AS CounterName,';
       END
    END

    DECLARE @BranchNameSelect NVARCHAR(MAX) = N'CAST(''N/A'' AS NVARCHAR(200)) AS BranchName,';
    DECLARE @BranchJoinClause NVARCHAR(MAX) = N'';
    IF @HasBranchesTable = 1
    BEGIN
        SET @BranchNameSelect = N'ISNULL(brnch.BranchName, ''N/A'') AS BranchName,';
        SET @BranchJoinClause = N'LEFT JOIN dbo.Branches brnch WITH (NOLOCK) ON brnch.BranchId = o.BranchId' + CHAR(10);
    END

    DECLARE @Sql NVARCHAR(MAX) = N'
    ;WITH FilteredPayments AS (
       SELECT
          p.Id,
          p.OrderId,
          p.PaymentMethodId,
          p.Amount,
          p.TipAmount,
          p.DiscAmount,
          p.CGSTAmount,
          p.SGSTAmount,
          p.RoundoffAdjustmentAmt,
          p.ProcessedByName,
          p.LastFourDigits,
          p.CardType,
          p.ReferenceNumber,
          p.CreatedAt,
          p.Status
       FROM Payments p WITH (NOLOCK)
       WHERE p.CreatedAt >= @FromDate
          AND p.CreatedAt < DATEADD(DAY, 1, @ToDate)
          AND p.Status IN (1, 3)
          AND (@PaymentMethodId IS NULL OR p.PaymentMethodId = @PaymentMethodId)
          AND (@UserId IS NULL OR p.ProcessedBy = @UserId)
    )
    SELECT
       o.OrderNumber AS OrderNo,
       ISNULL(o.GlobalBillNo, N'''') AS BillNo,
       ' + @BranchNameSelect + N'
       ISNULL(t.TableName, ''N/A'') AS TableNo,
       ISNULL(p.ProcessedByName, ''System'') AS Username,
           ' + @CounterSelect + N'
       CASE WHEN p.Status = 3 THEN -(ISNULL(o.Subtotal, 0) - ISNULL(p.DiscAmount, 0))
            ELSE ISNULL(o.Subtotal, 0) - ISNULL(p.DiscAmount, 0)
       END AS ActualBillAmount,
       CASE WHEN p.Status = 3 THEN -ISNULL(p.DiscAmount, 0)
            ELSE ISNULL(p.DiscAmount, 0)
       END AS DiscountAmount,
       CASE WHEN p.Status = 3 THEN -(ISNULL(p.CGSTAmount, 0) + ISNULL(p.SGSTAmount, 0))
            ELSE ISNULL(p.CGSTAmount, 0) + ISNULL(p.SGSTAmount, 0)
       END AS GSTAmount,
       CASE WHEN p.Status = 3 THEN -ISNULL(p.RoundoffAdjustmentAmt, 0)
            ELSE ISNULL(p.RoundoffAdjustmentAmt, 0)
       END AS RoundOffAmount,
       CASE WHEN p.Status = 3 THEN -(p.Amount + ISNULL(p.TipAmount, 0) + ISNULL(p.RoundoffAdjustmentAmt, 0))
            ELSE p.Amount + ISNULL(p.TipAmount, 0) + ISNULL(p.RoundoffAdjustmentAmt, 0)
       END AS ReceiptAmount,
       CASE WHEN p.Status = 3 THEN pm.Name + '' (REFUND)''
            ELSE pm.Name
       END AS PaymentMethod,
       CASE WHEN p.Status = 3 THEN ''🔴 REFUND - Payment voided'' ELSE '''' END +
       STUFF(
          CASE WHEN p.DiscAmount > 0 THEN '' | Discount: ₹'' + CAST(p.DiscAmount AS VARCHAR(20)) ELSE '''' END +
          CASE WHEN ISNULL(p.CGSTAmount, 0) + ISNULL(p.SGSTAmount, 0) > 0
              THEN '' | GST: ₹'' + CAST(ISNULL(p.CGSTAmount, 0) + ISNULL(p.SGSTAmount, 0) AS VARCHAR(20)) ELSE '''' END +
          CASE WHEN ISNULL(p.LastFourDigits, '''') <> ''''
              THEN '' | Card: '' + p.CardType + '' *'' + p.LastFourDigits ELSE '''' END +
          CASE WHEN ISNULL(p.ReferenceNumber, '''') <> ''''
              THEN '' | Ref: '' + p.ReferenceNumber ELSE '''' END +
          CASE WHEN ISNULL(p.TipAmount, 0) > 0
              THEN '' | Tip: ₹'' + CAST(p.TipAmount AS VARCHAR(20)) ELSE '''' END,
          1, 3, ''''
       ) AS Details,
       p.CreatedAt AS PaymentDate,
       p.Status AS PaymentStatus
    FROM FilteredPayments p
    INNER JOIN Orders o WITH (NOLOCK) ON p.OrderId = o.Id
        ' + @CounterJoin + N'
        ' + @BranchJoinClause + N'
    INNER JOIN PaymentMethods pm WITH (NOLOCK) ON p.PaymentMethodId = pm.Id
    LEFT JOIN (
       SELECT OrderId, MIN(TableId) AS TableId
       FROM OrderTables WITH (NOLOCK)
       GROUP BY OrderId
    ) ot ON o.Id = ot.OrderId
    LEFT JOIN Tables t WITH (NOLOCK) ON ot.TableId = t.Id
    WHERE (
        (@BranchIds IS NOT NULL AND COL_LENGTH(''dbo.Orders'', ''BranchId'') IS NOT NULL
         AND o.BranchId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@BranchIds, '','')) )
        OR
        (@BranchIds IS NULL AND (@BranchId IS NULL OR COL_LENGTH(''dbo.Orders'', ''BranchId'') IS NULL OR o.BranchId = @BranchId))
    )
    ';

    IF @CounterId IS NOT NULL AND @CounterColumn IS NOT NULL
        SET @Sql += N'AND o.' + QUOTENAME(@CounterColumn) + N' = @CounterId' + CHAR(10);

    SET @Sql += N'ORDER BY p.CreatedAt DESC, o.OrderNumber;';

    EXEC sp_executesql
       @Sql,
       N'@FromDate DATE, @ToDate DATE, @PaymentMethodId INT, @UserId INT, @CounterId INT, @BranchId INT, @BranchIds NVARCHAR(MAX)',
       @FromDate = @FromDate,
       @ToDate = @ToDate,
       @PaymentMethodId = @PaymentMethodId,
       @UserId = @UserId,
       @CounterId = @CounterId,
       @BranchId = @BranchId,
       @BranchIds = @BranchIds;
END
GO

-- ============================================================
-- 3. usp_GetPurchaseRegister
-- ============================================================
ALTER PROCEDURE dbo.usp_GetPurchaseRegister
    @BranchId   INT,
    @FromDate   DATE,
    @ToDate     DATE,
    @SupplierId INT = NULL,
    @BranchIds  NVARCHAR(MAX) = NULL   -- NEW: comma-separated for multi-branch
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        g.GRNId,
        g.GRNNumber,
        g.GRNDate,
        ISNULL(g.InvoiceNo,'')               AS InvoiceNo,
        p.PartyName                           AS SupplierName,
        gd.GodownName,
        ISNULL(br.BranchName, 'N/A')          AS BranchName,
        g.SubTotal,
        g.TotalGSTAmount,
        g.TotalAmount,
        ISNULL(po.PONumber,'')               AS PONumber
    FROM dbo.GRNMaster g
    INNER JOIN dbo.Parties p        ON p.Id  = g.SupplierId
    INNER JOIN dbo.Godowns gd       ON gd.Id = g.GodownId
    LEFT  JOIN dbo.Branches br      ON br.BranchId = g.BranchId
    LEFT  JOIN dbo.PurchaseOrder po ON po.POId = g.POId
    WHERE g.GRNDate  >= @FromDate
      AND g.GRNDate  <= @ToDate
      AND g.Status    = 'Posted'
      AND (@SupplierId IS NULL OR g.SupplierId = @SupplierId)
      AND (
            (@BranchIds IS NOT NULL AND g.BranchId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@BranchIds, ',')))
          OR
            (@BranchIds IS NULL AND g.BranchId = @BranchId)
          )
    ORDER BY g.GRNDate DESC, g.GRNNumber;
END
GO
