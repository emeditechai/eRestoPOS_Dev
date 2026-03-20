-- System Audit Log table + trigger deployment
-- Production deployment script
-- Applies trigger-based audit capture for:
--   dbo.MenuItems
--   dbo.Ingredients
--   dbo.UomMaster
--   dbo.Users
--   dbo.Branches

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID(N'dbo.SystemAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemAuditLog
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemAuditLog PRIMARY KEY,
        Module         NVARCHAR(100) NOT NULL,
        Action         NVARCHAR(50)  NOT NULL,
        EntityId       INT           NULL,
        EntityName     NVARCHAR(500) NULL,
        FieldName      NVARCHAR(200) NULL,
        OldValue       NVARCHAR(MAX) NULL,
        NewValue       NVARCHAR(MAX) NULL,
        BranchId       INT           NULL,
        ChangedBy      INT           NOT NULL CONSTRAINT DF_SystemAuditLog_ChangedBy DEFAULT (0),
        ChangedByName  NVARCHAR(200) NOT NULL CONSTRAINT DF_SystemAuditLog_ChangedByName DEFAULT (N'System'),
        ChangedDate    DATETIME      NOT NULL CONSTRAINT DF_SystemAuditLog_ChangedDate DEFAULT (GETDATE()),
        IPAddress      NVARCHAR(50)  NULL,
        AdditionalInfo NVARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SystemAuditLog') AND name = N'IX_SystemAuditLog_Module')
    CREATE INDEX IX_SystemAuditLog_Module ON dbo.SystemAuditLog(Module);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SystemAuditLog') AND name = N'IX_SystemAuditLog_ChangedDate')
    CREATE INDEX IX_SystemAuditLog_ChangedDate ON dbo.SystemAuditLog(ChangedDate DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SystemAuditLog') AND name = N'IX_SystemAuditLog_ChangedBy')
    CREATE INDEX IX_SystemAuditLog_ChangedBy ON dbo.SystemAuditLog(ChangedBy);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SystemAuditLog') AND name = N'IX_SystemAuditLog_BranchId')
    CREATE INDEX IX_SystemAuditLog_BranchId ON dbo.SystemAuditLog(BranchId);
GO

CREATE OR ALTER TRIGGER dbo.trg_SystemAudit_MenuItems
ON dbo.MenuItems
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedBy INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditUserId')), 0);
    DECLARE @ChangedByName NVARCHAR(200) = COALESCE(TRY_CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'AuditUserName')), ORIGINAL_LOGIN(), N'System');
    DECLARE @BranchId INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditBranchId'));
    DECLARE @IPAddress NVARCHAR(50) = TRY_CONVERT(NVARCHAR(50), SESSION_CONTEXT(N'AuditIpAddress'));
    DECLARE @Module NVARCHAR(100) = COALESCE(TRY_CONVERT(NVARCHAR(100), SESSION_CONTEXT(N'AuditModule')), N'MenuItem');

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            @Module,
            N'Update',
            i.Id,
            NULLIF(LTRIM(RTRIM(i.Name)), N''),
            N'Record',
            oldData.RowJson,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:MenuItems'
        FROM inserted i
        INNER JOIN deleted d ON d.Id = i.Id
        CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson)
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM inserted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            @Module,
            N'Create',
            i.Id,
            NULLIF(LTRIM(RTRIM(i.Name)), N''),
            N'Record',
            NULL,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:MenuItems'
        FROM inserted i
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    INSERT INTO dbo.SystemAuditLog
        (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
         BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
    SELECT
        @Module,
        N'Delete',
        d.Id,
        NULLIF(LTRIM(RTRIM(d.Name)), N''),
        N'Record',
        oldData.RowJson,
        NULL,
        @BranchId,
        @ChangedBy,
        @ChangedByName,
        GETDATE(),
        @IPAddress,
        N'Trigger:MenuItems'
    FROM deleted d
    CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson);
END
GO

CREATE OR ALTER TRIGGER dbo.trg_SystemAudit_Ingredients
ON dbo.Ingredients
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedBy INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditUserId')), 0);
    DECLARE @ChangedByName NVARCHAR(200) = COALESCE(TRY_CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'AuditUserName')), ORIGINAL_LOGIN(), N'System');
    DECLARE @BranchId INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditBranchId'));
    DECLARE @IPAddress NVARCHAR(50) = TRY_CONVERT(NVARCHAR(50), SESSION_CONTEXT(N'AuditIpAddress'));

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'Ingredient',
            N'Update',
            i.Id,
            NULLIF(LTRIM(RTRIM(i.IngredientsName)), N''),
            N'Record',
            oldData.RowJson,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:Ingredients'
        FROM inserted i
        INNER JOIN deleted d ON d.Id = i.Id
        CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson)
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM inserted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'Ingredient',
            N'Create',
            i.Id,
            NULLIF(LTRIM(RTRIM(i.IngredientsName)), N''),
            N'Record',
            NULL,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:Ingredients'
        FROM inserted i
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    INSERT INTO dbo.SystemAuditLog
        (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
         BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
    SELECT
        N'Ingredient',
        N'Delete',
        d.Id,
        NULLIF(LTRIM(RTRIM(d.IngredientsName)), N''),
        N'Record',
        oldData.RowJson,
        NULL,
        @BranchId,
        @ChangedBy,
        @ChangedByName,
        GETDATE(),
        @IPAddress,
        N'Trigger:Ingredients'
    FROM deleted d
    CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson);
END
GO

CREATE OR ALTER TRIGGER dbo.trg_SystemAudit_UomMaster
ON dbo.UomMaster
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedBy INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditUserId')), 0);
    DECLARE @ChangedByName NVARCHAR(200) = COALESCE(TRY_CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'AuditUserName')), ORIGINAL_LOGIN(), N'System');
    DECLARE @BranchId INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditBranchId'));
    DECLARE @IPAddress NVARCHAR(50) = TRY_CONVERT(NVARCHAR(50), SESSION_CONTEXT(N'AuditIpAddress'));

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'UOM',
            N'Update',
            i.UOMId,
            NULLIF(LTRIM(RTRIM(CONCAT(i.UOMCode, N' - ', i.UOMName))), N''),
            N'Record',
            oldData.RowJson,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:UomMaster'
        FROM inserted i
        INNER JOIN deleted d ON d.UOMId = i.UOMId
        CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson)
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM inserted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'UOM',
            N'Create',
            i.UOMId,
            NULLIF(LTRIM(RTRIM(CONCAT(i.UOMCode, N' - ', i.UOMName))), N''),
            N'Record',
            NULL,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:UomMaster'
        FROM inserted i
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    INSERT INTO dbo.SystemAuditLog
        (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
         BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
    SELECT
        N'UOM',
        N'Delete',
        d.UOMId,
        NULLIF(LTRIM(RTRIM(CONCAT(d.UOMCode, N' - ', d.UOMName))), N''),
        N'Record',
        oldData.RowJson,
        NULL,
        @BranchId,
        @ChangedBy,
        @ChangedByName,
        GETDATE(),
        @IPAddress,
        N'Trigger:UomMaster'
    FROM deleted d
    CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson);
END
GO

CREATE OR ALTER TRIGGER dbo.trg_SystemAudit_Users
ON dbo.Users
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedBy INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditUserId')), 0);
    DECLARE @ChangedByName NVARCHAR(200) = COALESCE(TRY_CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'AuditUserName')), ORIGINAL_LOGIN(), N'System');
    DECLARE @BranchId INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditBranchId'));
    DECLARE @IPAddress NVARCHAR(50) = TRY_CONVERT(NVARCHAR(50), SESSION_CONTEXT(N'AuditIpAddress'));

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'User',
            N'Update',
            i.Id,
            NULLIF(LTRIM(RTRIM(i.Username)), N''),
            N'Record',
            oldData.RowJson,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:Users'
        FROM inserted i
        INNER JOIN deleted d ON d.Id = i.Id
        CROSS APPLY (
            SELECT d.Id, d.Username, d.FirstName, d.LastName, d.Email, d.IsActive
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) oldData(RowJson)
        CROSS APPLY (
            SELECT i.Id, i.Username, i.FirstName, i.LastName, i.Email, i.IsActive
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) newData(RowJson);

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM inserted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'User',
            N'Create',
            i.Id,
            NULLIF(LTRIM(RTRIM(i.Username)), N''),
            N'Record',
            NULL,
            newData.RowJson,
            @BranchId,
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:Users'
        FROM inserted i
        CROSS APPLY (
            SELECT i.Id, i.Username, i.FirstName, i.LastName, i.Email, i.IsActive
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) newData(RowJson);

        RETURN;
    END

    INSERT INTO dbo.SystemAuditLog
        (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
         BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
    SELECT
        N'User',
        N'Delete',
        d.Id,
        NULLIF(LTRIM(RTRIM(d.Username)), N''),
        N'Record',
        oldData.RowJson,
        NULL,
        @BranchId,
        @ChangedBy,
        @ChangedByName,
        GETDATE(),
        @IPAddress,
        N'Trigger:Users'
    FROM deleted d
    CROSS APPLY (
        SELECT d.Id, d.Username, d.FirstName, d.LastName, d.Email, d.IsActive
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) oldData(RowJson);
END
GO

CREATE OR ALTER TRIGGER dbo.trg_SystemAudit_Branches
ON dbo.Branches
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedBy INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditUserId')), 0);
    DECLARE @ChangedByName NVARCHAR(200) = COALESCE(TRY_CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'AuditUserName')), ORIGINAL_LOGIN(), N'System');
    DECLARE @SessionBranchId INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'AuditBranchId'));
    DECLARE @IPAddress NVARCHAR(50) = TRY_CONVERT(NVARCHAR(50), SESSION_CONTEXT(N'AuditIpAddress'));

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'Branch',
            N'Update',
            i.BranchId,
            NULLIF(LTRIM(RTRIM(CONCAT(i.BranchCode, N' - ', i.BranchName))), N''),
            N'Record',
            oldData.RowJson,
            newData.RowJson,
            COALESCE(i.BranchId, d.BranchId, @SessionBranchId),
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:Branches'
        FROM inserted i
        INNER JOIN deleted d ON d.BranchId = i.BranchId
        CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson)
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM inserted)
    BEGIN
        INSERT INTO dbo.SystemAuditLog
            (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
             BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
        SELECT
            N'Branch',
            N'Create',
            i.BranchId,
            NULLIF(LTRIM(RTRIM(CONCAT(i.BranchCode, N' - ', i.BranchName))), N''),
            N'Record',
            NULL,
            newData.RowJson,
            COALESCE(i.BranchId, @SessionBranchId),
            @ChangedBy,
            @ChangedByName,
            GETDATE(),
            @IPAddress,
            N'Trigger:Branches'
        FROM inserted i
        CROSS APPLY (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) newData(RowJson);

        RETURN;
    END

    INSERT INTO dbo.SystemAuditLog
        (Module, Action, EntityId, EntityName, FieldName, OldValue, NewValue,
         BranchId, ChangedBy, ChangedByName, ChangedDate, IPAddress, AdditionalInfo)
    SELECT
        N'Branch',
        N'Delete',
        d.BranchId,
        NULLIF(LTRIM(RTRIM(CONCAT(d.BranchCode, N' - ', d.BranchName))), N''),
        N'Record',
        oldData.RowJson,
        NULL,
        COALESCE(d.BranchId, @SessionBranchId),
        @ChangedBy,
        @ChangedByName,
        GETDATE(),
        @IPAddress,
        N'Trigger:Branches'
    FROM deleted d
    CROSS APPLY (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) oldData(RowJson);
END
GO
