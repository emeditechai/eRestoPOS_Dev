-- Stored procedure: usp_GetCustomerAnalysis
-- Parameters: @FromDate DATE = NULL, @ToDate DATE = NULL
-- Result sets:
-- 1) Summary: TotalCustomers, NewCustomers, ReturningCustomers, AverageVisitsPerCustomer, TotalRevenue
-- 2) TopCustomers: CustomerId, Name, Phone, Visits, Revenue, LTV
-- 3) VisitFrequency: PeriodLabel, Visits, Revenue
-- 4) LoyaltyBuckets: Bucket, CustomerCount
-- 5) Demographics: Category, Count

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.usp_GetCustomerAnalysis', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetCustomerAnalysis
GO

CREATE PROCEDURE dbo.usp_GetCustomerAnalysis
    @FromDate DATE = NULL,
    @ToDate DATE = NULL,
    @BranchId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Start DATETIME = COALESCE(CAST(@FromDate AS DATETIME), DATEADD(day, -30, CAST(GETDATE() AS DATE)));
    DECLARE @End DATETIME = DATEADD(day, 1, COALESCE(CAST(@ToDate AS DATETIME), CAST(GETDATE() AS DATE)));
    DECLARE @HasOrdersBranch BIT = CASE WHEN COL_LENGTH('dbo.Orders', 'BranchId') IS NULL THEN 0 ELSE 1 END;

    -- 1) Summary
    SELECT 
                (SELECT COUNT(DISTINCT CustomerPhone) FROM Orders o WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId)) AS TotalCustomers,
                (SELECT COUNT(DISTINCT CustomerPhone) FROM Orders o WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End AND o.CreatedAt >= DATEADD(day, -30, @Start) AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId)) AS NewCustomers,
        0 AS ReturningCustomers,
        0.0 AS AverageVisitsPerCustomer,
        ISNULL(SUM(o.TotalAmount),0) AS TotalRevenue
    FROM Orders o
        WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End
            AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId);

    -- 2) Top Customers
    -- Use dynamic SQL so the procedure can be created even if the Orders.TableName column does not exist.
    IF EXISTS (
        SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Orders') AND name = 'TableName'
    )
    BEGIN
        DECLARE @sqlWithTableName NVARCHAR(MAX) = N'
            SELECT TOP 10
                NULL AS CustomerId,
                CASE WHEN ISNULL(LTRIM(RTRIM(o.CustomerName)), '''') <> '''' THEN o.CustomerName
                     WHEN ISNULL(LTRIM(RTRIM(o.TableName)), '''') <> '''' THEN o.TableName
                     ELSE ISNULL(o.CustomerPhone, ''''Unknown'''') END AS Name,
                ISNULL(o.CustomerPhone, '''') AS Phone,
                COUNT(DISTINCT o.Id) AS Visits,
                SUM(o.TotalAmount) AS Revenue,
                SUM(o.TotalAmount) AS LTV
            FROM Orders o
                        WHERE o.CreatedAt >= @StartParam AND o.CreatedAt < @EndParam
                            AND (@BranchIdParam IS NULL OR @HasOrdersBranchParam = 0 OR o.BranchId = @BranchIdParam)
            GROUP BY o.CustomerName, o.TableName, o.CustomerPhone
            ORDER BY Visits DESC;';

        EXEC sp_executesql @sqlWithTableName,
                        N'@StartParam DATETIME, @EndParam DATETIME, @BranchIdParam INT, @HasOrdersBranchParam BIT',
                        @StartParam = @Start, @EndParam = @End, @BranchIdParam = @BranchId, @HasOrdersBranchParam = @HasOrdersBranch;
    END
    ELSE
    BEGIN
        DECLARE @sqlNoTableName NVARCHAR(MAX) = N'
            SELECT TOP 10
                NULL AS CustomerId,
                CASE WHEN ISNULL(LTRIM(RTRIM(o.CustomerName)), '''') <> '''' THEN o.CustomerName
                     ELSE ISNULL(o.CustomerPhone, ''''Unknown'''') END AS Name,
                ISNULL(o.CustomerPhone, '''') AS Phone,
                COUNT(DISTINCT o.Id) AS Visits,
                SUM(o.TotalAmount) AS Revenue,
                SUM(o.TotalAmount) AS LTV
            FROM Orders o
                        WHERE o.CreatedAt >= @StartParam AND o.CreatedAt < @EndParam
                            AND (@BranchIdParam IS NULL OR @HasOrdersBranchParam = 0 OR o.BranchId = @BranchIdParam)
            GROUP BY o.CustomerName, o.CustomerPhone
            ORDER BY Visits DESC;';

        EXEC sp_executesql @sqlNoTableName,
                        N'@StartParam DATETIME, @EndParam DATETIME, @BranchIdParam INT, @HasOrdersBranchParam BIT',
                        @StartParam = @Start, @EndParam = @End, @BranchIdParam = @BranchId, @HasOrdersBranchParam = @HasOrdersBranch;
    END

    -- 3) Visit Frequency (by week)
    SELECT
        CONVERT(varchar(10), DATEADD(week, DATEDIFF(week, 0, o.CreatedAt), 0), 120) AS PeriodLabel,
        COUNT(DISTINCT o.Id) AS Visits,
        SUM(o.TotalAmount) AS Revenue
    FROM Orders o
    WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End
            AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId)
    GROUP BY DATEADD(week, DATEDIFF(week, 0, o.CreatedAt), 0)
    ORDER BY PeriodLabel;

    -- 4) Loyalty buckets
    SELECT '1 Visit' AS Bucket, COUNT(*) AS CustomerCount FROM (
        SELECT CustomerPhone, COUNT(*) AS Visits FROM Orders o WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId) GROUP BY CustomerPhone HAVING COUNT(*) = 1
    ) t
    UNION ALL
    SELECT '2-3 Visits', COUNT(*) FROM (
        SELECT CustomerPhone, COUNT(*) AS Visits FROM Orders o WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId) GROUP BY CustomerPhone HAVING COUNT(*) BETWEEN 2 AND 3
    ) t2
    UNION ALL
    SELECT '4+ Visits', COUNT(*) FROM (
        SELECT CustomerPhone, COUNT(*) AS Visits FROM Orders o WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId) GROUP BY CustomerPhone HAVING COUNT(*) >= 4
    ) t3;

    -- 5) Demographics (best-effort: gender if available)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Gender')
    BEGIN
        DECLARE @demSql NVARCHAR(MAX) = N'
            SELECT ISNULL(u.Gender, ''Unknown'') AS Category,
                   COUNT(DISTINCT CASE WHEN u.Id IS NOT NULL THEN CAST(u.Id AS NVARCHAR(100)) ELSE ISNULL(o.CustomerPhone,'''') END) AS Count
            FROM Orders o
            LEFT JOIN Users u ON o.UserId = u.Id
                        WHERE o.CreatedAt >= @StartParam AND o.CreatedAt < @EndParam
                            AND (@BranchIdParam IS NULL OR @HasOrdersBranchParam = 0 OR o.BranchId = @BranchIdParam)
            GROUP BY ISNULL(u.Gender, ''Unknown'')
            ORDER BY Category;';

        EXEC sp_executesql @demSql,
                        N'@StartParam DATETIME, @EndParam DATETIME, @BranchIdParam INT, @HasOrdersBranchParam BIT',
                        @StartParam = @Start, @EndParam = @End, @BranchIdParam = @BranchId, @HasOrdersBranchParam = @HasOrdersBranch;
    END
    ELSE
    BEGIN
        SELECT 'Unknown' AS Category,
               COUNT(DISTINCT CASE WHEN u.Id IS NOT NULL THEN CAST(u.Id AS NVARCHAR(100)) ELSE ISNULL(o.CustomerPhone,'') END) AS Count
        FROM Orders o
        LEFT JOIN Users u ON o.UserId = u.Id
                WHERE o.CreatedAt >= @Start AND o.CreatedAt < @End
                    AND (@BranchId IS NULL OR @HasOrdersBranch = 0 OR o.BranchId = @BranchId);
    END
END
GO
