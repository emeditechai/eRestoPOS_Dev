-- ============================================================
-- Stock Transfer Godown Helper Stored Procedures
-- ============================================================
USE [dev_Restaurant];
GO

-- ------------------------------------------------------------
-- usp_GetTransferFromGodowns
--   Main Branch   : returns all active IsMainGodown=1 godowns
--                   from every branch
--   Non-Main Branch: returns only own branch main godown
--                   (IsDisabled=1 so UI can pre-select & lock it)
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_GetTransferFromGodowns
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsMainBranch BIT = 0;
    SELECT @IsMainBranch = ISNULL(Is_MainBranch, 0)
    FROM   dbo.Branches
    WHERE  BranchId = @BranchId;

    IF @IsMainBranch = 1
    BEGIN
        -- Main branch: all main godowns across all branches
        SELECT
            g.Id              AS GodownId,
            g.GodownName,
            b.BranchName,
            g.BranchId,
            g.IsMainGodown,
            CAST(1 AS BIT)    AS IsLoginBranchMain,
            CAST(0 AS BIT)    AS IsDisabled
        FROM  dbo.Godowns  g
        JOIN  dbo.Branches b ON b.BranchId = g.BranchId
        WHERE g.IsActive    = 1
          AND g.IsMainGodown = 1
          AND b.IsActive     = 1
        ORDER BY b.BranchName, g.GodownName;
    END
    ELSE
    BEGIN
        -- Non-main branch: only own main godown (will be pre-selected & disabled)
        SELECT
            g.Id              AS GodownId,
            g.GodownName,
            b.BranchName,
            g.BranchId,
            g.IsMainGodown,
            CAST(0 AS BIT)    AS IsLoginBranchMain,
            CAST(1 AS BIT)    AS IsDisabled
        FROM  dbo.Godowns  g
        JOIN  dbo.Branches b ON b.BranchId = g.BranchId
        WHERE g.IsActive    = 1
          AND g.IsMainGodown = 1
          AND g.BranchId    = @BranchId
          AND b.IsActive     = 1
        ORDER BY g.GodownName;
    END
END;
GO

-- ------------------------------------------------------------
-- usp_GetTransferToGodowns
--   Main Branch   : returns all active IsMainGodown=1 godowns
--                   from every branch
--   Non-Main Branch: returns all main godowns EXCEPT own branch
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_GetTransferToGodowns
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsMainBranch BIT = 0;
    SELECT @IsMainBranch = ISNULL(Is_MainBranch, 0)
    FROM   dbo.Branches
    WHERE  BranchId = @BranchId;

    IF @IsMainBranch = 1
    BEGIN
        -- Main branch: all main godowns across all branches
        SELECT
            g.Id              AS GodownId,
            g.GodownName,
            b.BranchName,
            g.BranchId,
            g.IsMainGodown,
            CAST(1 AS BIT)    AS IsLoginBranchMain,
            CAST(0 AS BIT)    AS IsDisabled
        FROM  dbo.Godowns  g
        JOIN  dbo.Branches b ON b.BranchId = g.BranchId
        WHERE g.IsActive    = 1
          AND g.IsMainGodown = 1
          AND b.IsActive     = 1
        ORDER BY b.BranchName, g.GodownName;
    END
    ELSE
    BEGIN
        -- Non-main branch: all main godowns EXCEPT own branch godowns
        SELECT
            g.Id              AS GodownId,
            g.GodownName,
            b.BranchName,
            g.BranchId,
            g.IsMainGodown,
            CAST(0 AS BIT)    AS IsLoginBranchMain,
            CAST(0 AS BIT)    AS IsDisabled
        FROM  dbo.Godowns  g
        JOIN  dbo.Branches b ON b.BranchId = g.BranchId
        WHERE g.IsActive    = 1
          AND g.IsMainGodown = 1
          AND g.BranchId   <> @BranchId
          AND b.IsActive     = 1
        ORDER BY b.BranchName, g.GodownName;
    END
END;
GO
