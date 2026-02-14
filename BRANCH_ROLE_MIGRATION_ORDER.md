# Branch-wise Role Migration Order (One-Time)

Use this runbook when enabling branch-wise role assignment (`UserBranchRoles`) in an existing environment.

## 1) Pre-checks
- Confirm target database and take a full backup.
- Ensure `Branches` table exists and has valid active rows.
- Ensure all existing users that should log in are active.

## 2) Script execution order (important)
Run in this exact order:

1. `create_branches_table.sql` (root)
2. `SQL/create_user_branches_table.sql` (root)
3. `RestaurantManagementSystem/RestaurantManagementSystem/SQL/create_user_branch_roles_table.sql`

## 3) Optional one-time backfill (legacy users)
If users already have records in `UserRoles` and `UserBranches`, backfill into `UserBranchRoles`:

```sql
INSERT INTO dbo.UserBranchRoles (UserId, BranchId, RoleId, IsActive, CreatedAt, UpdatedAt)
SELECT ub.UserId, ub.BranchId, ur.RoleId, 1, SYSUTCDATETIME(), NULL
FROM dbo.UserBranches ub
INNER JOIN dbo.UserRoles ur ON ur.UserId = ub.UserId
LEFT JOIN dbo.UserBranchRoles ubr
    ON ubr.UserId = ub.UserId
    AND ubr.BranchId = ub.BranchId
    AND ubr.RoleId = ur.RoleId
WHERE ubr.Id IS NULL;
```

## 4) Verification queries
```sql
-- Basic counts
SELECT COUNT(*) AS BranchesCount FROM dbo.Branches;
SELECT COUNT(*) AS UserBranchesCount FROM dbo.UserBranches;
SELECT COUNT(*) AS UserBranchRolesCount FROM dbo.UserBranchRoles;

-- Any selected branch without roles? (should be zero rows)
SELECT ub.UserId, ub.BranchId
FROM dbo.UserBranches ub
LEFT JOIN dbo.UserBranchRoles ubr
    ON ubr.UserId = ub.UserId
    AND ubr.BranchId = ub.BranchId
    AND ISNULL(ubr.IsActive, 1) = 1
WHERE ISNULL(ub.IsActive, 1) = 1
GROUP BY ub.UserId, ub.BranchId
HAVING COUNT(ubr.Id) = 0;
```

## 5) App smoke test
- Open User Master and assign 2 branches with different roles.
- Login with that user.
- Select branch A and verify role popup shows only branch A roles.
- Switch to branch B and verify role popup shows only branch B roles.

## 6) Rollback note
- If needed, revert app binaries first, then restore DB backup.
- Do not drop new tables in-place on production unless rollback is explicitly approved.
