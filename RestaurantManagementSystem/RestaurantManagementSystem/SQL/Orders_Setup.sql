-- Create tables for UC-003: Capture Dine-In Order

-- Create MenuItems Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MenuItems')
BEGIN
    CREATE TABLE [dbo].[MenuItems] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [Price] DECIMAL(10, 2) NOT NULL,
        [CategoryId] INT NOT NULL,
        [IsAvailable] BIT NOT NULL DEFAULT 1,
        [PrepTime] INT NULL, -- Estimated prep time in minutes
        [ImagePath] NVARCHAR(255) NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [FK_MenuItems_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [Categories]([Id])
    );
END
GO

-- Create Modifiers Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Modifiers')
BEGIN
    CREATE TABLE [dbo].[Modifiers] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Price] DECIMAL(10, 2) NOT NULL DEFAULT 0,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Create MenuItem_Modifiers linking table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MenuItem_Modifiers')
BEGIN
    CREATE TABLE [dbo].[MenuItem_Modifiers] (
        [MenuItemId] INT NOT NULL,
        [ModifierId] INT NOT NULL,
        PRIMARY KEY ([MenuItemId], [ModifierId]),
        CONSTRAINT [FK_MenuItem_Modifiers_MenuItems] FOREIGN KEY ([MenuItemId]) REFERENCES [MenuItems]([Id]),
        CONSTRAINT [FK_MenuItem_Modifiers_Modifiers] FOREIGN KEY ([ModifierId]) REFERENCES [Modifiers]([Id])
    );
END
GO

-- Create Allergens Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Allergens')
BEGIN
    CREATE TABLE [dbo].[Allergens] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [IconPath] NVARCHAR(255) NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Create MenuItem_Allergens linking table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MenuItem_Allergens')
BEGIN
    CREATE TABLE [dbo].[MenuItem_Allergens] (
        [MenuItemId] INT NOT NULL,
        [AllergenId] INT NOT NULL,
        PRIMARY KEY ([MenuItemId], [AllergenId]),
        CONSTRAINT [FK_MenuItem_Allergens_MenuItems] FOREIGN KEY ([MenuItemId]) REFERENCES [MenuItems]([Id]),
        CONSTRAINT [FK_MenuItem_Allergens_Allergens] FOREIGN KEY ([AllergenId]) REFERENCES [Allergens]([Id])
    );
END
GO

-- Create CourseTypes Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CourseTypes')
BEGIN
    CREATE TABLE [dbo].[CourseTypes] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(50) NOT NULL,
        [DisplayOrder] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE()
    );
    
    -- Insert default course types
    INSERT INTO [CourseTypes] ([Name], [DisplayOrder])
    VALUES 
        ('Appetizer', 1),
        ('Soup/Salad', 2),
        ('Main Course', 3),
        ('Dessert', 4),
        ('Beverage', 5);
END
GO

-- Create Orders Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    CREATE TABLE [dbo].[Orders] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [OrderNumber] NVARCHAR(20) NOT NULL,
        [BranchId] INT NULL,
        [TableTurnoverId] INT NULL, -- NULL for takeout/delivery orders
        [OrderType] INT NOT NULL, -- 0=Dine-In, 1=Takeout, 2=Delivery, 3=Online
        [Status] INT NOT NULL DEFAULT 0, -- 0=Open, 1=In Progress, 2=Ready, 3=Completed, 4=Cancelled
        [UserId] INT NULL, -- Server or user who created the order
        [CustomerName] NVARCHAR(100) NULL,
        [CustomerPhone] NVARCHAR(20) NULL,
        [Customeremailid] NVARCHAR(100) NULL,
        [Subtotal] DECIMAL(10, 2) NOT NULL DEFAULT 0,
        [TaxAmount] DECIMAL(10, 2) NOT NULL DEFAULT 0,
        [TipAmount] DECIMAL(10, 2) NOT NULL DEFAULT 0,
        [DiscountAmount] DECIMAL(10, 2) NOT NULL DEFAULT 0,
        [TotalAmount] DECIMAL(10, 2) NOT NULL DEFAULT 0,
        [SpecialInstructions] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [CompletedAt] DATETIME NULL,
        CONSTRAINT [FK_Orders_TableTurnovers] FOREIGN KEY ([TableTurnoverId]) REFERENCES [TableTurnovers]([Id]),
        CONSTRAINT [FK_Orders_Users] FOREIGN KEY ([UserId]) REFERENCES [Users]([Id])
    );
END
GO

-- Ensure existing databases get BranchId on Orders if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'BranchId' AND Object_ID = OBJECT_ID(N'dbo.Orders'))
    BEGIN
        ALTER TABLE dbo.Orders ADD BranchId INT NULL;
    END
END
GO

-- Ensure existing databases get Customeremailid on Orders if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'Customeremailid' AND Object_ID = OBJECT_ID(N'dbo.Orders'))
    BEGIN
        ALTER TABLE dbo.Orders ADD Customeremailid NVARCHAR(100) NULL;
    END
END
GO

-- Create OrderItems Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderItems')
BEGIN
    CREATE TABLE [dbo].[OrderItems] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [OrderId] INT NOT NULL,
        [MenuItemId] INT NOT NULL,
        [Quantity] INT NOT NULL DEFAULT 1,
        [UnitPrice] DECIMAL(10, 2) NOT NULL,
        [Subtotal] DECIMAL(10, 2) NOT NULL,
        [SpecialInstructions] NVARCHAR(500) NULL,
        [CourseId] INT NULL,
        [Status] INT NOT NULL DEFAULT 0, -- 0=New, 1=Fired, 2=Cooking, 3=Ready, 4=Delivered, 5=Cancelled
        [FireTime] DATETIME NULL, -- When the item was sent to the kitchen
        [CompletionTime] DATETIME NULL, -- When the kitchen completed the item
        [DeliveryTime] DATETIME NULL, -- When the item was delivered to the table
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [FK_OrderItems_Orders] FOREIGN KEY ([OrderId]) REFERENCES [Orders]([Id]),
        CONSTRAINT [FK_OrderItems_MenuItems] FOREIGN KEY ([MenuItemId]) REFERENCES [MenuItems]([Id]),
        CONSTRAINT [FK_OrderItems_CourseTypes] FOREIGN KEY ([CourseId]) REFERENCES [CourseTypes]([Id])
    );
END
GO

-- Create OrderItemModifiers Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderItemModifiers')
BEGIN
    CREATE TABLE [dbo].[OrderItemModifiers] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [OrderItemId] INT NOT NULL,
        [ModifierId] INT NOT NULL,
        [Price] DECIMAL(10, 2) NOT NULL,
        CONSTRAINT [FK_OrderItemModifiers_OrderItems] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems]([Id]),
        CONSTRAINT [FK_OrderItemModifiers_Modifiers] FOREIGN KEY ([ModifierId]) REFERENCES [Modifiers]([Id])
    );
END
GO

-- Create KitchenTickets Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'KitchenTickets')
BEGIN
    CREATE TABLE [dbo].[KitchenTickets] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [TicketNumber] NVARCHAR(20) NOT NULL,
        [OrderId] INT NOT NULL,
        [StationId] INT NULL, -- NULL for tickets not assigned to a specific station
        [Status] INT NOT NULL DEFAULT 0, -- 0=New, 1=In Progress, 2=Ready, 3=Completed, 4=Cancelled
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [CompletedAt] DATETIME NULL,
        CONSTRAINT [FK_KitchenTickets_Orders] FOREIGN KEY ([OrderId]) REFERENCES [Orders]([Id])
    );
END
GO

-- Create KitchenTicketItems Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'KitchenTicketItems')
BEGIN
    CREATE TABLE [dbo].[KitchenTicketItems] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [KitchenTicketId] INT NOT NULL,
        [OrderItemId] INT NOT NULL,
        [Status] INT NOT NULL DEFAULT 0, -- 0=New, 1=In Progress, 2=Ready, 3=Completed, 4=Cancelled
        [StartTime] DATETIME NULL,
        [CompletionTime] DATETIME NULL,
        [Notes] NVARCHAR(500) NULL,
        CONSTRAINT [FK_KitchenTicketItems_KitchenTickets] FOREIGN KEY ([KitchenTicketId]) REFERENCES [KitchenTickets]([Id]),
        CONSTRAINT [FK_KitchenTicketItems_OrderItems] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems]([Id])
    );
END
GO

-- Ensure existing databases have UpdatedAt on OrderItems if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderItems')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'UpdatedAt' AND Object_ID = OBJECT_ID(N'dbo.OrderItems'))
    BEGIN
        ALTER TABLE dbo.OrderItems ADD UpdatedAt DATETIME NULL;
    END
END
GO

-- Ensure existing databases have UpdatedAt on KitchenTickets if it's missing
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'KitchenTickets')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'UpdatedAt' AND Object_ID = OBJECT_ID(N'dbo.KitchenTickets'))
    BEGIN
        ALTER TABLE dbo.KitchenTickets ADD UpdatedAt DATETIME NULL;
    END
END
GO

-- Create stored procedure for creating a new order
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_CreateOrder')
    DROP PROCEDURE usp_CreateOrder
GO

CREATE PROCEDURE [dbo].[usp_CreateOrder]
    @TableTurnoverId INT = NULL,
    @OrderType INT,
    @UserId INT,
    @CustomerName NVARCHAR(100) = NULL,
    @CustomerPhone NVARCHAR(20) = NULL,
    @CustomerEmailId NVARCHAR(100) = NULL,
    @SpecialInstructions NVARCHAR(500) = NULL,
    @OrderByUserId INT = NULL,
    @OrderByUserName NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @OrderNumber NVARCHAR(20);
    DECLARE @OrderId INT;
    DECLARE @Message NVARCHAR(200);

    -- OrderNumber is assigned when the first menu item is added (prevents consuming numbers for abandoned orders)
    SET @OrderNumber = '';
    
    BEGIN TRANSACTION;
    
    BEGIN TRY
        -- Create new order (store who created the order)
        INSERT INTO [Orders] (
            [OrderNumber],
            [TableTurnoverId],
            [OrderType],
            [Status],
            [UserId],
            [CustomerName],
            [CustomerPhone],
            [Customeremailid],
            [SpecialInstructions],
            [Order_by_UserID],
            [Order_by_UserName],
            [CreatedAt],
            [UpdatedAt]
        ) VALUES (
            @OrderNumber,
            @TableTurnoverId,
            @OrderType,
            0, -- Open
            @UserId,
            @CustomerName,
            @CustomerPhone,
            @CustomerEmailId,
            @SpecialInstructions,
            @OrderByUserId,
            @OrderByUserName,
            GETDATE(),
            GETDATE()
        );
        
        SET @OrderId = SCOPE_IDENTITY();
        
        -- If table turnover is provided, update its status to InService
        IF @TableTurnoverId IS NOT NULL
        BEGIN
            UPDATE [TableTurnovers]
            SET [Status] = 1, -- InService
                [StartedServiceAt] = 
                    CASE 
                        WHEN [StartedServiceAt] IS NULL THEN GETDATE() 
                        ELSE [StartedServiceAt] 
                    END
            WHERE [Id] = @TableTurnoverId AND [Status] = 0;
        END
        
        COMMIT TRANSACTION;
        
        -- Return order details
        SELECT @OrderId AS OrderId, @OrderNumber AS OrderNumber, 'Order created successfully.' AS [Message];
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        SET @Message = 'Error creating order: ' + ERROR_MESSAGE();
        SELECT 0 AS OrderId, '' AS OrderNumber, @Message AS [Message];
    END CATCH
END
GO

-- Create stored procedure for adding an item to an order
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_AddOrderItem')
    DROP PROCEDURE usp_AddOrderItem
GO

CREATE PROCEDURE [dbo].[usp_AddOrderItem]
    @OrderId INT,
    @MenuItemId INT,
    @Quantity INT,
    @SpecialInstructions NVARCHAR(500) = NULL,
    @CourseId INT = NULL,
    @ModifierIds NVARCHAR(MAX) = NULL -- Comma-separated list of modifier IDs
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @UnitPrice DECIMAL(10, 2);
    DECLARE @Subtotal DECIMAL(10, 2);
    DECLARE @OrderItemId INT;
    DECLARE @Message NVARCHAR(200);
    DECLARE @OrderNumber NVARCHAR(20);
    DECLARE @GlobalBillNo NVARCHAR(50);
    
    -- Check if order exists
    IF NOT EXISTS (SELECT 1 FROM [Orders] WHERE [Id] = @OrderId)
    BEGIN
        SELECT 'Order does not exist.' AS [Message];
        RETURN;
    END
    
    -- Check if menu item exists and get price
    IF NOT EXISTS (SELECT 1 FROM [MenuItems] WHERE [Id] = @MenuItemId AND [IsAvailable] = 1)
    BEGIN
        SELECT 'Menu item does not exist or is not available.' AS [Message];
        RETURN;
    END
    
    -- Get menu item price based on order type
    -- OrderType: 0=Dine-In, 1=Takeout, 2=Delivery, 3=Online
    DECLARE @OrderType INT;
    SELECT @OrderType = [OrderType] FROM [Orders] WHERE [Id] = @OrderId;
    
    -- Select price based on order type
    -- Dine-In (0): Use Price column
    -- Takeout (1): Use TakeoutPrice if available, fallback to Price
    -- Delivery (2): Use DeliveryPrice if available, fallback to Price
    -- Online (3): Use DeliveryPrice if available, fallback to Price
    SELECT @UnitPrice = CASE 
        WHEN @OrderType = 1 THEN ISNULL([TakeoutPrice], [Price])  -- Takeout
        WHEN @OrderType IN (2, 3) THEN ISNULL([DeliveryPrice], [Price])  -- Delivery or Online
        ELSE [Price]  -- Dine-In (0) or default
    END
    FROM [MenuItems] WHERE [Id] = @MenuItemId;
    
    -- Calculate subtotal
    SET @Subtotal = @UnitPrice * @Quantity;
    
    BEGIN TRANSACTION;
    
    BEGIN TRY
        -- Assign OrderNumber on first item add (schema uses NOT NULL; blank means "not assigned yet")
         SELECT @OrderNumber = o.OrderNumber,
             @GlobalBillNo = CASE WHEN COL_LENGTH('dbo.Orders','GlobalBillNo') IS NOT NULL THEN o.GlobalBillNo ELSE NULL END
        FROM dbo.Orders o WITH (UPDLOCK, HOLDLOCK)
        WHERE o.Id = @OrderId;

        IF (@OrderNumber IS NULL OR LTRIM(RTRIM(@OrderNumber)) = '')
        BEGIN
            DECLARE @Today VARCHAR(8) = CONVERT(VARCHAR(8), GETDATE(), 112);
            DECLARE @OrderCount INT;
            DECLARE @HasOrdersBranch BIT = CASE WHEN COL_LENGTH('dbo.Orders', 'BranchId') IS NULL THEN 0 ELSE 1 END;
            DECLARE @OrderBranchId INT = NULL;
            DECLARE @OrderPrefix NVARCHAR(20) = 'ORD';

            IF @HasOrdersBranch = 1
            BEGIN
                DECLARE @BranchSql NVARCHAR(MAX) = N'
                    SELECT @OrderBranchIdOut = BranchId
                    FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                    WHERE Id = @OrderIdIn;';

                EXEC sp_executesql
                    @BranchSql,
                    N'@OrderIdIn INT, @OrderBranchIdOut INT OUTPUT',
                    @OrderIdIn = @OrderId,
                    @OrderBranchIdOut = @OrderBranchId OUTPUT;

                IF @OrderBranchId IS NOT NULL
                BEGIN
                    SELECT TOP 1 @OrderPrefix = ISNULL(NULLIF(LTRIM(RTRIM(BranchCode)), ''), 'ORD')
                    FROM dbo.Branches
                    WHERE BranchId = @OrderBranchId;
                END
            END

            IF @HasOrdersBranch = 1 AND @OrderBranchId IS NOT NULL
            BEGIN
                DECLARE @CountSql NVARCHAR(MAX) = N'
                    SELECT @OrderCountOut = ISNULL(MAX(CAST(RIGHT(OrderNumber, 4) AS INT)), 0) + 1
                    FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                    WHERE OrderNumber LIKE @PrefixIn + ''-'' + @TodayIn + ''-%''
                      AND BranchId = @BranchIdIn;';

                EXEC sp_executesql
                    @CountSql,
                    N'@TodayIn VARCHAR(8), @PrefixIn NVARCHAR(20), @BranchIdIn INT, @OrderCountOut INT OUTPUT',
                    @TodayIn = @Today,
                    @PrefixIn = @OrderPrefix,
                    @BranchIdIn = @OrderBranchId,
                    @OrderCountOut = @OrderCount OUTPUT;
            END
            ELSE
            BEGIN
                SELECT @OrderCount = ISNULL(MAX(CAST(RIGHT(OrderNumber, 4) AS INT)), 0) + 1
                FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                WHERE OrderNumber LIKE @OrderPrefix + '-' + @Today + '-%';
            END

            SET @OrderNumber = @OrderPrefix + '-' + @Today + '-' + RIGHT('0000' + CAST(@OrderCount AS VARCHAR(4)), 4);

            UPDATE dbo.Orders
            SET OrderNumber = @OrderNumber,
                UpdatedAt = GETDATE()
            WHERE Id = @OrderId;
        END

        IF (COL_LENGTH('dbo.Orders','GlobalBillNo') IS NOT NULL
            AND (@GlobalBillNo IS NULL OR LTRIM(RTRIM(@GlobalBillNo)) = '')
            AND @OrderNumber IS NOT NULL AND LTRIM(RTRIM(@OrderNumber)) <> '')
        BEGIN
            DECLARE @NowDate DATE = CAST(GETDATE() AS DATE);
            DECLARE @FyStartYear INT = CASE WHEN MONTH(@NowDate) >= 4 THEN YEAR(@NowDate) ELSE YEAR(@NowDate) - 1 END;
            DECLARE @FyEndYear INT = @FyStartYear + 1;
            DECLARE @FyCode VARCHAR(4) = RIGHT(CAST(@FyStartYear AS VARCHAR(4)), 2) + RIGHT(CAST(@FyEndYear AS VARCHAR(4)), 2);
            DECLARE @NextSeq INT;

            SELECT @NextSeq = ISNULL(MAX(TRY_CAST(RIGHT(GlobalBillNo, 6) AS INT)), 0) + 1
            FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
            WHERE GlobalBillNo LIKE 'INV-' + @FyCode + '-%';

            SET @GlobalBillNo = 'INV-' + @FyCode + '-' + RIGHT('000000' + CAST(@NextSeq AS VARCHAR(6)), 6);

            UPDATE dbo.Orders
            SET GlobalBillNo = @GlobalBillNo,
                UpdatedAt = GETDATE()
            WHERE Id = @OrderId;
        END

        -- Add order item
        INSERT INTO [OrderItems] (
            [OrderId],
            [MenuItemId],
            [Quantity],
            [UnitPrice],
            [Subtotal],
            [SpecialInstructions],
            [CourseId],
            [CreatedAt],
            [UpdatedAt]
        ) VALUES (
            @OrderId,
            @MenuItemId,
            @Quantity,
            @UnitPrice,
            @Subtotal,
            @SpecialInstructions,
            @CourseId,
            GETDATE(),
            GETDATE()
        );
        
        SET @OrderItemId = SCOPE_IDENTITY();
        
        -- Add modifiers if provided
        IF @ModifierIds IS NOT NULL AND LEN(@ModifierIds) > 0
        BEGIN
            -- Split the comma-separated list of modifier IDs
            WITH ModifierCTE AS (
                SELECT CAST(value AS INT) AS ModifierId
                FROM STRING_SPLIT(@ModifierIds, ',')
            )
            INSERT INTO [OrderItemModifiers] ([OrderItemId], [ModifierId], [Price])
            SELECT @OrderItemId, m.ModifierId, mo.[Price]
            FROM ModifierCTE m
            JOIN [Modifiers] mo ON m.ModifierId = mo.[Id];
            
            -- Update order item subtotal to include modifier prices
            UPDATE oi
            SET oi.[Subtotal] = oi.[Subtotal] + (
                SELECT ISNULL(SUM(oim.[Price]), 0) * oi.[Quantity]
                FROM [OrderItemModifiers] oim
                WHERE oim.[OrderItemId] = oi.[Id]
            )
            FROM [OrderItems] oi
            WHERE oi.[Id] = @OrderItemId;
        END
        
        -- Update order totals
        UPDATE o
        SET o.[Subtotal] = (
                SELECT SUM(oi.[Subtotal])
                FROM [OrderItems] oi
                WHERE oi.[OrderId] = o.[Id]
            ),
            o.[TaxAmount] = (
                SELECT SUM(oi.[Subtotal]) * 0.10 -- Assuming 10% tax rate
                FROM [OrderItems] oi
                WHERE oi.[OrderId] = o.[Id]
            ),
            o.[UpdatedAt] = GETDATE()
        FROM [Orders] o
        WHERE o.[Id] = @OrderId;
        
        -- Update total amount
        UPDATE [Orders]
        SET [TotalAmount] = [Subtotal] + [TaxAmount] - [DiscountAmount] + [TipAmount],
            [UpdatedAt] = GETDATE()
        WHERE [Id] = @OrderId;
        
        COMMIT TRANSACTION;
        
        SET @Message = 'Item added to order successfully.';
        SELECT @OrderItemId AS OrderItemId, @Message AS [Message], @OrderNumber AS OrderNumber;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        SET @Message = 'Error adding item to order: ' + ERROR_MESSAGE();
        SELECT 0 AS OrderItemId, @Message AS [Message];
    END CATCH
END
GO

-- Create stored procedure for firing order items to the kitchen
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_FireOrderItems')
    DROP PROCEDURE usp_FireOrderItems
GO

CREATE PROCEDURE [dbo].[usp_FireOrderItems]
    @OrderId INT,
    @OrderItemIds NVARCHAR(MAX) = NULL -- Comma-separated list of order item IDs or NULL for all unfired items
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Message NVARCHAR(200);
    DECLARE @TicketNumber NVARCHAR(20);
    DECLARE @KitchenTicketId INT;
    DECLARE @OrderBranchId INT;
    
    -- Check if order exists
    IF NOT EXISTS (SELECT 1 FROM [Orders] WHERE [Id] = @OrderId)
    BEGIN
        SELECT 'Order does not exist.' AS [Message];
        RETURN;
    END
    
    SELECT @OrderBranchId = BranchId
    FROM [Orders]
    WHERE [Id] = @OrderId;

    -- Generate unique ticket number per day and per branch
    SELECT @TicketNumber =
        'KOT-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + '-' +
        RIGHT('0000' + CAST(
            ISNULL(MAX(TRY_CAST(RIGHT(kt.TicketNumber, 4) AS INT)), 0) + 1
        AS NVARCHAR(4)), 4)
    FROM KitchenTickets kt WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN [Orders] o2 ON o2.[Id] = kt.[OrderId]
    WHERE LEFT(kt.TicketNumber, 12) = 'KOT-' + CONVERT(NVARCHAR(8), GETDATE(), 112)
      AND ((@OrderBranchId IS NULL AND o2.BranchId IS NULL) OR o2.BranchId = @OrderBranchId);
    
    BEGIN TRANSACTION;
    
    BEGIN TRY
        -- Create kitchen ticket
        INSERT INTO [KitchenTickets] (
            [TicketNumber],
            [OrderId],
            [Status],
            [CreatedAt],
            [UpdatedAt]
        ) VALUES (
            @TicketNumber,
            @OrderId,
            0, -- New
            GETDATE(),
            GETDATE()
        );
        
        SET @KitchenTicketId = SCOPE_IDENTITY();
        
        -- Update order items status and add them to kitchen ticket items
        IF @OrderItemIds IS NOT NULL AND LEN(@OrderItemIds) > 0
        BEGIN
            -- Split the comma-separated list of order item IDs
            WITH OrderItemCTE AS (
                SELECT CAST(value AS INT) AS OrderItemId
                FROM STRING_SPLIT(@OrderItemIds, ',')
            )
            -- Update order items
            UPDATE oi
            SET oi.[Status] = 1, -- Fired
                oi.[FireTime] = GETDATE(),
                oi.[UpdatedAt] = GETDATE()
            FROM [OrderItems] oi
            JOIN OrderItemCTE cte ON oi.[Id] = cte.OrderItemId
            WHERE oi.[OrderId] = @OrderId AND oi.[Status] = 0; -- Only unfired items
            
            -- Add to kitchen ticket items
            INSERT INTO [KitchenTicketItems] ([KitchenTicketId], [OrderItemId], [Status])
            SELECT @KitchenTicketId, cte.OrderItemId, 0 -- New
            FROM OrderItemCTE cte
            JOIN [OrderItems] oi ON cte.OrderItemId = oi.[Id]
            WHERE oi.[OrderId] = @OrderId;
        END
        ELSE
        BEGIN
            -- Update all unfired order items
            UPDATE oi
            SET oi.[Status] = 1, -- Fired
                oi.[FireTime] = GETDATE(),
                oi.[UpdatedAt] = GETDATE()
            FROM [OrderItems] oi
            WHERE oi.[OrderId] = @OrderId AND oi.[Status] = 0; -- Only unfired items
            
            -- Add all unfired items to kitchen ticket items
            INSERT INTO [KitchenTicketItems] ([KitchenTicketId], [OrderItemId], [Status])
            SELECT @KitchenTicketId, oi.[Id], 0 -- New
            FROM [OrderItems] oi
            WHERE oi.[OrderId] = @OrderId AND oi.[Status] = 1; -- Just fired items
        END
        
        -- Update order status to In Progress if it was Open
        UPDATE [Orders]
        SET [Status] = CASE WHEN [Status] = 0 THEN 1 ELSE [Status] END, -- Set to In Progress if Open
            [UpdatedAt] = GETDATE()
        WHERE [Id] = @OrderId;
        
        COMMIT TRANSACTION;
        
        SET @Message = 'Items fired to kitchen successfully.';
        SELECT @KitchenTicketId AS KitchenTicketId, @TicketNumber AS TicketNumber, @Message AS [Message];
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        SET @Message = 'Error firing items to kitchen: ' + ERROR_MESSAGE();
        SELECT 0 AS KitchenTicketId, '' AS TicketNumber, @Message AS [Message];
    END CATCH
END
GO
