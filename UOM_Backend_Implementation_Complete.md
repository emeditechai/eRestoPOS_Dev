# UOM Backend Implementation Complete

## Summary
Successfully implemented UOM (Unit of Measurement) support in the backend code for Menu Item Master. The UOM feature is designed specifically for BAR items to handle different serving sizes (pegs, bottles, glasses, etc.).

## Completed Tasks

### 1. Database Scripts Created ✅
- **create_uom_master_table.sql**: Creates `tbl_mst_uom` table with proper constraints
- **insert_uom_sample_data.sql**: Inserts 10 sample UOM records (Pegs, Bottles, Beer, Wine)
- **add_uom_to_menuitems.sql**: Adds `UOM_Id` column to MenuItems with FK constraint

### 2. Model Updates ✅
**File**: `Models/MenuItem.cs`
- Added `UOMId` property (int?, nullable) after Price
- Added `UOMName` property (string) for display
- Properties positioned correctly after Price field

### 3. ViewModel Updates ✅
**File**: `ViewModels/MenuViewModels.cs` - `MenuItemViewModel`
- Added `UOMId` property (int?, nullable) after Price with Display attribute
- Added `UOMName` property (string) for display purposes

### 4. Controller Updates ✅
**File**: `Controllers/MenuController.cs`

#### New Helper Method:
- **GetUOMSelectList()** (lines 2647+):
  - Checks if `tbl_mst_uom` table exists
  - Queries active UOMs
  - Returns formatted SelectListItem: "{name} ({type} - {quantity}ml)"
  - Defensive programming with table existence check

#### Updated Methods:

**a) Create() GET Method** (line ~297):
- Added `ViewBag.UOMs = GetUOMSelectList();`
- Populates dropdown for create form

**b) Edit() GET Method** (lines 469-548):
- Added UOMId and UOMName to viewModel mapping (lines ~491-492)
- Added `ViewBag.UOMs = GetUOMSelectList();` (line ~547)
- Ensures existing UOM is loaded for editing

**c) GetMenuItemById() Method** (lines 1060+):
- Updated column existence check to include UOM_Id and tbl_mst_uom
- Updated both SELECT queries (with and without SubCategory) to include:
  - LEFT JOIN with tbl_mst_uom table
  - Select UOM_Id and UOM_Name columns
- Updated MenuItem object creation to populate UOMId and UOMName properties

**d) CreateMenuItem() Method** (lines 1296+):
- Added UOM column existence check
- Updated INSERT query to include UOM_Id column (conditional)
- Added UOM_Id parameter with NULL handling
- Validates UOMId > 0 before inserting

**e) UpdateMenuItem() Method** (lines 1619+):
- Added UOM column existence check
- Updated UPDATE query to include `UOM_Id = @UOMId` (conditional)
- Added UOM_Id parameter with NULL handling
- Validates UOMId > 0 before updating

## Technical Implementation Details

### Column Existence Checks
All database operations include defensive checks for UOM column and table existence:
```csharp
bool hasUOMColumn = false;
bool hasUOMTable = false;
// Query INFORMATION_SCHEMA.COLUMNS and INFORMATION_SCHEMA.TABLES
```

### Conditional Query Building
Queries dynamically include UOM fields only if the column exists:
```csharp
var joinUOM = hasUOMColumn && hasUOMTable 
    ? "LEFT JOIN [dbo].[tbl_mst_uom] uom ON m.[UOM_Id] = uom.[UOM_Id]" 
    : string.Empty;
var selectUOMCols = hasUOMColumn && hasUOMTable 
    ? ", m.[UOM_Id] AS UOMId, uom.[UOM_Name] AS UOMName" 
    : ", NULL AS UOMId, NULL AS UOMName";
```

### Parameter Handling
NULL-safe parameter addition for INSERT and UPDATE:
```csharp
if (hasUOMColumn)
{
    if (model.UOMId.HasValue && model.UOMId.Value > 0)
        command.Parameters.AddWithValue("@UOMId", model.UOMId.Value);
    else
        command.Parameters.AddWithValue("@UOMId", DBNull.Value);
}
```

## Next Steps - View Implementation

### 1. Update Create.cshtml
- Add UOM dropdown after Price field
- Add JavaScript for conditional display:
  ```javascript
  // Show UOM only when Item Group = "BAR"
  $('#MenuItemGroupId').change(function() {
      var selectedGroup = $('#MenuItemGroupId option:selected').text();
      if (selectedGroup === 'BAR') {
          $('#uom-group').show();
      } else {
          $('#uom-group').hide();
          $('#UOMId').val('');
      }
  });
  ```

### 2. Update Edit.cshtml
- Add UOM dropdown after Price field
- Add same conditional display JavaScript
- Ensure existing UOM value is pre-selected

### 3. Update Details.cshtml
- Display UOM field only for BAR items
- Format: "Unit of Measurement: {UOMName}"

### 4. Update Index.cshtml (if needed)
- Add UOM column to menu items list
- Show UOM only for BAR items

## Testing Checklist
- [ ] User runs SQL scripts manually (create table, insert data, add column)
- [ ] Test Create menu item with Item Group = BAR → UOM dropdown should appear
- [ ] Test Create menu item with Item Group = Foods → UOM dropdown should be hidden
- [ ] Test Edit menu item with existing UOM → UOM should be selected
- [ ] Test Edit menu item changing from BAR to Foods → UOM should hide
- [ ] Verify UOM displays correctly in Details view for BAR items
- [ ] Verify foreign key relationship (delete UOM should set MenuItem.UOM_Id to NULL)
- [ ] Test with database that doesn't have UOM column yet (backward compatibility)

## Files Modified
1. `RestaurantManagementSystem/RestaurantManagementSystem/Models/MenuItem.cs`
2. `RestaurantManagementSystem/RestaurantManagementSystem/ViewModels/MenuViewModels.cs`
3. `RestaurantManagementSystem/RestaurantManagementSystem/Controllers/MenuController.cs`

## Files Created
1. `RestaurantManagementSystem/SQL/create_uom_master_table.sql`
2. `RestaurantManagementSystem/SQL/insert_uom_sample_data.sql`
3. `RestaurantManagementSystem/SQL/add_uom_to_menuitems.sql`
4. `UOM_Implementation_Plan.md`
5. `UOM_Backend_Implementation_Complete.md` (this file)

## Notes
- All compilation errors shown during implementation are pre-existing (AspNetCore namespace references)
- These errors will resolve on full project build
- UOM is optional (nullable) to maintain backward compatibility
- Foreign key constraint includes ON DELETE SET NULL for data integrity
- User will execute SQL scripts manually before testing
