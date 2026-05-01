-- =============================================================================
-- Script : seed_uom_indian_standard.sql
-- Purpose: Align dbo.UomMaster with Indian GST notified UOM codes (CBIC) and
--          seed the standard set of UOMs commonly used in F&B / restaurants.
--
-- Reference : CBIC / GSTN notified Unit Quantity Codes (UQC) used on
--             tax invoices, e-invoice (IRN) and e-way bill.
--             https://einvoice1.gst.gov.in/Others/MasterCodes
--
-- Safe to re-run (idempotent MERGE).
-- =============================================================================

USE [dev_Restaurant]
GO

SET NOCOUNT ON;

-- -----------------------------------------------------------------------------
-- 0. Migrate legacy codes that pre-date the GST-standard codes.
--    KG  -> KGS,  GRM -> GMS,  ML -> MLT
--    Only renames if the target standard code is not already present, so
--    foreign-key references (BaseUOMId) are preserved.
-- -----------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM dbo.UomMaster WHERE UOMCode = 'KG')
   AND NOT EXISTS (SELECT 1 FROM dbo.UomMaster WHERE UOMCode = 'KGS')
BEGIN
    UPDATE dbo.UomMaster
       SET UOMCode   = 'KGS',
           UpdatedAt = SYSUTCDATETIME()
     WHERE UOMCode = 'KG';
    PRINT 'Renamed legacy KG -> KGS.';
END

IF EXISTS (SELECT 1 FROM dbo.UomMaster WHERE UOMCode = 'GRM')
   AND NOT EXISTS (SELECT 1 FROM dbo.UomMaster WHERE UOMCode = 'GMS')
BEGIN
    UPDATE dbo.UomMaster
       SET UOMCode   = 'GMS',
           UpdatedAt = SYSUTCDATETIME()
     WHERE UOMCode = 'GRM';
    PRINT 'Renamed legacy GRM -> GMS.';
END

IF EXISTS (SELECT 1 FROM dbo.UomMaster WHERE UOMCode = 'ML')
   AND NOT EXISTS (SELECT 1 FROM dbo.UomMaster WHERE UOMCode = 'MLT')
BEGIN
    UPDATE dbo.UomMaster
       SET UOMCode   = 'MLT',
           UpdatedAt = SYSUTCDATETIME()
     WHERE UOMCode = 'ML';
    PRINT 'Renamed legacy ML -> MLT.';
END
GO

-- -----------------------------------------------------------------------------
-- 1. BASE UNITS (BaseUOMId = NULL, ConversionFactor = 1)
--    Per SI / Indian Legal Metrology, the base units are:
--      Weight : Kilogram (KGS)
--      Volume : Litre    (LTR)
--      Length : Metre    (MTR)        -- handy for packaging
--      Count  : Numbers  (NOS)
-- -----------------------------------------------------------------------------

MERGE [dbo].[UomMaster] AS target
USING (VALUES
    -- Code,    Name,        Type,     BaseUOMId, CF,        PackSize, DP, Description
    (N'KGS',    N'Kilogram', N'Weight', NULL,     1.000000,  NULL,     3, N'Indian GST UQC – base weight unit (KGS)'),
    (N'LTR',    N'Litre',    N'Volume', NULL,     1.000000,  NULL,     3, N'Indian GST UQC – base volume unit (LTR)'),
    (N'NOS',    N'Numbers',  N'Count',  NULL,     1.000000,  NULL,     0, N'Indian GST UQC – base count unit (NOS)'),
    (N'PCS',    N'Pieces',   N'Count',  NULL,     1.000000,  NULL,     0, N'Pieces – synonymous with NOS for kitchen items')
) AS source (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description)
ON target.UOMCode = source.UOMCode
WHEN MATCHED THEN UPDATE SET
        UOMName          = source.UOMName,
        UOMType          = source.UOMType,
        BaseUOMId        = source.BaseUOMId,
        ConversionFactor = source.ConversionFactor,
        DecimalPlaces    = source.DecimalPlaces,
        Description      = source.Description,
        UpdatedAt        = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description, IsActive)
    VALUES (source.UOMCode, source.UOMName, source.UOMType, source.BaseUOMId, source.ConversionFactor,
            source.PackSize, source.DecimalPlaces, source.Description, 1);
GO

-- -----------------------------------------------------------------------------
-- 2. DERIVED UNITS – referencing the base units above.
-- -----------------------------------------------------------------------------

DECLARE @KgsId INT = (SELECT UOMId FROM dbo.UomMaster WHERE UOMCode = 'KGS');
DECLARE @LtrId INT = (SELECT UOMId FROM dbo.UomMaster WHERE UOMCode = 'LTR');
DECLARE @NosId INT = (SELECT UOMId FROM dbo.UomMaster WHERE UOMCode = 'NOS');

MERGE [dbo].[UomMaster] AS target
USING (VALUES
    -- ── WEIGHT (base = KGS) ───────────────────────────────────────────────
    (N'GMS',  N'Grams',         N'Weight', @KgsId, 0.001000,    NULL,  3, N'Indian GST UQC – 1 GMS = 0.001 KGS'),
    (N'MG',   N'Milligram',     N'Weight', @KgsId, 0.000001,    NULL,  3, N'1 MG = 0.000001 KGS – used for spices / additives'),
    (N'QTL',  N'Quintal',       N'Weight', @KgsId, 100.000000,  NULL,  2, N'Indian GST UQC – 1 QTL = 100 KGS'),
    (N'TON',  N'Tonne',         N'Weight', @KgsId, 1000.000000, NULL,  3, N'Indian GST UQC – 1 TON = 1000 KGS (Metric Tonne)'),

    -- ── VOLUME (base = LTR) ───────────────────────────────────────────────
    (N'MLT',  N'Millilitre',    N'Volume', @LtrId, 0.001000,    NULL,  0, N'Indian GST UQC – 1 MLT = 0.001 LTR'),
    (N'CL',   N'Centilitre',    N'Volume', @LtrId, 0.010000,    NULL,  1, N'1 CL = 0.01 LTR – common in bar (30 ML peg = 3 CL)'),
    (N'KLR',  N'Kilolitre',     N'Volume', @LtrId, 1000.000000, NULL,  3, N'Indian GST UQC – 1 KLR = 1000 LTR'),
    (N'PEG',  N'Peg (30 ML)',   N'Volume', @LtrId, 0.030000,    NULL,  0, N'Standard small peg = 30 ML (Indian bar standard)'),
    (N'LPEG', N'Large Peg',     N'Volume', @LtrId, 0.060000,    NULL,  0, N'Large peg = 60 ML (Indian bar standard)'),
    (N'TSP',  N'Teaspoon',      N'Volume', @LtrId, 0.005000,    NULL,  1, N'Kitchen measure – 1 TSP ≈ 5 ML'),
    (N'TBSP', N'Tablespoon',    N'Volume', @LtrId, 0.015000,    NULL,  1, N'Kitchen measure – 1 TBSP ≈ 15 ML'),
    (N'CUP',  N'Cup',           N'Volume', @LtrId, 0.250000,    NULL,  1, N'Kitchen measure – 1 CUP = 250 ML (metric cup)'),

    -- ── COUNT (base = NOS) ────────────────────────────────────────────────
    (N'DOZ',  N'Dozen',         N'Count',  @NosId, 12.000000,   NULL,  0, N'Indian GST UQC – 1 DOZ = 12 NOS'),
    (N'PRS',  N'Pairs',         N'Count',  @NosId, 2.000000,    NULL,  0, N'Indian GST UQC – 1 PRS = 2 NOS'),
    (N'GRS',  N'Gross',         N'Count',  @NosId, 144.000000,  NULL,  0, N'Indian GST UQC – 1 GRS = 144 NOS'),
    (N'THD',  N'Thousands',     N'Count',  @NosId, 1000.000000, NULL,  0, N'Indian GST UQC – 1 THD = 1000 NOS'),
    (N'PAC',  N'Packs',         N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – update CF per item (e.g. pack of 6, 10, 12)'),
    (N'BOX',  N'Box',           N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – update CF per item'),
    (N'CTN',  N'Cartons',       N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – update CF per item'),
    (N'BAG',  N'Bags',          N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – update CF per item'),
    (N'BAL',  N'Bale',          N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Bale'),
    (N'BDL',  N'Bundles',       N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Bundles'),
    (N'BUN',  N'Bunches',       N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Bunches (e.g. coriander, mint)'),
    (N'CAN',  N'Cans',          N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Cans'),
    (N'DRM',  N'Drums',         N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Drums'),
    (N'ROL',  N'Rolls',         N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Rolls'),
    (N'SET',  N'Sets',          N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Sets'),
    (N'TUB',  N'Tubes',         N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Tubes'),
    (N'TBS',  N'Tablets',       N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – Tablets'),
    (N'TRAY', N'Tray',          N'Count',  @NosId, 30.000000,   NULL,  0, N'Egg tray – 1 TRAY = 30 NOS (industry standard)'),
    (N'UNT',  N'Units',         N'Count',  @NosId, 1.000000,    NULL,  0, N'Indian GST UQC – generic unit'),

    -- ── OTHER (no base) ───────────────────────────────────────────────────
    (N'BTL',     N'Bottle',  N'Other',  NULL, 1.000000, NULL, 0, N'Indian GST UQC – Bottle (used for bar/beverages)'),
    (N'PORTION', N'Portion', N'Other',  NULL, 1.000000, NULL, 2, N'Recipe portion unit (kitchen recipe yield)'),
    (N'SERVE',   N'Serving', N'Other',  NULL, 1.000000, NULL, 2, N'Service portion (per plate)'),
    (N'OTH',     N'Others',  N'Other',  NULL, 1.000000, NULL, 0, N'Indian GST UQC – fallback for unmapped UOMs')
) AS source (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description)
ON target.UOMCode = source.UOMCode
WHEN MATCHED THEN UPDATE SET
        UOMName          = source.UOMName,
        UOMType          = source.UOMType,
        BaseUOMId        = source.BaseUOMId,
        ConversionFactor = source.ConversionFactor,
        DecimalPlaces    = source.DecimalPlaces,
        Description      = source.Description,
        UpdatedAt        = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description, IsActive)
    VALUES (source.UOMCode, source.UOMName, source.UOMType, source.BaseUOMId, source.ConversionFactor,
            source.PackSize, source.DecimalPlaces, source.Description, 1);

PRINT 'Indian-standard UOM seed data applied.';
GO

-- -----------------------------------------------------------------------------
-- 3. Verify
-- -----------------------------------------------------------------------------

SELECT
    u.UOMId,
    u.UOMCode,
    u.UOMName,
    u.UOMType,
    b.UOMCode AS BaseUOMCode,
    u.ConversionFactor,
    u.DecimalPlaces,
    u.IsActive
FROM dbo.UomMaster u
LEFT JOIN dbo.UomMaster b ON u.BaseUOMId = b.UOMId
ORDER BY u.UOMType, u.ConversionFactor DESC;
GO
