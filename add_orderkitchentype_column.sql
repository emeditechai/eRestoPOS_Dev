-- Add OrderKitchenType column to Orders table if it doesn't exist
-- This column tracks whether an order was created from "Orders" (Foods) or "Bar" navigation
-- to ensure the correct item group is selected when viewing order details

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.Orders') 
    AND name = 'OrderKitchenType'
)
BEGIN
    PRINT 'Adding OrderKitchenType column to Orders table...'
    
    ALTER TABLE dbo.Orders
    ADD OrderKitchenType NVARCHAR(50) NULL;
    
    PRINT 'OrderKitchenType column added successfully.'
    
    -- Set default value to 'Foods' for existing orders without kitchen tickets
    UPDATE o
    SET o.OrderKitchenType = 'Foods'
    FROM dbo.Orders o
    WHERE o.OrderKitchenType IS NULL;
    
    PRINT 'Default OrderKitchenType set to ''Foods'' for existing orders.'
    
    -- Update orders with BAR/BOT kitchen tickets to have OrderKitchenType = 'Bar'
    UPDATE o
    SET o.OrderKitchenType = 'Bar'
    FROM dbo.Orders o
    WHERE EXISTS (
        SELECT 1 
        FROM KitchenTickets kt
        WHERE kt.OrderId = o.Id 
        AND (kt.KitchenStation = 'BAR' OR kt.TicketNumber LIKE 'BOT-%')
    )
    AND o.OrderKitchenType = 'Foods'; -- Only update if currently set to Foods
    
    PRINT 'OrderKitchenType updated to ''Bar'' for orders with bar tickets.'
END
ELSE
BEGIN
    PRINT 'OrderKitchenType column already exists in Orders table.'
END
GO
