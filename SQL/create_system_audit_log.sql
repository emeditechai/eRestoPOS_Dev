-- ============================================================
-- Migration: Create SystemAuditLog table for non-order audit
-- Covers   : MenuItemRate, Ingredients, UOM, User, Branch
-- Deploy   : Run once per environment (dev, staging, prod)
-- Safe     : Idempotent
-- Date     : 2026-03-20
-- ============================================================
SET NOCOUNT ON;

-- ── 1. Table ────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SystemAuditLog')
BEGIN
    CREATE TABLE dbo.SystemAuditLog
    (
        Id            INT           IDENTITY(1,1) PRIMARY KEY,
        Module        NVARCHAR(100) NOT NULL,        -- e.g. MenuItemRate, Ingredient, UOM, User, Branch
        Action        NVARCHAR(50)  NOT NULL,        -- Create | Update | Delete | PasswordChange
        EntityId      INT           NULL,
        EntityName    NVARCHAR(500) NULL,            -- human-readable label (item name, branch name, etc.)
        FieldName     NVARCHAR(200) NULL,
        OldValue      NVARCHAR(MAX) NULL,
        NewValue      NVARCHAR(MAX) NULL,
        BranchId      INT           NULL,
        ChangedBy     INT           NOT NULL,
        ChangedByName NVARCHAR(200) NOT NULL,
        ChangedDate   DATETIME      NOT NULL DEFAULT GETDATE(),
        IPAddress     NVARCHAR(50)  NULL,
        AdditionalInfo NVARCHAR(MAX) NULL
    );

    CREATE INDEX IX_SystemAuditLog_Module      ON dbo.SystemAuditLog(Module);
    CREATE INDEX IX_SystemAuditLog_ChangedDate ON dbo.SystemAuditLog(ChangedDate DESC);
    CREATE INDEX IX_SystemAuditLog_ChangedBy   ON dbo.SystemAuditLog(ChangedBy);
    CREATE INDEX IX_SystemAuditLog_BranchId    ON dbo.SystemAuditLog(BranchId);

    PRINT 'SystemAuditLog table created.';
END
ELSE
BEGIN
    PRINT 'SystemAuditLog already exists – skipping.';
END
GO

-- ── 2. Stored procedure to insert a log entry ───────────────
IF EXISTS (SELECT 1 FROM sys.objects WHERE type = 'P' AND name = 'usp_LogSystemAudit')
    DROP PROCEDURE dbo.usp_LogSystemAudit;
GO

CREATE PROCEDURE dbo.usp_LogSystemAudit
    @Module        NVARCHAR(100),
    @Action        NVARCHAR(50),
    @EntityId      INT           = NULL,
    @EntityName    NVARCHAR(500) = NULL,
    @FieldName     NVARCHAR(200) = NULL,
    @OldValue      NVARCHAR(MAX) = NULL,
    @NewValue      NVARCHAR(MAX) = NULL,
    @BranchId      INT           = NULL,
    @ChangedBy     INT,
    @ChangedByName NVARCHAR(200),
    @IPAddress     NVARCHAR(50)  = NULL,
    @AdditionalInfo NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.SystemAuditLog
        (Module, Action, EntityId, EntityName, FieldName,
         OldValue, NewValue, BranchId,
         ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
    VALUES
        (@Module, @Action, @EntityId, @EntityName, @FieldName,
         @OldValue, @NewValue, @BranchId,
         @ChangedBy, @ChangedByName, GETDATE(), @IPAddress, @AdditionalInfo);
END
GO

PRINT 'usp_LogSystemAudit created/replaced.';

-- ── 3. Verify ────────────────────────────────────────────────
SELECT OBJECT_NAME(object_id) AS ObjectName, type_desc
FROM sys.objects
WHERE name IN ('SystemAuditLog','usp_LogSystemAudit')
ORDER BY name;
GO
