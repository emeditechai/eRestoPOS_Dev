USE [dev_Restaurant];
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetTransferRegister
    @BranchId INT,
    @FromDate DATE,
    @ToDate   DATE,
    @GodownId INT = NULL          -- 0 or NULL = all godowns
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsMainBranch BIT = 0;
    SELECT @IsMainBranch = ISNULL(Is_MainBranch, 0) FROM dbo.Branches WHERE BranchId = @BranchId;

    IF @GodownId = 0 SET @GodownId = NULL;

    SELECT
        st.TransferId,
        st.TransferNumber,
        st.TransferDate,
        st.TransferType,
        fg.GodownName   AS FromGodownName,
        tg.GodownName   AS ToGodownName,
        fb.BranchName   AS FromBranchName,
        tb.BranchName   AS ToBranchName,
        st.TotalQty,
        st.TotalValue,
        st.Status,
        ISNULL(st.Remarks, '') AS Remarks,
        -- Direction relative to the requesting branch
        CASE
            WHEN fg.BranchId = @BranchId THEN 'SENT'
            ELSE 'RECEIVED'
        END AS Direction
    FROM dbo.StockTransfer st
    INNER JOIN dbo.Godowns fg ON fg.Id      = st.FromGodownId
    INNER JOIN dbo.Godowns tg ON tg.Id      = st.ToGodownId
    INNER JOIN dbo.Branches fb ON fb.BranchId = fg.BranchId
    INNER JOIN dbo.Branches tb ON tb.BranchId = tg.BranchId
    WHERE
        -- Show transfers where this branch is the SENDER or the RECEIVER
        (
            fg.BranchId = @BranchId          -- sent by this branch
            OR tg.BranchId = @BranchId       -- received by this branch
            OR @IsMainBranch = 1             -- main branch sees everything
        )
        AND st.TransferDate >= @FromDate
        AND st.TransferDate <= @ToDate
        AND st.Status = 'Posted'
        AND (
            @GodownId IS NULL
            OR st.FromGodownId = @GodownId
            OR st.ToGodownId   = @GodownId
        )
    ORDER BY st.TransferDate DESC, st.TransferNumber;
END;
GO

PRINT 'usp_GetTransferRegister updated successfully.';
GO
