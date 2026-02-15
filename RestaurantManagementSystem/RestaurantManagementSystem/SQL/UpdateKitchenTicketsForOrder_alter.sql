CREATE OR ALTER PROCEDURE [dbo].[UpdateKitchenTicketsForOrder]
    @OrderId INT
AS
BEGIN
    DECLARE @OrderNumber NVARCHAR(20);
    DECLARE @TableName NVARCHAR(50) = NULL;
    DECLARE @OrderBranchId INT = NULL;
    
    -- Get order information
    SELECT 
        @OrderNumber = OrderNumber,
        @TableName = t.TableName,
        @OrderBranchId = o.BranchId
    FROM 
        Orders o
    LEFT JOIN 
        TableTurnovers tt ON o.TableTurnoverId = tt.Id
    LEFT JOIN 
        Tables t ON tt.TableId = t.Id
    WHERE 
        o.Id = @OrderId;
    
    -- Ensure we have a non-null OrderNumber. If the Orders row has NULL (migrated DB differences),
    -- try a direct SELECT with NOLOCK and finally fall back to a generated value based on OrderId.
    IF @OrderNumber IS NULL OR LTRIM(RTRIM(@OrderNumber)) = ''
    BEGIN
        SELECT @OrderNumber = OrderNumber FROM Orders WITH (NOLOCK) WHERE Id = @OrderId;
    END

    IF @OrderNumber IS NULL OR LTRIM(RTRIM(@OrderNumber)) = ''
    BEGIN
        -- Fallback: generate an order number from the OrderId to avoid NULL inserts into KitchenTickets
        SET @OrderNumber = 'ORD-' + RIGHT('00000' + CAST(@OrderId AS VARCHAR(10)), 5);
    END
    
    -- Process items by kitchen station
    DECLARE @StationId INT;
    DECLARE @StationName NVARCHAR(50);
    
    -- Get distinct kitchen stations for items in this order
    DECLARE station_cursor CURSOR FOR
    SELECT DISTINCT 
        ks.Id,
        ks.Name
    FROM 
        OrderItems oi
    INNER JOIN 
        MenuItems mi ON oi.MenuItemId = mi.Id
    INNER JOIN 
        MenuItemKitchenStations miks ON mi.Id = miks.MenuItemId
    INNER JOIN 
        KitchenStations ks ON miks.KitchenStationId = ks.Id
    WHERE 
        oi.OrderId = @OrderId
        AND oi.Status < 5 -- Not cancelled
        AND ks.IsActive = 1;
    
    OPEN station_cursor;
    FETCH NEXT FROM station_cursor INTO @StationId, @StationName;
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Check if ticket already exists for this order and station
        DECLARE @TicketId INT = NULL;
        
        SELECT @TicketId = Id
        FROM KitchenTickets
        WHERE OrderId = @OrderId AND KitchenStationId = @StationId;
        
        IF @TicketId IS NULL
        BEGIN
            DECLARE @StationTicketNumber NVARCHAR(20);

            SELECT @StationTicketNumber =
                'KOT-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + '-' +
                RIGHT('0000' + CAST(
                    ISNULL(MAX(TRY_CAST(RIGHT(kt.TicketNumber, 4) AS INT)), 0) + 1
                AS NVARCHAR(4)), 4)
            FROM KitchenTickets kt WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN Orders o2 ON o2.Id = kt.OrderId
            WHERE LEFT(kt.TicketNumber, 12) = 'KOT-' + CONVERT(NVARCHAR(8), GETDATE(), 112)
              AND ((@OrderBranchId IS NULL AND o2.BranchId IS NULL) OR o2.BranchId = @OrderBranchId);

            -- Create new ticket for this station
            INSERT INTO KitchenTickets (
                TicketNumber,
                OrderId,
                OrderNumber,
                KitchenStationId,
                StationName,
                TableName,
                Status,
                CreatedAt
            )
            VALUES (
                @StationTicketNumber,
                @OrderId,
                @OrderNumber,
                @StationId,
                @StationName,
                @TableName,
                0, -- New status
                GETDATE()
            );
            
            SET @TicketId = SCOPE_IDENTITY();
        END
        
        -- Process items for this station
        INSERT INTO KitchenTicketItems (
            KitchenTicketId,
            OrderItemId,
            MenuItemName,
            Quantity,
            SpecialInstructions,
            Status,
            KitchenStationId,
            StationName,
            PrepTime
        )
        SELECT 
            @TicketId,
            oi.Id,
            mi.Name,
            oi.Quantity,
            oi.SpecialInstructions,
            0, -- New status
            @StationId,
            @StationName,
            ISNULL(mi.PrepTime, 10) -- Default 10 minutes if not specified
        FROM 
            OrderItems oi
        INNER JOIN 
            MenuItems mi ON oi.MenuItemId = mi.Id
        INNER JOIN 
            MenuItemKitchenStations miks ON mi.Id = miks.MenuItemId
        WHERE 
            oi.OrderId = @OrderId
            AND miks.KitchenStationId = @StationId
            AND oi.Status < 5 -- Not cancelled
            AND NOT EXISTS (
                -- Don't duplicate items already in kitchen ticket
                SELECT 1
                FROM KitchenTicketItems kti
                WHERE kti.OrderItemId = oi.Id AND kti.KitchenTicketId = @TicketId
            );
        
        -- Add modifiers to kitchen ticket items
        INSERT INTO KitchenTicketItemModifiers (
            KitchenTicketItemId,
            ModifierText
        )
        SELECT 
            kti.Id,
            m.Name + ' (' + oimv.Value + ')'
        FROM 
            OrderItems oi
        INNER JOIN 
            OrderItemModifierValues oimv ON oi.Id = oimv.OrderItemId
        INNER JOIN 
            Modifiers m ON oimv.ModifierId = m.Id
        INNER JOIN 
            KitchenTicketItems kti ON oi.Id = kti.OrderItemId
        WHERE 
            oi.OrderId = @OrderId
            AND kti.KitchenTicketId = @TicketId
            AND NOT EXISTS (
                -- Don't duplicate modifiers
                SELECT 1
                FROM KitchenTicketItemModifiers ktim
                WHERE ktim.KitchenTicketItemId = kti.Id AND ktim.ModifierText = m.Name + ' (' + oimv.Value + ')'
            );
        
        FETCH NEXT FROM station_cursor INTO @StationId, @StationName;
    END
    
    CLOSE station_cursor;
    DEALLOCATE station_cursor;
    
    -- Check if there are items with no assigned kitchen station
    -- Create a general ticket for these items
    IF EXISTS (
        SELECT 1
        FROM OrderItems oi
        WHERE oi.OrderId = @OrderId
        AND oi.Status < 5 -- Not cancelled
        AND NOT EXISTS (
            SELECT 1 
            FROM KitchenTicketItems kti
            INNER JOIN KitchenTickets kt ON kti.KitchenTicketId = kt.Id
            WHERE kti.OrderItemId = oi.Id
        )
    )
    BEGIN
        -- Check if general ticket already exists
        DECLARE @GeneralTicketId INT = NULL;
        
        SELECT @GeneralTicketId = Id
        FROM KitchenTickets
        WHERE OrderId = @OrderId AND KitchenStationId IS NULL;
        
        IF @GeneralTicketId IS NULL
        BEGIN
            DECLARE @GeneralTicketNumber NVARCHAR(20);

            SELECT @GeneralTicketNumber =
                'KOT-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + '-' +
                RIGHT('0000' + CAST(
                    ISNULL(MAX(TRY_CAST(RIGHT(kt.TicketNumber, 4) AS INT)), 0) + 1
                AS NVARCHAR(4)), 4)
            FROM KitchenTickets kt WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN Orders o2 ON o2.Id = kt.OrderId
            WHERE LEFT(kt.TicketNumber, 12) = 'KOT-' + CONVERT(NVARCHAR(8), GETDATE(), 112)
              AND ((@OrderBranchId IS NULL AND o2.BranchId IS NULL) OR o2.BranchId = @OrderBranchId);

            -- Create general ticket
            INSERT INTO KitchenTickets (
                TicketNumber,
                OrderId,
                OrderNumber,
                KitchenStationId,
                StationName,
                TableName,
                Status,
                CreatedAt
            )
            VALUES (
                @GeneralTicketNumber,
                @OrderId,
                @OrderNumber,
                NULL, -- No specific station
                'General Kitchen',
                @TableName,
                0, -- New status
                GETDATE()
            );
            
            SET @GeneralTicketId = SCOPE_IDENTITY();
        END
        
        -- Process unassigned items
        INSERT INTO KitchenTicketItems (
            KitchenTicketId,
            OrderItemId,
            MenuItemName,
            Quantity,
            SpecialInstructions,
            Status,
            KitchenStationId,
            StationName,
            PrepTime
        )
        SELECT 
            @GeneralTicketId,
            oi.Id,
            mi.Name,
            oi.Quantity,
            oi.SpecialInstructions,
            0, -- New status
            NULL, -- No specific station
            'General Kitchen',
            ISNULL(mi.PrepTime, 10) -- Default 10 minutes if not specified
        FROM 
            OrderItems oi
        INNER JOIN 
            MenuItems mi ON oi.MenuItemId = mi.Id
        WHERE 
            oi.OrderId = @OrderId
            AND oi.Status < 5 -- Not cancelled
            AND NOT EXISTS (
                -- Only include items not already assigned to a station
                SELECT 1 
                FROM KitchenTicketItems kti
                INNER JOIN KitchenTickets kt ON kti.KitchenTicketId = kt.Id
                WHERE kti.OrderItemId = oi.Id
            );
        
        -- Add modifiers to general kitchen ticket items
        INSERT INTO KitchenTicketItemModifiers (
            KitchenTicketItemId,
            ModifierText
        )
        SELECT 
            kti.Id,
            m.Name + ' (' + oimv.Value + ')'
        FROM 
            OrderItems oi
        INNER JOIN 
            OrderItemModifierValues oimv ON oi.Id = oimv.OrderItemId
        INNER JOIN 
            Modifiers m ON oimv.ModifierId = m.Id
        INNER JOIN 
            KitchenTicketItems kti ON oi.Id = kti.OrderItemId
        WHERE 
            oi.OrderId = @OrderId
            AND kti.KitchenTicketId = @GeneralTicketId
            AND NOT EXISTS (
                -- Don't duplicate modifiers
                SELECT 1
                FROM KitchenTicketItemModifiers ktim
                WHERE ktim.KitchenTicketItemId = kti.Id AND ktim.ModifierText = m.Name + ' (' + oimv.Value + ')'
            );
    END
END
GO
