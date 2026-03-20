-- ============================================================
-- Migration: Add IsRestrictMenuEditNonMainBranch to RestaurantSettings
-- Feature  : Restrict Menu Add/Edit/Delete from Non-Main Branch
-- Deploy   : Run once on each environment (dev, staging, prod)
-- Safe     : Idempotent – checks column existence before altering
-- Date     : 2026-03-20
-- ============================================================

-- 1. Add column to RestaurantSettings (if not already present)
IF COL_LENGTH('dbo.RestaurantSettings', 'IsRestrictMenuEditNonMainBranch') IS NULL
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
    ADD [IsRestrictMenuEditNonMainBranch] BIT NOT NULL DEFAULT 0;

    PRINT 'Column IsRestrictMenuEditNonMainBranch added to dbo.RestaurantSettings (default = 0 = NO restriction).';
END
ELSE
BEGIN
    PRINT 'Column IsRestrictMenuEditNonMainBranch already exists – no change needed.';
END
GO

-- 2. Ensure all existing rows have the column set (NULL safety)
UPDATE dbo.RestaurantSettings
SET    IsRestrictMenuEditNonMainBranch = ISNULL(IsRestrictMenuEditNonMainBranch, 0)
WHERE  IsRestrictMenuEditNonMainBranch IS NULL;
GO

-- 3. Verify
SELECT Id, BranchId, IsRestrictMenuEditNonMainBranch
FROM   dbo.RestaurantSettings
ORDER  BY Id;
GO
