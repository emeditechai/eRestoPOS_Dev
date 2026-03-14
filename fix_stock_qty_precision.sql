-- ============================================================
-- fix_stock_qty_precision.sql
-- Expands stock quantity columns from decimal(18,3) to decimal(18,6)
-- so that sub-gram purchases (e.g. KG→MG factor 1,000,000) are
-- stored correctly and not rounded to zero.
--
-- Affected tables:
--   CurrentStock  : BalanceQty  decimal(18,3) → decimal(18,6)
--   StockLedger   : InQuantity  decimal(18,3) → decimal(18,6)
--                   OutQuantity decimal(18,3) → decimal(18,6)
--                   BalanceQty  decimal(18,3) → decimal(18,6)
--
-- Computed columns dropped & recreated (they reference qty fields):
--   CurrentStock.StockValue  = BalanceQty * AverageCost
--   StockLedger.TotalValue   = (InQuantity - OutQuantity) * UnitCost
-- ============================================================

-- ───────────────────────────────────────────────────────────────
-- 1. CurrentStock
-- ───────────────────────────────────────────────────────────────
-- Drop computed column that depends on BalanceQty
IF COL_LENGTH('dbo.CurrentStock', 'StockValue') IS NOT NULL
    ALTER TABLE dbo.CurrentStock DROP COLUMN StockValue;

-- Expand BalanceQty precision
ALTER TABLE dbo.CurrentStock
    ALTER COLUMN BalanceQty DECIMAL(18, 6) NOT NULL;

-- Recreate computed column
ALTER TABLE dbo.CurrentStock
    ADD StockValue AS (BalanceQty * AverageCost);

-- ───────────────────────────────────────────────────────────────
-- 2. StockLedger
-- ───────────────────────────────────────────────────────────────
-- Drop computed column that depends on InQuantity / OutQuantity
IF COL_LENGTH('dbo.StockLedger', 'TotalValue') IS NOT NULL
    ALTER TABLE dbo.StockLedger DROP COLUMN TotalValue;

-- Expand qty columns
ALTER TABLE dbo.StockLedger
    ALTER COLUMN InQuantity  DECIMAL(18, 6) NOT NULL;

ALTER TABLE dbo.StockLedger
    ALTER COLUMN OutQuantity DECIMAL(18, 6) NOT NULL;

ALTER TABLE dbo.StockLedger
    ALTER COLUMN BalanceQty  DECIMAL(18, 6) NOT NULL;

-- Recreate computed column
ALTER TABLE dbo.StockLedger
    ADD TotalValue AS ((InQuantity - OutQuantity) * UnitCost);

-- ───────────────────────────────────────────────────────────────
-- 3. Verify
-- ───────────────────────────────────────────────────────────────
SELECT
    t.name      AS TableName,
    c.name      AS ColumnName,
    tp.name     AS DataType,
    c.precision AS Precision,
    c.scale     AS Scale,
    c.is_computed
FROM sys.columns c
JOIN sys.tables  t  ON t.object_id = c.object_id
JOIN sys.types   tp ON tp.user_type_id = c.user_type_id
WHERE t.name IN ('CurrentStock','StockLedger')
  AND c.name IN ('BalanceQty','InQuantity','OutQuantity','StockValue','TotalValue')
ORDER BY t.name, c.name;
GO
