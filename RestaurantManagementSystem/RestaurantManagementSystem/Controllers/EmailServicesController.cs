using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.ViewModels;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Filters;
using RestaurantManagementSystem.Models.Authorization;
using RestaurantManagementSystem.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize(Roles = "Administrator,Manager,Floor Manager")]
    [RequirePermission("NAV_SETTINGS_EMAIL_SERVICES", PermissionAction.View)]
    public class EmailServicesController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<EmailServicesController> _logger;
        private readonly IConfiguration _configuration;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _encryptionIV;

        public EmailServicesController(
            IConfiguration configuration,
            ILogger<EmailServicesController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string not found");
            _logger = logger;
            _configuration = configuration;
            
            // Get encryption keys from configuration
            var keyString = configuration["Encryption:Key"];
            var ivString = configuration["Encryption:IV"];
            
            if (string.IsNullOrEmpty(keyString) || string.IsNullOrEmpty(ivString))
            {
                throw new InvalidOperationException("Encryption keys not configured");
            }
            
            _encryptionKey = Convert.FromBase64String(keyString);
            _encryptionIV = Convert.FromBase64String(ivString);
        }

        // GET: EmailServices
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

                var viewModel = new EmailServicesViewModel
                {
                    TodayBirthdays = await GetTodayBirthdaysAsync(activeBranchId),
                    TodayAnniversaries = await GetTodayAnniversariesAsync(activeBranchId),
                    AllGuests = await GetAllGuestsWithEmailAsync(activeBranchId),
                    CustomTemplates = await GetCustomTemplatesAsync(activeBranchId)
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Email Services page");
                TempData["ErrorMessage"] = $"Error loading email services: {ex.Message}";
                return View(new EmailServicesViewModel());
            }
        }

        // POST: EmailServices/AutoFireEmails
        [HttpPost]
        public async Task<IActionResult> AutoFireEmails([FromBody] AutoFireEmailRequest request)
        {
            _logger.LogInformation("AutoFireEmails called - EmailType: {EmailType}, GuestCount: {Count}", 
                request?.EmailType, request?.GuestIds?.Count ?? 0);
            
            var stopwatch = Stopwatch.StartNew();
            var result = new EmailCampaignResultViewModel();

            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    return Json(new { success = false, message = "No active branch selected" });
                }

                if (request == null)
                {
                    _logger.LogWarning("AutoFireEmails - Request is null");
                    return Json(new { success = false, message = "Invalid request" });
                }
                
                if (request.GuestIds == null || !request.GuestIds.Any())
                {
                    _logger.LogWarning("AutoFireEmails - No guests selected");
                    return Json(new { success = false, message = "No guests selected" });
                }

                // Get mail configuration
                var mailConfig = await GetMailConfigurationAsync(activeBranchId);
                if (mailConfig == null)
                {
                    return Json(new { success = false, message = "Mail configuration not found. Please configure email settings first." });
                }

                // Get template
                var template = await GetDefaultTemplateAsync(request.EmailType, activeBranchId);
                if (template == null)
                {
                    return Json(new { success = false, message = $"No default {request.EmailType} template found" });
                }

                // Get guests
                var guests = await GetGuestsByIdsAsync(request.GuestIds, activeBranchId);
                result.TotalAttempted = guests.Count;

                foreach (var guest in guests)
                {
                    try
                    {
                        // Replace placeholders in template
                        var subject = ReplacePlaceholders(template.Subject, guest, mailConfig);
                        var body = ReplacePlaceholders(template.BodyHtml, guest, mailConfig);

                        // Send email
                        var emailResult = await SendEmailAsync(mailConfig, guest.Email, subject, body);

                        if (emailResult.Success)
                        {
                            result.SuccessCount++;
                            
                            // Log to tbl_EmailLog
                            await LogEmailAsync(
                                toEmail: guest.Email ?? string.Empty,
                                subject: subject,
                                body: body,
                                status: "Success",
                                errorMessage: null,
                                processingTimeMs: emailResult.ProcessingTimeMs,
                                fromEmail: mailConfig.FromEmail,
                                fromName: mailConfig.FromName,
                                smtpServer: mailConfig.SmtpServer,
                                smtpPort: mailConfig.SmtpPort,
                                emailType: $"{request.EmailType} Campaign",
                                branchId: activeBranchId
                            );
                            
                            // Log to campaign history
                            await LogCampaignHistoryAsync(new EmailCampaignHistory
                            {
                                CampaignType = request.EmailType,
                                GuestId = guest.Id,
                                GuestName = guest.GuestName ?? "Unknown",
                                GuestEmail = guest.Email ?? string.Empty,
                                EmailSubject = subject,
                                EmailBody = body,
                                SentAt = DateTime.Now,
                                Status = "Success",
                                ProcessingTimeMs = emailResult.ProcessingTimeMs,
                                SentBy = GetCurrentUserId()
                            }, activeBranchId);
                        }
                        else
                        {
                            result.FailureCount++;
                            result.Errors.Add($"{guest.GuestName}: {emailResult.ErrorMessage}");
                            
                            // Log to tbl_EmailLog
                            await LogEmailAsync(
                                toEmail: guest.Email ?? string.Empty,
                                subject: subject,
                                body: body,
                                status: "Failed",
                                errorMessage: emailResult.ErrorMessage,
                                processingTimeMs: emailResult.ProcessingTimeMs,
                                fromEmail: mailConfig.FromEmail,
                                fromName: mailConfig.FromName,
                                smtpServer: mailConfig.SmtpServer,
                                smtpPort: mailConfig.SmtpPort,
                                emailType: $"{request.EmailType} Campaign",
                                branchId: activeBranchId
                            );
                            
                            // Log failed attempt to campaign history
                            await LogCampaignHistoryAsync(new EmailCampaignHistory
                            {
                                CampaignType = request.EmailType,
                                GuestId = guest.Id,
                                GuestName = guest.GuestName ?? "Unknown",
                                GuestEmail = guest.Email ?? string.Empty,
                                EmailSubject = subject,
                                EmailBody = body,
                                SentAt = DateTime.Now,
                                Status = "Failed",
                                ErrorMessage = emailResult.ErrorMessage,
                                SentBy = GetCurrentUserId()
                            }, activeBranchId);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailureCount++;
                        result.Errors.Add($"{guest.GuestName}: {ex.Message}");
                        _logger.LogError(ex, "Error sending email to guest {GuestId}", guest.Id);
                        
                        // Log exception to tbl_EmailLog
                        try
                        {
                            await LogEmailAsync(
                                toEmail: guest.Email ?? string.Empty,
                                subject: template?.Subject ?? "Unknown",
                                body: "Exception occurred: " + ex.Message,
                                status: "Exception",
                                errorMessage: ex.Message + (ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : ""),
                                processingTimeMs: 0,
                                fromEmail: mailConfig.FromEmail,
                                fromName: mailConfig.FromName,
                                smtpServer: mailConfig.SmtpServer,
                                smtpPort: mailConfig.SmtpPort,
                                emailType: $"{request.EmailType} Campaign",
                                branchId: activeBranchId
                            );
                        }
                        catch { /* Ignore logging errors */ }
                    }
                }

                stopwatch.Stop();
                result.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;

                return Json(new
                {
                    success = true,
                    result = result,
                    message = $"Email campaign completed: {result.SuccessCount} sent, {result.FailureCount} failed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoFireEmails");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: EmailServices/SendCustomEmail
        [HttpPost]
        public async Task<IActionResult> SendCustomEmail([FromBody] SendCustomEmailRequest request)
        {
            _logger.LogInformation("SendCustomEmail called - GuestCount: {Count}", request?.GuestIds?.Count ?? 0);
            
            var stopwatch = Stopwatch.StartNew();
            var result = new EmailCampaignResultViewModel();

            try
            {
                var activeBranchId = User.GetActiveBranchId();
                if (!activeBranchId.HasValue)
                {
                    return Json(new { success = false, message = "No active branch selected" });
                }

                if (request == null)
                {
                    _logger.LogWarning("SendCustomEmail - Request is null");
                    return Json(new { success = false, message = "Invalid request" });
                }
                
                if (request.GuestIds == null || !request.GuestIds.Any())
                {
                    _logger.LogWarning("SendCustomEmail - No guests selected");
                    return Json(new { success = false, message = "No guests selected" });
                }

                // Get mail configuration
                var mailConfig = await GetMailConfigurationAsync(activeBranchId);
                if (mailConfig == null)
                {
                    return Json(new { success = false, message = "Mail configuration not found" });
                }

                string subject, body;

                if (request.TemplateId.HasValue)
                {
                    // Use template
                    var template = await GetTemplateByIdAsync(request.TemplateId.Value, activeBranchId);
                    if (template == null)
                    {
                        return Json(new { success = false, message = "Template not found" });
                    }
                    subject = template.Subject;
                    body = template.BodyHtml;
                }
                else
                {
                    // Use custom subject and body
                    if (string.IsNullOrWhiteSpace(request.CustomSubject) || string.IsNullOrWhiteSpace(request.CustomBody))
                    {
                        return Json(new { success = false, message = "Please provide either a template or custom subject and body" });
                    }
                    subject = request.CustomSubject;
                    body = request.CustomBody;
                }

                // Get guests
                var guests = await GetGuestsByIdsAsync(request.GuestIds, activeBranchId);
                result.TotalAttempted = guests.Count;

                foreach (var guest in guests)
                {
                    try
                    {
                        // Replace placeholders
                        var personalizedSubject = ReplacePlaceholders(subject, guest, mailConfig);
                        var personalizedBody = ReplacePlaceholders(body, guest, mailConfig);

                        // Send email
                        var emailResult = await SendEmailAsync(mailConfig, guest.Email, personalizedSubject, personalizedBody);

                        if (emailResult.Success)
                        {
                            result.SuccessCount++;
                            
                            // Log to tbl_EmailLog
                            await LogEmailAsync(
                                toEmail: guest.Email ?? string.Empty,
                                subject: personalizedSubject,
                                body: personalizedBody,
                                status: "Success",
                                errorMessage: null,
                                processingTimeMs: emailResult.ProcessingTimeMs,
                                fromEmail: mailConfig.FromEmail,
                                fromName: mailConfig.FromName,
                                smtpServer: mailConfig.SmtpServer,
                                smtpPort: mailConfig.SmtpPort,
                                emailType: "Custom Campaign",
                                branchId: activeBranchId
                            );
                            
                            await LogCampaignHistoryAsync(new EmailCampaignHistory
                            {
                                CampaignType = "Custom",
                                GuestId = guest.Id,
                                GuestName = guest.GuestName ?? "Unknown",
                                GuestEmail = guest.Email ?? string.Empty,
                                EmailSubject = personalizedSubject,
                                EmailBody = personalizedBody,
                                SentAt = DateTime.Now,
                                Status = "Success",
                                ProcessingTimeMs = emailResult.ProcessingTimeMs,
                                SentBy = GetCurrentUserId()
                            }, activeBranchId);
                        }
                        else
                        {
                            result.FailureCount++;
                            result.Errors.Add($"{guest.GuestName}: {emailResult.ErrorMessage}");
                            
                            // Log to tbl_EmailLog
                            await LogEmailAsync(
                                toEmail: guest.Email ?? string.Empty,
                                subject: personalizedSubject,
                                body: personalizedBody,
                                status: "Failed",
                                errorMessage: emailResult.ErrorMessage,
                                processingTimeMs: emailResult.ProcessingTimeMs,
                                fromEmail: mailConfig.FromEmail,
                                fromName: mailConfig.FromName,
                                smtpServer: mailConfig.SmtpServer,
                                smtpPort: mailConfig.SmtpPort,
                                emailType: "Custom Campaign",
                                branchId: activeBranchId
                            );
                            
                            await LogCampaignHistoryAsync(new EmailCampaignHistory
                            {
                                CampaignType = "Custom",
                                GuestId = guest.Id,
                                GuestName = guest.GuestName ?? "Unknown",
                                GuestEmail = guest.Email ?? string.Empty,
                                EmailSubject = personalizedSubject,
                                EmailBody = personalizedBody,
                                SentAt = DateTime.Now,
                                Status = "Failed",
                                ErrorMessage = emailResult.ErrorMessage,
                                SentBy = GetCurrentUserId()
                            }, activeBranchId);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailureCount++;
                        result.Errors.Add($"{guest.GuestName}: {ex.Message}");
                        _logger.LogError(ex, "Error sending custom email to guest {GuestId}", guest.Id);
                        
                        // Log exception to tbl_EmailLog
                        try
                        {
                            await LogEmailAsync(
                                toEmail: guest.Email ?? string.Empty,
                                subject: subject ?? "Unknown",
                                body: "Exception occurred: " + ex.Message,
                                status: "Exception",
                                errorMessage: ex.Message + (ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : ""),
                                processingTimeMs: 0,
                                fromEmail: mailConfig.FromEmail,
                                fromName: mailConfig.FromName,
                                smtpServer: mailConfig.SmtpServer,
                                smtpPort: mailConfig.SmtpPort,
                                emailType: "Custom Campaign",
                                branchId: activeBranchId
                            );
                        }
                        catch { /* Ignore logging errors */ }
                    }
                }

                stopwatch.Stop();
                result.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;

                return Json(new
                {
                    success = true,
                    result = result,
                    message = $"Custom email campaign completed: {result.SuccessCount} sent, {result.FailureCount} failed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendCustomEmail");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        #region Helper Methods

        private async Task<List<BirthdayGuestViewModel>> GetTodayBirthdaysAsync(int? branchId)
        {
            var birthdays = new List<BirthdayGuestViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasFeedbackBranch = await HasColumnAsync(connection, "GuestFeedback", "BranchId");
                var hasCampaignBranch = await HasColumnAsync(connection, "tbl_EmailCampaignHistory", "BranchId");

                var guestBranchFilter = hasFeedbackBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;
                var campaignBranchFilter = hasCampaignBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT 
                        gf.Id, 
                        gf.GuestName, 
                        gf.Email, 
                        gf.GuestBirthDate,
                        DATEDIFF(YEAR, gf.GuestBirthDate, GETDATE()) - 
                        CASE WHEN (MONTH(gf.GuestBirthDate) > MONTH(GETDATE())) OR 
                                  (MONTH(gf.GuestBirthDate) = MONTH(GETDATE()) AND DAY(gf.GuestBirthDate) > DAY(GETDATE()))
                        THEN 1 ELSE 0 END as Age,
                        (SELECT TOP 1 SentAt FROM tbl_EmailCampaignHistory 
                         WHERE GuestEmail = gf.Email 
                         AND CampaignType = 'Birthday' 
                         AND CAST(SentAt AS DATE) = CAST(GETDATE() AS DATE)
                         AND Status = 'Success'
                         {campaignBranchFilter}
                         ORDER BY SentAt DESC) as LastSentDate
                    FROM (
                        SELECT 
                            MIN(Id) as Id,
                            MAX(GuestName) as GuestName,
                            Email,
                            GuestBirthDate
                        FROM GuestFeedback
                        WHERE Email IS NOT NULL 
                        AND Email <> ''
                        AND GuestBirthDate IS NOT NULL
                        AND MONTH(GuestBirthDate) = MONTH(GETDATE())
                        AND DAY(GuestBirthDate) = DAY(GETDATE())
                        {guestBranchFilter}
                        GROUP BY Email, GuestBirthDate
                    ) gf
                    ORDER BY gf.GuestName";

                using (var command = new SqlCommand(query, connection))
                {
                    if (branchId.HasValue && (hasFeedbackBranch || hasCampaignBranch))
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        birthdays.Add(new BirthdayGuestViewModel
                        {
                            GuestId = reader.GetInt32(0),
                            GuestName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                            Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            BirthDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            Age = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            LastSentDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                            AlreadySent = !reader.IsDBNull(5)
                        });
                    }
                }
                }
            }

            return birthdays;
        }

        private async Task<List<AnniversaryGuestViewModel>> GetTodayAnniversariesAsync(int? branchId)
        {
            var anniversaries = new List<AnniversaryGuestViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasFeedbackBranch = await HasColumnAsync(connection, "GuestFeedback", "BranchId");
                var hasCampaignBranch = await HasColumnAsync(connection, "tbl_EmailCampaignHistory", "BranchId");

                var guestBranchFilter = hasFeedbackBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;
                var campaignBranchFilter = hasCampaignBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT 
                        gf.Id, 
                        gf.GuestName, 
                        gf.Email, 
                        gf.AnniversaryDate,
                        DATEDIFF(YEAR, gf.AnniversaryDate, GETDATE()) - 
                        CASE WHEN (MONTH(gf.AnniversaryDate) > MONTH(GETDATE())) OR 
                                  (MONTH(gf.AnniversaryDate) = MONTH(GETDATE()) AND DAY(gf.AnniversaryDate) > DAY(GETDATE()))
                        THEN 1 ELSE 0 END as Years,
                        (SELECT TOP 1 SentAt FROM tbl_EmailCampaignHistory 
                         WHERE GuestEmail = gf.Email 
                         AND CampaignType = 'Anniversary' 
                         AND CAST(SentAt AS DATE) = CAST(GETDATE() AS DATE)
                         AND Status = 'Success'
                         {campaignBranchFilter}
                         ORDER BY SentAt DESC) as LastSentDate
                    FROM (
                        SELECT 
                            MIN(Id) as Id,
                            MAX(GuestName) as GuestName,
                            Email,
                            AnniversaryDate
                        FROM GuestFeedback
                        WHERE Email IS NOT NULL 
                        AND Email <> ''
                        AND AnniversaryDate IS NOT NULL
                        AND MONTH(AnniversaryDate) = MONTH(GETDATE())
                        AND DAY(AnniversaryDate) = DAY(GETDATE())
                        {guestBranchFilter}
                        GROUP BY Email, AnniversaryDate
                    ) gf
                    ORDER BY gf.GuestName";

                using (var command = new SqlCommand(query, connection))
                {
                    if (branchId.HasValue && (hasFeedbackBranch || hasCampaignBranch))
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        anniversaries.Add(new AnniversaryGuestViewModel
                        {
                            GuestId = reader.GetInt32(0),
                            GuestName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                            Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            AnniversaryDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            Years = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            LastSentDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                            AlreadySent = !reader.IsDBNull(5)
                        });
                    }
                }
                }
            }

            return anniversaries;
        }

        private async Task<List<GuestEmailViewModel>> GetAllGuestsWithEmailAsync(int? branchId)
        {
            var guests = new List<GuestEmailViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasFeedbackBranch = await HasColumnAsync(connection, "GuestFeedback", "BranchId");
                var branchFilter = hasFeedbackBranch && branchId.HasValue ? "AND gf.BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT 
                        MIN(gf.Id) as Id,
                        MAX(gf.GuestName) as GuestName,
                        gf.Email,
                        MAX(gf.VisitDate) as LastVisitDate,
                        COUNT(*) as TotalVisits
                    FROM GuestFeedback gf
                    WHERE gf.Email IS NOT NULL 
                    AND gf.Email <> ''
                    AND gf.GuestName IS NOT NULL
                    {branchFilter}
                    GROUP BY gf.Email
                    ORDER BY MAX(gf.GuestName)";

                using (var command = new SqlCommand(query, connection))
                {
                    if (hasFeedbackBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        guests.Add(new GuestEmailViewModel
                        {
                            GuestId = reader.GetInt32(0),
                            GuestName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                            Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            LastVisitDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            TotalVisits = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                        });
                    }
                }
                }
            }

            return guests;
        }

        private async Task<List<EmailTemplateViewModel>> GetCustomTemplatesAsync(int? branchId)
        {
            var templates = new List<EmailTemplateViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT EmailTemplateID, TemplateName, TemplateType, Subject, BodyHtml, IsActive, IsDefault
                    FROM tbl_EmailTemplates
                    WHERE TemplateType IN ('Custom', 'Promotional')
                    AND IsActive = 1
                    {branchFilter}
                    ORDER BY TemplateName";

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
                        templates.Add(new EmailTemplateViewModel
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
            }

            return templates;
        }

        private async Task<EmailTemplate?> GetDefaultTemplateAsync(string templateType, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT TOP 1 EmailTemplateID, TemplateName, TemplateType, Subject, BodyHtml, IsActive, IsDefault
                    FROM tbl_EmailTemplates
                    WHERE TemplateType = @TemplateType
                    AND IsActive = 1
                    AND IsDefault = 1
                    {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TemplateType", templateType);
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

        private async Task<EmailTemplate?> GetTemplateByIdAsync(int templateId, int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasTemplateBranch = await HasColumnAsync(connection, "tbl_EmailTemplates", "BranchId");
                var branchFilter = hasTemplateBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT EmailTemplateID, TemplateName, TemplateType, Subject, BodyHtml, IsActive, IsDefault
                    FROM tbl_EmailTemplates
                    WHERE EmailTemplateID = @EmailTemplateID
                    {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmailTemplateID", templateId);
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

        private async Task<List<GuestFeedback>> GetGuestsByIdsAsync(List<int> guestIds, int? branchId)
        {
            var guests = new List<GuestFeedback>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasFeedbackBranch = await HasColumnAsync(connection, "GuestFeedback", "BranchId");
                var branchFilter = hasFeedbackBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var ids = string.Join(",", guestIds);
                var query = $@"
                    SELECT Id, GuestName, Email, GuestBirthDate, AnniversaryDate
                    FROM GuestFeedback
                    WHERE Id IN ({ids})
                    {branchFilter}";

                using (var command = new SqlCommand(query, connection))
                {
                    if (hasFeedbackBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        guests.Add(new GuestFeedback
                        {
                            Id = reader.GetInt32(0),
                            GuestName = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                            GuestBirthDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            AnniversaryDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4)
                        });
                    }
                }
                }
            }

            return guests;
        }

        private string ReplacePlaceholders(string template, GuestFeedback guest, MailConfigurationViewModel mailConfig)
        {
            var result = template;
            
            result = result.Replace("{GuestName}", guest.GuestName ?? "Valued Guest");
            result = result.Replace("{RestaurantName}", mailConfig.FromName ?? "Restaurant");
            result = result.Replace("{Year}", DateTime.Now.Year.ToString());
            
            if (guest.GuestBirthDate.HasValue)
            {
                var age = DateTime.Now.Year - guest.GuestBirthDate.Value.Year;
                if (guest.GuestBirthDate.Value > DateTime.Now.AddYears(-age)) age--;
                result = result.Replace("{Age}", age.ToString());
            }
            
            if (guest.AnniversaryDate.HasValue)
            {
                var years = DateTime.Now.Year - guest.AnniversaryDate.Value.Year;
                if (guest.AnniversaryDate.Value > DateTime.Now.AddYears(-years)) years--;
                result = result.Replace("{Years}", years.ToString());
            }
            
            return result;
        }

        private async Task<(bool Success, string? ErrorMessage, int ProcessingTimeMs)> SendEmailAsync(
            MailConfigurationViewModel mailConfig, string? toEmail, string subject, string body)
        {
            if (string.IsNullOrEmpty(toEmail))
            {
                return (false, "Email address is empty", 0);
            }

            return await MailKitEmailHelper.SendEmailAsync(
                smtpServer: mailConfig.SmtpServer,
                smtpPort: mailConfig.SmtpPort,
                smtpUsername: mailConfig.SmtpUsername,
                smtpPassword: mailConfig.SmtpPassword,
                enableSsl: mailConfig.EnableSSL,
                fromEmail: mailConfig.FromEmail,
                fromName: mailConfig.FromName,
                toEmail: toEmail,
                subject: subject,
                htmlBody: body,
                logger: _logger);
        }

        private async Task<MailConfigurationViewModel?> GetMailConfigurationAsync(int? branchId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var hasMailBranch = await HasColumnAsync(connection, "tbl_MailConfiguration", "BranchId");
                var branchFilter = hasMailBranch && branchId.HasValue ? "AND BranchId = @BranchId" : string.Empty;

                var query = $@"
                    SELECT Id, SmtpServer, SmtpPort, SmtpUsername, SmtpPassword, EnableSSL, 
                           FromEmail, FromName, AdminNotificationEmail, IsActive 
                    FROM tbl_MailConfiguration
                    WHERE IsActive = 1
                    {branchFilter}
                    ORDER BY Id DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    if (hasMailBranch && branchId.HasValue)
                    {
                        command.Parameters.AddWithValue("@BranchId", branchId.Value);
                    }
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var encryptedPassword = reader.GetString(4);
                        var decryptedPassword = DecryptPassword(encryptedPassword);
                        
                        return new MailConfigurationViewModel
                        {
                            Id = reader.GetInt32(0),
                            SmtpServer = reader.GetString(1),
                            SmtpPort = reader.GetInt32(2),
                            SmtpUsername = reader.GetString(3),
                            SmtpPassword = decryptedPassword,
                            EnableSSL = reader.GetBoolean(5),
                            FromEmail = reader.GetString(6),
                            FromName = reader.GetString(7),
                            AdminNotificationEmail = reader.IsDBNull(8) ? null : reader.GetString(8),
                            IsActive = reader.GetBoolean(9)
                        };
                    }
                }
                }
            }

            return null;
        }

        private string DecryptPassword(string encryptedPassword)
        {
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.IV = _encryptionIV;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                    var cipherBytes = Convert.FromBase64String(encryptedPassword);

                    using (var msDecrypt = new MemoryStream(cipherBytes))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt password, returning as-is");
                return encryptedPassword;
            }
        }

        private async Task LogCampaignHistoryAsync(EmailCampaignHistory history, int? branchId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var hasCampaignBranch = await HasColumnAsync(connection, "tbl_EmailCampaignHistory", "BranchId");

                    var query = hasCampaignBranch
                        ? @"INSERT INTO tbl_EmailCampaignHistory (
                                CampaignType, GuestId, GuestName, GuestEmail, EmailSubject, EmailBody,
                                SentAt, Status, ErrorMessage, ProcessingTimeMs, SentBy, BranchId
                           ) VALUES (
                                @CampaignType, @GuestId, @GuestName, @GuestEmail, @EmailSubject, @EmailBody,
                                @SentAt, @Status, @ErrorMessage, @ProcessingTimeMs, @SentBy, @BranchId
                           )"
                        : @"INSERT INTO tbl_EmailCampaignHistory (
                                CampaignType, GuestId, GuestName, GuestEmail, EmailSubject, EmailBody,
                                SentAt, Status, ErrorMessage, ProcessingTimeMs, SentBy
                           ) VALUES (
                                @CampaignType, @GuestId, @GuestName, @GuestEmail, @EmailSubject, @EmailBody,
                                @SentAt, @Status, @ErrorMessage, @ProcessingTimeMs, @SentBy
                           )";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CampaignType", history.CampaignType);
                        command.Parameters.AddWithValue("@GuestId", history.GuestId);
                        command.Parameters.AddWithValue("@GuestName", history.GuestName);
                        command.Parameters.AddWithValue("@GuestEmail", history.GuestEmail);
                        command.Parameters.AddWithValue("@EmailSubject", history.EmailSubject);
                        command.Parameters.AddWithValue("@EmailBody", history.EmailBody);
                        command.Parameters.AddWithValue("@SentAt", history.SentAt);
                        command.Parameters.AddWithValue("@Status", history.Status);
                        command.Parameters.AddWithValue("@ErrorMessage", (object?)history.ErrorMessage ?? DBNull.Value);
                        command.Parameters.AddWithValue("@ProcessingTimeMs", (object?)history.ProcessingTimeMs ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SentBy", (object?)history.SentBy ?? DBNull.Value);
                        if (hasCampaignBranch)
                        {
                            command.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
                        }

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging campaign history");
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

        private async Task LogEmailAsync(
            string toEmail,
            string subject,
            string body,
            string status,
            string? errorMessage,
            int? processingTimeMs,
            string? fromEmail,
            string? fromName,
            string? smtpServer,
            int? smtpPort,
            string? emailType = null,
            int? branchId = null)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var hasEmailLogBranch = await HasColumnAsync(connection, "tbl_EmailLog", "BranchId");

                    var query = hasEmailLogBranch
                        ? @"INSERT INTO tbl_EmailLog (
                                ToEmail, FromEmail, FromName, Subject, Body, Status, ErrorMessage,
                                SentAt, ProcessingTimeMs, SmtpServer, SmtpPort, SmtpUsername,
                                SmtpUseSsl, SmtpTimeout, EmailType, SentBy, SentFrom, BranchId
                           ) VALUES (
                                @ToEmail, @FromEmail, @FromName, @Subject, @Body, @Status, @ErrorMessage,
                                @SentAt, @ProcessingTimeMs, @SmtpServer, @SmtpPort, @SmtpUsername,
                                @SmtpUseSsl, @SmtpTimeout, @EmailType, @SentBy, @SentFrom, @BranchId
                           )"
                        : @"INSERT INTO tbl_EmailLog (
                                ToEmail, FromEmail, FromName, Subject, Body, Status, ErrorMessage,
                                SentAt, ProcessingTimeMs, SmtpServer, SmtpPort, SmtpUsername,
                                SmtpUseSsl, SmtpTimeout, EmailType, SentBy, SentFrom
                           ) VALUES (
                                @ToEmail, @FromEmail, @FromName, @Subject, @Body, @Status, @ErrorMessage,
                                @SentAt, @ProcessingTimeMs, @SmtpServer, @SmtpPort, @SmtpUsername,
                                @SmtpUseSsl, @SmtpTimeout, @EmailType, @SentBy, @SentFrom
                           )";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ToEmail", toEmail);
                        command.Parameters.AddWithValue("@FromEmail", (object?)fromEmail ?? DBNull.Value);
                        command.Parameters.AddWithValue("@FromName", (object?)fromName ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Subject", subject);
                        command.Parameters.AddWithValue("@Body", body);
                        command.Parameters.AddWithValue("@Status", status);
                        command.Parameters.AddWithValue("@ErrorMessage", (object?)errorMessage ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SentAt", DateTime.Now);
                        command.Parameters.AddWithValue("@ProcessingTimeMs", (object?)processingTimeMs ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SmtpServer", (object?)smtpServer ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SmtpPort", (object?)smtpPort ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SmtpUsername", (object?)fromEmail ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SmtpUseSsl", true);
                        command.Parameters.AddWithValue("@SmtpTimeout", 30000);
                        command.Parameters.AddWithValue("@EmailType", (object?)emailType ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SentBy", (object?)GetCurrentUserId() ?? DBNull.Value);
                        command.Parameters.AddWithValue("@SentFrom", "Email Services");
                        if (hasEmailLogBranch)
                        {
                            command.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
                        }

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging email to tbl_EmailLog");
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

        #endregion
    }
}
