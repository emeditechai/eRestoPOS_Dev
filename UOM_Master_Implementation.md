# UOM Master – Implementation Reference

**Module:** Stock Masters → UOM Master  
**Purpose:** Unit of Measurement master for Bill of Material (BOM) in the Restaurant Management System  
**Author:** GitHub Copilot  
**Date:** 6 March 2026  
**Commit:** `788f444`

---

## Table of Contents


1. [Overview](#overview)
2. [Files Created / Modified](#files-created--modified)
3. [Database Schema](#database-schema)
4. [Conversion Logic](#conversion-logic)
5. [Seeded UOM Records](#seeded-uom-records)
6. [Navigation Setup](#navigation-setup)
7. [Controller Logic](#controller-logic)
8. [View Features](#view-features)
9. [BOM Integration Points](#bom-integration-points)
10. [Known Constraints & Rules](#known-constraints--rules)
11. [How to Deploy on a Fresh Database](#how-to-deploy-on-a-fresh-database)
12. [Error Reference](#error-reference)

---

## Overview

The UOM Master stores all units of measurement used across:
- **Purchase Orders** (how ingredients are bought — e.g. 50 KG sack)
- **Inventory / Stock** (how ingredients are stored — e.g. GRM)
- **BOM / Recipes** (how ingredients are used per dish — e.g. 250 GRM)

The design uses a **self-referencing hierarchy** — derived units (GRM) point to their base unit (KG) via `BaseUOMId`. A single conversion factor normalizes any quantity to the base unit for stock accounting.

---

## Files Created / Modified

| File | Type | Description |
|------|------|-------------|
| `Models/UomMaster.cs` | New | Entity model |
| `Controllers/UomController.cs` | New | Full CRUD controller |
| `Views/Uom/Index.cshtml` | New | Master page UI |
| `SQL/create_uom_master_v2.sql` | New | Idempotent table + seed script |
| `SQL/add_stocks_navigation.sql` | New | Navigation menu migration script |
| `Data/RestaurantDbContext.cs` | Modified | Added `DbSet<UomMaster>` + EF config |
| `SQL/create_navigation_permissions.sql` | Modified | Added Stocks nav entries to master seed |
| `Services/AdminInitializationHostedService.cs` | Modified | Auto-seeds Stocks navigation on startup |

---

## Database Schema

```sql
CREATE TABLE [dbo].[UomMaster] (
    [UOMId]            INT            IDENTITY(1,1) NOT NULL,  -- PK
    [UOMCode]          NVARCHAR(15)   NOT NULL,                 -- e.g. KG, GRM, LTR (UNIQUE)
    [UOMName]          NVARCHAR(100)  NOT NULL,                 -- e.g. Kilogram, Gram
    [UOMType]          NVARCHAR(20)   NOT NULL DEFAULT 'Count', -- Weight | Volume | Count | Other
    [BaseUOMId]        INT            NULL,                     -- NULL = this IS the base unit
    [ConversionFactor] DECIMAL(18,6)  NOT NULL DEFAULT 1,       -- 1 [this UOM] = CF [Base UOM]
    [PackSize]         DECIMAL(18,3)  NULL,                     -- e.g. 50 for a 50-KG bag
    [DecimalPlaces]    INT            NOT NULL DEFAULT 3,       -- display precision
    [Description]      NVARCHAR(300)  NULL,
    [IsActive]         BIT            NOT NULL DEFAULT 1,
    [CreatedAt]        DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]        DATETIME2(3)   NULL,

    CONSTRAINT [PK_UomMaster]             PRIMARY KEY ([UOMId]),
    CONSTRAINT [UQ_UomMaster_UOMCode]     UNIQUE ([UOMCode]),
    CONSTRAINT [CHK_UomMaster_Type]       CHECK ([UOMType] IN ('Weight','Volume','Count','Other')),
    CONSTRAINT [CHK_UomMaster_ConversionFactor] CHECK ([ConversionFactor] > 0),
    CONSTRAINT [FK_UomMaster_BaseUOM]     FOREIGN KEY ([BaseUOMId])
                                          REFERENCES [dbo].[UomMaster]([UOMId])
                                          ON DELETE NO ACTION   -- ← must be NO ACTION (not SET NULL)
);
```

### Indexes

```sql
CREATE INDEX [IX_UomMaster_UOMType]   ON [dbo].[UomMaster] ([UOMType]);
CREATE INDEX [IX_UomMaster_IsActive]  ON [dbo].[UomMaster] ([IsActive]);
CREATE INDEX [IX_UomMaster_BaseUOMId] ON [dbo].[UomMaster] ([BaseUOMId]);
```

---

## Conversion Logic

### Formula

$$\text{Quantity in Base Unit} = \text{Quantity} \times \text{ConversionFactor}$$

### Examples

| UOM  | BaseUOM | ConversionFactor | Calculation |
|------|---------|-----------------|-------------|
| KG   | *(self)* | 1.000000 | 5 KG = 5 × 1 = **5 KG** |
| GRM  | KG      | 0.001000 | 500 GRM = 500 × 0.001 = **0.5 KG** |
| MG   | KG      | 0.000001 | 1000 MG = 1000 × 0.000001 = **0.001 KG** |
| LTR  | *(self)* | 1.000000 | 2 LTR = 2 × 1 = **2 LTR** |
| ML   | LTR     | 0.001000 | 250 ML = 250 × 0.001 = **0.25 LTR** |
| CL   | LTR     | 0.010000 | 15 CL = 15 × 0.01 = **0.15 LTR** |
| PCS  | *(self)* | 1.000000 | 10 PCS = 10 × 1 = **10 PCS** |
| DOZ  | PCS     | 12.00000 | 3 DOZ = 3 × 12 = **36 PCS** |

### Self-Referencing Hierarchy Design

```
UomMaster table
│
├── KG   (BaseUOMId = NULL)  ← base unit
│   ├── GRM  (BaseUOMId = KG.UOMId,  CF = 0.001)
│   └── MG   (BaseUOMId = KG.UOMId,  CF = 0.000001)
│
├── LTR  (BaseUOMId = NULL)  ← base unit
│   ├── ML   (BaseUOMId = LTR.UOMId, CF = 0.001)
│   └── CL   (BaseUOMId = LTR.UOMId, CF = 0.01)
│
└── PCS  (BaseUOMId = NULL)  ← base unit
    ├── DOZ  (BaseUOMId = PCS.UOMId, CF = 12)
    └── PACK (BaseUOMId = PCS.UOMId, CF = 1 — variable, update per item)
```

---

## Seeded UOM Records

### Base Units (BaseUOMId = NULL)

| Code | Name     | Type   | CF  |
|------|----------|--------|-----|
| KG   | Kilogram | Weight | 1   |
| LTR  | Litre    | Volume | 1   |
| PCS  | Pieces   | Count  | 1   |

### Derived Units

| Code    | Name        | Type   | Base | ConversionFactor |
|---------|-------------|--------|------|-----------------|
| GRM     | Gram        | Weight | KG   | 0.001000 |
| MG      | Milligram   | Weight | KG   | 0.000001 |
| ML      | Millilitre  | Volume | LTR  | 0.001000 |
| CL      | Centilitre  | Volume | LTR  | 0.010000 |
| DOZ     | Dozen       | Count  | PCS  | 12.00000 |
| PACK    | Pack        | Count  | PCS  | 1.000000 |
| BTL     | Bottle      | Other  | —    | 1.000000 |
| PORTION | Portion     | Other  | —    | 1.000000 |
| SERVE   | Serving     | Other  | —    | 1.000000 |

> **Note:** PACK has CF = 1 as a placeholder. Update per ingredient (e.g. 12 for a 12-pack).

---

## Navigation Setup

### Menu Tree Added

```
Stocks  (NAV_STOCKS)   DisplayOrder: 11   Color: #22c55e   Icon: fa-boxes
  └── UOM Master  (NAV_STOCKS_UOM)        Controller: Uom  Action: Index
```

### Navigation is seeded in three ways (all idempotent):

1. **On app startup** — `AdminInitializationHostedService.SeedStocksNavigationAsync()` runs automatically
2. **SQL script** — `SQL/add_stocks_navigation.sql` (standalone, run once)
3. **Master seed** — `SQL/create_navigation_permissions.sql` (full reseed)

### To add more items under Stocks in future:

```sql
INSERT INTO dbo.NavigationMenus (Code, ParentCode, DisplayName, ControllerName, ActionName, IconCss, DisplayOrder, ...)
VALUES ('NAV_STOCKS_INGREDIENTS', 'NAV_STOCKS', 'Ingredients', 'Ingredient', 'Index', 'fas fa-carrot ...', 2, ...);
```

---

## Controller Logic

### `UomController.cs` — Action Summary

| Action | Method | Description |
|--------|--------|-------------|
| `Index` | GET | Loads all UOMs with base UOM join. Calls `EnsureUomMasterTableExists()` |
| `Save` | POST | Handles both Create (UOMId=0) and Update. Validates code, name, type, CF > 0 |
| `ToggleActive` | POST | Flips `IsActive` bit without full form submit |
| `Delete` | POST | Checks if UOM is referenced as BaseUOM by another row before deleting |
| `GetUomJson` | GET | Returns active UOMs as JSON for use in AJAX dropdowns (BOM forms etc.) |

### Auto-Table Creation (EnsureUomMasterTableExists)

Called on `Index` — checks `INFORMATION_SCHEMA.TABLES` first. Only runs `CREATE TABLE` DDL if the table is absent. This means:
- No manual SQL script needed on fresh environments
- Near-zero overhead on every normal request (just one scalar query)

### Delete Guard Logic

```csharp
// Prevent deleting a base UOM that derived UOMs reference
SELECT COUNT(*) FROM UomMaster WHERE BaseUOMId = @Id
// If count > 0 → show error "Deactivate instead"
```

---

## View Features

### Layout: Split Panel (matches existing master pages)

```
┌─────────────────────────┬──────────────────────────────────────┐
│  CREATE / EDIT FORM     │  UOM LIST TABLE                      │
│                         │  [Type filter dropdown]              │
│  UOM Code               │  Code | Name | Type | Base | CF | .. │
│  UOM Name               │  ───────────────────────────────────  │
│  UOM Type               │  KG   Kilogram   Weight  Base   1    │
│  Base UOM               │  GRM  Gram       Weight  KG  0.001   │
│  Conv. Factor ← live    │  LTR  Litre      Volume  Base   1    │
│  Pack Size              │  ...                                  │
│  Decimal Places         │                                       │
│  Description            │  ── QUICK REFERENCE CARD ──          │
│  Active toggle          │  Weight | Volume | Count | Other      │
│  [Save] [Reset]         │                                       │
└─────────────────────────┴──────────────────────────────────────┘
```

### Key JS Behaviours

- **Live conversion preview** — typing in CF field shows "1 GRM = 0.001 KG" in real-time
- **Auto-uppercase** — UOM Code field forces uppercase as you type
- **Type filter** — filters table rows client-side without page reload
- **Inline edit** — clicking Edit populates the left form, no separate edit page
- **Delete confirm** — browser `confirm()` dialog before submit
- **Tooltip help** — Font Awesome info icons with Bootstrap tooltips on key fields

---

## BOM Integration Points

When building the BOM / Recipe module, reference UOM Master like this:

```csharp
// In BOM detail line:
public int QuantityUOMId { get; set; }          // FK → UomMaster
public int PurchaseUOMId { get; set; }          // FK → UomMaster
public decimal Quantity { get; set; }

// To convert recipe quantity to stock base unit:
decimal baseQty = detail.Quantity * uom.ConversionFactor;
```

### Three UOM contexts per ingredient

```
Purchase:   1 Sack  → PackSize = 50, UOM = KG
Storage:    50,000 GRM stored in stock
Recipe BOM: 250 GRM per dish portion
```

### AJAX endpoint for BOM dropdowns

```javascript
fetch('/Uom/GetUomJson')
  .then(r => r.json())
  .then(uoms => {
      // uoms = [{ id, code, name, type, cf }, ...]
      populateDropdown(uoms);
  });
```

---

## Known Constraints & Rules

| Rule | Reason |
|------|--------|
| `ForeignKey ON DELETE NO ACTION` | SQL Server raises Msg 1785 for self-referencing FK with SET NULL/CASCADE |
| Cannot delete a base UOM if derived UOMs exist | FK constraint + application-level guard in `Delete` action |
| UOMCode must be UNIQUE | Enforced by `UQ_UomMaster_UOMCode` constraint |
| ConversionFactor must be > 0 | `CHK_UomMaster_ConversionFactor` constraint — zero causes division-by-zero |
| UOMType restricted to 4 values | `CHK_UomMaster_Type` — Weight, Volume, Count, Other |
| Mixing types in BOM | Application must validate that recipe UOM type matches ingredient type |

---

## How to Deploy on a Fresh Database

### Option A – Automatic (recommended)
Just start the application. `AdminInitializationHostedService` will:
1. Create the `UomMaster` table on first `GET /Uom/Index` request
2. Insert `NAV_STOCKS` and `NAV_STOCKS_UOM` navigation rows on startup

### Option B – Manual SQL

Run scripts in this order:

```sql
-- 1. Navigation schema (if not already created)
-- SQL/create_navigation_permissions.sql

-- 2. UOM table + seed data
-- SQL/create_uom_master_v2.sql

-- 3. Stocks navigation entries (if not using full reseed)
-- SQL/add_stocks_navigation.sql
```

---

## Error Reference

| Error | Cause | Fix |
|-------|-------|-----|
| Msg 1785 – FOREIGN KEY may cause cycles | `ON DELETE SET NULL` on self-referencing FK | Use `ON DELETE NO ACTION` |
| Msg 208 – Invalid object name 'dbo.UomMaster' | Table didn't exist when seed ran | Run table creation first (Step 1 before Step 2) |
| Msg 2627 / 2601 – Unique constraint violation | Duplicate UOMCode | Use a different code |
| "Cannot delete – used as Base UOM" | Application guard triggered | Deactivate the UOM instead of deleting |

---

## Future Enhancements (Suggested)

- [ ] **Ingredient Master** — link `IngredientId` → `PurchaseUOMId`, `StorageUOMId` in UomMaster
- [ ] **BOM Header/Detail** — `BomDetail.QuantityUOMId` → UomMaster
- [ ] **UOM Conversion API** — `GET /Uom/Convert?from=GRM&to=KG&qty=500` → returns 0.5
- [ ] **Import/Export CSV** — bulk UOM upload for large setups
- [ ] **Audit Trail** — log UOM changes to existing `AuditTrail` table
- [ ] **Role-based access** — restrict UOM delete to Admin role only via `RoleMenuPermissions`
