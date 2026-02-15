using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Filters;
using RestaurantManagementSystem.Models.Authorization;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize(Roles = "Administrator,Manager")]
    [RequirePermission("NAV_SETTINGS_EMAIL_TEMPLATES", PermissionAction.View)]
    public class EmailTemplatesController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<EmailTemplatesController> _logger;

        public EmailTemplatesController(
            IConfiguration configuration,
            ILogger<EmailTemplatesController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string not found");
            _logger = logger;
        }

        // GET: EmailTemplates
        public async Task<IActionResult> Index()
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                    return RedirectToAction("Index", "Home");
                }

                var templates = await GetAllTemplatesAsync(activeBranchId);
                return View(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading email templates");
                TempData["ErrorMessage"] = $"Error loading templates: {ex.Message}";
                return View(new List<EmailTemplate>());
            }
        }

        // GET: EmailTemplates/Create
        public IActionResult Create()
        {
            if (!User.GetActiveBranchId().HasValue)
            {
                TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                return RedirectToAction("Index", "Home");
            }

            return View(new EmailTemplate());
        }

        // POST: EmailTemplates/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EmailTemplate template)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                    return RedirectToAction("Index", "Home");
                }

                if (ModelState.IsValid)
                {
                    await CreateTemplateAsync(template, activeBranchId);
                    TempData["SuccessMessage"] = "Email template created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                return View(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating email template");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return View(template);
            }
        }

        // GET: EmailTemplates/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                    return RedirectToAction("Index", "Home");
                }

                var template = await GetTemplateByIdAsync(id, activeBranchId);
                if (template == null)
                {
                    TempData["ErrorMessage"] = "Template not found";
                    return RedirectToAction(nameof(Index));
                }
                return View(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading template for edit");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: EmailTemplates/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EmailTemplate template)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                    return RedirectToAction("Index", "Home");
                }

                if (id != template.EmailTemplateID)
                {
                    return NotFound();
                }

                if (ModelState.IsValid)
                {
                    await UpdateTemplateAsync(template, activeBranchId);
                    TempData["SuccessMessage"] = "Email template updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                return View(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating email template");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return View(template);
            }
        }

        // POST: EmailTemplates/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    TempData["ErrorMessage"] = "No active branch selected. Please select a branch first.";
                    return RedirectToAction("Index", "Home");
                }

                await DeleteTemplateAsync(id, activeBranchId);
                TempData["SuccessMessage"] = "Email template deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting email template");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: EmailTemplates/SetDefault/5
        [HttpPost]
        public async Task<IActionResult> SetDefault(int id, string templateType)
        {
            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    return Json(new { success = false, message = "No active branch selected" });
                }

                await SetDefaultTemplateAsync(id, templateType, activeBranchId);
                return Json(new { success = true, message = "Default template set successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting default template");
                return Json(new { success = false, message = ex.Message });
            }
        }

        #region Helper Methods

        private async Task<List<EmailTemplate>> GetAllTemplatesAsync(int? branchId)
        {
            var templates = new List<EmailTemplate>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "WHERE BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT EmailTemplateID, TemplateName, TemplateType, Subject, BodyHtml, 
                           IsActive, IsDefault, CreatedAt, UpdatedAt
                    FROM tbl_EmailTemplates
                    {branchFilter}
                    ORDER BY TemplateType, TemplateName";

                using (var command = new SqlCommand(query, connection))
                {
                    if (hasTemplateBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        templates.Add(new EmailTemplate
                        {
                            EmailTemplateID = reader.GetInt32(0),
                            TemplateName = reader.GetString(1),
                            TemplateType = reader.GetString(2),
                            Subject = reader.GetString(3),
                            BodyHtml = reader.GetString(4),
                            IsActive = reader.GetBoolean(5),
                            IsDefault = reader.GetBoolean(6),
                            CreatedAt = reader.GetDateTime(7),
                            UpdatedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
                        });
                    }
                }
                }
            }

            return templates;
        }

        private async Task<EmailTemplate?> GetTemplateByIdAsync(int id, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT EmailTemplateID, TemplateName, TemplateType, Subject, BodyHtml, 
                           IsActive, IsDefault
                    FROM tbl_EmailTemplates
                    WHERE EmailTemplateID = @Id
                    {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    if (hasTemplateBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new EmailTemplate
                            {
                                EmailTemplateID = reader.GetInt32(0),
                                TemplateName = reader.GetString(1),
                                TemplateType = reader.GetString(2),
                                Subject = reader.GetString(3),
                                BodyHtml = reader.GetString(4),
                                IsActive = reader.GetBoolean(5),
                                IsDefault = reader.GetBoolean(6)
                            };
                        }
                    }
                }
            }

            return null;
        }

        private async Task CreateTemplateAsync(EmailTemplate template, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");

                var query = hasTemplateBranch
                    ? @"INSERT INTO tbl_EmailTemplates (
                            TemplateName, TemplateType, Subject, BodyHtml,
                            IsActive, IsDefault, CreatedBy, CreatedAt, BranchId
                        ) VALUES (
                            @TemplateName, @TemplateType, @Subject, @BodyHtml,
                            @IsActive, @IsDefault, @CreatedBy, GETDATE(), @BranchId
                        )"
                    : @"INSERT INTO tbl_EmailTemplates (
                            TemplateName, TemplateType, Subject, BodyHtml,
                            IsActive, IsDefault, CreatedBy, CreatedAt
                        ) VALUES (
                            @TemplateName, @TemplateType, @Subject, @BodyHtml,
                            @IsActive, @IsDefault, @CreatedBy, GETDATE()
                        )";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TemplateName", template.TemplateName);
                    command.Parameters.AddWithValue("@TemplateType", template.TemplateType);
                    command.Parameters.AddWithValue("@Subject", template.Subject);
                    command.Parameters.AddWithValue("@BodyHtml", template.BodyHtml);
                    command.Parameters.AddWithValue("@IsActive", template.IsActive);
                    command.Parameters.AddWithValue("@IsDefault", template.IsDefault);
                    command.Parameters.AddWithValue("@CreatedBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                    if (hasTemplateBranch)
                    {
                        command.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
                    }

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task UpdateTemplateAsync(EmailTemplate template, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    UPDATE tbl_EmailTemplates
                    SET TemplateName = @TemplateName,
                        TemplateType = @TemplateType,
                        Subject = @Subject,
                        BodyHtml = @BodyHtml,
                        IsActive = @IsActive,
                        IsDefault = @IsDefault,
                        UpdatedBy = @UpdatedBy,
                        UpdatedAt = GETDATE()
                    WHERE EmailTemplateID = @Id
                    {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", template.EmailTemplateID);
                    command.Parameters.AddWithValue("@TemplateName", template.TemplateName);
                    command.Parameters.AddWithValue("@TemplateType", template.TemplateType);
                    command.Parameters.AddWithValue("@Subject", template.Subject);
                    command.Parameters.AddWithValue("@BodyHtml", template.BodyHtml);
                    command.Parameters.AddWithValue("@IsActive", template.IsActive);
                    command.Parameters.AddWithValue("@IsDefault", template.IsDefault);
                    command.Parameters.AddWithValue("@UpdatedBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                    if (hasTemplateBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task DeleteTemplateAsync(int id, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $"DELETE FROM tbl_EmailTemplates WHERE EmailTemplateID = @Id {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    if (hasTemplateBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task SetDefaultTemplateAsync(int id, string templateType, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                // First, remove default from all templates of this type
                var query1 = $@"
                    UPDATE tbl_EmailTemplates
                    SET IsDefault = 0
                    WHERE TemplateType = @TemplateType
                    {branchFilter}";

                using (var command = new SqlCommand(query1, connection))
                {
                    command.Parameters.AddWithValue("@TemplateType", templateType);
                    if (hasTemplateBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                    await command.ExecuteNonQueryAsync();
                }

                // Then set this template as default
                var query2 = $@"
                    UPDATE tbl_EmailTemplates
                    SET IsDefault = 1
                    WHERE EmailTemplateID = @Id
                    {branchFilter}";

                using (var command = new SqlCommand(query2, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    if (hasTemplateBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<bool> HasColumnAsync(SqlConnection connection, string tableName, string columnName)
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(1)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName", connection);
                cmd.Parameters.AddWithValue("@TableName", tableName);
                cmd.Parameters.AddWithValue("@ColumnName", columnName);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                return false;
            }
        }

        private int? GetCurrentUserId()
        {
            try
            {
                var userIdClaim = User.FindFirst("UserID")?.Value;
                if (int.TryParse(userIdClaim, out int userId))
                {
                    return userId;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
