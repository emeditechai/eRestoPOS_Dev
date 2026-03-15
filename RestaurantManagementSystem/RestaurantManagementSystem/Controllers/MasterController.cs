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
using RestaurantManagementSystem.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    public class MasterController : Controller
    {
        private readonly RestaurantDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<MasterController> _logger;
        
        public MasterController(RestaurantDbContext dbContext, IConfiguration configuration,
            IEmailSender emailSender, ILogger<MasterController> logger)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _emailSender = emailSender;
            _logger = logger;
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

    // ── Item Master (Ingredients) ─────────────────────────────────────────────

    // JSON endpoint used by PO / GRN / other inventory forms
    [HttpGet]
    public IActionResult GetIngredientsJson()
    {
        var result = new List<object>();
        try
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(@"
                SELECT i.Id, i.IngredientsName, ISNULL(i.Code,'') AS Code,
                       ISNULL(p.UOMId,0)        AS uomId,
                       ISNULL(p.UOMCode,'')     AS uomCode,
                       ISNULL(p.UOMName,'')     AS uomName,
                       ISNULL(i.GSTPercent,0)   AS gstPercent
                FROM   dbo.Ingredients i
                LEFT JOIN dbo.UomMaster p ON p.UOMId = i.PurchaseUOMId
                WHERE  ISNULL(i.IsActive,1) = 1
                ORDER  BY i.IngredientsName", con);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                result.Add(new {
                    id         = rdr.GetInt32(0),
                    name       = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    code       = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    uomId      = rdr.GetInt32(3),
                    uomCode    = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                    uomName    = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                    gstPercent = rdr.IsDBNull(6) ? 0m : rdr.GetDecimal(6)
                });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
        return Json(result);
    }

    // Ingredients List
    public IActionResult IngredientsList()
    {
        EnsureIngredientsColumnsExist();

        // Load with UOM names via LEFT JOIN using raw SQL for reliability
        var ingredients = new List<Ingredients>();
        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            using (var cmd = new SqlCommand(@"
SELECT i.Id, i.IngredientsName, i.DisplayName, i.Code,
       i.ItemCategory, i.Description,
       i.PurchaseUOMId, p.UOMCode AS PurchaseUOMCode, p.UOMName AS PurchaseUOMName,
       i.RecipeUOMId,   r.UOMCode AS RecipeUOMCode,   r.UOMName AS RecipeUOMName,
       i.PurchaseToRecipeFactor, i.StandardCost, i.ReorderLevel,
       ISNULL(i.IsActive, 1) AS IsActive,
       i.CreatedAt, i.UpdatedAt
FROM   dbo.Ingredients i
LEFT JOIN dbo.UomMaster p ON p.UOMId = i.PurchaseUOMId
LEFT JOIN dbo.UomMaster r ON r.UOMId = i.RecipeUOMId
ORDER  BY i.IngredientsName", connection))
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    ingredients.Add(new Ingredients
                    {
                        Id                     = reader.GetInt32(reader.GetOrdinal("Id")),
                        IngredientsName        = reader["IngredientsName"]?.ToString() ?? "",
                        DisplayName            = reader["DisplayName"]?.ToString(),
                        Code                   = reader["Code"]?.ToString(),
                        ItemCategory           = reader["ItemCategory"]?.ToString(),
                        Description            = reader["Description"]?.ToString(),
                        PurchaseUOMId          = reader["PurchaseUOMId"] == DBNull.Value ? null : (int?)reader.GetInt32(reader.GetOrdinal("PurchaseUOMId")),
                        PurchaseUOMCode        = reader["PurchaseUOMCode"]?.ToString(),
                        PurchaseUOMName        = reader["PurchaseUOMName"]?.ToString(),
                        RecipeUOMId            = reader["RecipeUOMId"] == DBNull.Value ? null : (int?)reader.GetInt32(reader.GetOrdinal("RecipeUOMId")),
                        RecipeUOMCode          = reader["RecipeUOMCode"]?.ToString(),
                        RecipeUOMName          = reader["RecipeUOMName"]?.ToString(),
                        PurchaseToRecipeFactor = reader["PurchaseToRecipeFactor"] == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("PurchaseToRecipeFactor")),
                        StandardCost           = reader["StandardCost"] == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("StandardCost")),
                        ReorderLevel           = reader["ReorderLevel"] == DBNull.Value ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("ReorderLevel")),
                        IsActive               = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        CreatedAt              = reader["CreatedAt"] == DBNull.Value ? null : (DateTime?)reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt              = reader["UpdatedAt"] == DBNull.Value ? null : (DateTime?)reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                    });
                }
            }
        }

        ViewBag.Categories = GetItemCategoryList();
        return View(ingredients);
    }

    // Add/Edit/View Form
    public IActionResult IngredientsForm(int? id, bool isView = false)
    {
        EnsureIngredientsColumnsExist();
        Ingredients model = new Ingredients { IngredientsName = "", IsActive = true };

        if (id.HasValue && id.Value > 0)
        {
            model = _dbContext.Ingredients.FirstOrDefault(i => i.Id == id.Value) ?? model;
        }

        ViewBag.IsView     = isView;
        ViewBag.AllUoms    = GetUomSelectList();
        ViewBag.Categories = GetItemCategoryList();
        return View("Ingredients", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult IngredientsForm(Ingredients model)
    {
        EnsureIngredientsColumnsExist();

        // Remove navigation property validation noise
        ModelState.Remove(nameof(Ingredients.PurchaseUOM));
        ModelState.Remove(nameof(Ingredients.RecipeUOM));

        if (ModelState.IsValid)
        {
            if (model.Id == 0)
            {
                model.CreatedAt = DateTime.UtcNow;
                model.UpdatedAt = null;
                _dbContext.Ingredients.Add(model);
                _dbContext.SaveChanges();
                TempData["ResultMessage"] = "Item added successfully.";
            }
            else
            {
                var existing = _dbContext.Ingredients.FirstOrDefault(i => i.Id == model.Id);
                if (existing != null)
                {
                    existing.IngredientsName        = model.IngredientsName;
                    existing.DisplayName            = model.DisplayName;
                    existing.Code                   = model.Code;
                    existing.ItemCategory           = model.ItemCategory;
                    existing.Description            = model.Description;
                    existing.PurchaseUOMId          = model.PurchaseUOMId;
                    existing.RecipeUOMId            = model.RecipeUOMId;
                    existing.PurchaseToRecipeFactor = model.PurchaseToRecipeFactor;
                    existing.StandardCost           = model.StandardCost;
                    existing.ReorderLevel           = model.ReorderLevel;
                    existing.GSTPercent             = model.GSTPercent;
                    existing.IsActive               = model.IsActive;
                    existing.UpdatedAt              = DateTime.UtcNow;
                    _dbContext.SaveChanges();
                    TempData["ResultMessage"] = "Item updated successfully.";
                }
                else
                {
                    TempData["ResultMessage"] = "Item update failed. Record not found.";
                }
            }
            return RedirectToAction("IngredientsList");
        }

        ViewBag.IsView     = false;
        ViewBag.AllUoms    = GetUomSelectList();
        ViewBag.Categories = GetItemCategoryList();
        return View("Ingredients", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ToggleIngredientActive(int id)
    {
        var item = _dbContext.Ingredients.FirstOrDefault(i => i.Id == id);
        if (item != null)
        {
            item.IsActive  = !item.IsActive;
            item.UpdatedAt = DateTime.UtcNow;
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"Item {(item.IsActive ? "activated" : "deactivated")} successfully.";
        }
        return RedirectToAction(nameof(IngredientsList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteIngredient(int id)
    {
        // Guard: check if used in recipe BOM
        var inUse = _dbContext.MenuItemIngredients.Any(m => m.IngredientId == id);
        if (inUse)
        {
            TempData["ResultMessage"] = "Cannot delete: this item is linked to one or more recipes. Deactivate it instead.";
            return RedirectToAction(nameof(IngredientsList));
        }

        var item = _dbContext.Ingredients.FirstOrDefault(i => i.Id == id);
        if (item != null)
        {
            _dbContext.Ingredients.Remove(item);
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = "Item deleted successfully.";
        }
        return RedirectToAction(nameof(IngredientsList));
    }

    // Helper: UOM select list for dropdowns
    private SelectList GetUomSelectList(int? selected = null)
    {
        var uoms = _dbContext.UomMasters
            .Where(u => u.IsActive)
            .OrderBy(u => u.UOMType).ThenBy(u => u.UOMName)
            .Select(u => new { u.UOMId, Label = $"{u.UOMCode} – {u.UOMName}" })
            .ToList();
        return new SelectList(uoms, "UOMId", "Label", selected);
    }

    // Helper: Item category list – reads from dbo.StockItemCategories table
    private SelectList GetItemCategoryList(string? selected = null)
    {
        EnsureStockItemCategoriesTableExists();
        var cats = _dbContext.StockItemCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { Val = c.Name, Txt = c.Name })
            .ToList();
        return new SelectList(cats, "Val", "Txt", selected);
    }

    // ── Stock Item Category CRUD ──────────────────────────────────────────────

    public IActionResult StockCategoryList()
    {
        EnsureStockItemCategoriesTableExists();
        var cats = _dbContext.StockItemCategories
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .ToList();
        return View(cats);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StockCategorySave(StockItemCategory model)
    {
        EnsureStockItemCategoriesTableExists();
        ModelState.Remove(nameof(StockItemCategory.Description));

        if (!ModelState.IsValid)
        {
            TempData["ResultMessage"] = "Validation failed. Please check the form.";
            return RedirectToAction(nameof(StockCategoryList));
        }

        if (model.Id == 0)
        {
            model.CreatedAt = DateTime.UtcNow;
            _dbContext.StockItemCategories.Add(model);
            TempData["ResultMessage"] = $"Category '{model.Name}' added successfully.";
        }
        else
        {
            var existing = _dbContext.StockItemCategories.FirstOrDefault(c => c.Id == model.Id);
            if (existing != null)
            {
                existing.Name         = model.Name;
                existing.Description  = model.Description;
                existing.DisplayOrder = model.DisplayOrder;
                existing.IsActive     = model.IsActive;
                existing.UpdatedAt    = DateTime.UtcNow;
                TempData["ResultMessage"] = $"Category '{model.Name}' updated successfully.";
            }
        }

        _dbContext.SaveChanges();
        return RedirectToAction(nameof(StockCategoryList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StockCategoryToggleActive(int id)
    {
        var cat = _dbContext.StockItemCategories.FirstOrDefault(c => c.Id == id);
        if (cat != null)
        {
            cat.IsActive  = !cat.IsActive;
            cat.UpdatedAt = DateTime.UtcNow;
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"'{cat.Name}' {(cat.IsActive ? "activated" : "deactivated")}.";
        }
        return RedirectToAction(nameof(StockCategoryList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StockCategoryDelete(int id)
    {
        // Guard: used in Item Master?
        var inUse = _dbContext.Ingredients.Any(i => i.ItemCategory != null &&
                    _dbContext.StockItemCategories.Any(c => c.Id == id && c.Name == i.ItemCategory));
        if (inUse)
        {
            TempData["ResultMessage"] = "Cannot delete: this category is used by one or more items. Deactivate it instead.";
            return RedirectToAction(nameof(StockCategoryList));
        }

        var cat = _dbContext.StockItemCategories.FirstOrDefault(c => c.Id == id);
        if (cat != null)
        {
            _dbContext.StockItemCategories.Remove(cat);
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"Category '{cat.Name}' deleted.";
        }
        return RedirectToAction(nameof(StockCategoryList));
    }

    private void EnsureStockItemCategoriesTableExists()
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.StockItemCategories') IS NULL
BEGIN
    CREATE TABLE dbo.StockItemCategories (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        Name         NVARCHAR(100) NOT NULL,
        Description  NVARCHAR(300) NULL,
        DisplayOrder INT           NOT NULL DEFAULT 0,
        IsActive     BIT           NOT NULL DEFAULT 1,
        CreatedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2     NULL
    );

    -- Seed default categories
    INSERT INTO dbo.StockItemCategories (Name, DisplayOrder, IsActive, CreatedAt) VALUES
        ('Vegetable',         1,  1, SYSUTCDATETIME()),
        ('Meat',              2,  1, SYSUTCDATETIME()),
        ('Seafood',           3,  1, SYSUTCDATETIME()),
        ('Spice & Herb',      4,  1, SYSUTCDATETIME()),
        ('Dairy',             5,  1, SYSUTCDATETIME()),
        ('Grain & Flour',     6,  1, SYSUTCDATETIME()),
        ('Beverage',          7,  1, SYSUTCDATETIME()),
        ('Sauce & Condiment', 8,  1, SYSUTCDATETIME()),
        ('Packaging',         9,  1, SYSUTCDATETIME()),
        ('Finish Goods',      10, 1, SYSUTCDATETIME()),
        ('Other',             11, 1, SYSUTCDATETIME());
END
", connection);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    //  GODOWN MASTER
    // ═══════════════════════════════════════════════════════════════

    public IActionResult GodownList()
    {
        var activeBranchId = User.GetActiveBranchId();
        if (!activeBranchId.HasValue)
        {
            TempData["ResultMessage"] = "Please select an active branch first.";
            return View(new List<Godown>());
        }
        EnsureGodownsTableExists();
        var godowns = _dbContext.Godowns
            .Where(g => g.BranchId == activeBranchId.Value)
            .OrderByDescending(g => g.IsMainGodown)
            .ThenBy(g => g.GodownName)
            .ToList();
        return View(godowns);
    }

    [HttpGet]
    public IActionResult GodownForm(int? id)
    {
        var activeBranchId = User.GetActiveBranchId();
        if (!activeBranchId.HasValue)
        {
            TempData["ResultMessage"] = "Please select an active branch first.";
            return RedirectToAction(nameof(GodownList));
        }
        EnsureGodownsTableExists();

        Godown model;
        if (id.HasValue && id.Value > 0)
        {
            var existing = _dbContext.Godowns.FirstOrDefault(g => g.Id == id.Value && g.BranchId == activeBranchId.Value);
            if (existing == null)
            {
                TempData["ResultMessage"] = "Godown not found.";
                return RedirectToAction(nameof(GodownList));
            }
            model = existing;
        }
        else
        {
            model = new Godown { BranchId = activeBranchId.Value, IsActive = true };
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GodownForm(Godown model)
    {
        var activeBranchId = User.GetActiveBranchId();
        if (!activeBranchId.HasValue)
        {
            TempData["ResultMessage"] = "Please select an active branch first.";
            return RedirectToAction(nameof(GodownList));
        }
        model.BranchId = activeBranchId.Value;
        EnsureGodownsTableExists();

        ModelState.Remove(nameof(Godown.Address));

        if (!ModelState.IsValid)
            return View(model);

        // Normalise code to uppercase
        model.Code = model.Code.Trim().ToUpper();

        // Unique-code check within branch
        bool codeExists = _dbContext.Godowns.Any(g =>
            g.BranchId == activeBranchId.Value &&
            g.Code == model.Code &&
            g.Id != model.Id);
        if (codeExists)
        {
            ModelState.AddModelError(nameof(Godown.Code), $"Code '{model.Code}' already exists in this branch.");
            return View(model);
        }

        // Main-godown uniqueness: only one per branch
        if (model.IsMainGodown)
        {
            bool mainExists = _dbContext.Godowns.Any(g =>
                g.BranchId == activeBranchId.Value &&
                g.IsMainGodown &&
                g.Id != model.Id);
            if (mainExists)
            {
                ModelState.AddModelError(nameof(Godown.IsMainGodown),
                    "This branch already has a Main Godown. Please deselect the existing one first.");
                return View(model);
            }
        }

        if (model.Id == 0)
        {
            model.CreatedAt = DateTime.UtcNow;
            _dbContext.Godowns.Add(model);
            TempData["ResultMessage"] = $"Godown '{model.GodownName}' added successfully.";
        }
        else
        {
            var existing = _dbContext.Godowns.FirstOrDefault(g => g.Id == model.Id && g.BranchId == activeBranchId.Value);
            if (existing == null)
            {
                TempData["ResultMessage"] = "Godown not found.";
                return RedirectToAction(nameof(GodownList));
            }
            existing.Code          = model.Code;
            existing.GodownName    = model.GodownName;
            existing.IsMainGodown  = model.IsMainGodown;
            existing.Address       = model.Address;
            existing.IsActive      = model.IsActive;
            existing.UpdatedAt     = DateTime.UtcNow;
            TempData["ResultMessage"] = $"Godown '{model.GodownName}' updated successfully.";
        }

        _dbContext.SaveChanges();
        return RedirectToAction(nameof(GodownList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GodownToggleActive(int id)
    {
        var activeBranchId = User.GetActiveBranchId();
        var g = _dbContext.Godowns.FirstOrDefault(x => x.Id == id && x.BranchId == activeBranchId);
        if (g != null)
        {
            g.IsActive  = !g.IsActive;
            g.UpdatedAt = DateTime.UtcNow;
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"'{g.GodownName}' {(g.IsActive ? "activated" : "deactivated")}.";
        }
        return RedirectToAction(nameof(GodownList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GodownDelete(int id)
    {
        var activeBranchId = User.GetActiveBranchId();
        var g = _dbContext.Godowns.FirstOrDefault(x => x.Id == id && x.BranchId == activeBranchId);
        if (g != null)
        {
            if (g.IsMainGodown)
            {
                TempData["ResultMessage"] = "Cannot delete the Main Godown. Set another godown as Main first.";
                return RedirectToAction(nameof(GodownList));
            }
            _dbContext.Godowns.Remove(g);
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"Godown '{g.GodownName}' deleted.";
        }
        return RedirectToAction(nameof(GodownList));
    }

    private void EnsureGodownsTableExists()
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.Godowns') IS NULL
BEGIN
    CREATE TABLE dbo.Godowns (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        BranchId      INT           NOT NULL,
        Code          NVARCHAR(20)  NOT NULL,
        GodownName    NVARCHAR(150) NOT NULL,
        IsMainGodown  BIT           NOT NULL DEFAULT 0,
        Address       NVARCHAR(500) NULL,
        IsActive      BIT           NOT NULL DEFAULT 1,
        CreatedAt     DATETIME2     NULL,
        UpdatedAt     DATETIME2     NULL,
        CONSTRAINT UQ_Godowns_BranchCode UNIQUE (BranchId, Code)
    );
END
", connection);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    //  PARTY MASTER
    // ═══════════════════════════════════════════════════════════════

    public IActionResult PartyList()
    {
        EnsurePartiesTableExists();
        var parties = _dbContext.Parties
            .OrderBy(p => p.PartyName)
            .ToList();
        return View(parties);
    }

    [HttpGet]
    public IActionResult PartyForm(int? id, bool isView = false)
    {
        EnsurePartiesTableExists();
        PartyMaster model;
        if (id.HasValue && id.Value > 0)
        {
            var existing = _dbContext.Parties.FirstOrDefault(p => p.Id == id.Value);
            if (existing == null)
            {
                TempData["ResultMessage"] = "Party not found.";
                return RedirectToAction(nameof(PartyList));
            }
            model = existing;
        }
        else
        {
            model = new PartyMaster { IsActive = true };
            isView = false; // cannot view a new record
        }
        ViewBag.IsView = isView;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PartyForm(PartyMaster model, bool SendNotification = false)
    {
        EnsurePartiesTableExists();

        ModelState.Remove(nameof(PartyMaster.Email));
        ModelState.Remove(nameof(PartyMaster.Address));
        ModelState.Remove(nameof(PartyMaster.PinCode));
        ModelState.Remove(nameof(PartyMaster.AllowBalance));

        if (!ModelState.IsValid)
            return View(model);

        // Normalise code
        model.PartyCode = model.PartyCode.Trim().ToUpper();

        // Unique code check
        bool codeExists = _dbContext.Parties.Any(p =>
            p.PartyCode == model.PartyCode && p.Id != model.Id);
        if (codeExists)
        {
            ModelState.AddModelError(nameof(PartyMaster.PartyCode),
                $"Party Code '{model.PartyCode}' already exists.");
            return View(model);
        }

        // If credit not allowed, clear balance
        if (!model.IsCreditAllow)
            model.AllowBalance = null;

        bool isNew = model.Id == 0;

        if (isNew)
        {
            model.CreatedAt = DateTime.UtcNow;
            _dbContext.Parties.Add(model);
            TempData["ResultMessage"] = $"Party '{model.PartyName}' added successfully.";
        }
        else
        {
            var existing = _dbContext.Parties.FirstOrDefault(p => p.Id == model.Id);
            if (existing == null)
            {
                TempData["ResultMessage"] = "Party not found.";
                return RedirectToAction(nameof(PartyList));
            }
            existing.PartyCode     = model.PartyCode;
            existing.PartyName     = model.PartyName;
            existing.PartyType     = model.PartyType;
            existing.Email         = model.Email;
            existing.PhoneNumber   = model.PhoneNumber;
            existing.Address       = model.Address;
            existing.PinCode       = model.PinCode;
            existing.IsCreditAllow = model.IsCreditAllow;
            existing.AllowBalance  = model.AllowBalance;
            existing.IsActive      = model.IsActive;
            existing.UpdatedAt     = DateTime.UtcNow;
            TempData["ResultMessage"] = $"Party '{model.PartyName}' updated successfully.";
        }

        _dbContext.SaveChanges();

        // Send welcome email only when creating a new party AND the checkbox was ticked
        if (isNew && SendNotification)
            await SendPartyEmailNotificationAsync(model);

        return RedirectToAction(nameof(PartyList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PartyToggleActive(int id)
    {
        var party = _dbContext.Parties.FirstOrDefault(p => p.Id == id);
        if (party != null)
        {
            party.IsActive  = !party.IsActive;
            party.UpdatedAt = DateTime.UtcNow;
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"'{party.PartyName}' {(party.IsActive ? "activated" : "deactivated")}.";
        }
        return RedirectToAction(nameof(PartyList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PartyDelete(int id)
    {
        var party = _dbContext.Parties.FirstOrDefault(p => p.Id == id);
        if (party != null)
        {
            _dbContext.Parties.Remove(party);
            _dbContext.SaveChanges();
            TempData["ResultMessage"] = $"Party '{party.PartyName}' deleted.";
        }
        return RedirectToAction(nameof(PartyList));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Party Email Notification
    // ──────────────────────────────────────────────────────────────────────────

    private async Task SendPartyEmailNotificationAsync(PartyMaster party)
    {
        if (string.IsNullOrWhiteSpace(party.Email)) return;

        var subject = $"Welcome to Our Network – {party.PartyName}";
        var htmlBody = BuildPartyEmailHtml(party);

        try
        {
            var result = await _emailSender.SendAsync(
                party.Email,
                subject,
                htmlBody,
                emailType: "PartyWelcome",
                sentFrom: "MasterController");

            if (!result.Success)
                _logger.LogWarning("Party email not sent to {Email}: {Error}", party.Email, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending party email to {Email}", party.Email);
        }
    }

    private static string BuildPartyEmailHtml(PartyMaster party)
    {
        const string headerBg    = "#1a6b4a";
        const string accentColor = "#28a745";
        var dateStr = DateTime.Now.ToString("dd MMM yyyy, hh:mm tt");
        var creditSection = party.IsCreditAllow
            ? $@"<tr>
                    <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Credit Allowed</td>
                    <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;font-weight:600;color:{accentColor};"">Yes</td>
                 </tr>
                 {(party.AllowBalance.HasValue ? $@"<tr>
                    <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Credit Limit</td>
                    <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;font-weight:600;color:#333;"">₹ {party.AllowBalance:N2}</td>
                 </tr>" : "")}"
            : $@"<tr>
                    <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Credit Allowed</td>
                    <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;color:#888;"">No</td>
                 </tr>";

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"" />
<meta name=""viewport"" content=""width=device-width,initial-scale=1.0""/>
<title>Welcome – {party.PartyName}</title>
</head>
<body style=""margin:0;padding:0;background:#f4f6f9;font-family:Arial,Helvetica,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f4f6f9;padding:30px 0;"">
    <tr><td align=""center"">

      <!-- Card -->
      <table width=""620"" cellpadding=""0"" cellspacing=""0""
             style=""background:#fff;border-radius:10px;overflow:hidden;
                    box-shadow:0 4px 20px rgba(0,0,0,0.10);max-width:620px;"">

        <!-- Header -->
        <tr>
          <td style=""background:{headerBg};padding:40px 40px 30px;text-align:center;"">
            <div style=""display:inline-block;background:rgba(255,255,255,0.15);
                        border-radius:50%;padding:16px;margin-bottom:12px;"">
              <span style=""font-size:36px;"">🤝</span>
            </div>
            <h1 style=""margin:0 0 6px;color:#fff;font-size:26px;font-weight:700;
                        letter-spacing:0.5px;"">Welcome!</h1>
            <p style=""margin:0;color:rgba(255,255,255,0.85);font-size:14px;"">We are delighted to welcome you to our partner network.</p>
          </td>
        </tr>

        <!-- Status Badge -->
        <tr>
          <td style=""background:#f8f9fa;padding:14px 40px;text-align:center;
                      border-bottom:1px solid #e8ecf0;"">
            <span style=""display:inline-block;background:{accentColor};color:#fff;
                          font-size:12px;font-weight:700;letter-spacing:1px;
                          padding:5px 18px;border-radius:20px;text-transform:uppercase;"">
              New Partner
            </span>
            <span style=""margin-left:12px;color:#888;font-size:12px;"">
              {dateStr}
            </span>
          </td>
        </tr>

        <!-- Body -->
        <tr>
          <td style=""padding:34px 40px 28px;"">

            <h2 style=""margin:0 0 18px;font-size:16px;color:#333;font-weight:700;
                        text-transform:uppercase;letter-spacing:0.5px;
                        border-left:4px solid {accentColor};padding-left:12px;"">
              Party Details
            </h2>

            <table width=""100%"" cellpadding=""0"" cellspacing=""0""
                   style=""border:1px solid #e8ecf0;border-radius:8px;overflow:hidden;"">
              <tr style=""background:{headerBg};"">
                <td style=""padding:10px 12px;color:#fff;font-size:13px;font-weight:700;
                            text-transform:uppercase;letter-spacing:0.5px;"">Field</td>
                <td style=""padding:10px 12px;color:#fff;font-size:13px;font-weight:700;
                            text-transform:uppercase;letter-spacing:0.5px;"">Value</td>
              </tr>
              <tr>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Party Code</td>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;font-weight:700;color:{accentColor};"">
                  {System.Net.WebUtility.HtmlEncode(party.PartyCode)}
                </td>
              </tr>
              <tr style=""background:#fafafa;"">
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Party Name</td>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;font-weight:600;color:#333;"">
                  {System.Net.WebUtility.HtmlEncode(party.PartyName)}
                </td>
              </tr>
              <tr>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Party Type</td>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;color:#333;"">
                  {System.Net.WebUtility.HtmlEncode(party.PartyType)}
                </td>
              </tr>
              <tr style=""background:#fafafa;"">
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Phone</td>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;color:#333;"">
                  {System.Net.WebUtility.HtmlEncode(party.PhoneNumber)}
                </td>
              </tr>
              <tr>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Email</td>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;color:#333;"">
                  {System.Net.WebUtility.HtmlEncode(party.Email ?? "-")}
                </td>
              </tr>
              {(!string.IsNullOrWhiteSpace(party.Address) ? $@"<tr style=""background:#fafafa;"">
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;color:#555;font-size:14px;"">Address</td>
                <td style=""padding:8px 10px;border-bottom:1px solid #f0f0f0;font-size:14px;color:#333;"">
                  {System.Net.WebUtility.HtmlEncode(party.Address)}{(!string.IsNullOrWhiteSpace(party.PinCode) ? " – " + System.Net.WebUtility.HtmlEncode(party.PinCode) : "")}
                </td>
              </tr>" : "")}
              {creditSection}
              <tr>
                <td style=""padding:8px 10px;color:#555;font-size:14px;"">Status</td>
                <td style=""padding:8px 10px;font-size:14px;font-weight:600;
                            color:{(party.IsActive ? "#28a745" : "#dc3545")};"">
                  {(party.IsActive ? "✅ Active" : "⛔ Inactive")}
                </td>
              </tr>
            </table>

          </td>
        </tr>

        <!-- Info Box -->
        <tr>
          <td style=""padding:0 40px 30px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0""
                   style=""background:#f0f7ff;border-left:4px solid {accentColor};
                           border-radius:4px;padding:16px;"">
              <tr>
                <td style=""padding:14px 16px;font-size:13px;color:#444;line-height:1.6;"">
                  Please keep this email for your records. If you have any questions about your account or need assistance, contact our support team.
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- Footer -->
        <tr>
          <td style=""background:{headerBg};padding:22px 40px;text-align:center;"">
            <p style=""margin:0 0 6px;color:rgba(255,255,255,0.9);font-size:13px;"">
              This is an automated notification from the Restaurant Management System.
            </p>
            <p style=""margin:0;color:rgba(255,255,255,0.6);font-size:11px;"">
              © {DateTime.Now.Year} Restaurant Management System. All rights reserved.
            </p>
          </td>
        </tr>

      </table>
      <!-- /Card -->

    </td></tr>
  </table>
</body>
</html>";
    }

    // ──────────────────────────────────────────────────────────────────────────

    private void EnsurePartiesTableExists()
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.Parties') IS NULL
BEGIN
    CREATE TABLE dbo.Parties (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Parties PRIMARY KEY,
        PartyCode     NVARCHAR(20)   NOT NULL,
        PartyName     NVARCHAR(200)  NOT NULL,
        PartyType     NVARCHAR(20)   NOT NULL,
        Email         NVARCHAR(200)  NULL,
        PhoneNumber   NVARCHAR(20)   NOT NULL,
        Address       NVARCHAR(500)  NULL,
        PinCode       NVARCHAR(10)   NULL,
        IsCreditAllow BIT            NOT NULL CONSTRAINT DF_Parties_IsCreditAllow DEFAULT 0,
        AllowBalance  DECIMAL(18,2)  NULL,
        IsActive      BIT            NOT NULL CONSTRAINT DF_Parties_IsActive      DEFAULT 1,
        CreatedAt     DATETIME2      NULL,
        UpdatedAt     DATETIME2      NULL,
        CONSTRAINT UQ_Parties_PartyCode UNIQUE (PartyCode)
    );
END
", connection);
        cmd.ExecuteNonQuery();
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
SELECT b.BranchId, b.BranchCode, b.BranchName, ISNULL(bl.LocationName, '') AS LocationName
FROM dbo.Branches b
LEFT JOIN dbo.BranchLocations bl ON bl.LocationId = b.BranchLocationId
WHERE ISNULL(b.IsActive, 1) = 1
ORDER BY ISNULL(b.Is_MainBranch, 0) DESC, b.BranchCode, b.BranchName", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var branchId       = reader.GetInt32(0);
                        var branchCode     = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var branchName     = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var locationName   = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        var displayName    = string.IsNullOrWhiteSpace(locationName)
                            ? branchName
                            : $"{branchName} - {locationName}";
                        var text = string.IsNullOrWhiteSpace(branchCode)
                            ? displayName
                            : $"{branchCode} - {displayName}";

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

        private void EnsureIngredientsColumnsExist()
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var cmd = new SqlCommand(@"
-- Original column
IF COL_LENGTH('dbo.Ingredients', 'BranchId') IS NULL
    ALTER TABLE dbo.Ingredients ADD BranchId INT NULL;

-- Item Master extension columns
IF COL_LENGTH('dbo.Ingredients', 'ItemCategory') IS NULL
    ALTER TABLE dbo.Ingredients ADD ItemCategory NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.Ingredients', 'Description') IS NULL
    ALTER TABLE dbo.Ingredients ADD Description NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.Ingredients', 'PurchaseUOMId') IS NULL
    ALTER TABLE dbo.Ingredients ADD PurchaseUOMId INT NULL;

IF COL_LENGTH('dbo.Ingredients', 'RecipeUOMId') IS NULL
    ALTER TABLE dbo.Ingredients ADD RecipeUOMId INT NULL;

IF COL_LENGTH('dbo.Ingredients', 'PurchaseToRecipeFactor') IS NULL
    ALTER TABLE dbo.Ingredients ADD PurchaseToRecipeFactor DECIMAL(18,6) NULL;

IF COL_LENGTH('dbo.Ingredients', 'StandardCost') IS NULL
    ALTER TABLE dbo.Ingredients ADD StandardCost DECIMAL(18,4) NULL;

IF COL_LENGTH('dbo.Ingredients', 'ReorderLevel') IS NULL
    ALTER TABLE dbo.Ingredients ADD ReorderLevel DECIMAL(18,3) NULL;

IF COL_LENGTH('dbo.Ingredients', 'GSTPercent') IS NULL
    ALTER TABLE dbo.Ingredients ADD GSTPercent DECIMAL(5,2) NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.Ingredients', 'IsActive') IS NULL
    ALTER TABLE dbo.Ingredients ADD IsActive BIT NOT NULL DEFAULT 1;

IF COL_LENGTH('dbo.Ingredients', 'CreatedAt') IS NULL
    ALTER TABLE dbo.Ingredients ADD CreatedAt DATETIME2 NULL DEFAULT SYSUTCDATETIME();

-- Backfill NULL CreatedAt for rows added before this column existed
UPDATE dbo.Ingredients SET CreatedAt = SYSUTCDATETIME() WHERE CreatedAt IS NULL;

IF COL_LENGTH('dbo.Ingredients', 'UpdatedAt') IS NULL
    ALTER TABLE dbo.Ingredients ADD UpdatedAt DATETIME2 NULL;

-- FK: Ingredients.PurchaseUOMId → UomMaster (idempotent)
IF OBJECT_ID('dbo.UomMaster') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = 'FK_Ingredients_PurchaseUOM' AND parent_object_id = OBJECT_ID('dbo.Ingredients'))
    BEGIN
        ALTER TABLE dbo.Ingredients
            ADD CONSTRAINT FK_Ingredients_PurchaseUOM
            FOREIGN KEY (PurchaseUOMId) REFERENCES dbo.UomMaster(UOMId);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = 'FK_Ingredients_RecipeUOM' AND parent_object_id = OBJECT_ID('dbo.Ingredients'))
    BEGIN
        ALTER TABLE dbo.Ingredients
            ADD CONSTRAINT FK_Ingredients_RecipeUOM
            FOREIGN KEY (RecipeUOMId) REFERENCES dbo.UomMaster(UOMId);
    END
END

-- FK: MenuItemIngredients.UOMId → UomMaster (only when both tables exist)
IF OBJECT_ID('dbo.UomMaster') IS NOT NULL
   AND OBJECT_ID('dbo.MenuItemIngredients') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.MenuItemIngredients', 'UOMId') IS NULL
        ALTER TABLE dbo.MenuItemIngredients ADD UOMId INT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = 'FK_MenuItemIngredients_UOM'
          AND parent_object_id = OBJECT_ID('dbo.MenuItemIngredients'))
    BEGIN
        ALTER TABLE dbo.MenuItemIngredients
            ADD CONSTRAINT FK_MenuItemIngredients_UOM
            FOREIGN KEY (UOMId) REFERENCES dbo.UomMaster(UOMId);
    END

    -- Make Unit column nullable (backward compat)
    IF EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        WHERE o.name = 'MenuItemIngredients' AND c.name = 'Unit' AND c.is_nullable = 0)
    BEGIN
        ALTER TABLE dbo.MenuItemIngredients ALTER COLUMN Unit NVARCHAR(20) NULL;
    END
END
", connection);
            cmd.ExecuteNonQuery();
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
                ViewBag.BranchLocations = GetBranchLocationSelectList();
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

                if (model.BranchLocationId <= 0)
                {
                    ModelState.AddModelError(nameof(BranchMaster.BranchLocationId), "Branch Location is required.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.IsView = isView;
                    ViewBag.BranchLocations = GetBranchLocationSelectList();
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
-- 1. BranchLocations master table
IF OBJECT_ID(N'dbo.BranchLocations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BranchLocations
    (
        LocationId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BranchLocations PRIMARY KEY,
        LocationName NVARCHAR(100) NOT NULL,
        IsActive     BIT NOT NULL CONSTRAINT DF_BranchLocations_IsActive DEFAULT (1),
        CONSTRAINT UQ_BranchLocations_Name UNIQUE (LocationName)
    );
    -- Seed with common locations
    INSERT INTO dbo.BranchLocations (LocationName) VALUES ('Kolkata'),('Kalyani'),('Howrah');
END

-- 2. Branches table
IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches
    (
        BranchId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
        BranchCode       NVARCHAR(20) NOT NULL,
        BranchName       NVARCHAR(150) NOT NULL,
        BranchLocationId INT NULL,
        Is_MainBranch    BIT NULL,
        IsActive         BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT (1),
        CreatedAt        DATETIME NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT (GETDATE()),
        UpdatedAt        DATETIME NULL,
        CONSTRAINT UQ_Branches_BranchCode UNIQUE (BranchCode)
    );
END

-- 3. Migrate: add BranchLocationId column if Branches existed before
IF COL_LENGTH(N'dbo.Branches', N'BranchLocationId') IS NULL
    ALTER TABLE dbo.Branches ADD BranchLocationId INT NULL;
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
SELECT b.BranchId, b.BranchCode, b.BranchName, b.Is_MainBranch, b.IsActive, b.CreatedAt, b.UpdatedAt,
       ISNULL(b.BranchLocationId, 0) AS BranchLocationId,
       ISNULL(bl.LocationName, '') AS LocationName
FROM dbo.Branches b
LEFT JOIN dbo.BranchLocations bl ON bl.LocationId = b.BranchLocationId
ORDER BY b.BranchCode", connection))
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
                            UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                            BranchLocationId = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                            BranchLocationName = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
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
SELECT TOP 1 b.BranchId, b.BranchCode, b.BranchName, b.Is_MainBranch, b.IsActive, b.CreatedAt, b.UpdatedAt,
       ISNULL(b.BranchLocationId, 0) AS BranchLocationId,
       ISNULL(bl.LocationName, '') AS LocationName
FROM dbo.Branches b
LEFT JOIN dbo.BranchLocations bl ON bl.LocationId = b.BranchLocationId
WHERE b.BranchId = @BranchId", connection))
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
                            UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                            BranchLocationId = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                            BranchLocationName = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
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
INSERT INTO dbo.Branches (BranchCode, BranchName, BranchLocationId, Is_MainBranch, IsActive, CreatedAt, UpdatedAt)
VALUES (@BranchCode, @BranchName, @BranchLocationId, @IsMainBranch, @IsActive, GETDATE(), NULL)
", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchCode", NormalizeBranchCode(model.BranchCode));
                    cmd.Parameters.AddWithValue("@BranchName", model.BranchName);
                    cmd.Parameters.AddWithValue("@BranchLocationId", model.BranchLocationId);
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
    BranchLocationId = @BranchLocationId,
    Is_MainBranch = @IsMainBranch,
    IsActive = @IsActive,
    UpdatedAt = GETDATE()
WHERE BranchId = @BranchId
", connection))
                {
                    cmd.Parameters.AddWithValue("@BranchId", model.BranchId);
                    cmd.Parameters.AddWithValue("@BranchCode", NormalizeBranchCode(model.BranchCode));
                    cmd.Parameters.AddWithValue("@BranchName", model.BranchName);
                    cmd.Parameters.AddWithValue("@BranchLocationId", model.BranchLocationId);
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

        // ─────────────────────────────────────────────
        // Branch Location Master CRUD
        // ─────────────────────────────────────────────

        public IActionResult BranchLocationList()
        {
            try
            {
                if (!IsMainBranchActiveSession()) return MainBranchAccessDenied();
                EnsureBranchesTableExists();
                var list = ReadBranchLocations();
                return View(list);
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to load locations: {ex.Message}";
                return View(new List<BranchLocation>());
            }
        }

        public IActionResult BranchLocationForm(int? locationId, bool isView = false)
        {
            try
            {
                if (!IsMainBranchActiveSession()) return MainBranchAccessDenied();
                EnsureBranchesTableExists();
                var model = new BranchLocation { IsActive = true };
                if (locationId.HasValue && locationId.Value > 0)
                    model = ReadBranchLocationById(locationId.Value) ?? model;
                ViewBag.IsView = isView;
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to load location: {ex.Message}";
                return RedirectToAction(nameof(BranchLocationList));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BranchLocationForm(BranchLocation model, bool isView = false)
        {
            try
            {
                if (!IsMainBranchActiveSession()) return MainBranchAccessDenied();
                EnsureBranchesTableExists();

                model.LocationName = (model.LocationName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(model.LocationName))
                    ModelState.AddModelError(nameof(BranchLocation.LocationName), "Location Name is required.");

                if (!ModelState.IsValid)
                {
                    ViewBag.IsView = isView;
                    return View(model);
                }

                bool ok = model.LocationId > 0
                    ? UpdateBranchLocation(model)
                    : InsertBranchLocation(model);

                TempData["ResultMessage"] = ok
                    ? (model.LocationId > 0 ? "Location updated successfully." : "Location added successfully.")
                    : (model.LocationId > 0 ? "Location update failed." : "Location add failed.");

                return RedirectToAction(nameof(BranchLocationList));
            }
            catch (Exception ex)
            {
                TempData["ResultMessage"] = $"Failed to save location: {ex.Message}";
                ViewBag.IsView = isView;
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetBranchLocationStatus(int locationId, bool isActive)
        {
            try
            {
                if (!IsMainBranchActiveSession()) return MainBranchAccessDenied();
                EnsureBranchesTableExists();
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(
                    "UPDATE dbo.BranchLocations SET IsActive=@A WHERE LocationId=@Id", con);
                cmd.Parameters.AddWithValue("@A", isActive);
                cmd.Parameters.AddWithValue("@Id", locationId);
                cmd.ExecuteNonQuery();
                TempData["ResultMessage"] = isActive ? "Location activated." : "Location deactivated.";
            }
            catch (Exception ex) { TempData["ResultMessage"] = $"Failed: {ex.Message}"; }
            return RedirectToAction(nameof(BranchLocationList));
        }

        private List<BranchLocation> ReadBranchLocations()
        {
            var list = new List<BranchLocation>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT LocationId, LocationName, IsActive FROM dbo.BranchLocations ORDER BY LocationName", con);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                list.Add(new BranchLocation
                {
                    LocationId   = rdr.GetInt32(0),
                    LocationName = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                    IsActive     = !rdr.IsDBNull(2) && rdr.GetBoolean(2)
                });
            return list;
        }

        private BranchLocation? ReadBranchLocationById(int locationId)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 LocationId, LocationName, IsActive FROM dbo.BranchLocations WHERE LocationId=@Id", con);
            cmd.Parameters.AddWithValue("@Id", locationId);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;
            return new BranchLocation
            {
                LocationId   = rdr.GetInt32(0),
                LocationName = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                IsActive     = !rdr.IsDBNull(2) && rdr.GetBoolean(2)
            };
        }

        private bool InsertBranchLocation(BranchLocation model)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "INSERT INTO dbo.BranchLocations (LocationName, IsActive) VALUES (@N, @A)", con);
            cmd.Parameters.AddWithValue("@N", model.LocationName);
            cmd.Parameters.AddWithValue("@A", model.IsActive);
            return cmd.ExecuteNonQuery() > 0;
        }

        private bool UpdateBranchLocation(BranchLocation model)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "UPDATE dbo.BranchLocations SET LocationName=@N, IsActive=@A WHERE LocationId=@Id", con);
            cmd.Parameters.AddWithValue("@N", model.LocationName);
            cmd.Parameters.AddWithValue("@A", model.IsActive);
            cmd.Parameters.AddWithValue("@Id", model.LocationId);
            return cmd.ExecuteNonQuery() > 0;
        }

        private List<SelectListItem> GetBranchLocationSelectList()
        {
            var items = new List<SelectListItem>();
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var cmd = new SqlCommand(
                "SELECT LocationId, LocationName FROM dbo.BranchLocations WHERE ISNULL(IsActive,1)=1 ORDER BY LocationName", con);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                items.Add(new SelectListItem
                {
                    Value = rdr.GetInt32(0).ToString(),
                    Text  = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1)
                });
            return items;
        }
}
}