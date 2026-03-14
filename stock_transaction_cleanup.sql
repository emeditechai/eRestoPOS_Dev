-- =============================================================================
-- STOCK TRANSACTION CLEANUP SCRIPT
-- Generated : 14 March 2026
-- Purpose   : Remove ALL inventory transactional data while preserving
--             every master/configuration record.
--
-- CLEARS   : Purchase Orders, GRN, Stock Transfers, Damage Entries,
--             Opening Stock, Stock Ledger, Current Stock,
--             BOM Lines (MenuItemIngredients), BOM Headers (Recipes),
--             PO Number Sequence counter
--
-- KEEPS     : UomMaster, StockItemCategories, Godowns, Parties (supplier master),
--             InventoryParameters, MenuItems, Categories, all other master tables
--
-- NOTE      : Ingredients IS cleaned — it is per-deployment setup data.
--             UomMaster and Godowns are kept as they are true master config.
--
-- ⚠️  RUN ON PRODUCTION WITH CAUTION  ⚠️
-- Take a full database backup before executing.
-- Execute in one transaction — rolls back entirely if anything fails.
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ---------------------------------------------------------------------------
-- SAFETY GUARD 1: BLOCK execution on the DEV database entirely.
--   This script must NEVER run on dev_Restaurant.
--   It will hard-abort if the connected DB name matches.
-- ---------------------------------------------------------------------------
IF DB_NAME() = 'dev_Restaurant'
BEGIN
    RAISERROR(
        '🚫 BLOCKED: This script is NOT allowed to run on the DEV database (dev_Restaurant). Connect to the Production database and try again.',
        20, 1) WITH LOG;
END
GO

-- ---------------------------------------------------------------------------
-- SAFETY GUARD 2: Explicit confirmation token.
--   Change the value below from 'NO' to 'YES_I_HAVE_A_BACKUP'
--   only after taking a full backup of the production database.
-- ---------------------------------------------------------------------------
DECLARE @ConfirmClean NVARCHAR(30) = 'NO';   -- ← change to 'YES_I_HAVE_A_BACKUP'

IF @ConfirmClean <> 'YES_I_HAVE_A_BACKUP'
BEGIN
    RAISERROR(
        '🚫 BLOCKED: Set @ConfirmClean = ''YES_I_HAVE_A_BACKUP'' after taking a full DB backup.',
        16, 1);
    RETURN;
END
GO

-- ---------------------------------------------------------------------------
-- SAFETY GUARD 3: Print target DB and server so you can visually confirm.
-- ---------------------------------------------------------------------------
PRINT '=================================================';
PRINT ' TARGET SERVER  : ' + @@SERVERNAME;
PRINT ' TARGET DATABASE: ' + DB_NAME();
PRINT ' EXECUTED BY    : ' + SUSER_SNAME();
PRINT ' TIME           : ' + CONVERT(VARCHAR, GETDATE(), 120);
PRINT '=================================================';
GO

BEGIN TRANSACTION;
BEGIN TRY

    PRINT '======================================================';
    PRINT ' Stock Transaction Cleanup — Started: ' + CONVERT(VARCHAR, GETDATE(), 120);
    PRINT '======================================================';

    -- =========================================================
    -- STEP 1: GRN Details  (child of GRNMaster)
    -- =========================================================
    PRINT 'Step 1: Deleting GRNDetails ...';
    DELETE FROM dbo.GRNDetails;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 2: GRN Master  (references PurchaseOrder.POId)
    -- =========================================================
    PRINT 'Step 2: Deleting GRNMaster ...';
    DELETE FROM dbo.GRNMaster;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 3: Purchase Order Details  (child of PurchaseOrder)
    -- =========================================================
    PRINT 'Step 3: Deleting PurchaseOrderDetails ...';
    DELETE FROM dbo.PurchaseOrderDetails;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 4: Purchase Orders
    -- =========================================================
    PRINT 'Step 4: Deleting PurchaseOrder ...';
    DELETE FROM dbo.PurchaseOrder;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- Reset PO identity seed
    DBCC CHECKIDENT ('dbo.PurchaseOrder', RESEED, 0);

    -- =========================================================
    -- STEP 5: Stock Transfer Details  (child of StockTransfer)
    -- =========================================================
    PRINT 'Step 5: Deleting StockTransferDetails ...';
    DELETE FROM dbo.StockTransferDetails;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 6: Stock Transfers
    -- =========================================================
    PRINT 'Step 6: Deleting StockTransfer ...';
    DELETE FROM dbo.StockTransfer;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.StockTransfer', RESEED, 0);

    -- =========================================================
    -- STEP 7: Damage Entry Details  (child of DamageEntry)
    -- =========================================================
    PRINT 'Step 7: Deleting DamageEntryDetails ...';
    DELETE FROM dbo.DamageEntryDetails;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 8: Damage Entries
    -- =========================================================
    PRINT 'Step 8: Deleting DamageEntry ...';
    DELETE FROM dbo.DamageEntry;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.DamageEntry', RESEED, 0);

    -- =========================================================
    -- STEP 9: Opening Stock
    -- =========================================================
    PRINT 'Step 9: Deleting OpeningStock ...';
    DELETE FROM dbo.OpeningStock;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.OpeningStock', RESEED, 0);

    -- =========================================================
    -- STEP 10: Stock Ledger  (all movement history)
    -- =========================================================
    PRINT 'Step 10: Deleting StockLedger ...';
    DELETE FROM dbo.StockLedger;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.StockLedger', RESEED, 0);

    -- =========================================================
    -- STEP 11: Current Stock  (live balance snapshot)
    -- =========================================================
    PRINT 'Step 11: Deleting CurrentStock ...';
    DELETE FROM dbo.CurrentStock;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.CurrentStock', RESEED, 0);

    -- =========================================================
    -- STEP 12: BOM Lines  (MenuItemIngredients)
    --          Maps menu items → ingredients with quantities
    -- =========================================================
    PRINT 'Step 12: Deleting MenuItemIngredients (BOM lines) ...';
    DELETE FROM dbo.MenuItemIngredients;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.MenuItemIngredients', RESEED, 0);

    -- =========================================================
    -- STEP 13: Recipe Steps  (child of Recipes)
    -- =========================================================
    PRINT 'Step 13: Deleting RecipeSteps ...';
    DELETE FROM dbo.RecipeSteps;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 14: BOM / Recipe Headers  (Recipes)
    --          Stores yield %, preparation time, computed cost
    --          per menu item — rebuilt when BOM lines are entered
    -- =========================================================
    PRINT 'Step 14: Deleting Recipes (BOM headers) ...';
    DELETE FROM dbo.Recipes;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- STEP 15: Ingredients (item master per deployment)
    --          All referencing transactional tables are already cleared above.
    --          Safe to delete now. UomMaster and Godowns are intentionally kept.
    -- =========================================================
    PRINT 'Step 15: Deleting Ingredients ...';
    DELETE FROM dbo.Ingredients;
    PRINT '  Rows deleted: ' + CAST(@@ROWCOUNT AS VARCHAR);

    DBCC CHECKIDENT ('dbo.Ingredients', RESEED, 0);

    -- =========================================================
    -- STEP 16: Reset PO Number Sequence counter
    --          PONumberSequence stores (FYCode, LastSeq)
    --          Reset LastSeq to 0 so numbering restarts from PO-001
    -- =========================================================
    PRINT 'Step 16: Resetting PONumberSequence counter ...';
    UPDATE dbo.PONumberSequence SET LastSeq = 0;
    -- If you want to remove old financial year rows entirely:
    -- DELETE FROM dbo.PONumberSequence;
    PRINT '  Rows updated: ' + CAST(@@ROWCOUNT AS VARCHAR);

    -- =========================================================
    -- VERIFICATION summary before commit
    -- =========================================================
    PRINT '';
    PRINT '--- Verification: all transactional tables should show 0 ---';

    SELECT 'GRNDetails'          AS TableName, COUNT(*) AS Remaining FROM dbo.GRNDetails          UNION ALL
    SELECT 'GRNMaster'           AS TableName, COUNT(*) AS Remaining FROM dbo.GRNMaster           UNION ALL
    SELECT 'PurchaseOrderDetails',              COUNT(*)              FROM dbo.PurchaseOrderDetails UNION ALL
    SELECT 'PurchaseOrder',                     COUNT(*)              FROM dbo.PurchaseOrder        UNION ALL
    SELECT 'StockTransferDetails',              COUNT(*)              FROM dbo.StockTransferDetails UNION ALL
    SELECT 'StockTransfer',                     COUNT(*)              FROM dbo.StockTransfer        UNION ALL
    SELECT 'DamageEntryDetails',                COUNT(*)              FROM dbo.DamageEntryDetails   UNION ALL
    SELECT 'DamageEntry',                       COUNT(*)              FROM dbo.DamageEntry          UNION ALL
    SELECT 'OpeningStock',                      COUNT(*)              FROM dbo.OpeningStock         UNION ALL
    SELECT 'StockLedger',                       COUNT(*)              FROM dbo.StockLedger          UNION ALL
    SELECT 'CurrentStock',                      COUNT(*)              FROM dbo.CurrentStock         UNION ALL
    SELECT 'MenuItemIngredients',               COUNT(*)              FROM dbo.MenuItemIngredients  UNION ALL
    SELECT 'RecipeSteps',                       COUNT(*)              FROM dbo.RecipeSteps          UNION ALL
    SELECT 'Recipes',                           COUNT(*)              FROM dbo.Recipes             UNION ALL
    SELECT 'Ingredients',                       COUNT(*)              FROM dbo.Ingredients;

    PRINT '';
    PRINT '--- Verification: master tables should be UNCHANGED ---';

    SELECT 'UomMaster'           AS TableName, COUNT(*) AS Remaining FROM dbo.UomMaster           UNION ALL
    SELECT 'StockItemCategories',               COUNT(*)              FROM dbo.StockItemCategories  UNION ALL
    SELECT 'Godowns',                           COUNT(*)              FROM dbo.Godowns              UNION ALL
    SELECT 'Parties',                           COUNT(*)              FROM dbo.Parties              UNION ALL
    SELECT 'InventoryParameters',               COUNT(*)              FROM dbo.InventoryParameters  UNION ALL
    SELECT 'MenuItems',                         COUNT(*)              FROM dbo.MenuItems;

    -- =========================================================
    -- COMMIT
    -- =========================================================
    COMMIT TRANSACTION;
    PRINT '';
    PRINT '======================================================';
    PRINT ' ✅ Cleanup COMMITTED successfully.';
    PRINT ' Timestamp: ' + CONVERT(VARCHAR, GETDATE(), 120);
    PRINT '======================================================';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT '';
    PRINT '======================================================';
    PRINT ' ❌ ERROR — Transaction ROLLED BACK. Nothing was changed.';
    PRINT ' Error ' + CAST(ERROR_NUMBER() AS VARCHAR) + ': ' + ERROR_MESSAGE();
    PRINT ' Line: ' + CAST(ERROR_LINE() AS VARCHAR);
    PRINT '======================================================';
    -- Re-throw so calling tool also sees the error
    THROW;
END CATCH;
GO
