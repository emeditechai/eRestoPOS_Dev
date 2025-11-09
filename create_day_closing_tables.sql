-- =====================================================
-- Day Closing Process - Database Migration Script
-- Created: 2025-11-09
-- Purpose: Create tables for cashier day opening, closing, and audit
-- =====================================================

USE [dev_Restaurant];
GO

-- =====================================================
-- 1. CashierDayOpening Table
-- Stores opening float assigned to each cashier
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CashierDayOpening]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[CashierDayOpening]
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [BusinessDate] DATE NOT NULL,
        [CashierId] INT NOT NULL,
        [CashierName] NVARCHAR(100) NOT NULL,
        [OpeningFloat] DECIMAL(10,2) NOT NULL DEFAULT 0,
        [CreatedBy] NVARCHAR(50) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedBy] NVARCHAR(50) NULL,
        [UpdatedAt] DATETIME NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_CashierDayOpening] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_CashierDayOpening_Users] FOREIGN KEY([CashierId]) 
            REFERENCES [dbo].[Users]([Id]) ON DELETE NO ACTION,
        CONSTRAINT [UQ_CashierDayOpening_Date_Cashier] UNIQUE ([BusinessDate], [CashierId])
    );
    
    CREATE NONCLUSTERED INDEX [IX_CashierDayOpening_BusinessDate] 
        ON [dbo].[CashierDayOpening]([BusinessDate] DESC);
    
    CREATE NONCLUSTERED INDEX [IX_CashierDayOpening_CashierId] 
        ON [dbo].[CashierDayOpening]([CashierId]);
    
    PRINT 'Table CashierDayOpening created successfully';
END
ELSE
BEGIN
    PRINT 'Table CashierDayOpening already exists';
END
GO

-- =====================================================
-- 2. CashierDayClose Table
-- Stores end-of-day cash declaration and variance
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CashierDayClose]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[CashierDayClose]
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [BusinessDate] DATE NOT NULL,
        [CashierId] INT NOT NULL,
        [CashierName] NVARCHAR(100) NOT NULL,
        [SystemAmount] DECIMAL(10,2) NOT NULL DEFAULT 0, -- Total cash from POS
        [DeclaredAmount] DECIMAL(10,2) NULL, -- Cash declared by cashier
        [OpeningFloat] DECIMAL(10,2) NOT NULL DEFAULT 0, -- Opening float from CashierDayOpening
        [Variance] DECIMAL(10,2) NULL, -- Calculated: (DeclaredAmount + OpeningFloat) - SystemAmount
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'PENDING', -- PENDING, OK, CHECK, LOCKED
        [ApprovedBy] NVARCHAR(50) NULL,
        [ApprovalComment] NVARCHAR(500) NULL,
        [LockedFlag] BIT NOT NULL DEFAULT 0,
        [LockedAt] DATETIME NULL,
        [LockedBy] NVARCHAR(50) NULL,
        [CreatedBy] NVARCHAR(50) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedBy] NVARCHAR(50) NULL,
        [UpdatedAt] DATETIME NULL,
        CONSTRAINT [PK_CashierDayClose] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_CashierDayClose_Users] FOREIGN KEY([CashierId]) 
            REFERENCES [dbo].[Users]([Id]) ON DELETE NO ACTION,
        CONSTRAINT [UQ_CashierDayClose_Date_Cashier] UNIQUE ([BusinessDate], [CashierId]),
        CONSTRAINT [CHK_CashierDayClose_Status] CHECK ([Status] IN ('PENDING', 'OK', 'CHECK', 'LOCKED'))
    );
    
    CREATE NONCLUSTERED INDEX [IX_CashierDayClose_BusinessDate] 
        ON [dbo].[CashierDayClose]([BusinessDate] DESC);
    
    CREATE NONCLUSTERED INDEX [IX_CashierDayClose_Status] 
        ON [dbo].[CashierDayClose]([Status]);
    
    CREATE NONCLUSTERED INDEX [IX_CashierDayClose_LockedFlag] 
        ON [dbo].[CashierDayClose]([LockedFlag], [BusinessDate]);
    
    PRINT 'Table CashierDayClose created successfully';
END
ELSE
BEGIN
    PRINT 'Table CashierDayClose already exists';
END
GO

-- =====================================================
-- 3. DayLockAudit Table
-- Stores audit trail for day lock/unlock operations
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DayLockAudit]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[DayLockAudit]
    (
        [LockId] INT IDENTITY(1,1) NOT NULL,
        [BusinessDate] DATE NOT NULL,
        [LockedBy] NVARCHAR(50) NOT NULL,
        [LockTime] DATETIME NOT NULL DEFAULT GETDATE(),
        [Remarks] NVARCHAR(1000) NULL,
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'LOCKED', -- LOCKED, REOPENED
        [ReopenedBy] NVARCHAR(50) NULL,
        [ReopenedAt] DATETIME NULL,
        [ReopenReason] NVARCHAR(500) NULL,
        CONSTRAINT [PK_DayLockAudit] PRIMARY KEY CLUSTERED ([LockId] ASC),
        CONSTRAINT [CHK_DayLockAudit_Status] CHECK ([Status] IN ('LOCKED', 'REOPENED'))
    );
    
    CREATE NONCLUSTERED INDEX [IX_DayLockAudit_BusinessDate] 
        ON [dbo].[DayLockAudit]([BusinessDate] DESC);
    
    CREATE NONCLUSTERED INDEX [IX_DayLockAudit_Status] 
        ON [dbo].[DayLockAudit]([Status], [BusinessDate]);
    
    PRINT 'Table DayLockAudit created successfully';
END
ELSE
BEGIN
    PRINT 'Table DayLockAudit already exists';
END
GO

-- =====================================================
-- 4. Stored Procedure: usp_InitializeDayOpening
-- Creates opening float records for all active cashiers
-- =====================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_InitializeDayOpening]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_InitializeDayOpening];
END
GO

CREATE PROCEDURE [dbo].[usp_InitializeDayOpening]
    @BusinessDate DATE,
    @CashierId INT,
    @OpeningFloat DECIMAL(10,2),
    @CreatedBy NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        -- Check if opening already exists
        IF EXISTS (SELECT 1 FROM CashierDayOpening 
                   WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId)
        BEGIN
            -- Update existing record
            UPDATE CashierDayOpening
            SET OpeningFloat = @OpeningFloat,
                UpdatedBy = @CreatedBy,
                UpdatedAt = GETDATE()
            WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
            
            SELECT 'Updated' AS Result, 'Opening float updated successfully' AS Message;
        END
        ELSE
        BEGIN
            -- Get cashier name
            DECLARE @CashierName NVARCHAR(100);
            SELECT @CashierName = Username FROM Users WHERE Id = @CashierId;
            
            -- Insert new record
            INSERT INTO CashierDayOpening (BusinessDate, CashierId, CashierName, OpeningFloat, CreatedBy)
            VALUES (@BusinessDate, @CashierId, @CashierName, @OpeningFloat, @CreatedBy);
            
            -- Also create corresponding close record
            INSERT INTO CashierDayClose (BusinessDate, CashierId, CashierName, OpeningFloat, SystemAmount, CreatedBy)
            VALUES (@BusinessDate, @CashierId, @CashierName, @OpeningFloat, 0, @CreatedBy);
            
            SELECT 'Created' AS Result, 'Opening float created successfully' AS Message;
        END
        
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO

-- =====================================================
-- 5. Stored Procedure: usp_GetCashierSystemAmount
-- Calculates system cash amount from Orders/Payments
-- =====================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetCashierSystemAmount]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_GetCashierSystemAmount];
END
GO

CREATE PROCEDURE [dbo].[usp_GetCashierSystemAmount]
    @BusinessDate DATE,
    @CashierId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Get total cash collected by cashier from Payments table
    -- This query joins Orders with Payments to get cash payments for the specific cashier and date
    SELECT 
        ISNULL(SUM(p.Amount), 0) AS SystemAmount
    FROM Orders o
    INNER JOIN Payments p ON p.OrderId = o.Id
    INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
    WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
      AND o.CashierId = @CashierId
      AND pm.Name = 'CASH'
      AND p.Status = 1 -- Approved payments only
      AND o.Status IN (2, 3); -- Completed or Paid status
END
GO

-- =====================================================
-- 6. Stored Procedure: usp_SaveDeclaredCash
-- Updates declared cash and calculates variance
-- =====================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_SaveDeclaredCash]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_SaveDeclaredCash];
END
GO

CREATE PROCEDURE [dbo].[usp_SaveDeclaredCash]
    @BusinessDate DATE,
    @CashierId INT,
    @DeclaredAmount DECIMAL(10,2),
    @UpdatedBy NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @OpeningFloat DECIMAL(10,2);
        DECLARE @SystemAmount DECIMAL(10,2);
        DECLARE @Variance DECIMAL(10,2);
        DECLARE @Status NVARCHAR(20);
        
        -- Get opening float and system amount
        SELECT @OpeningFloat = OpeningFloat, @SystemAmount = SystemAmount
        FROM CashierDayClose
        WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
        
        -- Calculate variance: (Declared + Opening) - System
        SET @Variance = (@DeclaredAmount + @OpeningFloat) - @SystemAmount;
        
        -- Determine status based on variance threshold
        IF ABS(@Variance) > 100
            SET @Status = 'CHECK'; -- Requires approval
        ELSE
            SET @Status = 'OK';
        
        -- Update close record
        UPDATE CashierDayClose
        SET DeclaredAmount = @DeclaredAmount,
            Variance = @Variance,
            Status = @Status,
            UpdatedBy = @UpdatedBy,
            UpdatedAt = GETDATE()
        WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
        
        COMMIT TRANSACTION;
        
        SELECT 'Success' AS Result, 
               @Variance AS Variance, 
               @Status AS Status,
               'Cash declaration saved successfully' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO

-- =====================================================
-- 7. Stored Procedure: usp_LockDay
-- Locks the business day after validation
-- =====================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_LockDay]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_LockDay];
END
GO

CREATE PROCEDURE [dbo].[usp_LockDay]
    @BusinessDate DATE,
    @LockedBy NVARCHAR(50),
    @Remarks NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @IssueCount INT;
        
        -- Check for unresolved variances
        SELECT @IssueCount = COUNT(*)
        FROM CashierDayClose
        WHERE BusinessDate = @BusinessDate 
          AND Status = 'CHECK'
          AND LockedFlag = 0;
        
        IF @IssueCount > 0
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT 'Error' AS Result, 
                   @IssueCount AS IssueCount,
                   'Cannot lock day: ' + CAST(@IssueCount AS VARCHAR) + ' cashier(s) have unresolved variances' AS Message;
            RETURN;
        END
        
        -- Lock all cashier records for the day
        UPDATE CashierDayClose
        SET LockedFlag = 1,
            LockedAt = GETDATE(),
            LockedBy = @LockedBy,
            Status = 'LOCKED',
            UpdatedBy = @LockedBy,
            UpdatedAt = GETDATE()
        WHERE BusinessDate = @BusinessDate;
        
        -- Insert audit record
        INSERT INTO DayLockAudit (BusinessDate, LockedBy, LockTime, Remarks, Status)
        VALUES (@BusinessDate, @LockedBy, GETDATE(), @Remarks, 'LOCKED');
        
        COMMIT TRANSACTION;
        
        SELECT 'Success' AS Result, 
               'Day locked successfully' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO

-- =====================================================
-- 8. Stored Procedure: usp_GetDayClosingSummary
-- Retrieves day closing summary for dashboard
-- =====================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetDayClosingSummary]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_GetDayClosingSummary];
END
GO

CREATE PROCEDURE [dbo].[usp_GetDayClosingSummary]
    @BusinessDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Get cashier closing details
    SELECT 
        cdc.Id,
        cdc.CashierId,
        cdc.CashierName,
        cdc.OpeningFloat,
        cdc.SystemAmount,
        cdc.DeclaredAmount,
        cdc.Variance,
        cdc.Status,
        cdc.ApprovedBy,
        cdc.ApprovalComment,
        cdc.LockedFlag,
        cdc.LockedAt,
        cdc.LockedBy,
        -- Calculate expected cash
        (cdc.SystemAmount + cdc.OpeningFloat) AS ExpectedCash
    FROM CashierDayClose cdc
    WHERE cdc.BusinessDate = @BusinessDate
    ORDER BY cdc.CashierName;
    
    -- Get day lock status
    SELECT TOP 1
        LockId,
        BusinessDate,
        LockedBy,
        LockTime,
        Remarks,
        Status
    FROM DayLockAudit
    WHERE BusinessDate = @BusinessDate
    ORDER BY LockTime DESC;
END
GO

-- =====================================================
-- 9. Add CashierId column to Orders if not exists
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Orders]') AND name = 'CashierId')
BEGIN
    ALTER TABLE [dbo].[Orders]
    ADD [CashierId] INT NULL;
    
    PRINT 'Column CashierId added to Orders table';
    
    -- Add foreign key constraint
    ALTER TABLE [dbo].[Orders]
    ADD CONSTRAINT [FK_Orders_Cashier] FOREIGN KEY([CashierId]) 
        REFERENCES [dbo].[Users]([Id]) ON DELETE NO ACTION;
    
    PRINT 'Foreign key FK_Orders_Cashier created';
END
ELSE
BEGIN
    PRINT 'Column CashierId already exists in Orders table';
END
GO

-- =====================================================
-- Grant Permissions (adjust as needed)
-- =====================================================
PRINT 'Day Closing tables and procedures created successfully!';
PRINT 'Please review and test before deploying to production.';
GO
