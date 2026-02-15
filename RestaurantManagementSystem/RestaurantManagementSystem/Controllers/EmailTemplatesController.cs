using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Filters;
using RestaurantManagementSystem.Models.Authorization;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
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
                ViewBag.TargetBranches = await GetTargetBranchesAsync(activeBranchId.Value);
                ViewBag.CanCopyEmailTemplates = CanCurrentUserCopyEmailTemplates(activeBranchId.Value);
                return View(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading email templates");
                TempData["ErrorMessage"] = $"Error loading templates: {ex.Message}";
                return View(new List<EmailTemplate>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("NAV_SETTINGS_EMAIL_TEMPLATES", PermissionAction.Edit)]
        public async Task<IActionResult> CopyToBranch([FromBody] CopyEmailTemplatesRequest request)
        {
            var activeBranchId = User.GetActiveBranchId();
            if (!activeBranchId.HasValue)
            {
                return Json(new { success = false, message = "Active branch is required." });
            }

            if (!CanCurrentUserCopyEmailTemplates(activeBranchId.Value))
            {
                return Json(new { success = false, message = "Template copy is allowed only for Administrator in Main Branch." });
            }

            if (request == null || request.TargetBranchId <= 0)
            {
                return Json(new { success = false, message = "Please select a valid target branch." });
            }

            if (request.TargetBranchId == activeBranchId.Value)
            {
                return Json(new { success = false, message = "Source and target branch cannot be the same." });
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                    if (!hasTemplateBranch)
                    {
                        return Json(new { success = false, message = "Template BranchId column is missing. Please run branch-wise template schema update." });
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        var targetBranchCheckSql = @"
SELECT COUNT(1)
FROM dbo.Branches
WHERE BranchId = @TargetBranchId
  AND ISNULL(IsActive, 1) = 1";

                        using (var targetBranchCmd = new SqlCommand(targetBranchCheckSql, connection, transaction))
                        {
                            targetBranchCmd.Parameters.AddWithValue("@TargetBranchId", request.TargetBranchId);
                            var exists = Convert.ToInt32(await targetBranchCmd.ExecuteScalarAsync()) > 0;
                            if (!exists)
                            {
                                return Json(new { success = false, message = "Target branch not found or inactive." });
                            }
                        }

                        var sourceTemplates = new List<EmailTemplate>();
                        var sourceSql = @"
SELECT EmailTemplateID, TemplateName, TemplateType, Subject, BodyHtml, IsActive, IsDefault
FROM dbo.tbl_EmailTemplates
WHERE BranchId = @SourceBranchId";

                        using (var sourceCmd = new SqlCommand(sourceSql, connection, transaction))
                        {
                            sourceCmd.Parameters.AddWithValue("@SourceBranchId", activeBranchId.Value);
                            using (var reader = await sourceCmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    sourceTemplates.Add(new EmailTemplate
                                    {
                                        EmailTemplateID = reader.GetInt32(0),
                                        TemplateName = reader.GetString(1),
                                        TemplateType = reader.GetString(2),
                                        Subject = reader.GetString(3),
                                        BodyHtml = reader.GetString(4),
                                        IsActive = reader.GetBoolean(5),
                                        IsDefault = reader.GetBoolean(6)
                                    });
                                }
                            }
                        }

                        if (sourceTemplates.Count == 0)
                        {
                            return Json(new { success = false, message = "No templates found in source branch." });
                        }

                        var nonDefaultSql = @"
UPDATE dbo.tbl_EmailTemplates
SET IsDefault = 0,
    UpdatedBy = @UpdatedBy,
    UpdatedAt = GETDATE()
WHERE BranchId = @TargetBranchId";

                        using (var nonDefaultCmd = new SqlCommand(nonDefaultSql, connection, transaction))
                        {
                            nonDefaultCmd.Parameters.AddWithValue("@TargetBranchId", request.TargetBranchId);
                            nonDefaultCmd.Parameters.AddWithValue("@UpdatedBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                            await nonDefaultCmd.ExecuteNonQueryAsync();
                        }

                        var upsertSql = @"
IF EXISTS (
    SELECT 1
    FROM dbo.tbl_EmailTemplates
    WHERE BranchId = @TargetBranchId
      AND TemplateName = @TemplateName
      AND TemplateType = @TemplateType
)
BEGIN
    UPDATE dbo.tbl_EmailTemplates
    SET Subject = @Subject,
        BodyHtml = @BodyHtml,
        IsActive = @IsActive,
        IsDefault = @IsDefault,
        UpdatedBy = @UpdatedBy,
        UpdatedAt = GETDATE()
    WHERE BranchId = @TargetBranchId
      AND TemplateName = @TemplateName
      AND TemplateType = @TemplateType;
END
ELSE
BEGIN
    INSERT INTO dbo.tbl_EmailTemplates
    (
        TemplateName, TemplateType, Subject, BodyHtml,
        IsActive, IsDefault, CreatedBy, CreatedAt, BranchId
    )
    VALUES
    (
        @TemplateName, @TemplateType, @Subject, @BodyHtml,
        @IsActive, @IsDefault, @CreatedBy, GETDATE(), @TargetBranchId
    );
END";

                        foreach (var template in sourceTemplates)
                        {
                            using (var upsertCmd = new SqlCommand(upsertSql, connection, transaction))
                            {
                                upsertCmd.Parameters.AddWithValue("@TargetBranchId", request.TargetBranchId);
                                upsertCmd.Parameters.AddWithValue("@TemplateName", template.TemplateName);
                                upsertCmd.Parameters.AddWithValue("@TemplateType", template.TemplateType);
                                upsertCmd.Parameters.AddWithValue("@Subject", template.Subject);
                                upsertCmd.Parameters.AddWithValue("@BodyHtml", template.BodyHtml);
                                upsertCmd.Parameters.AddWithValue("@IsActive", template.IsActive);
                                upsertCmd.Parameters.AddWithValue("@IsDefault", template.IsDefault);
                                upsertCmd.Parameters.AddWithValue("@CreatedBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                                upsertCmd.Parameters.AddWithValue("@UpdatedBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                                await upsertCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var targetDefaults = sourceTemplates
                            .Where(t => t.IsDefault)
                            .GroupBy(t => t.TemplateType)
                            .Select(g => new { TemplateType = g.Key, TemplateName = g.First().TemplateName })
                            .ToList();

                        var normalizeDefaultSql = @"
UPDATE t
SET t.IsDefault = CASE WHEN t.TemplateName = @DefaultTemplateName THEN 1 ELSE 0 END,
    t.UpdatedBy = @UpdatedBy,
    t.UpdatedAt = GETDATE()
FROM dbo.tbl_EmailTemplates t
WHERE t.BranchId = @TargetBranchId
  AND t.TemplateType = @TemplateType";

                        foreach (var item in targetDefaults)
                        {
                            using (var normalizeCmd = new SqlCommand(normalizeDefaultSql, connection, transaction))
                            {
                                normalizeCmd.Parameters.AddWithValue("@TargetBranchId", request.TargetBranchId);
                                normalizeCmd.Parameters.AddWithValue("@TemplateType", item.TemplateType);
                                normalizeCmd.Parameters.AddWithValue("@DefaultTemplateName", item.TemplateName);
                                normalizeCmd.Parameters.AddWithValue("@UpdatedBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                                await normalizeCmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                }

                return Json(new { success = true, message = "Templates copied successfully to target branch." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying templates to target branch");
                return Json(new { success = false, message = $"Error copying templates: {ex.Message}" });
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

        private bool CanCurrentUserCopyEmailTemplates(int activeBranchId)
        {
            if (User?.Identity?.IsAuthenticated != true || !User.IsInRole("Administrator"))
            {
                return false;
            }

            try
            {
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
                        cmd.Parameters.AddWithValue("@BranchId", activeBranchId);
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<List<BranchMaster>> GetTargetBranchesAsync(int activeBranchId)
        {
            var branches = new List<BranchMaster>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var cmd = new SqlCommand(@"
SELECT BranchId, BranchCode, BranchName, ISNULL(Is_MainBranch, 0), ISNULL(IsActive, 1)
FROM dbo.Branches
WHERE ISNULL(IsActive, 1) = 1
  AND BranchId <> @ActiveBranchId
ORDER BY BranchName", connection))
                {
                    cmd.Parameters.AddWithValue("@ActiveBranchId", activeBranchId);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            branches.Add(new BranchMaster
                            {
                                BranchId = reader.GetInt32(0),
                                BranchCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                BranchName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Is_MainBranch = !reader.IsDBNull(3) && reader.GetBoolean(3),
                                IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4)
                            });
                        }
                    }
                }
            }

            return branches;
        }

        #endregion
    }

    public class CopyEmailTemplatesRequest
    {
        public int TargetBranchId { get; set; }
    }
}
