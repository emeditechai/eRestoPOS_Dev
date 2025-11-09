-- =====================================================
-- Cash Closing Report - Stored Procedure
-- Database: dev_Restaurant
-- Purpose: Generate comprehensive cash closing report with date range
-- =====================================================

USE [dev_Restaurant];
GO

-- =====================================================
-- Stored Procedure: usp_GetCashClosingReport
-- Generates detailed cash closing report for date range
-- =====================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetCashClosingReport]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_GetCashClosingReport];
END
GO

CREATE PROCEDURE [dbo].[usp_GetCashClosingReport]
    @StartDate DATE,
    @EndDate DATE,
    @CashierId INT = NULL -- Optional: Filter by specific cashier
AS
BEGIN
    SET NOCOUNT ON;
    
    -- =====================================================
    -- RESULT SET 1: Summary Statistics
    -- =====================================================
    SELECT 
        COUNT(DISTINCT BusinessDate) AS TotalDays,
        COUNT(DISTINCT CashierId) AS TotalCashiers,
        SUM(OpeningFloat) AS TotalOpeningFloat,
        SUM(SystemAmount) AS TotalSystemAmount,
        SUM(DeclaredAmount) AS TotalDeclaredAmount,
        SUM(Variance) AS TotalVariance,
        SUM(CASE WHEN Variance > 0 THEN Variance ELSE 0 END) AS TotalCashOver,
        SUM(CASE WHEN Variance < 0 THEN ABS(Variance) ELSE 0 END) AS TotalCashShort,
        COUNT(CASE WHEN Status = 'OK' THEN 1 END) AS ApprovedCount,
        COUNT(CASE WHEN Status = 'CHECK' THEN 1 END) AS PendingApprovalCount,
        COUNT(CASE WHEN Status = 'LOCKED' THEN 1 END) AS LockedCount
    FROM CashierDayClose
    WHERE BusinessDate BETWEEN @StartDate AND @EndDate
      AND (@CashierId IS NULL OR CashierId = @CashierId);
    
    -- =====================================================
    -- RESULT SET 2: Daily Summary (Grouped by Date)
    -- =====================================================
    SELECT 
        BusinessDate,
        COUNT(DISTINCT CashierId) AS CashierCount,
        SUM(OpeningFloat) AS DayOpeningFloat,
        SUM(SystemAmount) AS DaySystemAmount,
        SUM(DeclaredAmount) AS DayDeclaredAmount,
        SUM(Variance) AS DayVariance,
        SUM(CASE WHEN Variance > 0 THEN Variance ELSE 0 END) AS DayCashOver,
        SUM(CASE WHEN Variance < 0 THEN ABS(Variance) ELSE 0 END) AS DayCashShort,
        MAX(CASE WHEN LockedFlag = 1 THEN 'Yes' ELSE 'No' END) AS IsDayLocked
    FROM CashierDayClose
    WHERE BusinessDate BETWEEN @StartDate AND @EndDate
      AND (@CashierId IS NULL OR CashierId = @CashierId)
    GROUP BY BusinessDate
    ORDER BY BusinessDate DESC;
    
    -- =====================================================
    -- RESULT SET 3: Detailed Cashier Records
    -- =====================================================
    SELECT 
        cdc.BusinessDate,
        cdc.CashierId,
        cdc.CashierName,
        cdc.OpeningFloat,
        cdc.SystemAmount,
        (cdc.SystemAmount + cdc.OpeningFloat) AS ExpectedCash,
        cdc.DeclaredAmount,
        cdc.Variance,
        CASE 
            WHEN cdc.Variance > 0 THEN 'Over'
            WHEN cdc.Variance < 0 THEN 'Short'
            ELSE 'Exact'
        END AS VarianceType,
        cdc.Status,
        cdc.ApprovedBy,
        cdc.ApprovalComment,
        cdc.LockedFlag,
        cdc.LockedAt,
        cdc.LockedBy,
        cdc.CreatedAt,
        cdc.UpdatedAt
    FROM CashierDayClose cdc
    WHERE cdc.BusinessDate BETWEEN @StartDate AND @EndDate
      AND (@CashierId IS NULL OR cdc.CashierId = @CashierId)
    ORDER BY cdc.BusinessDate DESC, cdc.CashierName;
    
    -- =====================================================
    -- RESULT SET 4: Cashier Performance (Grouped by Cashier)
    -- =====================================================
    SELECT 
        cdc.CashierId,
        cdc.CashierName,
        COUNT(*) AS TotalDaysWorked,
        SUM(cdc.SystemAmount) AS TotalCashCollected,
        AVG(cdc.Variance) AS AverageVariance,
        MIN(cdc.Variance) AS BestVariance,
        MAX(cdc.Variance) AS WorstVariance,
        SUM(CASE WHEN ABS(cdc.Variance) <= 100 THEN 1 ELSE 0 END) AS DaysWithinTolerance,
        SUM(CASE WHEN ABS(cdc.Variance) > 100 THEN 1 ELSE 0 END) AS DaysAboveTolerance,
        COUNT(CASE WHEN cdc.Status = 'OK' THEN 1 END) AS ApprovedDays,
        COUNT(CASE WHEN cdc.Status = 'CHECK' THEN 1 END) AS PendingDays
    FROM CashierDayClose cdc
    WHERE cdc.BusinessDate BETWEEN @StartDate AND @EndDate
      AND (@CashierId IS NULL OR cdc.CashierId = @CashierId)
    GROUP BY cdc.CashierId, cdc.CashierName
    ORDER BY cdc.CashierName;
    
    -- =====================================================
    -- RESULT SET 5: Day Lock Audit Trail
    -- =====================================================
    SELECT 
        dla.BusinessDate,
        dla.LockedBy,
        dla.LockTime,
        dla.Remarks,
        dla.Status,
        dla.ReopenedBy,
        dla.ReopenedAt,
        dla.ReopenReason
    FROM DayLockAudit dla
    WHERE dla.BusinessDate BETWEEN @StartDate AND @EndDate
    ORDER BY dla.BusinessDate DESC, dla.LockTime DESC;
    
END
GO

PRINT '✓ Stored Procedure usp_GetCashClosingReport created successfully!';
PRINT '';
PRINT 'Usage Examples:';
PRINT '1. Get report for specific date range:';
PRINT '   EXEC usp_GetCashClosingReport @StartDate = ''2025-11-01'', @EndDate = ''2025-11-09''';
PRINT '';
PRINT '2. Get report for specific cashier:';
PRINT '   EXEC usp_GetCashClosingReport @StartDate = ''2025-11-01'', @EndDate = ''2025-11-09'', @CashierId = 2';
PRINT '';
PRINT '3. Get report for today only:';
PRINT '   EXEC usp_GetCashClosingReport @StartDate = CAST(GETDATE() AS DATE), @EndDate = CAST(GETDATE() AS DATE)';
GO
