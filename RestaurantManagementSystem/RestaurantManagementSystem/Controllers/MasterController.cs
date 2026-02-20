using Microsoft.AspNetCore.Mvc;
using RestaurantManagementSystem.Models;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using RestaurantManagementSystem.Data;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using RestaurantManagementSystem.Utilities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagementSystem.Controllers
{
    public class MasterController : Controller
    {
        private readonly RestaurantDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        
        public MasterController(RestaurantDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        // Category List
        public IActionResult CategoryList()
        {
            var categories = _dbContext.Categories.ToList();
            return View(categories);
    }

    // Category Add/Edit/View Form
    public IActionResult CategoryForm(int? id, bool isView = false)
    {
        Category model = new Category { Name = "" };
        if (id.HasValue)
        {
            model = _dbContext.Categories.FirstOrDefault(c => c.Id == id.Value) ?? model;
        }
        
        ViewBag.IsView = isView;
        return View(model);
    }

    [HttpPostAttribute]
    public IActionResult CategoryForm(Category model)
    {
        string resultMessage;
        
        if (model.Id > 0)
        {
            // Update existing category
            var existingCategory = _dbContext.Categories.FirstOrDefault(c => c.Id == model.Id);
            if (existingCategory == null)
            {
                TempData["ResultMessage"] = "Category update failed. Id not found.";
                return RedirectToAction("CategoryList");
            }
            
            existingCategory.Name = model.Name;
            existingCategory.IsActive = model.IsActive;
            _dbContext.SaveChanges();
            resultMessage = "Category updated successfully.";
        }
        else
        {
            // Add new category
            _dbContext.Categories.Add(model);
            _dbContext.SaveChanges();
            resultMessage = "Category added successfully.";
        }
        
        TempData["ResultMessage"] = resultMessage;
        return RedirectToAction("CategoryList");
    }

    // Ingredients List
    public IActionResult IngredientsList()
    {
        var activeBranchId = User.GetActiveBranchId();
        if (!activeBranchId.HasValue)
        {
            TempData["ResultMessage"] = "Please select an active branch first.";
            return View(new List<Ingredients>());
        }

        EnsureIngredientsBranchColumnExists();
        var ingredients = _dbContext.Ingredients.Where(i => i.BranchId == activeBranchId.Value).ToList();
        
        // If there are no ingredients, seed some sample data
        if (!ingredients.Any())
        {
            _dbContext.Ingredients.AddRange(
                new Ingredients { IngredientsName = "Tomato", DisplayName = "Tomato", Code = "TMT", BranchId = activeBranchId.Value },
                new Ingredients { IngredientsName = "Cheese", DisplayName = "Cheese", Code = "CHS", BranchId = activeBranchId.Value }
            );
            _dbContext.SaveChanges();
            ingredients = _dbContext.Ingredients.Where(i => i.BranchId == activeBranchId.Value).ToList();
        }
        
        return View(ingredients);
    }

    // Add/Edit/View Form
    public IActionResult IngredientsForm(int? id, bool isView = false)
    {
        var activeBranchId = User.GetActiveBranchId();
        if (!activeBranchId.HasValue)
        {
            TempData["ResultMessage"] = "Please select an active branch first.";
            return RedirectToAction(nameof(IngredientsList));
        }

        EnsureIngredientsBranchColumnExists();
        Ingredients model = new Ingredients { IngredientsName = "" };
        
        if (id.HasValue)
        {
            model = _dbContext.Ingredients.FirstOrDefault(i => i.Id == id.Value && i.BranchId == activeBranchId.Value) ?? model;
        }
        
        ViewBag.IsView = isView;
        return View("Ingredients", model);
    }

    [HttpPostAttribute]
    public IActionResult IngredientsForm(Ingredients model)
    {
        var activeBranchId = User.GetActiveBranchId();
        if (!activeBranchId.HasValue)
        {
            TempData["ResultMessage"] = "Please select an active branch first.";
            return RedirectToAction(nameof(IngredientsList));
        }

        EnsureIngredientsBranchColumnExists();
        model.BranchId = activeBranchId.Value;

        if (ModelState.IsValid)
        {
            if (model.Id == 0)
            {
                // Add new ingredient
                _dbContext.Ingredients.Add(model);
                _dbContext.SaveChanges();
                TempData["ResultMessage"] = "Ingredient added successfully.";
            }
            else
            {
                // Update existing ingredient
                var existingIngredient = _dbContext.Ingredients.FirstOrDefault(i => i.Id == model.Id && i.BranchId == activeBranchId.Value);
                if (existingIngredient != null)
                {
                    existingIngredient.IngredientsName = model.IngredientsName;
                    existingIngredient.DisplayName = model.DisplayName;
                    existingIngredient.Code = model.Code;
                    existingIngredient.BranchId = activeBranchId.Value;
                    _dbContext.SaveChanges();
                    TempData["ResultMessage"] = "Ingredient updated successfully.";
                }
                else
                {
                    TempData["ResultMessage"] = "Ingredient update failed. Id not found.";
                }
            }
            return RedirectToAction("IngredientsList");
        }
        return View("Ingredients", model);
    }

        // Counter Master List
        public IActionResult CounterList()
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ResultMessage"] = "Please select an active branch first.";
                    return View(new List<CounterMaster>());
                }

                EnsureCountersTableExists();
                var counters = ReadCounters(activeBranchId.Value);
                return View(counters);
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to load counters: {ex.Message}";
                return View(new List<CounterMaster>());
            }
        }

        // Counter Master Add/Edit/View Form
        public IActionResult CounterForm(int? id, bool isView = false)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ResultMessage"] = "Please select an active branch first.";
                    return RedirectToAction(nameof(CounterList));
                }

                EnsureCountersTableExists();
                EnsureBranchesTableExists();
                var model = new CounterMaster();
                var isMainBranch = IsMainBranchActiveSession();

                if (id.HasValue && id.Value > 0)
                {
                    model = ReadCounterById(id.Value, activeBranchId.Value) ?? model;
                }
                else
                {
                    model.BranchId = activeBranchId.Value;
                }

                ViewBag.IsView = isView;
                ViewBag.IsMainBranch = isMainBranch;
                ViewBag.Branches = GetActiveBranchSelectList();
                ViewBag.ActiveBranchId = activeBranchId.Value;
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to load counter: {ex.Message}";
                return RedirectToAction(nameof(CounterList));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CounterForm(CounterMaster model, bool isView = false)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ResultMessage"] = "Please select an active branch first.";
                    return RedirectToAction(nameof(CounterList));
                }

                EnsureCountersTableExists();
                EnsureBranchesTableExists();

                var isMainBranch = IsMainBranchActiveSession();

                var targetBranchId = activeBranchId.Value;
                if (model.Id == 0 && isMainBranch && model.BranchId.HasValue)
                {
                    targetBranchId = model.BranchId.Value;
                }

                model.BranchId = targetBranchId;

                if (!IsBranchActive(targetBranchId))
                {
                    ModelState.AddModelError(nameof(CounterMaster.BranchId), "Selected branch is invalid or inactive.");
                }

                model.CounterCode = (model.CounterCode ?? string.Empty).Trim();
                model.CounterName = (model.CounterName ?? string.Empty).Trim();

                // Unique validation for CounterCode
                if (string.IsNullOrWhiteSpace(model.CounterCode))
                {
                    ModelState.AddModelError(nameof(CounterMaster.CounterCode), "Counter Code is required.");
                }

                if (string.IsNullOrWhiteSpace(model.CounterName))
                {
                    ModelState.AddModelError(nameof(CounterMaster.CounterName), "Counter Name is required.");
                }

                if (!string.IsNullOrWhiteSpace(model.CounterCode))
                {
                    var duplicateBranchId = model.Id > 0 ? activeBranchId.Value : targetBranchId;
                    var duplicate = CounterCodeExists(model.CounterCode, model.Id, duplicateBranchId);
                    if (duplicate)
                    {
                        ModelState.AddModelError(nameof(CounterMaster.CounterCode), "Counter Code already exists.");
                    }
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.IsView = isView;
                    ViewBag.IsMainBranch = isMainBranch;
                    ViewBag.Branches = GetActiveBranchSelectList();
                    ViewBag.ActiveBranchId = activeBranchId.Value;
                    return View(model);
                }

                if (model.Id > 0)
                {
                    var updated = UpdateCounter(model, activeBranchId.Value);
                    TempData["ResultMessage"] = updated ? "Counter updated successfully." : "Counter update failed.";
                }
                else
                {
                    var inserted = InsertCounter(model, targetBranchId);
                    TempData["ResultMessage"] = inserted ? "Counter added successfully." : "Counter add failed.";
                }

                return RedirectToAction(nameof(CounterList));
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to save counter: {ex.Message}";
                ViewBag.IsView = isView;
                ViewBag.IsMainBranch = IsMainBranchActiveSession();
                ViewBag.Branches = GetActiveBranchSelectList();
                ViewBag.ActiveBranchId = User.GetActiveBranchId() ?? 0;
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetCounterStatus(int id, bool isActive)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ResultMessage"] = "Please select an active branch first.";
                    return RedirectToAction(nameof(CounterList));
                }

                EnsureCountersTableExists();
                var ok = SetCounterActive(id, isActive, activeBranchId.Value);
                TempData["ResultMessage"] = ok
                    ? (isActive ? "Counter activated successfully." : "Counter deactivated successfully.")
                    : "Counter status update failed.";
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Counter status update failed: {ex.Message}";
            }

            return RedirectToAction(nameof(CounterList));
        }

        private void EnsureCountersTableExists()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                using (var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.Counters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Counters
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Counters PRIMARY KEY,
        BranchId INT NULL,
        CounterCode NVARCHAR(50) NOT NULL,
        CounterName NVARCHAR(120) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Counters_IsActive DEFAULT (1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Counters_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL
    );

    CREATE INDEX IX_Counters_Branch_CounterCode ON dbo.Counters (BranchId, CounterCode);
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.Counters', 'BranchId') IS NULL
    BEGIN
        ALTER TABLE dbo.Counters ADD BranchId INT NULL;
    END

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_Counters_CounterCode'
          AND object_id = OBJECT_ID(N'dbo.Counters')
    )
    BEGIN
        DROP INDEX UX_Counters_CounterCode ON dbo.Counters;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_Counters_Branch_CounterCode'
          AND object_id = OBJECT_ID(N'dbo.Counters')
    )
    BEGIN
        CREATE INDEX IX_Counters_Branch_CounterCode ON dbo.Counters (BranchId, CounterCode);
    END
END
", connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private List<CounterMaster> ReadCounters(int branchId)
        {
            var list = new List<CounterMaster>();
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT Id, BranchId, CounterCode, CounterName, IsActive, CreatedAt, UpdatedAt
FROM dbo.Counters
WHERE BranchId = @BranchId
ORDER BY CounterCode", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    using (var reader = cmd.ExecuteReader())
                    {
                    while (reader.Read())
                    {
                        list.Add(new CounterMaster
                        {
                            Id = reader.GetInt32(0),
                            BranchId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                            CounterCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            CounterName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
                            CreatedAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                            UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                        });
                    }
                    }
                }
            }
            return list;
        }

        private CounterMaster? ReadCounterById(int id, int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT TOP 1 Id, BranchId, CounterCode, CounterName, IsActive, CreatedAt, UpdatedAt
FROM dbo.Counters
WHERE Id = @Id AND BranchId = @BranchId", connection))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        return new CounterMaster
                        {
                            Id = reader.GetInt32(0),
                            BranchId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                            CounterCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            CounterName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
                            CreatedAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                            UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                        };
                    }
                }
            }
        }

        private bool CounterCodeExists(string code, int excludeId, int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.Counters
WHERE UPPER(CounterCode) = UPPER(@Code)
  AND BranchId = @BranchId
  AND Id <> @ExcludeId", connection))
                {
                    cmd.Parameters.AddWithValue("@Code", code);
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    cmd.Parameters.AddWithValue("@ExcludeId", excludeId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private bool InsertCounter(CounterMaster model, int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
INSERT INTO dbo.Counters (BranchId, CounterCode, CounterName, IsActive, CreatedAt, UpdatedAt)
VALUES (@BranchId, @Code, @Name, @IsActive, SYSUTCDATETIME(), NULL)
", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    cmd.Parameters.AddWithValue("@Code", model.CounterCode);
                    cmd.Parameters.AddWithValue("@Name", model.CounterName);
                    cmd.Parameters.AddWithValue("@IsActive", model.IsActive);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private bool UpdateCounter(CounterMaster model, int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
UPDATE dbo.Counters
SET CounterCode = @Code,
    CounterName = @Name,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND BranchId = @BranchId
", connection))
                {
                    cmd.Parameters.AddWithValue("@Id", model.Id);
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    cmd.Parameters.AddWithValue("@Code", model.CounterCode);
                    cmd.Parameters.AddWithValue("@Name", model.CounterName);
                    cmd.Parameters.AddWithValue("@IsActive", model.IsActive);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private bool SetCounterActive(int id, bool isActive, int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
UPDATE dbo.Counters
SET IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND BranchId = @BranchId
", connection))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    cmd.Parameters.AddWithValue("@IsActive", isActive);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private bool IsBranchActive(int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.Branches
WHERE BranchId = @BranchId
  AND ISNULL(IsActive, 1) = 1", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private List<SelectListItem> GetActiveBranchSelectList()
        {
            var items = new List<SelectListItem>();

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT BranchId, BranchCode, BranchName
FROM dbo.Branches
WHERE ISNULL(IsActive, 1) = 1
ORDER BY ISNULL(Is_MainBranch, 0) DESC, BranchCode, BranchName", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var branchId = reader.GetInt32(0);
                        var branchCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var branchName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var text = string.IsNullOrWhiteSpace(branchCode)
                            ? branchName
                            : $"{branchCode} - {branchName}";

                        items.Add(new SelectListItem
                        {
                            Value = branchId.ToString(),
                            Text = text
                        });
                    }
                }
            }

            return items;
        }

        private void EnsureIngredientsBranchColumnExists()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
IF COL_LENGTH('dbo.Ingredients', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.Ingredients ADD BranchId INT NULL;
END
", connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private bool IsMainBranchActiveSession()
        {
            var activeBranchId = User.GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                return false;
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.Branches
WHERE BranchId = @BranchId
  AND ISNULL(IsActive, 1) = 1
  AND ISNULL(Is_MainBranch, 0) = 1", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private IActionResult MainBranchAccessDenied()
        {
            TempData["ResultMessage"] = "Branch Master is accessible only when logged into a Main Branch.";
            return RedirectToAction("Index", "Home");
        }

        // Branch Master List
        public IActionResult BranchList()
        {
            try
            {
                if (!IsMainBranchActiveSession())
                {
                    return MainBranchAccessDenied();
                }

                EnsureBranchesTableExists();
                var branches = ReadBranches();
                return View(branches);
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to load branches: {ex.Message}";
                return View(new List<BranchMaster>());
            }
        }

        // Branch Master Add/Edit/View Form
        public IActionResult BranchForm(int? branchId, bool isView = false)
        {
            try
            {
                if (!IsMainBranchActiveSession())
                {
                    return MainBranchAccessDenied();
                }

                EnsureBranchesTableExists();
                var model = new BranchMaster
                {
                    IsActive = true
                };

                if (branchId.HasValue && branchId.Value > 0)
                {
                    model = ReadBranchById(branchId.Value) ?? model;
                }

                ViewBag.IsView = isView;
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to load branch: {ex.Message}";
                return RedirectToAction(nameof(BranchList));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BranchForm(BranchMaster model, bool isView = false)
        {
            try
            {
                if (!IsMainBranchActiveSession())
                {
                    return MainBranchAccessDenied();
                }

                EnsureBranchesTableExists();

                model.BranchCode = NormalizeBranchCode(model.BranchCode);
                model.BranchName = (model.BranchName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(model.BranchCode))
                {
                    ModelState.AddModelError(nameof(BranchMaster.BranchCode), "Branch Code is required.");
                }

                if (!string.IsNullOrWhiteSpace(model.BranchCode) && model.BranchCode.Length > 4)
                {
                    ModelState.AddModelError(nameof(BranchMaster.BranchCode), "Branch Code must be maximum 4 characters.");
                }

                if (!string.IsNullOrWhiteSpace(model.BranchCode) && !IsBranchCodeAlphanumeric(model.BranchCode))
                {
                    ModelState.AddModelError(nameof(BranchMaster.BranchCode), "Branch Code must be alphanumeric only.");
                }

                if (string.IsNullOrWhiteSpace(model.BranchName))
                {
                    ModelState.AddModelError(nameof(BranchMaster.BranchName), "Branch Name is required.");
                }

                if (!string.IsNullOrWhiteSpace(model.BranchCode) && BranchCodeExists(model.BranchCode, model.BranchId))
                {
                    ModelState.AddModelError(nameof(BranchMaster.BranchCode), "Branch Code already exists.");
                }

                if (model.Is_MainBranch && MainBranchExists(model.BranchId))
                {
                    ModelState.AddModelError(nameof(BranchMaster.Is_MainBranch), "Another main branch already exists.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.IsView = isView;
                    return View(model);
                }

                if (model.BranchId > 0)
                {
                    var updated = UpdateBranch(model);
                    TempData["ResultMessage"] = updated ? "Branch updated successfully." : "Branch update failed.";
                }
                else
                {
                    var inserted = InsertBranch(model);
                    TempData["ResultMessage"] = inserted ? "Branch added successfully." : "Branch add failed.";
                }

                return RedirectToAction(nameof(BranchList));
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to save branch: {ex.Message}";
                ViewBag.IsView = isView;
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetBranchStatus(int branchId, bool isActive)
        {
            try
            {
                if (!IsMainBranchActiveSession())
                {
                    return MainBranchAccessDenied();
                }

                EnsureBranchesTableExists();
                var ok = SetBranchActive(branchId, isActive);
                TempData["ResultMessage"] = ok
                    ? (isActive ? "Branch activated successfully." : "Branch deactivated successfully.")
                    : "Branch status update failed.";
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Branch status update failed: {ex.Message}";
            }

            return RedirectToAction(nameof(BranchList));
        }

        private void EnsureBranchesTableExists()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches
    (
        BranchId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
        BranchCode NVARCHAR(20) NOT NULL,
        BranchName NVARCHAR(150) NOT NULL,
        Is_MainBranch BIT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT (1),
        CreatedAt DATETIME NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT (GETDATE()),
        UpdatedAt DATETIME NULL,
        CONSTRAINT UQ_Branches_BranchCode UNIQUE (BranchCode)
    );
END
", connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private List<BranchMaster> ReadBranches()
        {
            var list = new List<BranchMaster>();
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT BranchId, BranchCode, BranchName, Is_MainBranch, IsActive, CreatedAt, UpdatedAt
FROM dbo.Branches
ORDER BY BranchCode", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new BranchMaster
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

            return list;
        }

        private BranchMaster? ReadBranchById(int branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT TOP 1 BranchId, BranchCode, BranchName, Is_MainBranch, IsActive, CreatedAt, UpdatedAt
FROM dbo.Branches
WHERE BranchId = @BranchId", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        return new BranchMaster
                        {
                            BranchId = reader.GetInt32(0),
                            BranchCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            BranchName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            Is_MainBranch = !reader.IsDBNull(3) && reader.GetBoolean(3),
                            IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
                            CreatedAt = reader.IsDBNull(5) ? DateTime.Now : reader.GetDateTime(5),
                            UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                        };
                    }
                }
            }
        }

        private bool BranchCodeExists(string code, int excludeBranchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.Branches
WHERE UPPER(LTRIM(RTRIM(BranchCode))) = @Code
  AND BranchId <> @ExcludeBranchId", connection))
                {
                    cmd.Parameters.AddWithValue("@Code", NormalizeBranchCode(code));
                    cmd.Parameters.AddWithValue("@ExcludeBranchId", excludeBranchId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private bool MainBranchExists(int excludeBranchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.Branches
WHERE ISNULL(Is_MainBranch, 0) = 1
  AND BranchId <> @ExcludeBranchId", connection))
                {
                    cmd.Parameters.AddWithValue("@ExcludeBranchId", excludeBranchId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private bool InsertBranch(BranchMaster model)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
INSERT INTO dbo.Branches (BranchCode, BranchName, Is_MainBranch, IsActive, CreatedAt, UpdatedAt)
VALUES (@BranchCode, @BranchName, @IsMainBranch, @IsActive, GETDATE(), NULL)
", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchCode", NormalizeBranchCode(model.BranchCode));
                    cmd.Parameters.AddWithValue("@BranchName", model.BranchName);
                    cmd.Parameters.AddWithValue("@IsMainBranch", model.Is_MainBranch);
                    cmd.Parameters.AddWithValue("@IsActive", model.IsActive);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private bool UpdateBranch(BranchMaster model)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
UPDATE dbo.Branches
SET BranchCode = @BranchCode,
    BranchName = @BranchName,
    Is_MainBranch = @IsMainBranch,
    IsActive = @IsActive,
    UpdatedAt = GETDATE()
WHERE BranchId = @BranchId
", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", model.BranchId);
                    cmd.Parameters.AddWithValue("@BranchCode", NormalizeBranchCode(model.BranchCode));
                    cmd.Parameters.AddWithValue("@BranchName", model.BranchName);
                    cmd.Parameters.AddWithValue("@IsMainBranch", model.Is_MainBranch);
                    cmd.Parameters.AddWithValue("@IsActive", model.IsActive);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private static string NormalizeBranchCode(string? branchCode)
        {
            return (branchCode ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool IsBranchCodeAlphanumeric(string branchCode)
        {
            return branchCode.All(char.IsLetterOrDigit);
        }

        private bool SetBranchActive(int branchId, bool isActive)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
UPDATE dbo.Branches
SET IsActive = @IsActive,
    UpdatedAt = GETDATE()
WHERE BranchId = @BranchId
", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    cmd.Parameters.AddWithValue("@IsActive", isActive);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
}
}