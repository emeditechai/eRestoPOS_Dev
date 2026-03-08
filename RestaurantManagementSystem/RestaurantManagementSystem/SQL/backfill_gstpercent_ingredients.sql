-- =============================================
-- Migration : backfill_gstpercent_ingredients.sql
-- Purpose   : Back-fill GSTPercent on dbo.Ingredients
--             Step 1 – Ensure column exists
--             Step 2 – Category-level default GST rates
--             Step 3 – Item-specific overrides
--             Step 4 – Verification summary
-- Database  : dev_Restaurant
-- Safe      : Idempotent — re-runnable without side effects
-- GST Slabs : 0% / 5% / 12% / 18% / 28%
--   CGST = GSTPercent / 2  (intra-state)
--   SGST = GSTPercent / 2  (intra-state)
--   IGST = GSTPercent      (inter-state)
-- =============================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 1 : Ensure GSTPercent column exists
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 1: Ensure GSTPercent column exists ===';

IF COL_LENGTH('dbo.Ingredients', 'GSTPercent') IS NULL
BEGIN
    ALTER TABLE dbo.Ingredients
        ADD GSTPercent DECIMAL(5,2) NOT NULL DEFAULT 0;
    PRINT '  GSTPercent column created.';
END
ELSE
    PRINT '  GSTPercent column already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 2 : Category-level default GST rates
--          Adjust percentages below to match your business requirements.
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 2: Apply category-level default GST rates ===';

-- Fresh Vegetables, Eggs, Salt — 0% (GST exempt under Schedule 1)
UPDATE dbo.Ingredients
SET    GSTPercent = 10
WHERE  ItemCategory IN ('Raw Materials')
  AND  IngredientsName LIKE '%Vegetable%'
   OR  IngredientsName LIKE '%Tomato%'
   OR  IngredientsName LIKE '%Potato%'
   OR  IngredientsName LIKE '%Egg%'
   OR  IngredientsName LIKE '%Salt%';

PRINT '  Applied 0% to fresh vegetables / eggs / salt.';

-- Edible Oils, Flour (Maida, Atta), Spices — 5%
UPDATE dbo.Ingredients
SET    GSTPercent = 5
WHERE  ItemCategory IN ('Raw Materials')
  AND (
       IngredientsName LIKE '%Oil%'
    OR IngredientsName LIKE '%Maida%'
    OR IngredientsName LIKE '%Atta%'
    OR IngredientsName LIKE '%Flour%'
    OR IngredientsName LIKE '%Spice%'
    OR IngredientsName LIKE '%Masala%'
  );

PRINT '  Applied 5% to oils / flour / spices.';

-- Dairy (Cheese, Butter, Paneer, Ghee) — 12%
UPDATE dbo.Ingredients
SET    GSTPercent = 12
WHERE  ItemCategory IN ('Raw Materials')
  AND (
       IngredientsName LIKE '%Cheese%'
    OR IngredientsName LIKE '%Butter%'
    OR IngredientsName LIKE '%Paneer%'
    OR IngredientsName LIKE '%Ghee%'
    OR IngredientsName LIKE '%Cream%'
    OR IngredientsName LIKE '%Milk%'
  );

PRINT '  Applied 12% to dairy products.';

-- Sauces, Ketchup, Pickles — 12%
UPDATE dbo.Ingredients
SET    GSTPercent = 12
WHERE  ItemCategory IN ('Raw Materials')
  AND (
       IngredientsName LIKE '%Sauce%'
    OR IngredientsName LIKE '%Ketchup%'
    OR IngredientsName LIKE '%Pickle%'
    OR IngredientsName LIKE '%Chutney%'
    OR IngredientsName LIKE '%Jam%'
  );

PRINT '  Applied 12% to sauces / condiments.';

-- Packaged / Finish Items (Biscuits, Snacks, Beverages) — 18%
UPDATE dbo.Ingredients
SET    GSTPercent = 18
WHERE  ItemCategory IN ('Finish Items');

PRINT '  Applied 18% to Finish Items category.';

-- Beverages category — 18%
UPDATE dbo.Ingredients
SET    GSTPercent = 18
WHERE  ItemCategory IN ('Beverage', 'Beverages');

PRINT '  Applied 18% to Beverages category.';

-- Packaging Materials — 18%
UPDATE dbo.Ingredients
SET    GSTPercent = 18
WHERE  ItemCategory IN ('Packaging');

PRINT '  Applied 18% to Packaging category.';

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 3 : Item-specific overrides
--          Add / modify rows below for any item that needs a different rate.
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 3: Item-specific overrides ===';

-- Examples (uncomment and adjust as needed):

-- UPDATE dbo.Ingredients SET GSTPercent = 0   WHERE Id = 1;  -- Vegetable Tomato  → 0%
-- UPDATE dbo.Ingredients SET GSTPercent = 0   WHERE Id = 3;  -- Potato            → 0%
-- UPDATE dbo.Ingredients SET GSTPercent = 0   WHERE Id = 4;  -- Egg               → 0%
-- UPDATE dbo.Ingredients SET GSTPercent = 0   WHERE Id = 8;  -- Salt              → 0%
-- UPDATE dbo.Ingredients SET GSTPercent = 5   WHERE Id = 6;  -- Maida             → 5%
-- UPDATE dbo.Ingredients SET GSTPercent = 5   WHERE Id = 7;  -- Mastered Oil      → 5%
-- UPDATE dbo.Ingredients SET GSTPercent = 12  WHERE Id = 2;  -- Cheese            → 12%
-- UPDATE dbo.Ingredients SET GSTPercent = 12  WHERE Id = 9;  -- Sauce             → 12%
-- UPDATE dbo.Ingredients SET GSTPercent = 18  WHERE Id = 5;  -- Biscuit 200 GRM   → 18%

PRINT '  Item-specific overrides applied (if any uncommented above).';

COMMIT TRANSACTION;
PRINT '=== Transaction committed. ===';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 4 : Verification — review before finalising
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 4: Verification Summary ===';

SELECT
    Id,
    Code,
    IngredientsName,
    ISNULL(ItemCategory, '(none)')  AS Category,
    GSTPercent,
    CAST(GSTPercent / 2 AS DECIMAL(5,2))  AS CGST_Pct,
    CAST(GSTPercent / 2 AS DECIMAL(5,2))  AS SGST_Pct,
    GSTPercent                            AS IGST_Pct
FROM dbo.Ingredients
ORDER BY ItemCategory, IngredientsName;
GO

PRINT '=== Backfill complete. ===';
GO
