using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient; // Choose Microsoft.Data.SqlClient explicitly
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using RestaurantManagementSystem.Utilities;
using MenuItemIngredientViewModelModel = RestaurantManagementSystem.Models.MenuItemIngredientViewModel;

namespace RestaurantManagementSystem.Controllers
{
    public class RecipeController : Controller
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public RecipeController(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _webHostEnvironment = webHostEnvironment;
        }

        private int? GetActiveBranchId()
        {
            return User.GetActiveBranchId();
        }

        private void EnsureRecipesBranchColumnExists(Microsoft.Data.SqlClient.SqlConnection connection)
        {
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
IF COL_LENGTH('dbo.Recipes', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.Recipes ADD BranchId INT NULL;
END
", connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureMenuItemsBranchColumnExists(Microsoft.Data.SqlClient.SqlConnection connection)
        {
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
IF COL_LENGTH('dbo.MenuItems', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.MenuItems ADD BranchId INT NULL;
END
", connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureIngredientsBranchColumnExists(Microsoft.Data.SqlClient.SqlConnection connection)
        {
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
IF COL_LENGTH('dbo.Ingredients', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.Ingredients ADD BranchId INT NULL;
END
", connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        
        // GET: Recipe
        public IActionResult Index()
        {
            if (!GetActiveBranchId().HasValue)
            {
                TempData["ErrorMessage"] = "Please select an active branch first.";
                return View(new List<Recipe>());
            }

            var recipes = GetAllRecipes();
            return View(recipes);
        }
        
        // GET: Recipe/Dashboard
        public IActionResult Dashboard()
        {
            if (!GetActiveBranchId().HasValue)
            {
                TempData["ErrorMessage"] = "Please select an active branch first.";
                return View(new List<Recipe>());
            }

            var recipes = GetAllRecipes();
            return View(recipes);
        }

        // GET: Recipe/Details/5
        public IActionResult Details(int id)
        {
            var recipe = GetRecipeByMenuItemId(id);
            if (recipe == null)
            {
                return NotFound();
            }

            return View(recipe);
        }

        // GET: Recipe/Create/5 (where 5 is the MenuItemId)
        public IActionResult Create(int menuItemId)
        {
            var menuItem = GetMenuItemById(menuItemId);
            if (menuItem == null)
            {
                return NotFound();
            }

            ViewBag.MenuItem = menuItem;
            ViewBag.Ingredients = new SelectList(GetAllIngredients(), "Id", "IngredientsName");
            
            var recipeViewModel = new RecipeViewModel
            {
                MenuItemId = menuItemId,
                PreparationTimeMinutes = 15,
                CookingTimeMinutes = 15,
                Yield = 1,
                YieldPercentage = 100
            };
            
            return View(recipeViewModel);
        }

        // GET: Recipe/SetupRecipeScripts
        [HttpGetAttribute]
        public IActionResult SetupRecipeScripts()
        {
            try
            {
                var scriptPath = Path.Combine(_webHostEnvironment.ContentRootPath, "SQL", "Menu_Recipe_Setup.sql");
                if (!System.IO.File.Exists(scriptPath))
                {
                    return NotFound(new { ok = false, message = "Setup script not found." });
                }

                var script = System.IO.File.ReadAllText(scriptPath);
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    ExecuteSqlWithGoBatches(connection, script);
                }

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }

        private static void ExecuteSqlWithGoBatches(Microsoft.Data.SqlClient.SqlConnection connection, string script)
        {
            var batches = new List<string>();
            using (var reader = new StringReader(script))
            {
                var sb = new System.Text.StringBuilder();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sb.Length > 0)
                        {
                            batches.Add(sb.ToString());
                            sb.Clear();
                        }
                    }
                    else
                    {
                        sb.AppendLine(line);
                    }
                }
                if (sb.Length > 0) batches.Add(sb.ToString());
            }

            foreach (var batch in batches)
            {
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(batch, connection) { CommandTimeout = 120 };
                cmd.ExecuteNonQuery();
            }
        }

        // POST: Recipe/Create
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult Create(RecipeViewModel model)
        {
            if (ModelState.IsValid)
            {
                int recipeId;
                var activeBranchId = GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    ModelState.AddModelError("", "Please select an active branch first.");
                    ViewBag.Ingredients = new SelectList(GetAllIngredients(), "Id", "IngredientsName");
                    return View(model);
                }

                using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    EnsureRecipesBranchColumnExists(connection);
                    
                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_ManageRecipe", connection))
                    {
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        
                        command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                        command.Parameters.AddWithValue("@Title", model.Title);
                        command.Parameters.AddWithValue("@PreparationInstructions", model.PreparationInstructions);
                        command.Parameters.AddWithValue("@CookingInstructions", model.CookingInstructions);
                        command.Parameters.AddWithValue("@PlatingInstructions", (object?)model.PlatingInstructions ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Yield", model.Yield);
                        command.Parameters.AddWithValue("@YieldPercentage", model.YieldPercentage);
                        command.Parameters.AddWithValue("@PreparationTimeMinutes", model.PreparationTimeMinutes);
                        command.Parameters.AddWithValue("@CookingTimeMinutes", model.CookingTimeMinutes);
                        command.Parameters.AddWithValue("@Notes", (object?)model.Notes ?? DBNull.Value);
                        command.Parameters.AddWithValue("@UserId", 1);
                        
                        using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                recipeId = reader.GetInt32(reader.GetOrdinal("RecipeId"));
                            }
                            else
                            {
                                ModelState.AddModelError("", "Failed to create recipe");
                                return View(model);
                            }
                        }
                    }

                    using (var updateBranchCmd = new Microsoft.Data.SqlClient.SqlCommand(
                        "UPDATE dbo.Recipes SET BranchId = @BranchId WHERE Id = @RecipeId", connection))
                    {
                        updateBranchCmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        updateBranchCmd.Parameters.AddWithValue("@RecipeId", recipeId);
                        updateBranchCmd.ExecuteNonQuery();
                    }
                    
                    // Add recipe steps if provided
                    if (model.Steps != null && model.Steps.Count > 0)
                    {
                        bool stepProcExists = StoredProcExists(connection, "sp_ManageRecipeStep");
                        for (int i = 0; i < model.Steps.Count; i++)
                        {
                            var step = model.Steps[i];
                            if (stepProcExists)
                            {
                                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_ManageRecipeStep", connection))
                                {
                                    command.CommandType = System.Data.CommandType.StoredProcedure;
                                    
                                    command.Parameters.AddWithValue("@RecipeId", recipeId);
                                    command.Parameters.AddWithValue("@StepNumber", i + 1);
                                    command.Parameters.AddWithValue("@Description", step.Description);
                                    command.Parameters.AddWithValue("@TimeRequiredMinutes", (object?)step.TimeRequiredMinutes ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@Temperature", (object?)step.Temperature ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@SpecialEquipment", (object?)step.SpecialEquipment ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@Tips", (object?)step.Tips ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@ImagePath", (object?)step.ImagePath ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@IsUpdate", false);
                                    
                                    command.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                    
                    // Ingredients: be resilient to missing table/SP
                    bool hasIngredientsData = model.Ingredients != null && model.Ingredients.Count > 0;
                    if (hasIngredientsData)
                    {
                        bool nonUnderscoreExists = TableExists(connection, "MenuItemIngredients");
                        bool underscoreExists = TableExists(connection, "MenuItem_Ingredients");
                        string ingredientsTable = nonUnderscoreExists ? "MenuItemIngredients" : (underscoreExists ? "MenuItem_Ingredients" : string.Empty);
                        bool procExists = StoredProcExists(connection, "sp_ManageMenuItemIngredient");

                        if (!string.IsNullOrEmpty(ingredientsTable))
                        {
                            // Only use the SP if it exists AND the non-underscore table exists (matches SP body)
                            if (procExists && nonUnderscoreExists)
                            {
                                foreach (var ingredient in model.Ingredients)
                                {
                                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_ManageMenuItemIngredient", connection))
                                    {
                                        command.CommandType = System.Data.CommandType.StoredProcedure;
                                        command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                                        command.Parameters.AddWithValue("@IngredientId", ingredient.IngredientId);
                                        command.Parameters.AddWithValue("@Quantity", ingredient.Quantity);
                                        command.Parameters.AddWithValue("@Unit", ingredient.Unit);
                                        command.Parameters.AddWithValue("@IsOptional", ingredient.IsOptional);
                                        command.Parameters.AddWithValue("@Instructions", (object?)ingredient.Instructions ?? DBNull.Value);
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                            else
                            {
                                // Direct insert fallback to whichever table exists
                                foreach (var ingredient in model.Ingredients)
                                {
                                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand($@"INSERT INTO {ingredientsTable} (MenuItemId, IngredientId, Quantity, Unit, IsOptional, Instructions)
VALUES (@MenuItemId, @IngredientId, @Quantity, @Unit, @IsOptional, @Instructions)", connection))
                                    {
                                        command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                                        command.Parameters.AddWithValue("@IngredientId", ingredient.IngredientId);
                                        command.Parameters.AddWithValue("@Quantity", ingredient.Quantity);
                                        command.Parameters.AddWithValue("@Unit", ingredient.Unit);
                                        command.Parameters.AddWithValue("@IsOptional", ingredient.IsOptional);
                                        command.Parameters.AddWithValue("@Instructions", (object?)ingredient.Instructions ?? DBNull.Value);
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                        else
                        {
                            TempData["WarningMessage"] = "Ingredients table not found; ingredients were not saved.";
                        }
                    }
                }
                
                TempData["SuccessMessage"] = "Recipe created successfully";
                return RedirectToAction("Details", "Menu", new { id = model.MenuItemId });
            }
            
            ViewBag.Ingredients = new SelectList(GetAllIngredients(), "Id", "IngredientsName");
            return View(model);
        }
        
        // GET: Recipe/Edit/5 (where 5 is the MenuItemId)
        public IActionResult Edit(int menuItemId)
        {
            var recipe = GetRecipeByMenuItemId(menuItemId);
            if (recipe == null)
            {
                return RedirectToAction("Create", new { menuItemId });
            }
            
            var menuItem = GetMenuItemById(menuItemId);
            ViewBag.MenuItem = menuItem;
            ViewBag.Ingredients = new SelectList(GetAllIngredients(), "Id", "IngredientsName");
            
            return View(recipe);
        }
        
        // POST: Recipe/Edit
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult Edit(RecipeViewModel model)
        {
            if (ModelState.IsValid)
            {
                int recipeId;
                var activeBranchId = GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    ModelState.AddModelError("", "Please select an active branch first.");
                    ViewBag.Ingredients = new SelectList(GetAllIngredients(), "Id", "IngredientsName");
                    return View(model);
                }

                using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    EnsureRecipesBranchColumnExists(connection);
                    
                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_ManageRecipe", connection))
                    {
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        
                        command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                        command.Parameters.AddWithValue("@Title", model.Title);
                        command.Parameters.AddWithValue("@PreparationInstructions", model.PreparationInstructions);
                        command.Parameters.AddWithValue("@CookingInstructions", model.CookingInstructions);
                        command.Parameters.AddWithValue("@PlatingInstructions", (object?)model.PlatingInstructions ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Yield", model.Yield);
                        command.Parameters.AddWithValue("@YieldPercentage", model.YieldPercentage);
                        command.Parameters.AddWithValue("@PreparationTimeMinutes", model.PreparationTimeMinutes);
                        command.Parameters.AddWithValue("@CookingTimeMinutes", model.CookingTimeMinutes);
                        command.Parameters.AddWithValue("@Notes", (object?)model.Notes ?? DBNull.Value);
                        command.Parameters.AddWithValue("@UserId", 1);
                        
                        using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                recipeId = reader.GetInt32(reader.GetOrdinal("RecipeId"));
                            }
                            else
                            {
                                ModelState.AddModelError("", "Failed to update recipe");
                                return View(model);
                            }
                        }
                    }

                    using (var updateBranchCmd = new Microsoft.Data.SqlClient.SqlCommand(
                        "UPDATE dbo.Recipes SET BranchId = @BranchId WHERE Id = @RecipeId", connection))
                    {
                        updateBranchCmd.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        updateBranchCmd.Parameters.AddWithValue("@RecipeId", recipeId);
                        updateBranchCmd.ExecuteNonQuery();
                    }
                    
                    // Update recipe steps
                    if (model.Steps != null && model.Steps.Count > 0)
                    {
                        bool stepProcExists = StoredProcExists(connection, "sp_ManageRecipeStep");
                        for (int i = 0; i < model.Steps.Count; i++)
                        {
                            var step = model.Steps[i];
                            if (stepProcExists)
                            {
                                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_ManageRecipeStep", connection))
                                {
                                    command.CommandType = System.Data.CommandType.StoredProcedure;
                                    
                                    command.Parameters.AddWithValue("@RecipeId", recipeId);
                                    command.Parameters.AddWithValue("@StepNumber", i + 1);
                                    command.Parameters.AddWithValue("@Description", step.Description);
                                    command.Parameters.AddWithValue("@TimeRequiredMinutes", (object?)step.TimeRequiredMinutes ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@Temperature", (object?)step.Temperature ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@SpecialEquipment", (object?)step.SpecialEquipment ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@Tips", (object?)step.Tips ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@ImagePath", (object?)step.ImagePath ?? DBNull.Value);
                                    command.Parameters.AddWithValue("@IsUpdate", true);
                                    
                                    command.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                    
                    // Update ingredients: delete existing if table exists, then insert
            bool nonUnderscoreExists = TableExists(connection, "MenuItemIngredients");
            bool underscoreExists = TableExists(connection, "MenuItem_Ingredients");
            string ingredientsTable = nonUnderscoreExists ? "MenuItemIngredients" : (underscoreExists ? "MenuItem_Ingredients" : string.Empty);
                    if (!string.IsNullOrEmpty(ingredientsTable))
                    {
                        using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand($"DELETE FROM {ingredientsTable} WHERE MenuItemId = @MenuItemId", connection))
                        {
                            command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                            command.ExecuteNonQuery();
                        }
                        
                        if (model.Ingredients != null && model.Ingredients.Count > 0)
                        {
                bool procExists = StoredProcExists(connection, "sp_ManageMenuItemIngredient");
                if (procExists && nonUnderscoreExists)
                            {
                                foreach (var ingredient in model.Ingredients)
                                {
                                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_ManageMenuItemIngredient", connection))
                                    {
                                        command.CommandType = System.Data.CommandType.StoredProcedure;
                                        
                                        command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                                        command.Parameters.AddWithValue("@IngredientId", ingredient.IngredientId);
                                        command.Parameters.AddWithValue("@Quantity", ingredient.Quantity);
                                        command.Parameters.AddWithValue("@Unit", ingredient.Unit);
                                        command.Parameters.AddWithValue("@IsOptional", ingredient.IsOptional);
                                        command.Parameters.AddWithValue("@Instructions", (object?)ingredient.Instructions ?? DBNull.Value);
                                        
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                            else
                            {
                                foreach (var ingredient in model.Ingredients)
                                {
                                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand($@"INSERT INTO {ingredientsTable} (MenuItemId, IngredientId, Quantity, Unit, IsOptional, Instructions)
VALUES (@MenuItemId, @IngredientId, @Quantity, @Unit, @IsOptional, @Instructions)", connection))
                                    {
                                        command.Parameters.AddWithValue("@MenuItemId", model.MenuItemId);
                                        command.Parameters.AddWithValue("@IngredientId", ingredient.IngredientId);
                                        command.Parameters.AddWithValue("@Quantity", ingredient.Quantity);
                                        command.Parameters.AddWithValue("@Unit", ingredient.Unit);
                                        command.Parameters.AddWithValue("@IsOptional", ingredient.IsOptional);
                                        command.Parameters.AddWithValue("@Instructions", (object?)ingredient.Instructions ?? DBNull.Value);
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                    }
                    else if (model.Ingredients != null && model.Ingredients.Count > 0)
                    {
                        TempData["WarningMessage"] = "Ingredients table not found; ingredients were not saved.";
                    }
                }
                
                TempData["SuccessMessage"] = "Recipe updated successfully";
                return RedirectToAction("Details", "Menu", new { id = model.MenuItemId });
            }
            
            ViewBag.Ingredients = new SelectList(GetAllIngredients(), "Id", "IngredientsName");
            return View(model);
        }
        
        // GET: Recipe/CalculateSuggestedPrice/5?targetGP=40
        public IActionResult CalculateSuggestedPrice(int id, decimal targetGP = 40)
        {
            var menuItem = GetMenuItemById(id);
            if (menuItem == null)
            {
                return NotFound();
            }
            
            PriceSuggestionViewModel priceViewModel = null;
            
            using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_CalculateSuggestedPrice", connection))
                {
                    command.CommandType = System.Data.CommandType.StoredProcedure;
                    
                    command.Parameters.AddWithValue("@MenuItemId", id);
                    command.Parameters.AddWithValue("@TargetGPPercentage", targetGP);
                    
                    using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            priceViewModel = new PriceSuggestionViewModel
                            {
                                MenuItemId = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                                TotalCost = reader.GetDecimal(reader.GetOrdinal("TotalCost")),
                                TargetGPPercentage = reader.GetDecimal(reader.GetOrdinal("TargetGPPercentage")),
                                SuggestedPrice = reader.GetDecimal(reader.GetOrdinal("SuggestedPrice")),
                                CurrentPrice = menuItem.Price
                            };
                        }
                    }
                }
            }
            
            if (priceViewModel == null)
            {
                priceViewModel = new PriceSuggestionViewModel
                {
                    MenuItemId = id,
                    TotalCost = 0,
                    TargetGPPercentage = targetGP,
                    SuggestedPrice = 0,
                    CurrentPrice = menuItem.Price
                };
            }
            
            priceViewModel.MenuItemName = menuItem.Name;
            
            return View(priceViewModel);
        }
        
        // POST: Recipe/UpdatePrice
        [HttpPostAttribute]
        [ValidateAntiForgeryTokenAttribute]
        public IActionResult UpdatePrice(PriceSuggestionViewModel model)
        {
            if (ModelState.IsValid)
            {
                using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_UpdateMenuItem", connection))
                    {
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        
                        // Get current menu item data
                        var menuItem = GetMenuItemById(model.MenuItemId);
                        
                        command.Parameters.AddWithValue("@Id", model.MenuItemId);
                        command.Parameters.AddWithValue("@PLUCode", menuItem.PLUCode);
                        command.Parameters.AddWithValue("@Name", menuItem.Name);
                        command.Parameters.AddWithValue("@Description", menuItem.Description);
                        command.Parameters.AddWithValue("@Price", model.NewPrice);
                        command.Parameters.AddWithValue("@CategoryId", menuItem.CategoryId);
                        command.Parameters.AddWithValue("@ImagePath", menuItem.ImagePath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@IsAvailable", menuItem.IsAvailable);
                        command.Parameters.AddWithValue("@PreparationTimeMinutes", menuItem.PreparationTimeMinutes);
                        command.Parameters.AddWithValue("@CalorieCount", menuItem.CalorieCount ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@IsFeatured", menuItem.IsFeatured);
                        command.Parameters.AddWithValue("@IsSpecial", menuItem.IsSpecial);
                        command.Parameters.AddWithValue("@DiscountPercentage", menuItem.DiscountPercentage ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@KitchenStationId", menuItem.KitchenStationId ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@TargetGP", model.TargetGPPercentage);
                        command.Parameters.AddWithValue("@UserId", 1); // TODO: Get from authentication
                        
                        using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                bool needsApproval = reader.GetBoolean(reader.GetOrdinal("NeedsPriceApproval"));
                                if (needsApproval)
                                {
                                    TempData["WarningMessage"] = "Price change requires approval. The current price will remain until approved.";
                                }
                                else
                                {
                                    TempData["SuccessMessage"] = "Price updated successfully.";
                                }
                            }
                        }
                    }
                }
                
                return RedirectToAction("Details", "Menu", new { id = model.MenuItemId });
            }
            
            return View("CalculateSuggestedPrice", model);
        }
        
        // Helper methods
        private RecipeViewModel GetRecipeByMenuItemId(int menuItemId)
        {
            if (GetMenuItemById(menuItemId) == null)
            {
                return null;
            }

            RecipeViewModel recipe = null;
            
            using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                
                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("sp_GetRecipeByMenuItemId", connection))
                {
                    command.CommandType = System.Data.CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@MenuItemId", menuItemId);
                    
                    using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            recipe = new RecipeViewModel
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                MenuItemId = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                                Title = reader.GetString(reader.GetOrdinal("Title")),
                                PreparationInstructions = reader.GetString(reader.GetOrdinal("PreparationInstructions")),
                                CookingInstructions = reader.GetString(reader.GetOrdinal("CookingInstructions")),
                                PlatingInstructions = reader.IsDBNull(reader.GetOrdinal("PlatingInstructions")) ? null : reader.GetString(reader.GetOrdinal("PlatingInstructions")),
                                Yield = reader.GetInt32(reader.GetOrdinal("Yield")),
                                YieldPercentage = reader.GetDecimal(reader.GetOrdinal("YieldPercentage")),
                                PreparationTimeMinutes = reader.GetInt32(reader.GetOrdinal("PreparationTimeMinutes")),
                                CookingTimeMinutes = reader.GetInt32(reader.GetOrdinal("CookingTimeMinutes")),
                                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                                Version = reader.GetInt32(reader.GetOrdinal("Version")),
                                LastUpdated = reader.GetDateTime(reader.GetOrdinal("LastUpdated")),
                                IsArchived = false,
                                Steps = new List<RecipeStepViewModel>(),
                                Ingredients = new List<MenuItemIngredientViewModelModel>()
                            };
                        }
                        else
                        {
                            return null;
                        }

                        // Steps result set (2nd)
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                recipe.Steps.Add(new RecipeStepViewModel
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                    RecipeId = reader.GetInt32(reader.GetOrdinal("RecipeId")),
                                    StepNumber = reader.GetInt32(reader.GetOrdinal("StepNumber")),
                                    Description = reader.GetString(reader.GetOrdinal("Description")),
                                    TimeRequiredMinutes = reader.IsDBNull(reader.GetOrdinal("TimeRequiredMinutes")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("TimeRequiredMinutes")),
                                    Temperature = reader.IsDBNull(reader.GetOrdinal("Temperature")) ? null : reader.GetString(reader.GetOrdinal("Temperature")),
                                    SpecialEquipment = reader.IsDBNull(reader.GetOrdinal("SpecialEquipment")) ? null : reader.GetString(reader.GetOrdinal("SpecialEquipment")),
                                    Tips = reader.IsDBNull(reader.GetOrdinal("Tips")) ? null : reader.GetString(reader.GetOrdinal("Tips")),
                                    ImagePath = reader.IsDBNull(reader.GetOrdinal("ImagePath")) ? null : reader.GetString(reader.GetOrdinal("ImagePath"))
                                });
                            }
                        }

                        // Ingredients result set (3rd)
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                var vm = new MenuItemIngredientViewModelModel
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                    MenuItemId = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                                    IngredientId = reader.GetInt32(reader.GetOrdinal("IngredientId")),
                                    IngredientName = reader.GetString(reader.GetOrdinal("IngredientsName")),
                                    Quantity = reader.GetDecimal(reader.GetOrdinal("Quantity")),
                                    Unit = reader.GetString(reader.GetOrdinal("Unit")),
                                    IsOptional = reader.GetBoolean(reader.GetOrdinal("IsOptional"))
                                };
                                try
                                {
                                    int idx = reader.GetOrdinal("LastPurchaseCost");
                                    if (!reader.IsDBNull(idx))
                                    {
                                        vm.Cost = reader.GetDecimal(idx);
                                    }
                                }
                                catch { }
                                recipe.Ingredients.Add(vm);
                            }
                        }
                        // Allergens (4th) ignored
                    }

                    // Also fetch MenuItem name for display
                    try
                    {
                        using var menuCmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT Name FROM MenuItems WHERE Id = @Id", connection);
                        menuCmd.Parameters.AddWithValue("@Id", menuItemId);
                        var nameObj = menuCmd.ExecuteScalar();
                        if (nameObj != null && nameObj != DBNull.Value)
                        {
                            recipe.MenuItemName = Convert.ToString(nameObj);
                        }
                    }
                    catch { /* optional */ }
                }
            }
            return recipe;
        }

        private MenuItem GetMenuItemById(int id)
        {
            MenuItem menuItem = null;
            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                return null;
            }

            using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                EnsureMenuItemsBranchColumnExists(connection);
                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT m.Id, m.PLUCode, m.Name, m.Description, m.Price, m.CategoryId, c.Name AS CategoryName,
       m.ImagePath, m.IsAvailable, ISNULL(m.PrepTime, 0) AS PreparationTimeMinutes,
       m.CalorieCount, ISNULL(m.IsFeatured, 0) AS IsFeatured, ISNULL(m.IsSpecial, 0) AS IsSpecial,
       m.DiscountPercentage, m.TargetGP
FROM dbo.MenuItems m
INNER JOIN dbo.Categories c ON c.Id = m.CategoryId
WHERE m.Id = @Id AND m.BranchId = @BranchId", connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                    using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            menuItem = new MenuItem
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                PLUCode = reader.GetString(reader.GetOrdinal("PLUCode")),
                                Name = reader.GetString(reader.GetOrdinal("Name")),
                                Description = reader.GetString(reader.GetOrdinal("Description")),
                                Price = reader.GetDecimal(reader.GetOrdinal("Price")),
                                CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                                Category = new Category { Name = reader.GetString(reader.GetOrdinal("CategoryName")) },
                                ImagePath = reader.IsDBNull(reader.GetOrdinal("ImagePath")) ? null : reader.GetString(reader.GetOrdinal("ImagePath")),
                                IsAvailable = reader.GetBoolean(reader.GetOrdinal("IsAvailable")),
                                PreparationTimeMinutes = reader.GetInt32(reader.GetOrdinal("PreparationTimeMinutes")),
                                CalorieCount = reader.IsDBNull(reader.GetOrdinal("CalorieCount")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("CalorieCount")),
                                IsFeatured = reader.GetBoolean(reader.GetOrdinal("IsFeatured")),
                                IsSpecial = reader.GetBoolean(reader.GetOrdinal("IsSpecial")),
                                DiscountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("DiscountPercentage")),
                                TargetGP = reader.IsDBNull(reader.GetOrdinal("TargetGP")) ? null : (decimal?)reader.GetDecimal(reader.GetOrdinal("TargetGP")),
                                KitchenStationId = null
                            };
                        }
                    }
                }
            }
            return menuItem;
        }

        private List<Ingredients> GetAllIngredients()
        {
            var ingredients = new List<Ingredients>();
            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                return ingredients;
            }

            using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                EnsureIngredientsBranchColumnExists(connection);
                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand("SELECT Id, IngredientsName, DisplayName, Code FROM Ingredients WHERE BranchId = @BranchId ORDER BY IngredientsName", connection))
                {
                    command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                    using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ingredients.Add(new Ingredients
                            {
                                Id = reader.GetInt32(0),
                                IngredientsName = reader.GetString(1),
                                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Code = reader.IsDBNull(3) ? null : reader.GetString(3)
                            });
                        }
                    }
                }
            }
            return ingredients;
        }
        
        // Helper method to execute SQL script files
        private void ExecuteScriptFile(string fileName)
        {
            try
            {
                string scriptPath = Path.Combine(_webHostEnvironment.ContentRootPath, "SQL", fileName);
                if (System.IO.File.Exists(scriptPath))
                {
                    string script = System.IO.File.ReadAllText(scriptPath);
                    using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                    {
                        connection.Open();
                        
                        // Split the script by GO statements if needed
                        foreach (string batch in script.Split(new[] { "GO" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!string.IsNullOrWhiteSpace(batch))
                            {
                                using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand(batch, connection))
                                {
                                    try
                                    {
                                        command.ExecuteNonQuery();
                                    }
                                    catch (Exception ex)
                                    {
                                        
                                        // Continue with next batch even if this one fails
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    
                }
            }
            catch (Exception ex)
            {
                
            }
        }

        private List<Recipe> GetAllRecipes()
        {
            List<Recipe> recipes = new List<Recipe>();
            var activeBranchId = GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                return recipes;
            }
            
            try
            {
                // First, ensure stored procedures are available
                ExecuteScriptFile("create_sp_GetAllRecipes.sql");
                ExecuteScriptFile("update_sp_GetAllRecipes.sql");
                
                using (Microsoft.Data.SqlClient.SqlConnection connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    connection.Open();
                                        EnsureRecipesBranchColumnExists(connection);
                                        EnsureMenuItemsBranchColumnExists(connection);
                    
                                        using (Microsoft.Data.SqlClient.SqlCommand command = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT r.Id, r.MenuItemId, r.Title, r.PreparationInstructions, r.CookingInstructions,
             r.PlatingInstructions, r.Yield, r.YieldPercentage, r.PreparationTimeMinutes,
             r.CookingTimeMinutes, r.LastUpdated, r.Notes, ISNULL(r.IsArchived, 0) AS IsArchived,
             ISNULL(r.Version, 1) AS Version, m.Name AS MenuItemName
FROM dbo.Recipes r
INNER JOIN dbo.MenuItems m ON m.Id = r.MenuItemId
WHERE r.BranchId = @BranchId
    AND m.BranchId = @BranchId
ORDER BY m.Name", connection))
                    {
                                                command.Parameters.AddWithValue("@BranchId", activeBranchId.Value);
                        
                        using (Microsoft.Data.SqlClient.SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                recipes.Add(new Recipe
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                    MenuItemId = reader.GetInt32(reader.GetOrdinal("MenuItemId")),
                                    Title = reader.GetString(reader.GetOrdinal("Title")),
                                    PreparationInstructions = reader.IsDBNull(reader.GetOrdinal("PreparationInstructions")) ? null : reader.GetString(reader.GetOrdinal("PreparationInstructions")),
                                    CookingInstructions = reader.IsDBNull(reader.GetOrdinal("CookingInstructions")) ? null : reader.GetString(reader.GetOrdinal("CookingInstructions")),
                                    PlatingInstructions = reader.IsDBNull(reader.GetOrdinal("PlatingInstructions")) ? null : reader.GetString(reader.GetOrdinal("PlatingInstructions")),
                                    Yield = reader.GetInt32(reader.GetOrdinal("Yield")),
                                    YieldPercentage = reader.GetDecimal(reader.GetOrdinal("YieldPercentage")),
                                    PreparationTimeMinutes = reader.GetInt32(reader.GetOrdinal("PreparationTimeMinutes")),
                                    CookingTimeMinutes = reader.GetInt32(reader.GetOrdinal("CookingTimeMinutes")),
                                    LastUpdated = reader.GetDateTime(reader.GetOrdinal("LastUpdated")),
                                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                                    IsArchived = reader.GetBoolean(reader.GetOrdinal("IsArchived")),
                                    Version = reader.GetInt32(reader.GetOrdinal("Version")),
                                    MenuItemName = reader.GetString(reader.GetOrdinal("MenuItemName"))
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                
                // Log the error or display it
            }
            
            return recipes;
        }

        private bool TableExists(Microsoft.Data.SqlClient.SqlConnection connection, string tableName)
        {
            try
            {
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT CASE WHEN OBJECT_ID(@t, 'U') IS NOT NULL THEN 1 ELSE 0 END", connection);
                cmd.Parameters.AddWithValue("@t", tableName);
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
            catch { return false; }
        }

        private bool StoredProcExists(Microsoft.Data.SqlClient.SqlConnection connection, string procName)
        {
            try
            {
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"SELECT CASE WHEN OBJECT_ID(@p, 'P') IS NOT NULL THEN 1 ELSE 0 END", connection);
                cmd.Parameters.AddWithValue("@p", procName);
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
            catch { return false; }
        }
    }
}
