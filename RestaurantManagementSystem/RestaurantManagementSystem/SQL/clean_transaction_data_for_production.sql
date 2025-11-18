-- ============================================================================
-- Script: Clean Transaction Data for Production Deployment
-- Description: Removes all transactional data while preserving master data
-- Author: System
-- Date: 2025-11-18
-- ============================================================================
-- WARNING: This script will DELETE transaction data. Use with caution!
-- Recommended: Take a full database backup before running this script
-- ============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY
    PRINT '========================================';
    PRINT 'Starting Transaction Data Cleanup';
    PRINT 'Date: ' + CONVERT(VARCHAR, GETDATE(), 120);
    PRINT '========================================';
    PRINT '';

    -- ============================================================================
    -- 1. Clean Audit Trail Data
    -- ============================================================================
    PRINT '1. Cleaning Audit Trail Data...';
    IF OBJECT_ID('OrderAuditTrail', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE OrderAuditTrail;
        PRINT '   - OrderAuditTrail: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 2. Clean Payment Related Data (Must delete child first due to FK)
    -- ============================================================================
    PRINT '';
    PRINT '2. Cleaning Payment Data...';
    
    IF OBJECT_ID('PaymentSplits', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE PaymentSplits;
        PRINT '   - PaymentSplits: Truncated (identity reset)';
    END

    IF OBJECT_ID('Payments', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE Payments;
        PRINT '   - Payments: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 3. Clean Kitchen and Bar Ticket Data (Child tables first)
    -- ============================================================================
    PRINT '';
    PRINT '3. Cleaning Kitchen/Bar Ticket Data...';
    
    IF OBJECT_ID('KitchenTicketItems', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE KitchenTicketItems;
        PRINT '   - KitchenTicketItems: Truncated (identity reset)';
    END

    IF OBJECT_ID('KitchenTickets', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE KitchenTickets;
        PRINT '   - KitchenTickets (KOT): Truncated (identity reset)';
    END

    IF OBJECT_ID('BarTicketItems', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE BarTicketItems;
        PRINT '   - BarTicketItems: Truncated (identity reset)';
    END

    IF OBJECT_ID('BarTickets', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE BarTickets;
        PRINT '   - BarTickets (BOT): Truncated (identity reset)';
    END

    -- ============================================================================
    -- 4. Clean Order Related Data (Child tables first)
    -- ============================================================================
    PRINT '';
    PRINT '4. Cleaning Order Data...';
    
    IF OBJECT_ID('OrderItemModifiers', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE OrderItemModifiers;
        PRINT '   - OrderItemModifiers: Truncated (identity reset)';
    END

    IF OBJECT_ID('OrderItems', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE OrderItems;
        PRINT '   - OrderItems: Truncated (identity reset)';
    END

    IF OBJECT_ID('OrderTables', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE OrderTables;
        PRINT '   - OrderTables: Truncated (identity reset)';
    END

    IF OBJECT_ID('Orders', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE Orders;
        PRINT '   - Orders: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 5. Clean Table and Reservation Data
    -- ============================================================================
    PRINT '';
    PRINT '5. Cleaning Table Turnover and Reservation Data...';
    
    IF OBJECT_ID('TableTurnovers', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE TableTurnovers;
        PRINT '   - TableTurnovers: Truncated (identity reset)';
    END

    IF OBJECT_ID('Reservations', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE Reservations;
        PRINT '   - Reservations: Truncated (identity reset)';
    END

    IF OBJECT_ID('Waitlist', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE Waitlist;
        PRINT '   - Waitlist: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 6. Clean Feedback Data (Child table first)
    -- ============================================================================
    PRINT '';
    PRINT '6. Cleaning Feedback Data...';
    
    IF OBJECT_ID('FeedbackResponses', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE FeedbackResponses;
        PRINT '   - FeedbackResponses: Truncated (identity reset)';
    END

    IF OBJECT_ID('Feedback', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE Feedback;
        PRINT '   - Feedback: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 7. Clean Day Closing Data
    -- ============================================================================
    PRINT '';
    PRINT '7. Cleaning Day Closing Data...';
    
    IF OBJECT_ID('DayClosingRecords', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE DayClosingRecords;
        PRINT '   - DayClosingRecords: Truncated (identity reset)';
    END

    IF OBJECT_ID('CashierClosingRecords', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE CashierClosingRecords;
        PRINT '   - CashierClosingRecords: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 8. Clean Online Order Data (Child table first)
    -- ============================================================================
    PRINT '';
    PRINT '8. Cleaning Online Order Data...';
    
    IF OBJECT_ID('OnlineOrderItems', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE OnlineOrderItems;
        PRINT '   - OnlineOrderItems: Truncated (identity reset)';
    END

    IF OBJECT_ID('OnlineOrders', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE OnlineOrders;
        PRINT '   - OnlineOrders: Truncated (identity reset)';
    END

    -- ============================================================================
    -- 9. Reset Table Status
    -- ============================================================================
    PRINT '';
    PRINT '9. Resetting Table Status...';
    
    IF OBJECT_ID('Tables', 'U') IS NOT NULL
    BEGIN
        UPDATE Tables 
        SET Status = 0, -- Available
            CurrentOrder = NULL,
            LastOccupied = NULL;
        PRINT '   - Tables: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' tables reset to available';
    END

    -- ============================================================================
    -- 10. Clear System Audit Logs
    -- ============================================================================
    PRINT '';
    PRINT '10. Cleaning System Audit Logs...';
    
    IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
    BEGIN
        TRUNCATE TABLE AuditLogs;
        PRINT '   - AuditLogs: Truncated (identity reset)';
    END

    -- ============================================================================
    -- Commit Transaction
    -- ============================================================================
    COMMIT TRANSACTION;
    
    PRINT '';
    PRINT '========================================';
    PRINT 'Transaction Data Cleanup COMPLETED';
    PRINT 'Status: SUCCESS';
    PRINT 'Date: ' + CONVERT(VARCHAR, GETDATE(), 120);
    PRINT '========================================';
    PRINT '';
    PRINT 'ALL IDENTITY COLUMNS AUTOMATICALLY RESET BY TRUNCATE';
    PRINT '';
    PRINT 'MASTER DATA PRESERVED:';
    PRINT '- Users and Roles';
    PRINT '- Menu Items and Categories';
    PRINT '- Tables Configuration';
    PRINT '- Restaurant Settings';
    PRINT '- Bank Masters';
    PRINT '- Kitchen Stations';
    PRINT '- Navigation Menus and Permissions';
    PRINT '';
    PRINT 'Database is ready for production deployment!';

END TRY
BEGIN CATCH
    -- ============================================================================
    -- Error Handling
    -- ============================================================================
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    
    PRINT '';
    PRINT '========================================';
    PRINT 'ERROR: Transaction Data Cleanup FAILED';
    PRINT '========================================';
    PRINT 'Error Number: ' + CAST(ERROR_NUMBER() AS VARCHAR);
    PRINT 'Error Message: ' + ERROR_MESSAGE();
    PRINT 'Error Line: ' + CAST(ERROR_LINE() AS VARCHAR);
    PRINT '';
    PRINT 'Transaction has been rolled back.';
    PRINT 'No data was deleted.';
    PRINT '';
    
    -- Re-throw the error
    THROW;
END CATCH;

SET NOCOUNT OFF;
GO
