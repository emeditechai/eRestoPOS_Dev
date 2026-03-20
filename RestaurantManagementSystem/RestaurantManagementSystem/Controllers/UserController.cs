using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Services;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    public class UserController : Controller
    {
        private const string ProtectedAdminUsername = "admin";

        private bool IsProtectedAdminUsername(string? username)
        {
            return !string.IsNullOrWhiteSpace(username) && username.Trim().Equals(ProtectedAdminUsername, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCurrentUserProtectedAdmin()
        {
            return IsProtectedAdminUsername(User?.Identity?.Name);
        }

        private string? GetUsernameById(SqlConnection con, int userId)
        {
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT TOP 1 Username FROM dbo.Users WHERE Id = @Id", con))
            {
                cmd.Parameters.AddWithValue("@Id", userId);
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
        }

        // Stub: Ensures Users table exists (implement as needed)
        private void EnsureUsersTableExists(SqlConnection con) { /* TODO: Implement schema check if needed */ }

        // Stub: Ensures required columns exist in Users table (implement as needed)
        private void EnsureUserTableColumns(SqlConnection con) { /* TODO: Implement column check if needed */ }

        private void EnsureUserBranchesTableExists(SqlConnection con)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.UserBranches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranches
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranches PRIMARY KEY,
        UserId INT NOT NULL,
        BranchId INT NOT NULL,
        IsDefault BIT NOT NULL CONSTRAINT DF_UserBranches_IsDefault DEFAULT(0),
        IsActive BIT NOT NULL CONSTRAINT DF_UserBranches_IsActive DEFAULT(1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranches_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL,
        CONSTRAINT UQ_UserBranches_UserId_BranchId UNIQUE(UserId, BranchId)
    );

    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranches ADD CONSTRAINT FK_UserBranches_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id);

    IF OBJECT_ID(N'dbo.Branches', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranches ADD CONSTRAINT FK_UserBranches_Branches FOREIGN KEY(BranchId) REFERENCES dbo.Branches(BranchId);
END
";

            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureUserBranchRolesTableExists(SqlConnection con)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.UserBranchRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranchRoles
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranchRoles PRIMARY KEY,
        UserId INT NOT NULL,
        BranchId INT NOT NULL,
        RoleId INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_UserBranchRoles_IsActive DEFAULT(1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranchRoles_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL,
        CONSTRAINT UQ_UserBranchRoles_User_Branch_Role UNIQUE(UserId, BranchId, RoleId)
    );

    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranchRoles ADD CONSTRAINT FK_UserBranchRoles_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id);

    IF OBJECT_ID(N'dbo.Branches', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranchRoles ADD CONSTRAINT FK_UserBranchRoles_Branches FOREIGN KEY(BranchId) REFERENCES dbo.Branches(BranchId);

    IF OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranchRoles ADD CONSTRAINT FK_UserBranchRoles_Roles FOREIGN KEY(RoleId) REFERENCES dbo.Roles(Id);

    CREATE INDEX IX_UserBranchRoles_User_Branch ON dbo.UserBranchRoles(UserId, BranchId);
    CREATE INDEX IX_UserBranchRoles_RoleId ON dbo.UserBranchRoles(RoleId);
END
";

            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureBranchesTableExists(SqlConnection con)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches
    (
        BranchId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
        BranchCode NVARCHAR(20) NOT NULL CONSTRAINT UQ_Branches_BranchCode UNIQUE,
        BranchName NVARCHAR(150) NOT NULL,
        Is_MainBranch BIT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT(1),
        CreatedAt DATETIME NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT(GETDATE()),
        UpdatedAt DATETIME NULL
    );
END
";

            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private List<BranchMaster> GetAllBranches(SqlConnection con)
        {
            var branches = new List<BranchMaster>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT BranchId, BranchCode, BranchName, Is_MainBranch, IsActive, CreatedAt, UpdatedAt
FROM dbo.Branches
WHERE ISNULL(IsActive, 1) = 1
ORDER BY BranchCode", con))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    branches.Add(new BranchMaster
                    {
                        BranchId = reader.GetInt32(0),
                        BranchCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        BranchName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        Is_MainBranch = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
                        CreatedAt = reader.IsDBNull(5) ? DateTime.Now : reader.GetDateTime(5),
                        UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                    });
                }
            }

            return branches;
        }

        private bool IsMainBranch(SqlConnection con, int branchId)
        {
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT TOP 1 ISNULL(Is_MainBranch, 0)
FROM dbo.Branches
WHERE BranchId = @BranchId
  AND ISNULL(IsActive, 1) = 1", con))
            {
                cmd.Parameters.AddWithValue("@BranchId", branchId);
                var result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    return false;
                }

                return Convert.ToBoolean(result);
            }
        }

        private void EnsureUserCreatedBranchColumnExists(SqlConnection con)
        {
            const string sql = @"
IF COL_LENGTH('dbo.Users', 'CreatedBranchId') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD CreatedBranchId INT NULL;
END";

            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private HashSet<int> GetVisibleUserIdsForBranch(SqlConnection con, int activeBranchId)
        {
            var ids = new HashSet<int>();

            var hasCreatedBranchIdColumn = false;
            var hasLegacyBranchIdColumn = false;

            using (var schemaCmd = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT
    CASE WHEN COL_LENGTH('dbo.Users', 'CreatedBranchId') IS NULL THEN 0 ELSE 1 END AS HasCreatedBranchId,
    CASE WHEN COL_LENGTH('dbo.Users', 'BranchId') IS NULL THEN 0 ELSE 1 END AS HasLegacyBranchId", con))
            using (var reader = schemaCmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    hasCreatedBranchIdColumn = !reader.IsDBNull(0) && reader.GetInt32(0) == 1;
                    hasLegacyBranchIdColumn = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
                }
            }

            var whereConditions = new List<string>
            {
                @"EXISTS (
    SELECT 1
    FROM dbo.UserBranches ub
    WHERE ub.UserId = u.Id
      AND ub.BranchId = @ActiveBranchId
      AND ISNULL(ub.IsActive, 1) = 1
)"
            };

            if (hasCreatedBranchIdColumn)
            {
                whereConditions.Add("u.CreatedBranchId = @ActiveBranchId");
            }

            if (hasLegacyBranchIdColumn)
            {
                whereConditions.Add("u.BranchId = @ActiveBranchId");
            }

            var visibilitySql = $@"
SELECT DISTINCT u.Id
FROM dbo.Users u
WHERE {string.Join(" OR ", whereConditions)}";

            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(visibilitySql, con))
            {
                cmd.Parameters.AddWithValue("@ActiveBranchId", activeBranchId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ids.Add(reader.GetInt32(0));
                    }
                }
            }

            return ids;
        }

        private List<int> GetUserBranchIds(SqlConnection con, int userId)
        {
            var branchIds = new List<int>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT BranchId
FROM dbo.UserBranches
WHERE UserId = @UserId
  AND ISNULL(IsActive, 1) = 1", con))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        branchIds.Add(reader.GetInt32(0));
                    }
                }
            }

            return branchIds;
        }

        private List<BranchMaster> GetUserBranches(SqlConnection con, int userId)
        {
            var branches = new List<BranchMaster>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT b.BranchId, b.BranchCode, b.BranchName, ISNULL(ub.IsDefault, ISNULL(b.Is_MainBranch, 0)) AS IsMain, b.IsActive, b.CreatedAt, b.UpdatedAt
FROM dbo.Branches b
INNER JOIN dbo.UserBranches ub ON ub.BranchId = b.BranchId
WHERE ub.UserId = @UserId
  AND ISNULL(ub.IsActive, 1) = 1
  AND ISNULL(b.IsActive, 1) = 1
ORDER BY CASE WHEN ISNULL(ub.IsDefault, 0) = 1 THEN 0 ELSE 1 END, b.BranchCode", con))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        branches.Add(new BranchMaster
                        {
                            BranchId = reader.GetInt32(0),
                            BranchCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            BranchName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            Is_MainBranch = !reader.IsDBNull(3) && reader.GetBoolean(3),
                            IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
                            CreatedAt = reader.IsDBNull(5) ? DateTime.Now : reader.GetDateTime(5),
                            UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                        });
                    }
                }
            }

            return branches;
        }

        private Dictionary<int, List<int>> GetUserBranchRoleMappings(SqlConnection con, int userId)
        {
            var mappings = new Dictionary<int, List<int>>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT BranchId, RoleId
FROM dbo.UserBranchRoles
WHERE UserId = @UserId
  AND ISNULL(IsActive, 1) = 1", con))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var branchId = reader.GetInt32(0);
                        var roleId = reader.GetInt32(1);
                        if (!mappings.ContainsKey(branchId))
                        {
                            mappings[branchId] = new List<int>();
                        }

                        if (!mappings[branchId].Contains(roleId))
                        {
                            mappings[branchId].Add(roleId);
                        }
                    }
                }
            }

            return mappings;
        }

        private Dictionary<int, List<int>> ParseBranchRoleAssignments(string branchRoleAssignmentsJson)
        {
            if (string.IsNullOrWhiteSpace(branchRoleAssignmentsJson))
            {
                return new Dictionary<int, List<int>>();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(branchRoleAssignmentsJson) ?? new Dictionary<string, List<int>>();
                var result = new Dictionary<int, List<int>>();

                foreach (var kvp in parsed)
                {
                    if (!int.TryParse(kvp.Key, out var branchId))
                    {
                        continue;
                    }

                    var roleIds = (kvp.Value ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
                    result[branchId] = roleIds;
                }

                return result;
            }
            catch
            {
                return new Dictionary<int, List<int>>();
            }
        }

        // Stub: Checks if a username exists (implement as needed)
        private bool UserExists(string username, int? excludeUserId = null) { return false; /* TODO: Implement actual check */ }

        /// <summary>
        /// Returns true when the given email is already registered to a different user.
        /// Safe — returns false on any error or when the Email column doesn't exist.
        /// </summary>
        private bool EmailInUse(string? email, int? excludeUserId = null)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                using var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection"));
                con.Open();
                // Guard: check column exists first
                using (var chk = new Microsoft.Data.SqlClient.SqlCommand(
                    "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Users' AND COLUMN_NAME='Email'", con))
                {
                    if (Convert.ToInt32(chk.ExecuteScalar()) == 0) return false;
                }
                var sql = excludeUserId.HasValue
                    ? "SELECT COUNT(1) FROM dbo.Users WHERE LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@Email))) AND Id <> @ExcludeId"
                    : "SELECT COUNT(1) FROM dbo.Users WHERE LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@Email)))";
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Email", email.Trim());
                if (excludeUserId.HasValue) cmd.Parameters.AddWithValue("@ExcludeId", excludeUserId.Value);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            catch { return false; }
        }
        private readonly IConfiguration _config;
        private readonly UserRoleService _userRoleService;

        public UserController(IConfiguration configuration, UserRoleService userRoleService)
        {
            _config = configuration;
            _userRoleService = userRoleService;
        }

        // Users List
        public async Task<IActionResult> UserList()
        {
            try
            {
                ViewBag.CanManageProtectedAdminUser = IsCurrentUserProtectedAdmin();
                var activeBranchId = User.GetActiveBranchId();
                var users = new List<User>();
                bool canViewAllUsers = false;
                HashSet<int> visibleUserIds = new HashSet<int>();
                using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    con.Open();
                    
                    // First ensure Users table exists
                    EnsureUsersTableExists(con);
                    EnsureBranchesTableExists(con);
                    EnsureUserBranchesTableExists(con);
                    EnsureUserCreatedBranchColumnExists(con);

                    if (activeBranchId.HasValue)
                    {
                        canViewAllUsers = IsMainBranch(con, activeBranchId.Value);
                        if (!canViewAllUsers)
                        {
                            visibleUserIds = GetVisibleUserIdsForBranch(con, activeBranchId.Value);
                        }
                    }
                    
                    // Create or update a robust stored procedure to list users safely across schema variants
                    var createSp = @"CREATE OR ALTER PROCEDURE dbo.usp_GetUsersList
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @hasPhone bit = CASE WHEN COL_LENGTH('dbo.Users','Phone') IS NOT NULL THEN 1 ELSE 0 END;
    
    DECLARE @sql nvarchar(max) = N'SELECT Id, Username, FirstName, LastName, Email, IsActive, ' +
        CASE WHEN @hasPhone=1 THEN N'Phone' ELSE N'CAST(NULL AS NVARCHAR(20)) AS Phone' END +
    N' FROM dbo.Users';
    EXEC sp_executesql @sql;
END";
                    using (var createCmd = new Microsoft.Data.SqlClient.SqlCommand(createSp, con))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("dbo.usp_GetUsersList", con))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var user = new User
                                {
                                    Id = reader.GetInt32(0),
                                    Username = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                    FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                    LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                    Email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                    IsActive = reader.GetBoolean(5),
                                    Phone = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : string.Empty
                                };
                                users.Add(user);
                            }
                        }
                    }

                    if (!canViewAllUsers)
                    {
                        users = users.Where(u => visibleUserIds.Contains(u.Id)).ToList();
                    }
                }
                
                // For each user, get their roles
                foreach (var user in users)
                {
                    user.Roles = (await _userRoleService.GetUserRolesAsync(user.Id)).ToList();
                    using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                    {
                        con.Open();
                        user.Branches = GetUserBranches(con, user.Id);
                    }
                }

                return View(users);
            }
            catch (Exception ex)
            {
                // Display error in a friendly way
                ViewBag.ErrorMessage = $"Error loading users: {ex.Message}";
                return View(new List<User>());
            }
        }

        // User Add/Edit/View Form
        public async Task<IActionResult> UserForm(int? id, bool isView = false)
        {
            try
            {
                User model = new User { Username = "", FirstName = "", LastName = "" };
                ViewBag.IsView = isView;
                var activeBranchId = User.GetActiveBranchId();
                var canAssignMultipleBranches = true;
                
                // Get all roles for dropdown
                var allRoles = await _userRoleService.GetAllRolesAsync();
                ViewBag.AllRoles = allRoles;
                ViewBag.Roles = allRoles; // Adding this for backward compatibility

                if (id.HasValue)
                {
                    using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                    {
                        con.Open();
                        SqlAuditContext.Apply(con, User, HttpContext, activeBranchId, "User");
                        
                        // Ensure Users table and columns exist
                        EnsureUsersTableExists(con);
                        EnsureUserTableColumns(con);
                        EnsureBranchesTableExists(con);
                        EnsureUserBranchesTableExists(con);
                        EnsureUserBranchRolesTableExists(con);

                        var targetUsername = GetUsernameById(con, id.Value);
                        if (IsProtectedAdminUsername(targetUsername) && !IsCurrentUserProtectedAdmin())
                        {
                            TempData["ResultMessage"] = "Only admin login can view or edit the admin user.";
                            return RedirectToAction("UserList");
                        }
                        
                        // Create or alter a stored procedure to fetch a single user robustly
                        var createSp = @"CREATE OR ALTER PROCEDURE dbo.usp_GetUserById
                            @Id INT
                        AS
                        BEGIN
                            SET NOCOUNT ON;
                            DECLARE @hasPhone bit = CASE WHEN COL_LENGTH('dbo.Users','Phone') IS NOT NULL THEN 1 ELSE 0 END;
                            
                            DECLARE @sql nvarchar(max) = N'SELECT Id, Username, FirstName, LastName, Email, IsActive, ' +
                                CASE WHEN @hasPhone=1 THEN N'Phone' ELSE N'CAST(NULL AS NVARCHAR(20)) AS Phone' END +
                                N' FROM dbo.Users WHERE Id = @Id';
                            EXEC sp_executesql @sql, N'@Id int', @Id=@Id;
                        END";
                        using (var createCmd = new Microsoft.Data.SqlClient.SqlCommand(createSp, con))
                        {
                            createCmd.ExecuteNonQuery();
                        }
                        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("dbo.usp_GetUserById", con))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@Id", id.Value);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    model = new User
                                    {
                                        Id = reader.GetInt32(0),
                                        Username = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                        Email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                        IsActive = reader.GetBoolean(5),
                                        Phone = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : string.Empty
                                    };
                                }
                            }
                        }
                    }
                    
                    // Get user roles
                    if (model.Id > 0)
                    {
                        model.Roles = (await _userRoleService.GetUserRolesAsync(model.Id)).ToList();
                        
                        // Populate the SelectedRoleIds based on assigned roles
                        model.SelectedRoleIds = model.Roles.Select(r => r.Id).ToList();

                        using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                        {
                            con.Open();
                            EnsureUserBranchRolesTableExists(con);
                            model.SelectedBranchIds = GetUserBranchIds(con, model.Id);
                            var branchRoleMappings = GetUserBranchRoleMappings(con, model.Id);
                            model.SelectedRoleIds = branchRoleMappings.Values.SelectMany(x => x).Distinct().ToList();
                            ViewBag.SelectedBranchRoles = JsonSerializer.Serialize(branchRoleMappings.ToDictionary(x => x.Key.ToString(), x => x.Value));
                        }
                    }
                }

                using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    con.Open();
                    EnsureBranchesTableExists(con);
                    EnsureUserBranchesTableExists(con);
                    EnsureUserBranchRolesTableExists(con);
                    EnsureUserCreatedBranchColumnExists(con);

                    if (activeBranchId.HasValue)
                    {
                        canAssignMultipleBranches = IsMainBranch(con, activeBranchId.Value);
                    }

                    ViewBag.AllBranches = GetAllBranches(con);
                }

                if (!id.HasValue && !canAssignMultipleBranches && activeBranchId.HasValue)
                {
                    model.SelectedBranchIds = new List<int> { activeBranchId.Value };
                }

                ViewBag.ActiveBranchId = activeBranchId;
                ViewBag.CanAssignMultipleBranches = canAssignMultipleBranches;

                if (ViewBag.SelectedBranchRoles == null)
                {
                    ViewBag.SelectedBranchRoles = "{}";
                }

                return View(model);
            }
            catch (Exception ex)
            {
                // Display error in a friendly way
                ViewBag.ErrorMessage = $"Error loading user: {ex.Message}";
                ViewBag.AllBranches = new List<BranchMaster>();
                return View(new User { Username = "", FirstName = "", LastName = "" });
            }
        }

        // Save User
        [HttpPostAttribute]
        public async Task<IActionResult> SaveUser(User model, List<int> selectedRoles, List<int> selectedBranches, string branchRoleAssignmentsJson)
        {
            var activeBranchId = User.GetActiveBranchId();
            var canAssignMultipleBranches = false;

            using (var authCon = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                authCon.Open();
                EnsureBranchesTableExists(authCon);
                EnsureUserCreatedBranchColumnExists(authCon);
                if (activeBranchId.HasValue)
                {
                    canAssignMultipleBranches = IsMainBranch(authCon, activeBranchId.Value);
                }
            }

            if (model.Id == 0 && !canAssignMultipleBranches)
            {
                if (!activeBranchId.HasValue)
                {
                    ModelState.AddModelError("SelectedBranchIds", "Active branch is required to create user.");
                }
                else
                {
                    selectedBranches = new List<int> { activeBranchId.Value };

                    if (selectedRoles != null && selectedRoles.Count > 0)
                    {
                        branchRoleAssignmentsJson = JsonSerializer.Serialize(new Dictionary<string, List<int>>
                        {
                            [activeBranchId.Value.ToString()] = selectedRoles.Distinct().ToList()
                        });
                    }
                }
            }

            if (model.Id > 0)
            {
                using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    con.Open();
                    EnsureUsersTableExists(con);
                    var existingUsername = GetUsernameById(con, model.Id);
                    if (IsProtectedAdminUsername(existingUsername) && !IsCurrentUserProtectedAdmin())
                    {
                        TempData["ResultMessage"] = "Only admin login can edit the admin user.";
                        return RedirectToAction("UserList");
                    }
                }
            }

            // Always remove Password validation for existing users
            if (model.Id > 0 && ModelState.ContainsKey("Password"))
            {
                ModelState.Remove("Password");
            }
            
            // Handle password validation/binding for create vs edit
            if (model.Id == 0)
            {
                var postedPassword = Request.Form["password"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(postedPassword))
                {
                    ModelState.AddModelError("Password", "Password is required for new users.");
                }
                else
                {
                    model.Password = postedPassword.Trim();
                    // Remove default model state error for Password since we're setting it manually
                    if (ModelState.ContainsKey("Password")) ModelState.Remove("Password");
                }
            }
            else
            {
                var postedPassword = Request.Form["password"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(postedPassword))
                {
                    model.Password = postedPassword.Trim();
                }
            }
            
            // Boolean properties in C# are already non-nullable value types
            // The bool type can't be null, so no need to check for null
            
            // Get all roles for dropdown in case we need to return the view
            var allRoles = await _userRoleService.GetAllRolesAsync();
            ViewBag.AllRoles = allRoles;
            ViewBag.Roles = allRoles; // Adding this for backward compatibility
            selectedBranches ??= new List<int>();
            model.SelectedBranchIds = selectedBranches.Distinct().ToList();

            var branchRoleAssignments = ParseBranchRoleAssignments(branchRoleAssignmentsJson);

            if (branchRoleAssignments.Count == 0 && model.SelectedBranchIds.Count > 0 && selectedRoles != null && selectedRoles.Count > 0)
            {
                foreach (var branchId in model.SelectedBranchIds)
                {
                    branchRoleAssignments[branchId] = selectedRoles.Distinct().ToList();
                }
            }

            model.SelectedRoleIds = branchRoleAssignments.Values.SelectMany(x => x).Distinct().ToList();
            ViewBag.SelectedBranchRoles = JsonSerializer.Serialize(branchRoleAssignments.ToDictionary(x => x.Key.ToString(), x => x.Value));

            if (model.SelectedBranchIds == null || model.SelectedBranchIds.Count == 0)
            {
                ModelState.AddModelError("SelectedBranchIds", "Please assign at least one branch.");
            }

            if (branchRoleAssignments.Count == 0)
            {
                ModelState.AddModelError("SelectedRoleIds", "Please assign at least one role for selected branches.");
            }

            foreach (var branchId in model.SelectedBranchIds)
            {
                if (!branchRoleAssignments.TryGetValue(branchId, out var rolesForBranch) || rolesForBranch == null || rolesForBranch.Count == 0)
                {
                    ModelState.AddModelError("SelectedRoleIds", "Each selected branch must have at least one role.");
                    break;
                }
            }

            using (var branchCon = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                branchCon.Open();
                EnsureBranchesTableExists(branchCon);
                EnsureUserBranchesTableExists(branchCon);
                EnsureUserBranchRolesTableExists(branchCon);
                EnsureUserCreatedBranchColumnExists(branchCon);
                ViewBag.AllBranches = GetAllBranches(branchCon);
            }
            ViewBag.ActiveBranchId = activeBranchId;
            ViewBag.CanAssignMultipleBranches = canAssignMultipleBranches;

            if (ModelState.IsValid)
            {
                try
                {
                    string resultMessage = "";
                    bool isUsernameInUse = UserExists(model.Username, model.Id > 0 ? model.Id : null);

                    if (isUsernameInUse)
                    {
                        ModelState.AddModelError("Username", "Username is already in use");
                        return View("UserForm", model);
                    }

                    // Check email uniqueness before hitting the DB constraint
                    if (!string.IsNullOrWhiteSpace(model.Email) &&
                        EmailInUse(model.Email, model.Id > 0 ? model.Id : null))
                    {
                        ModelState.AddModelError("Email", $"The email address '{model.Email}' is already registered to another user. Please use a different email.");
                        return View("UserForm", model);
                    }

                    using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
                    {
                        con.Open();
                        
                        // Ensure Users table and columns exist
                        EnsureUsersTableExists(con);
                        EnsureUserTableColumns(con);
                        EnsureBranchesTableExists(con);
                        EnsureUserBranchesTableExists(con);
                        EnsureUserBranchRolesTableExists(con);
                        EnsureUserCreatedBranchColumnExists(con);
                        
                        int userId = model.Id;
                        var effectiveRoleIds = branchRoleAssignments.Values.SelectMany(x => x).Distinct().ToList();
                        string roleIds = effectiveRoleIds.Count > 0 ? string.Join(",", effectiveRoleIds) : "";
                        // Determine password and salt to use. If a plaintext password was provided, hash it with BCrypt and save its salt.
                        string passwordToUse = null;
                        string saltToUse = null;
                        if (!string.IsNullOrWhiteSpace(model.Password))
                        {
                            // If the provided value already looks like a BCrypt hash, use as-is and extract salt
                            var p = model.Password.Trim();
                            if (p.StartsWith("$2a$") || p.StartsWith("$2b$") || p.StartsWith("$2y$"))
                            {
                                passwordToUse = p;
                                // bcrypt salt is the first 29 chars of the hash
                                if (p.Length >= 29) saltToUse = p.Substring(0, 29);
                            }
                            else
                            {
                                saltToUse = BCrypt.Net.BCrypt.GenerateSalt(12);
                                passwordToUse = BCrypt.Net.BCrypt.HashPassword(p, saltToUse);
                            }
                        }
                        else if (model.Id > 0)
                        {
                            // No new password supplied for edit — preserve current stored hash and salt
                            using (var pwdCmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT PasswordHash, Salt FROM dbo.Users WHERE Id = @Id", con))
                            {
                                pwdCmd.Parameters.AddWithValue("@Id", model.Id);
                                using (var rdr = pwdCmd.ExecuteReader())
                                {
                                    if (rdr.Read())
                                    {
                                        passwordToUse = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                                        saltToUse = rdr.FieldCount > 1 && !rdr.IsDBNull(1) ? rdr.GetString(1) : null;
                                    }
                                }
                            }
                        }
                        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("dbo.usp_CreateOrUpdateUserWithRoles", con))
                        {
                            cmd.CommandType = System.Data.CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@Id", model.Id);
                            cmd.Parameters.AddWithValue("@Username", model.Username);
                            cmd.Parameters.AddWithValue("@PasswordHash", (object)passwordToUse ?? (object)DBNull.Value);
                            // Ensure we never pass NULL for Salt; use extracted/generated salt or empty string fallback
                            cmd.Parameters.AddWithValue("@Salt", (object)(saltToUse ?? string.Empty));
                            cmd.Parameters.AddWithValue("@FirstName", model.FirstName);
                            cmd.Parameters.AddWithValue("@LastName", model.LastName ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Email", model.Email ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(model.Phone) ? (object)DBNull.Value : model.Phone);
                            cmd.Parameters.AddWithValue("@IsActive", model.IsActive);
                            cmd.Parameters.AddWithValue("@RoleIds", roleIds);
                            var result = cmd.ExecuteReader();
                            if (result.Read())
                            {
                                userId = Convert.ToInt32(result["UserId"]);
                            }
                            result.Close();
                            resultMessage = model.Id == 0 ? "User added successfully" : "User updated successfully";
                        }

                        if (model.Id == 0 && activeBranchId.HasValue)
                        {
                            using (var createdBranchCmd = new Microsoft.Data.SqlClient.SqlCommand(@"
UPDATE dbo.Users
SET CreatedBranchId = @CreatedBranchId
WHERE Id = @UserId", con))
                            {
                                createdBranchCmd.Parameters.AddWithValue("@CreatedBranchId", activeBranchId.Value);
                                createdBranchCmd.Parameters.AddWithValue("@UserId", userId);
                                createdBranchCmd.ExecuteNonQuery();
                            }
                        }

                        using (var deleteBranchCmd = new Microsoft.Data.SqlClient.SqlCommand("DELETE FROM dbo.UserBranches WHERE UserId = @UserId", con))
                        {
                            deleteBranchCmd.Parameters.AddWithValue("@UserId", userId);
                            deleteBranchCmd.ExecuteNonQuery();
                        }

                        using (var deleteBranchRoleCmd = new Microsoft.Data.SqlClient.SqlCommand("DELETE FROM dbo.UserBranchRoles WHERE UserId = @UserId", con))
                        {
                            deleteBranchRoleCmd.Parameters.AddWithValue("@UserId", userId);
                            deleteBranchRoleCmd.ExecuteNonQuery();
                        }

                        if (model.SelectedBranchIds.Count > 0)
                        {
                            int defaultBranchId = model.SelectedBranchIds[0];
                            foreach (var branchId in model.SelectedBranchIds)
                            {
                                using (var insertBranchCmd = new Microsoft.Data.SqlClient.SqlCommand(@"
INSERT INTO dbo.UserBranches (UserId, BranchId, IsDefault, IsActive, CreatedAt, UpdatedAt)
VALUES (@UserId, @BranchId, @IsDefault, 1, SYSUTCDATETIME(), NULL)", con))
                                {
                                    insertBranchCmd.Parameters.AddWithValue("@UserId", userId);
                                    insertBranchCmd.Parameters.AddWithValue("@BranchId", branchId);
                                    insertBranchCmd.Parameters.AddWithValue("@IsDefault", branchId == defaultBranchId);
                                    insertBranchCmd.ExecuteNonQuery();
                                }

                                if (branchRoleAssignments.TryGetValue(branchId, out var roleList))
                                {
                                    foreach (var roleId in roleList.Distinct())
                                    {
                                        using (var insertBranchRoleCmd = new Microsoft.Data.SqlClient.SqlCommand(@"
INSERT INTO dbo.UserBranchRoles (UserId, BranchId, RoleId, IsActive, CreatedAt, UpdatedAt)
VALUES (@UserId, @BranchId, @RoleId, 1, SYSUTCDATETIME(), NULL)", con))
                                        {
                                            insertBranchRoleCmd.Parameters.AddWithValue("@UserId", userId);
                                            insertBranchRoleCmd.Parameters.AddWithValue("@BranchId", branchId);
                                            insertBranchRoleCmd.Parameters.AddWithValue("@RoleId", roleId);
                                            insertBranchRoleCmd.ExecuteNonQuery();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    TempData["ResultMessage"] = resultMessage;

                    // Audit log for user create/edit
                    var auditAction = model.Id == 0 ? "Create" : "Update";
                    var auditUid   = User.GetUserId() ?? 0;
                    var auditUname = User.Identity?.Name ?? "Unknown";
                    var auditIp    = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var branchList = string.Join(",", model.SelectedBranchIds);
                    try { await AuditTrailController.LogSystemAuditAsync(
                        _config.GetConnectionString("DefaultConnection")!,
                        "User", auditAction,
                        model.Id, model.Username, null,
                        null, $"{model.Username} ({model.FirstName} {model.LastName})",
                        activeBranchId, auditUid, auditUname, auditIp,
                        $"Email:{model.Email}, IsActive:{model.IsActive}, Branches:{branchList}"); } catch { }

                    // Audit password change separately if password was supplied for an existing user
                    if (model.Id > 0 && !string.IsNullOrWhiteSpace(Request.Form["password"].FirstOrDefault()))
                    {
                        try { await AuditTrailController.LogSystemAuditAsync(
                            _config.GetConnectionString("DefaultConnection")!,
                            "User", "PasswordChange",
                            model.Id, model.Username, "Password",
                            "***", "***",
                            activeBranchId, auditUid, auditUname, auditIp); } catch { }
                    }

                    return RedirectToAction("UserList");
                }
                catch (Exception ex)
                {
                    // Friendly message for email or username unique-key constraint violations
                    var msg = ex.Message ?? "";
                    if (msg.Contains("Email", StringComparison.OrdinalIgnoreCase) &&
                        (msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase) || msg.Contains("unique", StringComparison.OrdinalIgnoreCase) || msg.Contains("UNIQUE")))
                    {
                        ModelState.AddModelError("Email", $"The email address '{model.Email}' is already registered to another user. Please use a different email.");
                    }
                    else if (msg.Contains("Username", StringComparison.OrdinalIgnoreCase) &&
                        (msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase) || msg.Contains("unique", StringComparison.OrdinalIgnoreCase)))
                    {
                        ModelState.AddModelError("Username", "Username is already in use. Please choose a different username.");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, $"Error saving user: {ex.Message}");
                    }
                }
            }
            // Get roles for dropdown before returning
            var userRoles = _userRoleService.GetAllRolesAsync().Result;
            ViewBag.AllRoles = userRoles;
            ViewBag.Roles = userRoles; // Adding this for backward compatibility
            using (var con = new Microsoft.Data.SqlClient.SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                con.Open();
                EnsureBranchesTableExists(con);
                EnsureUserBranchesTableExists(con);
                EnsureUserBranchRolesTableExists(con);
                EnsureUserCreatedBranchColumnExists(con);
                ViewBag.AllBranches = GetAllBranches(con);
            }
            ViewBag.ActiveBranchId = activeBranchId;
            ViewBag.CanAssignMultipleBranches = canAssignMultipleBranches;
            if (ViewBag.SelectedBranchRoles == null)
            {
                ViewBag.SelectedBranchRoles = "{}";
            }
            // Show all ModelState errors in TempData for debugging
            if (!ModelState.IsValid)
            {
                var allErrors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                TempData["DebugErrors"] = allErrors;
            }
            return View("UserForm", model);
        }
        }
    }
