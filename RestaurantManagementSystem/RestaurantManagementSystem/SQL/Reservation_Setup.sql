-- Create Tables for Reservations Management

-- Create Tables Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tables')
BEGIN
    CREATE TABLE [dbo].[Tables] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [BranchId] INT NULL,
        [TableNumber] NVARCHAR(20) NOT NULL,
        [Capacity] INT NOT NULL,
        [Section] NVARCHAR(50) NULL,
        [IsAvailable] BIT NOT NULL DEFAULT 1,
        [Status] INT NOT NULL DEFAULT 0,  -- 0=Available, 1=Reserved, 2=Occupied, 3=Dirty, 4=Maintenance
        [MinPartySize] INT NOT NULL DEFAULT 1,
        [LastOccupiedAt] DATETIME NULL,
        [IsActive] BIT NOT NULL DEFAULT 1
    );

    -- Add some sample tables
    INSERT INTO [dbo].[Tables] (TableNumber, Capacity, Section, Status, MinPartySize)
    VALUES
        ('A1', 2, 'Main Floor', 0, 1),
        ('A2', 2, 'Main Floor', 0, 1),
        ('A3', 4, 'Main Floor', 0, 1),
        ('B1', 4, 'Window', 0, 2),
        ('B2', 4, 'Window', 0, 2),
        ('C1', 6, 'Patio', 0, 4),
        ('D1', 8, 'Private', 0, 6);
END
GO

-- Ensure existing databases get BranchId on Tables if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Tables')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'BranchId' AND Object_ID = OBJECT_ID(N'dbo.Tables'))
    BEGIN
        ALTER TABLE dbo.Tables ADD BranchId INT NULL;
    END
END
GO

-- Create Reservations Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Reservations')
BEGIN
    CREATE TABLE [dbo].[Reservations] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [BranchId] INT NULL,
        [GuestName] NVARCHAR(100) NOT NULL,
        [PhoneNumber] NVARCHAR(20) NOT NULL,
        [EmailAddress] NVARCHAR(100) NULL,
        [PartySize] INT NOT NULL,
        [ReservationDate] DATE NOT NULL,
        [ReservationTime] DATETIME NOT NULL,
        [SpecialRequests] NVARCHAR(200) NULL,
        [Notes] NVARCHAR(500) NULL,
        [TableId] INT NULL,
        [Status] INT NOT NULL DEFAULT 1,  -- 0=Pending, 1=Confirmed, 2=Seated, 3=Completed, 4=Cancelled, 5=NoShow, 6=Waitlisted
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [ReminderSent] BIT NOT NULL DEFAULT 0,
        [NoShow] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [FK_Reservations_Tables] FOREIGN KEY ([TableId]) REFERENCES [Tables]([Id])
    );

    -- Add some sample reservations
    DECLARE @Today DATE = GETDATE();
    DECLARE @Tomorrow DATE = DATEADD(DAY, 1, GETDATE());
    
    INSERT INTO [dbo].[Reservations] (GuestName, PhoneNumber, EmailAddress, PartySize, ReservationDate, ReservationTime, Status)
    VALUES
        ('John Smith', '555-123-4567', 'john@example.com', 2, @Today, DATEADD(HOUR, 18, @Today), 1),
        ('Emily Johnson', '555-234-5678', 'emily@example.com', 4, @Today, DATEADD(HOUR, 19, @Today), 1),
        ('Michael Brown', '555-345-6789', 'michael@example.com', 6, @Tomorrow, DATEADD(HOUR, 18, @Tomorrow), 1),
        ('Sarah Davis', '555-456-7890', 'sarah@example.com', 2, @Tomorrow, DATEADD(HOUR, 19, @Tomorrow), 1);
END
GO

-- Ensure existing databases get BranchId on Reservations if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Reservations')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'BranchId' AND Object_ID = OBJECT_ID(N'dbo.Reservations'))
    BEGIN
        ALTER TABLE dbo.Reservations ADD BranchId INT NULL;
    END
END
GO

-- Create Waitlist Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Waitlist')
BEGIN
    CREATE TABLE [dbo].[Waitlist] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [BranchId] INT NULL,
        [GuestName] NVARCHAR(100) NOT NULL,
        [PhoneNumber] NVARCHAR(20) NOT NULL,
        [PartySize] INT NOT NULL,
        [AddedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [QuotedWaitTime] INT NOT NULL DEFAULT 30,
        [NotifyWhenReady] BIT NOT NULL DEFAULT 1,
        [Notes] NVARCHAR(200) NULL,
        [Status] INT NOT NULL DEFAULT 0,  -- 0=Waiting, 1=Notified, 2=Seated, 3=Left, 4=NoResponse
        [NotifiedAt] DATETIME NULL,
        [SeatedAt] DATETIME NULL,
        [TableId] INT NULL,
        CONSTRAINT [FK_Waitlist_Tables] FOREIGN KEY ([TableId]) REFERENCES [Tables]([Id])
    );

    -- Add sample waitlist entries
    INSERT INTO [dbo].[Waitlist] (GuestName, PhoneNumber, PartySize, QuotedWaitTime, NotifyWhenReady)
    VALUES
        ('David Wilson', '555-567-8901', 3, 20, 1),
        ('Jennifer Lee', '555-678-9012', 2, 15, 1);
END
GO

-- Ensure existing databases get BranchId on Waitlist if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Waitlist')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'BranchId' AND Object_ID = OBJECT_ID(N'dbo.Waitlist'))
    BEGIN
        ALTER TABLE dbo.Waitlist ADD BranchId INT NULL;
    END
END
GO

-- Create stored procedure for reservation management
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_UpsertReservation')
    DROP PROCEDURE usp_UpsertReservation
GO

CREATE PROCEDURE [dbo].[usp_UpsertReservation]
    @Id INT,
    @BranchId INT = NULL,
    @GuestName NVARCHAR(100),
    @PhoneNumber NVARCHAR(20),
    @EmailAddress NVARCHAR(100) = NULL,
    @PartySize INT,
    @ReservationDate DATE,
    @ReservationTime DATETIME,
    @SpecialRequests NVARCHAR(200) = NULL,
    @Notes NVARCHAR(500) = NULL,
    @TableId INT = NULL,
    @Status INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Message NVARCHAR(200);
    DECLARE @ErrorMsg NVARCHAR(200) = NULL;

    -- Check if the table exists and is available
    IF @TableId IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1
            FROM [Tables]
            WHERE [Id] = @TableId
              AND (COL_LENGTH('dbo.Tables', 'BranchId') IS NULL OR @BranchId IS NULL OR [BranchId] = @BranchId)
        )
        BEGIN
            SET @ErrorMsg = 'Table does not exist.';
        END
        ELSE IF EXISTS (
            SELECT 1 FROM [Reservations] 
            WHERE [TableId] = @TableId 
            AND [Id] <> @Id  -- Exclude current reservation
            AND [Status] IN (1, 2)  -- Confirmed or Seated
            AND CONVERT(date, [ReservationDate]) = @ReservationDate
            AND DATEADD(HOUR, -1, @ReservationTime) < [ReservationTime]
            AND DATEADD(HOUR, 1, @ReservationTime) > [ReservationTime]
            AND (COL_LENGTH('dbo.Reservations', 'BranchId') IS NULL OR @BranchId IS NULL OR [BranchId] = @BranchId)
        )
        BEGIN
            SET @ErrorMsg = 'Table is already reserved for this time.';
        END
    END

    -- If there's an error, return it
    IF @ErrorMsg IS NOT NULL
    BEGIN
        SELECT @ErrorMsg AS [Message];
        RETURN;
    END
    
    -- INSERT or UPDATE based on Id
    IF @Id = 0
    BEGIN
        -- Insert new reservation
        INSERT INTO [Reservations] (
            [BranchId],
            [GuestName], 
            [PhoneNumber], 
            [EmailAddress], 
            [PartySize], 
            [ReservationDate], 
            [ReservationTime], 
            [SpecialRequests], 
            [Notes], 
            [TableId], 
            [Status],
            [CreatedAt],
            [UpdatedAt]
        )
        VALUES (
            @BranchId,
            @GuestName, 
            @PhoneNumber, 
            @EmailAddress, 
            @PartySize, 
            @ReservationDate, 
            @ReservationTime, 
            @SpecialRequests, 
            @Notes, 
            @TableId, 
            @Status,
            GETDATE(),
            GETDATE()
        );
        
        SET @Message = 'Reservation created successfully.';
    END
    ELSE
    BEGIN
        -- Update existing reservation
        UPDATE [Reservations]
        SET 
            [BranchId] = ISNULL(@BranchId, [BranchId]),
            [GuestName] = @GuestName,
            [PhoneNumber] = @PhoneNumber,
            [EmailAddress] = @EmailAddress,
            [PartySize] = @PartySize,
            [ReservationDate] = @ReservationDate,
            [ReservationTime] = @ReservationTime,
            [SpecialRequests] = @SpecialRequests,
            [Notes] = @Notes,
            [TableId] = @TableId,
            [Status] = @Status,
            [UpdatedAt] = GETDATE()
                WHERE [Id] = @Id
                    AND (COL_LENGTH('dbo.Reservations', 'BranchId') IS NULL OR @BranchId IS NULL OR [BranchId] = @BranchId);
        
        SET @Message = 'Reservation updated successfully.';
    END
    
    SELECT @Message AS [Message];
END
GO

-- Create stored procedure for waitlist management
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_UpsertWaitlist')
    DROP PROCEDURE usp_UpsertWaitlist
GO

CREATE PROCEDURE [dbo].[usp_UpsertWaitlist]
    @Id INT,
    @BranchId INT = NULL,
    @GuestName NVARCHAR(100),
    @PhoneNumber NVARCHAR(20),
    @PartySize INT,
    @QuotedWaitTime INT,
    @NotifyWhenReady BIT,
    @Notes NVARCHAR(200) = NULL,
    @Status INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Message NVARCHAR(200);
    
    -- INSERT or UPDATE based on Id
    IF @Id = 0
    BEGIN
        -- Insert new waitlist entry
        INSERT INTO [Waitlist] (
            [BranchId],
            [GuestName], 
            [PhoneNumber], 
            [PartySize], 
            [QuotedWaitTime], 
            [NotifyWhenReady], 
            [Notes], 
            [Status],
            [AddedAt]
        )
        VALUES (
            @BranchId,
            @GuestName, 
            @PhoneNumber, 
            @PartySize, 
            @QuotedWaitTime, 
            @NotifyWhenReady, 
            @Notes, 
            @Status,
            GETDATE()
        );
        
        SET @Message = 'Guest added to waitlist successfully.';
    END
    ELSE
    BEGIN
        -- Update existing waitlist entry
        UPDATE [Waitlist]
        SET 
                        [BranchId] = ISNULL(@BranchId, [BranchId]),
            [GuestName] = @GuestName,
            [PhoneNumber] = @PhoneNumber,
            [PartySize] = @PartySize,
            [QuotedWaitTime] = @QuotedWaitTime,
            [NotifyWhenReady] = @NotifyWhenReady,
            [Notes] = @Notes,
            [Status] = @Status
                WHERE [Id] = @Id
                    AND (COL_LENGTH('dbo.Waitlist', 'BranchId') IS NULL OR @BranchId IS NULL OR [BranchId] = @BranchId);
        
        SET @Message = 'Waitlist entry updated successfully.';
    END
    
    SELECT @Message AS [Message];
END
GO

-- Create stored procedure for table management
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_UpsertTable')
    DROP PROCEDURE usp_UpsertTable
GO

CREATE PROCEDURE [dbo].[usp_UpsertTable]
    @Id INT,
    @TableNumber NVARCHAR(20),
    @Capacity INT,
    @Section NVARCHAR(50) = NULL,
    @Status INT = 0,
    @MinPartySize INT = 1,
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Message NVARCHAR(200);

    -- Check if table number already exists (for different table)
    IF EXISTS (SELECT 1 FROM [Tables] WHERE [TableNumber] = @TableNumber AND [Id] <> @Id)
    BEGIN
        SELECT 'Table number already exists. Please use a different table number.' AS [Message];
        RETURN;
    END
    
    -- INSERT or UPDATE based on Id
    IF @Id = 0
    BEGIN
        -- Insert new table
        INSERT INTO [Tables] (
            [TableNumber], 
            [Capacity], 
            [Section], 
            [Status], 
            [MinPartySize], 
            [IsActive]
        )
        VALUES (
            @TableNumber, 
            @Capacity, 
            @Section, 
            @Status, 
            @MinPartySize, 
            @IsActive
        );
        
        SET @Message = 'Table created successfully.';
    END
    ELSE
    BEGIN
        -- Update existing table
        UPDATE [Tables]
        SET 
            [TableNumber] = @TableNumber,
            [Capacity] = @Capacity,
            [Section] = @Section,
            [Status] = @Status,
            [MinPartySize] = @MinPartySize,
            [IsActive] = @IsActive
        WHERE [Id] = @Id;
        
        SET @Message = 'Table updated successfully.';
    END
    
    SELECT @Message AS [Message];
END
GO
