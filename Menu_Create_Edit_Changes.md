# Menu Create/Edit Changes

Date: 2026-02-20

## Implemented

1. **Allergens tab hidden** on both Menu Create and Menu Edit pages.
   - UI is hidden (`d-none`) instead of fully removed so it can be re-enabled quickly later.
   - Existing allergen backend logic is kept intact.

2. **Branch dropdown added** on Menu Create and Menu Edit pages.
   - Dropdown is enabled only when the logged-in branch is the Main Branch.
   - For non-main branch users, dropdown is shown disabled and menu operations default to the login branch.

3. **Controller logic updated** to support branch selection safely.
   - Main branch can target selected active branch for create/edit operations.
   - Non-main branch always targets login branch.
   - Branch validation added for active/inactive checks.
   - Duplicate PLU validation now uses the resolved target branch.

4. **Model updated**
   - Added `BranchId` in `MenuItemViewModel` for form binding.

## Notes

- Existing core menu logic is preserved; changes are scoped to branch targeting and allergen tab visibility.
- Build validation completed successfully for `RestaurantManagementSystem.csproj`.
