-- =====================================================
-- Day Closing Diagnostic Queries
-- Use these to troubleshoot System Amount showing ₹0.00
-- Database: dev_Restaurant (NOT RestaurantDB)
-- =====================================================

USE [dev_Restaurant];
GO

-- =====================================================
-- 1. Check if Day Closing tables exist
-- =====================================================
PRINT '=== Checking Day Closing Tables ===';
SELECT name, create_date 
FROM sys.tables 
WHERE name IN ('CashierDayOpening', 'CashierDayClose', 'DayLockAudit')
ORDER BY name;
GO

-- =====================================================
-- 2. Check if CashierId column exists in Orders table
-- =====================================================
PRINT '';
PRINT '=== Checking CashierId Column ===';
SELECT 
    c.name AS ColumnName,
    t.name AS DataType,
    c.is_nullable AS IsNullable
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID('Orders') 
  AND c.name = 'CashierId';
GO

-- =====================================================
-- 3. Check today's orders and CashierId population
-- =====================================================
PRINT '';
PRINT '=== Orders for Today (2025-11-09) ===';
SELECT 
    COUNT(*) AS TotalOrders,
    COUNT(CashierId) AS OrdersWithCashier,
    COUNT(*) - COUNT(CashierId) AS OrdersWithoutCashier,
    SUM(CASE WHEN CashierId IS NOT NULL THEN TotalAmount ELSE 0 END) AS AmountWithCashier,
    SUM(CASE WHEN CashierId IS NULL THEN TotalAmount ELSE 0 END) AS AmountWithoutCashier
FROM Orders
WHERE CAST(CreatedAt AS DATE) = '2025-11-09';
GO

-- =====================================================
-- 4. Check payment methods
-- =====================================================
PRINT '';
PRINT '=== Payment Methods ===';
SELECT Id, Name, Description
FROM PaymentMethods
ORDER BY Name;
GO

-- =====================================================
-- 5. Check today's CASH payments by cashier
-- =====================================================
PRINT '';
PRINT '=== Cash Payments by Cashier (2025-11-09) ===';
SELECT 
    o.CashierId,
    u.Username AS CashierName,
    COUNT(DISTINCT o.Id) AS OrderCount,
    COUNT(p.Id) AS PaymentCount,
    SUM(p.Amount) AS TotalCashCollected
FROM Orders o
LEFT JOIN Users u ON u.Id = o.CashierId
LEFT JOIN Payments p ON p.OrderId = o.Id
LEFT JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
WHERE CAST(o.CreatedAt AS DATE) = '2025-11-09'
  AND pm.Name = 'CASH'
  AND p.Status = 1
  AND o.Status IN (2, 3)
GROUP BY o.CashierId, u.Username
ORDER BY u.Username;
GO

-- =====================================================
-- 6. Check CashierDayClose records for today
-- =====================================================
PRINT '';
PRINT '=== Day Closing Records (2025-11-09) ===';
SELECT 
    CashierId,
    CashierName,
    OpeningFloat,
    SystemAmount,
    DeclaredAmount,
    Variance,
    Status,
    CreatedAt
FROM CashierDayClose
WHERE BusinessDate = '2025-11-09'
ORDER BY CashierName;
GO

-- =====================================================
-- 7. Sample orders with payment details
-- =====================================================
PRINT '';
PRINT '=== Sample Orders Today (First 10) ===';
SELECT TOP 10
    o.Id,
    o.OrderNumber,
    o.CashierId,
    u.Username AS CashierName,
    o.TotalAmount,
    o.Status,
    pm.Name AS PaymentMethod,
    p.Amount AS PaymentAmount,
    p.Status AS PaymentStatus,
    o.CreatedAt
FROM Orders o
LEFT JOIN Users u ON u.Id = o.CashierId
LEFT JOIN Payments p ON p.OrderId = o.Id
LEFT JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
WHERE CAST(o.CreatedAt AS DATE) = '2025-11-09'
ORDER BY o.CreatedAt DESC;
GO

-- =====================================================
-- 8. Get user IDs for testing (cashiers)
-- =====================================================
PRINT '';
PRINT '=== Active Users (Potential Cashiers) ===';
SELECT 
    u.Id,
    u.Username,
    r.Name AS RoleName
FROM Users u
LEFT JOIN UserRoles ur ON ur.UserId = u.Id
LEFT JOIN Roles r ON r.Id = ur.RoleId
WHERE u.IsActive = 1
ORDER BY u.Username;
GO

-- =====================================================
-- 9. AUTOMATIC FIX: Set default cashier for orders without CashierId
--    This makes the system work immediately
-- =====================================================
PRINT '';
PRINT '=== AUTO-ASSIGNING ORDERS TO CASHIERS ===';

-- Get the first active Administrator user as default
DECLARE @DefaultCashierId INT;
SELECT TOP 1 @DefaultCashierId = u.Id
FROM Users u
INNER JOIN UserRoles ur ON ur.UserId = u.Id
INNER JOIN Roles r ON r.Id = ur.RoleId
WHERE u.IsActive = 1 AND r.Name = 'Administrator'
ORDER BY u.Id;

IF @DefaultCashierId IS NOT NULL
BEGIN
    -- Update today's orders without cashier
    UPDATE Orders
    SET CashierId = @DefaultCashierId
    WHERE CAST(CreatedAt AS DATE) = '2025-11-09'
      AND CashierId IS NULL;
    
    PRINT 'Orders updated with default cashier ID: ' + CAST(@DefaultCashierId AS VARCHAR);
    PRINT 'Affected rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
END
ELSE
BEGIN
    PRINT 'WARNING: No active Administrator found. Please manually assign CashierId.';
END
GO

-- =====================================================
-- 10. Test the stored procedure query
-- =====================================================
PRINT '';
PRINT '=== Testing System Amount Query ===';
PRINT 'Replace @CashierId with actual cashier ID';
PRINT '';

-- Example with CashierId = 2 (replace with your cashier ID)
DECLARE @BusinessDate DATE = '2025-11-09';
DECLARE @CashierId INT = 2;  -- CHANGE THIS

SELECT 
    ISNULL(SUM(p.Amount), 0) AS SystemAmount
FROM Orders o
INNER JOIN Payments p ON p.OrderId = o.Id
INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
  AND o.CashierId = @CashierId
  AND pm.Name = 'CASH'
  AND p.Status = 1
  AND o.Status IN (2, 3);
GO

PRINT '';
PRINT '=== Diagnostic Complete ===';
PRINT 'Review the results above to identify the issue.';
GO
