-- =============================================
-- Script : create_uom_master_v2.sql
-- Purpose: Create dbo.UomMaster table and seed
--          standard UOM records for Restaurant BOM.
-- Run on : dev_Restaurant  (or your target DB)
-- =============================================

USE [dev_Restaurant]
GO

-- ──────────────────────────────────────────────────────────────────────────────
-- 1. Create table (idempotent)
-- ──────────────────────────────────────────────────────────────────────────────

IF OBJECT_ID(N'dbo.UomMaster', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UomMaster] (
        [UOMId]            INT            IDENTITY(1,1) NOT NULL,
        [UOMCode]          NVARCHAR(15)   NOT NULL,
        [UOMName]          NVARCHAR(100)  NOT NULL,
        [UOMType]          NVARCHAR(20)   NOT NULL DEFAULT 'Count',
        [BaseUOMId]        INT            NULL,          -- NULL = this IS the base unit
        [ConversionFactor] DECIMAL(18,6)  NOT NULL DEFAULT 1,
        [PackSize]         DECIMAL(18,3)  NULL,          -- e.g. 50 for a 50-KG bag
        [DecimalPlaces]    INT            NOT NULL DEFAULT 3,
        [Description]      NVARCHAR(300)  NULL,
        [IsActive]         BIT            NOT NULL DEFAULT 1,
        [CreatedAt]        DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAt]        DATETIME2(3)   NULL,

        CONSTRAINT [PK_UomMaster]
            PRIMARY KEY CLUSTERED ([UOMId] ASC),

        CONSTRAINT [UQ_UomMaster_UOMCode]
            UNIQUE ([UOMCode]),

        CONSTRAINT [CHK_UomMaster_Type]
            CHECK ([UOMType] IN ('Weight','Volume','Count','Other')),

        CONSTRAINT [CHK_UomMaster_ConversionFactor]
            CHECK ([ConversionFactor] > 0),

        CONSTRAINT [FK_UomMaster_BaseUOM]
            FOREIGN KEY ([BaseUOMId]) REFERENCES [dbo].[UomMaster]([UOMId])
            ON DELETE NO ACTION
    );

    CREATE INDEX [IX_UomMaster_UOMType]
        ON [dbo].[UomMaster] ([UOMType]);

    CREATE INDEX [IX_UomMaster_IsActive]
        ON [dbo].[UomMaster] ([IsActive]);

    CREATE INDEX [IX_UomMaster_BaseUOMId]
        ON [dbo].[UomMaster] ([BaseUOMId]);

    PRINT 'dbo.UomMaster table created successfully.';
END
ELSE
    PRINT 'dbo.UomMaster table already exists – skipping CREATE.';
GO

-- ──────────────────────────────────────────────────────────────────────────────
-- 2. Seed standard UOM records (MERGE – safe to re-run)
-- ──────────────────────────────────────────────────────────────────────────────

-- Step A: Insert Base Units first (BaseUOMId = NULL)
MERGE [dbo].[UomMaster] AS target
USING (VALUES
    -- Code,  Name,          Type,    BaseUOMId, CF,  PackSize, DP, Description
    (N'KG',   N'Kilogram',   N'Weight', NULL, 1.000000, NULL, 3, N'Standard weight base unit'),
    (N'LTR',  N'Litre',      N'Volume', NULL, 1.000000, NULL, 3, N'Standard volume base unit'),
    (N'PCS',  N'Pieces',     N'Count',  NULL, 1.000000, NULL, 0, N'Individual count unit')
) AS source (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description)
ON target.UOMCode = source.UOMCode
WHEN MATCHED THEN
    UPDATE SET
        UOMName          = source.UOMName,
        UOMType          = source.UOMType,
        BaseUOMId        = source.BaseUOMId,
        ConversionFactor = source.ConversionFactor,
        PackSize         = source.PackSize,
        DecimalPlaces    = source.DecimalPlaces,
        Description      = source.Description,
        UpdatedAt        = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description, IsActive)
    VALUES (source.UOMCode, source.UOMName, source.UOMType, source.BaseUOMId, source.ConversionFactor,
            source.PackSize, source.DecimalPlaces, source.Description, 1);
GO

-- Step B: Insert Derived Units that reference base units
DECLARE @KgId  INT = (SELECT UOMId FROM dbo.UomMaster WHERE UOMCode = 'KG');
DECLARE @LtrId INT = (SELECT UOMId FROM dbo.UomMaster WHERE UOMCode = 'LTR');
DECLARE @PcsId INT = (SELECT UOMId FROM dbo.UomMaster WHERE UOMCode = 'PCS');

MERGE [dbo].[UomMaster] AS target
USING (VALUES
    -- ── Weight ──────────────────────────────────────────────────────────────
    (N'GRM',  N'Gram',       N'Weight', @KgId,  0.001000, NULL, 3, N'1 GRM = 0.001 KG'),
    (N'MG',   N'Milligram',  N'Weight', @KgId,  0.000001, NULL, 0, N'1 MG = 0.000001 KG – used for spices'),
    -- ── Volume ──────────────────────────────────────────────────────────────
    (N'ML',   N'Millilitre', N'Volume', @LtrId, 0.001000, NULL, 0, N'1 ML = 0.001 LTR'),
    (N'CL',   N'Centilitre', N'Volume', @LtrId, 0.010000, NULL, 1, N'1 CL = 0.01 LTR'),
    -- ── Count ───────────────────────────────────────────────────────────────
    (N'DOZ',  N'Dozen',      N'Count',  @PcsId, 12.00000, NULL, 0, N'1 DOZ = 12 PCS'),
    (N'PACK', N'Pack',       N'Count',  @PcsId, 1.000000, NULL, 0, N'Variable pack – update CF per item'),
    -- ── Other (no base) ─────────────────────────────────────────────────────
    (N'BTL',  N'Bottle',     N'Other',  NULL,   1.000000, NULL, 0, N'Whole bottle – used for bar items'),
    (N'PORTION', N'Portion', N'Other',  NULL,   1.000000, NULL, 2, N'Recipe portion unit'),
    (N'SERVE',   N'Serving', N'Other',  NULL,   1.000000, NULL, 2, N'Service portion')
) AS source (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description)
ON target.UOMCode = source.UOMCode
WHEN MATCHED THEN
    UPDATE SET
        UOMName          = source.UOMName,
        UOMType          = source.UOMType,
        BaseUOMId        = source.BaseUOMId,
        ConversionFactor = source.ConversionFactor,
        PackSize         = source.PackSize,
        DecimalPlaces    = source.DecimalPlaces,
        Description      = source.Description,
        UpdatedAt        = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UOMCode, UOMName, UOMType, BaseUOMId, ConversionFactor, PackSize, DecimalPlaces, Description, IsActive)
    VALUES (source.UOMCode, source.UOMName, source.UOMType, source.BaseUOMId, source.ConversionFactor,
            source.PackSize, source.DecimalPlaces, source.Description, 1);

PRINT 'UOM seed data applied.';
GO

-- ──────────────────────────────────────────────────────────────────────────────
-- 3. Verify
-- ──────────────────────────────────────────────────────────────────────────────

SELECT
    u.UOMId,
    u.UOMCode,
    u.UOMName,
    u.UOMType,
    b.UOMCode   AS BaseUOMCode,
    u.ConversionFactor,
    u.DecimalPlaces,
    u.IsActive
FROM dbo.UomMaster u
LEFT JOIN dbo.UomMaster b ON u.BaseUOMId = b.UOMId
ORDER BY u.UOMType, u.ConversionFactor DESC;
GO
