-- =====================================================
-- EASY DAY CLOSING SETUP - ONE-CLICK SOLUTION
-- Database: dev_Restaurant
-- Just execute this entire file and everything will work!
-- =====================================================

USE [dev_Restaurant];
GO

PRINT '========================================';
PRINT 'EASY DAY CLOSING SETUP - STARTING...';
PRINT '========================================';
PRINT '';

-- =====================================================
-- STEP 1: Create Day Closing Tables
-- =====================================================
PRINT 'STEP 1: Creating Day Closing tables...';

-- CashierDayOpening Table
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
    CREATE NONCLUSTERED INDEX [IX_CashierDayOpening_BusinessDate] ON [dbo].[CashierDayOpening]([BusinessDate] DESC);
    CREATE NONCLUSTERED INDEX [IX_CashierDayOpening_CashierId] ON [dbo].[CashierDayOpening]([CashierId]);
    PRINT '✓ CashierDayOpening table created';
END
ELSE PRINT '✓ CashierDayOpening table already exists';

-- CashierDayClose Table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CashierDayClose]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[CashierDayClose]
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [BusinessDate] DATE NOT NULL,
        [CashierId] INT NOT NULL,
        [CashierName] NVARCHAR(100) NOT NULL,
        [SystemAmount] DECIMAL(10,2) NOT NULL DEFAULT 0,
        [DeclaredAmount] DECIMAL(10,2) NULL,
        [OpeningFloat] DECIMAL(10,2) NOT NULL DEFAULT 0,
        [Variance] DECIMAL(10,2) NULL,
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'PENDING',
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
    CREATE NONCLUSTERED INDEX [IX_CashierDayClose_BusinessDate] ON [dbo].[CashierDayClose]([BusinessDate] DESC);
    CREATE NONCLUSTERED INDEX [IX_CashierDayClose_Status] ON [dbo].[CashierDayClose]([Status]);
    CREATE NONCLUSTERED INDEX [IX_CashierDayClose_LockedFlag] ON [dbo].[CashierDayClose]([LockedFlag], [BusinessDate]);
    PRINT '✓ CashierDayClose table created';
END
ELSE PRINT '✓ CashierDayClose table already exists';

-- DayLockAudit Table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DayLockAudit]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[DayLockAudit]
    (
        [LockId] INT IDENTITY(1,1) NOT NULL,
        [BusinessDate] DATE NOT NULL,
        [LockedBy] NVARCHAR(50) NOT NULL,
        [LockTime] DATETIME NOT NULL DEFAULT GETDATE(),
        [Remarks] NVARCHAR(1000) NULL,
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'LOCKED',
        [ReopenedBy] NVARCHAR(50) NULL,
        [ReopenedAt] DATETIME NULL,
        [ReopenReason] NVARCHAR(500) NULL,
        CONSTRAINT [PK_DayLockAudit] PRIMARY KEY CLUSTERED ([LockId] ASC),
        CONSTRAINT [CHK_DayLockAudit_Status] CHECK ([Status] IN ('LOCKED', 'REOPENED'))
    );
    CREATE NONCLUSTERED INDEX [IX_DayLockAudit_BusinessDate] ON [dbo].[DayLockAudit]([BusinessDate] DESC);
    CREATE NONCLUSTERED INDEX [IX_DayLockAudit_Status] ON [dbo].[DayLockAudit]([Status], [BusinessDate]);
    PRINT '✓ DayLockAudit table created';
END
ELSE PRINT '✓ DayLockAudit table already exists';

-- Add CashierId to Orders table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Orders]') AND name = 'CashierId')
BEGIN
    ALTER TABLE [dbo].[Orders] ADD [CashierId] INT NULL;
    ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Cashier] FOREIGN KEY([CashierId]) 
        REFERENCES [dbo].[Users]([Id]) ON DELETE NO ACTION;
    PRINT '✓ CashierId column added to Orders table';
END
ELSE PRINT '✓ CashierId column already exists in Orders table';

PRINT '';
PRINT 'STEP 1 COMPLETED: All tables created successfully!';
PRINT '';

-- =====================================================
-- STEP 2: Create Stored Procedures
-- =====================================================
PRINT 'STEP 2: Creating stored procedures...';

-- usp_InitializeDayOpening
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_InitializeDayOpening]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_InitializeDayOpening];
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
        IF EXISTS (SELECT 1 FROM CashierDayOpening WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId)
        BEGIN
            UPDATE CashierDayOpening SET OpeningFloat = @OpeningFloat, UpdatedBy = @CreatedBy, UpdatedAt = GETDATE()
            WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
            UPDATE CashierDayClose SET OpeningFloat = @OpeningFloat WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
            SELECT 'Updated' AS Result, 'Opening float updated successfully' AS Message;
        END
        ELSE
        BEGIN
            DECLARE @CashierName NVARCHAR(100);
            SELECT @CashierName = Username FROM Users WHERE Id = @CashierId;
            INSERT INTO CashierDayOpening (BusinessDate, CashierId, CashierName, OpeningFloat, CreatedBy)
            VALUES (@BusinessDate, @CashierId, @CashierName, @OpeningFloat, @CreatedBy);
            INSERT INTO CashierDayClose (BusinessDate, CashierId, CashierName, OpeningFloat, SystemAmount, CreatedBy)
            VALUES (@BusinessDate, @CashierId, @CashierName, @OpeningFloat, 0, @CreatedBy);
            SELECT 'Created' AS Result, 'Opening float created successfully' AS Message;
        END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO
PRINT '✓ usp_InitializeDayOpening created';

-- usp_GetCashierSystemAmount
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetCashierSystemAmount]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_GetCashierSystemAmount];
GO

CREATE PROCEDURE [dbo].[usp_GetCashierSystemAmount]
    @BusinessDate DATE,
    @CashierId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ISNULL(SUM(p.Amount), 0) AS SystemAmount
    FROM Orders o
    INNER JOIN Payments p ON p.OrderId = o.Id
    INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
    WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
      AND o.CashierId = @CashierId
      AND pm.Name = 'CASH'
      AND p.Status = 1
      AND o.Status IN (2, 3);
END
GO
PRINT '✓ usp_GetCashierSystemAmount created';

-- usp_SaveDeclaredCash
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_SaveDeclaredCash]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_SaveDeclaredCash];
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
        DECLARE @OpeningFloat DECIMAL(10,2), @SystemAmount DECIMAL(10,2), @Variance DECIMAL(10,2), @Status NVARCHAR(20);
        SELECT @OpeningFloat = OpeningFloat, @SystemAmount = SystemAmount FROM CashierDayClose
        WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
        SET @Variance = (@DeclaredAmount + @OpeningFloat) - @SystemAmount;
        SET @Status = CASE WHEN ABS(@Variance) > 100 THEN 'CHECK' ELSE 'OK' END;
        UPDATE CashierDayClose SET DeclaredAmount = @DeclaredAmount, Variance = @Variance, Status = @Status, UpdatedBy = @UpdatedBy, UpdatedAt = GETDATE()
        WHERE BusinessDate = @BusinessDate AND CashierId = @CashierId;
        COMMIT TRANSACTION;
        SELECT 'Success' AS Result, @Variance AS Variance, @Status AS Status, 'Cash declaration saved successfully' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO
PRINT '✓ usp_SaveDeclaredCash created';

-- usp_LockDay
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_LockDay]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_LockDay];
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
        SELECT @IssueCount = COUNT(*) FROM CashierDayClose WHERE BusinessDate = @BusinessDate AND Status = 'CHECK' AND LockedFlag = 0;
        IF @IssueCount > 0
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT 'Error' AS Result, @IssueCount AS IssueCount, 'Cannot lock day: ' + CAST(@IssueCount AS VARCHAR) + ' cashier(s) have unresolved variances' AS Message;
            RETURN;
        END
        UPDATE CashierDayClose SET LockedFlag = 1, LockedAt = GETDATE(), LockedBy = @LockedBy, Status = 'LOCKED', UpdatedBy = @LockedBy, UpdatedAt = GETDATE()
        WHERE BusinessDate = @BusinessDate;
        INSERT INTO DayLockAudit (BusinessDate, LockedBy, LockTime, Remarks, Status) VALUES (@BusinessDate, @LockedBy, GETDATE(), @Remarks, 'LOCKED');
        COMMIT TRANSACTION;
        SELECT 'Success' AS Result, 'Day locked successfully' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO
PRINT '✓ usp_LockDay created';

-- usp_GetDayClosingSummary
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetDayClosingSummary]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_GetDayClosingSummary];
GO

CREATE PROCEDURE [dbo].[usp_GetDayClosingSummary]
    @BusinessDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, CashierId, CashierName, OpeningFloat, SystemAmount, DeclaredAmount, Variance, Status, ApprovedBy, ApprovalComment, LockedFlag, LockedAt, LockedBy,
           (SystemAmount + OpeningFloat) AS ExpectedCash
    FROM CashierDayClose WHERE BusinessDate = @BusinessDate ORDER BY CashierName;
    
    SELECT TOP 1 LockId, BusinessDate, LockedBy, LockTime, Remarks, Status
    FROM DayLockAudit WHERE BusinessDate = @BusinessDate ORDER BY LockTime DESC;
END
GO
PRINT '✓ usp_GetDayClosingSummary created';

PRINT '';
PRINT 'STEP 2 COMPLETED: All stored procedures created successfully!';
PRINT '';

-- =====================================================
-- STEP 3: Auto-assign existing orders to cashiers
-- =====================================================
PRINT 'STEP 3: Auto-assigning existing orders to cashiers...';

DECLARE @DefaultCashierId INT;
DECLARE @TodayDate DATE = CAST(GETDATE() AS DATE);

-- Get first active administrator
SELECT TOP 1 @DefaultCashierId = u.Id
FROM Users u
INNER JOIN UserRoles ur ON ur.UserId = u.Id
INNER JOIN Roles r ON r.Id = ur.RoleId
WHERE u.IsActive = 1 AND r.Name = 'Administrator'
ORDER BY u.Id;

IF @DefaultCashierId IS NOT NULL
BEGIN
    -- Assign today's orders
    UPDATE Orders SET CashierId = @DefaultCashierId
    WHERE CAST(CreatedAt AS DATE) = @TodayDate AND CashierId IS NULL;
    
    DECLARE @UpdatedCount INT = @@ROWCOUNT;
    PRINT '✓ Auto-assigned ' + CAST(@UpdatedCount AS VARCHAR) + ' orders to cashier ID: ' + CAST(@DefaultCashierId AS VARCHAR);
END
ELSE
    PRINT '⚠ No administrator found. Orders need manual assignment.';

PRINT '';
PRINT 'STEP 3 COMPLETED: Order assignment done!';
PRINT '';

-- =====================================================
-- STEP 4: Create sample opening float (for testing)
-- =====================================================
PRINT 'STEP 4: Setting up sample data for today...';

IF @DefaultCashierId IS NOT NULL
BEGIN
    -- Check if opening already exists for today
    IF NOT EXISTS (SELECT 1 FROM CashierDayOpening WHERE BusinessDate = @TodayDate AND CashierId = @DefaultCashierId)
    BEGIN
        DECLARE @CashierName NVARCHAR(100);
        SELECT @CashierName = Username FROM Users WHERE Id = @DefaultCashierId;
        
        -- Create opening float
        INSERT INTO CashierDayOpening (BusinessDate, CashierId, CashierName, OpeningFloat, CreatedBy)
        VALUES (@TodayDate, @DefaultCashierId, @CashierName, 0, 'SYSTEM');
        
        -- Create closing record
        INSERT INTO CashierDayClose (BusinessDate, CashierId, CashierName, OpeningFloat, SystemAmount, CreatedBy)
        VALUES (@TodayDate, @DefaultCashierId, @CashierName, 0, 0, 'SYSTEM');
        
        -- Calculate system amount
        DECLARE @SystemAmount DECIMAL(10,2);
        SELECT @SystemAmount = ISNULL(SUM(p.Amount), 0)
        FROM Orders o
        INNER JOIN Payments p ON p.OrderId = o.Id
        INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
        WHERE CAST(o.CreatedAt AS DATE) = @TodayDate
          AND o.CashierId = @DefaultCashierId
          AND pm.Name = 'CASH'
          AND p.Status = 1
          AND o.Status IN (2, 3);
        
        -- Update system amount
        UPDATE CashierDayClose SET SystemAmount = @SystemAmount
        WHERE BusinessDate = @TodayDate AND CashierId = @DefaultCashierId;
        
        PRINT '✓ Created day closing record for ' + @CashierName;
        PRINT '  - Opening Float: ₹0.00';
        PRINT '  - System Amount: ₹' + CAST(@SystemAmount AS VARCHAR);
    END
    ELSE
    BEGIN
        PRINT '✓ Day closing record already exists for today';
        
        -- Just refresh system amount
        DECLARE @CurrentSystemAmount DECIMAL(10,2);
        SELECT @CurrentSystemAmount = ISNULL(SUM(p.Amount), 0)
        FROM Orders o
        INNER JOIN Payments p ON p.OrderId = o.Id
        INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
        WHERE CAST(o.CreatedAt AS DATE) = @TodayDate
          AND o.CashierId = @DefaultCashierId
          AND pm.Name = 'CASH'
          AND p.Status = 1
          AND o.Status IN (2, 3);
        
        UPDATE CashierDayClose SET SystemAmount = @CurrentSystemAmount
        WHERE BusinessDate = @TodayDate AND CashierId = @DefaultCashierId;
        
        PRINT '✓ System amount refreshed: ₹' + CAST(@CurrentSystemAmount AS VARCHAR);
    END
END

PRINT '';
PRINT 'STEP 4 COMPLETED: Sample data created!';
PRINT '';

-- =====================================================
-- FINAL SUMMARY
-- =====================================================
PRINT '========================================';
PRINT '✓✓✓ SETUP COMPLETED SUCCESSFULLY! ✓✓✓';
PRINT '========================================';
PRINT '';
PRINT 'What was done:';
PRINT '  1. Created 3 tables (CashierDayOpening, CashierDayClose, DayLockAudit)';
PRINT '  2. Created 5 stored procedures';
PRINT '  3. Added CashierId column to Orders table';
PRINT '  4. Auto-assigned today''s orders to a cashier';
PRINT '  5. Created initial day closing record for testing';
PRINT '';
PRINT 'NEXT STEPS - How to use:';
PRINT '  1. Login to your restaurant app';
PRINT '  2. Go to Settings → Day Closing';
PRINT '  3. You should see today''s dashboard with System Amount calculated';
PRINT '  4. Click "Open Float for Cashier" to set opening cash (e.g., ₹2000)';
PRINT '  5. Click "Refresh System Amounts" to update cash from sales';
PRINT '  6. Click "Declare" to enter actual counted cash';
PRINT '  7. System will auto-approve if variance ≤ ₹100';
PRINT '  8. Click "Lock Day" when all cashiers are done';
PRINT '  9. Click "View EOD Report" to print end-of-day summary';
PRINT '';
PRINT 'ENJOY YOUR SIMPLIFIED DAY CLOSING PROCESS! 🎉';
PRINT '========================================';
GO
