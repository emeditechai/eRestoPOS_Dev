IF OBJECT_ID('dbo.usp_GetWaitlistGuestReport', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetWaitlistGuestReport;
GO
CREATE PROCEDURE dbo.usp_GetWaitlistGuestReport
    @StartDate DATE = NULL,
    @EndDate DATE = NULL,
    @BranchId INT = NULL,
    @BranchIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @StartDate IS NULL AND @EndDate IS NULL
    BEGIN
        SET @StartDate = CAST(GETDATE() AS DATE);
        SET @EndDate = @StartDate;
    END
    ELSE IF @StartDate IS NULL
        SET @StartDate = @EndDate;
    ELSE IF @EndDate IS NULL
        SET @EndDate = @StartDate;

    IF OBJECT_ID('tempdb..#BranchFilter') IS NOT NULL DROP TABLE #BranchFilter;
    CREATE TABLE #BranchFilter (BranchId INT PRIMARY KEY);

    IF @BranchIds IS NOT NULL AND LTRIM(RTRIM(@BranchIds)) <> ''
    BEGIN
        INSERT INTO #BranchFilter (BranchId)
        SELECT DISTINCT TRY_CAST(value AS INT)
        FROM STRING_SPLIT(@BranchIds, ',')
        WHERE TRY_CAST(value AS INT) IS NOT NULL;
    END
    ELSE IF @BranchId IS NOT NULL
    BEGIN
        INSERT INTO #BranchFilter (BranchId) VALUES (@BranchId);
    END

    DECLARE @HasWaitlistBranch BIT = CASE WHEN COL_LENGTH('dbo.Waitlist', 'BranchId') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @HasTablesBranch BIT = CASE WHEN COL_LENGTH('dbo.Tables', 'BranchId') IS NOT NULL THEN 1 ELSE 0 END;

    DECLARE @BranchExpr NVARCHAR(200) = CASE 
        WHEN @HasWaitlistBranch = 1 AND @HasTablesBranch = 1 THEN N'COALESCE(w.BranchId, t.BranchId, 0)'
        WHEN @HasWaitlistBranch = 1 THEN N'ISNULL(w.BranchId, 0)'
        WHEN @HasTablesBranch = 1 THEN N'ISNULL(t.BranchId, 0)'
        ELSE N'CAST(0 AS INT)'
    END;

    DECLARE @BranchNameExpr NVARCHAR(200) = CASE
        WHEN @HasWaitlistBranch = 1 OR @HasTablesBranch = 1 THEN N'ISNULL(b.BranchName, '''')'
        ELSE N'CAST('''' AS NVARCHAR(150))'
    END;

    DECLARE @BranchJoin NVARCHAR(MAX) = CASE
        WHEN @HasWaitlistBranch = 1 OR @HasTablesBranch = 1 THEN N' LEFT JOIN dbo.Branches b ON b.BranchId = ' + @BranchExpr + CHAR(10)
        ELSE N''
    END;

    DECLARE @BranchFilterClause NVARCHAR(MAX) = CASE
        WHEN @HasWaitlistBranch = 1 OR @HasTablesBranch = 1 THEN N'
          AND ((SELECT COUNT(*) FROM #BranchFilter) = 0 OR ' + @BranchExpr + N' IN (SELECT BranchId FROM #BranchFilter))'
        ELSE N''
    END;

    DECLARE @Sql NVARCHAR(MAX) = N'
    IF OBJECT_ID(''tempdb..#WaitlistReport'') IS NOT NULL DROP TABLE #WaitlistReport;

    SELECT
        w.Id AS WaitlistId,
        w.AddedAt,
        ISNULL(w.GuestName, '''') AS GuestName,
        ISNULL(w.PhoneNumber, '''') AS PhoneNumber,
        ISNULL(w.PartySize, 0) AS PartySize,
        ISNULL(w.QuotedWaitTime, 0) AS QuotedWaitTime,
        CASE ISNULL(w.Status, 0)
            WHEN 0 THEN ''Waiting''
            WHEN 1 THEN ''Notified''
            WHEN 2 THEN ''Seated''
            WHEN 3 THEN ''Left''
            WHEN 4 THEN ''No Response''
            ELSE ''Unknown''
        END AS StatusText,
        w.NotifiedAt,
        w.SeatedAt,
        ISNULL(t.TableNumber, '''') AS TableNumber,
        ' + @BranchNameExpr + N' AS BranchName,
        ISNULL(w.Notes, '''') AS Notes,
        CASE WHEN w.SeatedAt IS NULL THEN NULL ELSE DATEDIFF(MINUTE, w.AddedAt, w.SeatedAt) END AS ActualWaitMinutes,
        CASE WHEN w.SeatedAt IS NOT NULL AND w.TableId IS NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS SeatedWithoutTable
    INTO #WaitlistReport
    FROM dbo.Waitlist w
    LEFT JOIN dbo.Tables t ON t.Id = w.TableId
    ' + @BranchJoin + N'
    WHERE CAST(w.AddedAt AS DATE) BETWEEN @StartDate AND @EndDate
    ' + @BranchFilterClause + N';

    SELECT
        COUNT(*) AS TotalGuests,
        SUM(CASE WHEN StatusText = ''Waiting'' THEN 1 ELSE 0 END) AS WaitingGuests,
        SUM(CASE WHEN StatusText = ''Notified'' THEN 1 ELSE 0 END) AS NotifiedGuests,
        SUM(CASE WHEN StatusText = ''Seated'' THEN 1 ELSE 0 END) AS SeatedGuests,
        SUM(CASE WHEN SeatedWithoutTable = 1 THEN 1 ELSE 0 END) AS SeatedWithoutTableGuests,
        CAST(ISNULL(AVG(CAST(QuotedWaitTime AS DECIMAL(18,2))), 0) AS DECIMAL(18,2)) AS AverageQuotedWaitTime,
        CAST(ISNULL(AVG(CAST(ActualWaitMinutes AS DECIMAL(18,2))), 0) AS DECIMAL(18,2)) AS AverageActualWaitTime
    FROM #WaitlistReport;

    SELECT
        WaitlistId,
        AddedAt,
        GuestName,
        PhoneNumber,
        PartySize,
        QuotedWaitTime,
        StatusText,
        NotifiedAt,
        SeatedAt,
        TableNumber,
        BranchName,
        Notes,
        ActualWaitMinutes,
        SeatedWithoutTable
    FROM #WaitlistReport
    ORDER BY AddedAt ASC, WaitlistId ASC;

    DROP TABLE #WaitlistReport;';

    EXEC sp_executesql @Sql, N'@StartDate DATE, @EndDate DATE', @StartDate, @EndDate;

    DROP TABLE #BranchFilter;
END
GO