-- Stored procedures for Bank Master (no DELETE - use status toggle)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_BankMaster_GetAll]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_BankMaster_GetAll]
GO

CREATE PROCEDURE [dbo].[usp_BankMaster_GetAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id = ID, BankName = bankname, Status = Status
    FROM dbo.bank_Master
    ORDER BY bankname;
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_BankMaster_GetById]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_BankMaster_GetById]
GO

CREATE PROCEDURE [dbo].[usp_BankMaster_GetById]
    @Id TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id = ID, BankName = bankname, Status = Status
    FROM dbo.bank_Master
    WHERE ID = @Id;
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_BankMaster_Create]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_BankMaster_Create]
GO

CREATE PROCEDURE [dbo].[usp_BankMaster_Create]
    @BankName VARCHAR(100),
    @Status BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        INSERT INTO dbo.bank_Master (bankname, Status)
        VALUES (RTRIM(LTRIM(@BankName)), @Status);

        SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewId, 1 AS Result, 'Bank created successfully.' AS Message;
    END TRY
    BEGIN CATCH
        SELECT 0 AS NewId, -1 AS Result, ERROR_MESSAGE() AS Message;
    END CATCH
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_BankMaster_Update]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_BankMaster_Update]
GO

CREATE PROCEDURE [dbo].[usp_BankMaster_Update]
    @Id TINYINT,
    @BankName VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        UPDATE dbo.bank_Master
        SET bankname = RTRIM(LTRIM(@BankName))
        WHERE ID = @Id;

        IF @@ROWCOUNT > 0
            SELECT 1 AS Result, 'Bank updated successfully.' AS Message;
        ELSE
            SELECT 0 AS Result, 'Bank not found.' AS Message;
    END TRY
    BEGIN CATCH
        SELECT -1 AS Result, ERROR_MESSAGE() AS Message;
    END CATCH
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_BankMaster_SetStatus]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_BankMaster_SetStatus]
GO

CREATE PROCEDURE [dbo].[usp_BankMaster_SetStatus]
    @Id TINYINT,
    @Status BIT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        UPDATE dbo.bank_Master
        SET Status = @Status
        WHERE ID = @Id;

        IF @@ROWCOUNT > 0
            SELECT 1 AS Result, 'Status updated successfully.' AS Message;
        ELSE
            SELECT 0 AS Result, 'Bank not found.' AS Message;
    END TRY
    BEGIN CATCH
        SELECT -1 AS Result, ERROR_MESSAGE() AS Message;
    END CATCH
END
GO

PRINT 'Bank master stored procedures created/updated (no delete).';
