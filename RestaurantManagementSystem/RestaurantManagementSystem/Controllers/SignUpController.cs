using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestaurantManagementSystem.Services;
using RestaurantManagementSystem.Utilities;
using RestaurantManagementSystem.ViewModels;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    [AllowAnonymous]
    public class SignUpController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<SignUpController> _logger;
        private readonly string _connectionString;

        public SignUpController(
            IConfiguration config,
            IEmailSender emailSender,
            ILogger<SignUpController> logger)
        {
            _config = config;
            _emailSender = emailSender;
            _logger = logger;
            _connectionString = _config.GetConnectionString("DefaultConnection");
        }

        // GET: /SignUp
        [HttpGet]
        public IActionResult Index()
        {
            // Allow authenticated users to view the Sign Up page so it can be accessed from the Settings nav menu

            EnsureFromSignupColumn();
            return View(new SignUpViewModel());
        }

        // GET: /SignUp/GetLocations
        [HttpGet]
        public async Task<IActionResult> GetLocations()
        {
            var locations = new System.Collections.Generic.List<string>();
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                
                // Only query if the table exists
                using var checkCmd = new SqlCommand("SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'BranchLocations'", con);
                if ((int)await checkCmd.ExecuteScalarAsync() > 0)
                {
                    using var cmd = new SqlCommand("SELECT LocationName FROM dbo.BranchLocations WHERE IsActive = 1 ORDER BY LocationName", con);
                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        locations.Add(rdr.GetString(0));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch locations for autocomplete.");
            }
            return Json(locations);
        }

        // POST: /SignUp/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(SignUpViewModel model)
        {
            if (!ModelState.IsValid || !model.TermsAccepted)
            {
                return Json(new SignUpResultViewModel
                {
                    Success = false,
                    Message = !model.TermsAccepted ? "You must accept the terms and conditions." : BuildValidationErrorMessage()
                });
            }

            try
            {
                EnsureFromSignupColumn();

                // Capture client IP and browser details server-side
                var clientInfo = ClientInfoHelper.GetBrowserInfo(HttpContext);

                using var con = new SqlConnection(_connectionString);
                con.Open();

                // --- STEP 1: Check/Create BranchLocations & Branches tables ---
                EnsureTablesExist(con);

                // --- STEP 2: Insert BranchLocation ---
                int locationId = InsertOrGetLocation(con, model.Location.Trim());

                // --- STEP 3: Insert Branch (Is_MainBranch = 0) with Client Tracking Info ---
                string branchCode = GenerateUniqueBranchCode(con, model.RestaurantName.Trim());
                int branchId = InsertBranch(con, branchCode, model.RestaurantName.Trim(), locationId, clientInfo);
                if (branchId <= 0)
                    return Json(new SignUpResultViewModel { Success = false, Message = "Failed to create branch. Please try again." });

                // --- Handle Logo Upload ---
                string logoPath = "";
                if (model.Logo != null && model.Logo.Length > 0)
                {
                    string uniqueFileName = $"logo_{DateTime.Now.ToString("yyyyMMddHHmmss")}{System.IO.Path.GetExtension(model.Logo.FileName)}";
                    string uploadPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "images", "restaurant");
                    if (!System.IO.Directory.Exists(uploadPath))
                        System.IO.Directory.CreateDirectory(uploadPath);

                    string filePath = System.IO.Path.Combine(uploadPath, uniqueFileName);
                    using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
                    {
                        await model.Logo.CopyToAsync(fileStream);
                    }
                    logoPath = $"/images/restaurant/{uniqueFileName}";
                }

                // Copy settings for new branch (best-effort) and set new properties
                CopySettingsForNewBranch(con, branchId, model, logoPath);

                // --- STEP 4: Generate unique Username ---
                string username = GenerateUniqueUsername(con, model.FirstName.Trim(), model.PhoneNumber.Trim());
                string password = username; // default password = username

                // Hash the password with BCrypt
                string salt = BCrypt.Net.BCrypt.GenerateSalt(12);
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(password, salt);

                // --- STEP 5: Insert User with Client Tracking Info ---
                int userId = InsertUser(con, username, passwordHash, salt,
                    model.FirstName.Trim(),
                    model.LastName?.Trim() ?? string.Empty,
                    model.Email?.Trim(),
                    model.PhoneNumber.Trim(),
                    branchId,
                    clientInfo);

                if (userId <= 0)
                    return Json(new SignUpResultViewModel { Success = false, Message = "Failed to create user account. Please try again." });

                // --- STEP 6: Insert UserBranches ---
                InsertUserBranch(con, userId, branchId);

                // --- STEP 7: Assign Administrator role ---
                int adminRoleId = GetAdministratorRoleId(con);
                if (adminRoleId > 0)
                {
                    InsertUserBranchRole(con, userId, branchId, adminRoleId);
                    InsertUserRole(con, userId, adminRoleId);
                }

                // --- STEP 8: Create Audit Log for SignUp ---
                CreateAuditLog(con, userId, "SIGNUP", $"New restaurant signup: {model.RestaurantName}, User: {username}, Browser: {clientInfo.FormattedSummary}", clientInfo.IpAddress, clientInfo.UserAgent, "Users", userId.ToString());

                // --- STEP 9: Send welcome email (if email provided) ---
                string appUrl = $"{Request.Scheme}://{Request.Host}";
                if (!string.IsNullOrWhiteSpace(model.Email))
                {
                    try
                    {
                        string emailBody = BuildWelcomeEmailHtml(
                            model.FirstName.Trim(),
                            model.RestaurantName.Trim(),
                            username,
                            password,
                            appUrl);

                        await _emailSender.SendAsync(
                            model.Email.Trim(),
                            "Welcome to eRestoPOS – Your Account Details",
                            emailBody,
                            emailType: "SignUpWelcome",
                            sentFrom: "SignUp",
                            branchId: 1); // Always use main branch (Branch ID 1) for SMTP settings
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogWarning(emailEx, "Welcome email could not be sent to {Email}", model.Email);
                    }
                }

                _logger.LogInformation("New signup: User={Username}, Branch={BranchName}, BranchId={BranchId}, IP={ClientIp}, Browser={BrowserSummary}",
                    username, model.RestaurantName, branchId, clientInfo.IpAddress, clientInfo.FormattedSummary);

                return Json(new SignUpResultViewModel
                {
                    Success = true,
                    Message = "Account created successfully!",
                    Username = username,
                    Password = password
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sign up for restaurant {RestaurantName}", model.RestaurantName);
                return Json(new SignUpResultViewModel
                {
                    Success = false,
                    Message = $"An error occurred: {ex.Message}"
                });
            }
        }

        // ─── Helper: Ensure from_Signup and tracking columns exist ────────────────
        private void EnsureFromSignupColumn()
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                using var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Users', 'from_Signup') IS NULL
        ALTER TABLE dbo.Users ADD from_Signup BIT NOT NULL DEFAULT 0;

    IF COL_LENGTH('dbo.Users', 'TermsAcceptedAt') IS NULL
        ALTER TABLE dbo.Users ADD TermsAcceptedAt DATETIME NULL;

    IF COL_LENGTH('dbo.Users', 'SignupIpAddress') IS NULL
        ALTER TABLE dbo.Users ADD SignupIpAddress NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.Users', 'SignupUserAgent') IS NULL
        ALTER TABLE dbo.Users ADD SignupUserAgent NVARCHAR(500) NULL;

    IF COL_LENGTH('dbo.Users', 'SignupBrowser') IS NULL
        ALTER TABLE dbo.Users ADD SignupBrowser NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.Users', 'SignupOS') IS NULL
        ALTER TABLE dbo.Users ADD SignupOS NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.Users', 'SignupDevice') IS NULL
        ALTER TABLE dbo.Users ADD SignupDevice NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.Users', 'SignupDate') IS NULL
        ALTER TABLE dbo.Users ADD SignupDate DATETIME NULL;

    IF COL_LENGTH('dbo.Users', 'SetupWizardCompleted') IS NULL
        ALTER TABLE dbo.Users ADD SetupWizardCompleted BIT NOT NULL DEFAULT 0;
END

IF OBJECT_ID(N'dbo.Branches', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Branches', 'from_Signup') IS NULL
        ALTER TABLE dbo.Branches ADD from_Signup BIT NOT NULL DEFAULT 0;

    IF COL_LENGTH('dbo.Branches', 'CreatedIpAddress') IS NULL
        ALTER TABLE dbo.Branches ADD CreatedIpAddress NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.Branches', 'CreatedUserAgent') IS NULL
        ALTER TABLE dbo.Branches ADD CreatedUserAgent NVARCHAR(500) NULL;

    IF COL_LENGTH('dbo.Branches', 'CreatedBrowser') IS NULL
        ALTER TABLE dbo.Branches ADD CreatedBrowser NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.Branches', 'CreatedDevice') IS NULL
        ALTER TABLE dbo.Branches ADD CreatedDevice NVARCHAR(50) NULL;
END", con);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not ensure from_Signup/client tracking columns.");
            }
        }

        // ─── Helper: Ensure required tables exist ─────────────────────────────
        private void EnsureTablesExist(SqlConnection con)
        {
            using var cmd = new SqlCommand(@"
-- BranchLocations
IF OBJECT_ID(N'dbo.BranchLocations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BranchLocations (
        LocationId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BranchLocations PRIMARY KEY,
        LocationName NVARCHAR(100) NOT NULL,
        IsActive     BIT NOT NULL CONSTRAINT DF_BranchLocations_IsActive DEFAULT (1),
        CONSTRAINT UQ_BranchLocations_Name UNIQUE (LocationName)
    );
END

-- Branches
IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches (
        BranchId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
        BranchCode       NVARCHAR(20) NOT NULL CONSTRAINT UQ_Branches_BranchCode UNIQUE,
        BranchName       NVARCHAR(150) NOT NULL,
        BranchLocationId INT NULL,
        Is_MainBranch    BIT NULL,
        IsActive         BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT (1),
        CreatedAt        DATETIME NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT (GETDATE()),
        UpdatedAt        DATETIME NULL
    );
END
IF COL_LENGTH(N'dbo.Branches', N'BranchLocationId') IS NULL
    ALTER TABLE dbo.Branches ADD BranchLocationId INT NULL;

-- UserBranches
IF OBJECT_ID(N'dbo.UserBranches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranches (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranches PRIMARY KEY,
        UserId    INT NOT NULL,
        BranchId  INT NOT NULL,
        IsDefault BIT NOT NULL CONSTRAINT DF_UserBranches_IsDefault DEFAULT(0),
        IsActive  BIT NOT NULL CONSTRAINT DF_UserBranches_IsActive DEFAULT(1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranches_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL,
        CONSTRAINT UQ_UserBranches_UserId_BranchId UNIQUE(UserId, BranchId)
    );
END

-- UserBranchRoles
IF OBJECT_ID(N'dbo.UserBranchRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranchRoles (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranchRoles PRIMARY KEY,
        UserId    INT NOT NULL,
        BranchId  INT NOT NULL,
        RoleId    INT NOT NULL,
        IsActive  BIT NOT NULL CONSTRAINT DF_UserBranchRoles_IsActive DEFAULT(1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranchRoles_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL,
        CONSTRAINT UQ_UserBranchRoles_User_Branch_Role UNIQUE(UserId, BranchId, RoleId)
    );
END

-- CreatedBranchId column on Users
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Users', 'CreatedBranchId') IS NULL
        ALTER TABLE dbo.Users ADD CreatedBranchId INT NULL;
END
", con);
            cmd.ExecuteNonQuery();
        }

        // ─── Helper: Insert or get existing location by name ──────────────────
        private int InsertOrGetLocation(SqlConnection con, string locationName)
        {
            // Check if location already exists
            using (var checkCmd = new SqlCommand(
                "SELECT TOP 1 LocationId FROM dbo.BranchLocations WHERE LOWER(LTRIM(RTRIM(LocationName))) = LOWER(LTRIM(RTRIM(@Name)))", con))
            {
                checkCmd.Parameters.AddWithValue("@Name", locationName);
                var existing = checkCmd.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                    return Convert.ToInt32(existing);
            }

            // Insert new location
            using var insertCmd = new SqlCommand(
                "INSERT INTO dbo.BranchLocations (LocationName, IsActive) VALUES (@Name, 1); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
            insertCmd.Parameters.AddWithValue("@Name", locationName);
            var result = insertCmd.ExecuteScalar();
            return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
        }

        // ─── Helper: Generate a unique 4-char branch code ─────────────────────
        private string GenerateUniqueBranchCode(SqlConnection con, string restaurantName)
        {
            // Strip non-alphanumeric chars and uppercase
            string cleaned = Regex.Replace(restaurantName.ToUpperInvariant(), @"[^A-Z0-9]", "");
            string baseCode = cleaned.Length >= 4 ? cleaned.Substring(0, 4) : cleaned.PadRight(4, 'X');

            // Check uniqueness, append number suffix if taken
            string candidate = baseCode;
            int suffix = 2;
            while (suffix <= 99)
            {
                using var checkCmd = new SqlCommand(
                    "SELECT COUNT(1) FROM dbo.Branches WHERE UPPER(BranchCode) = @Code", con);
                checkCmd.Parameters.AddWithValue("@Code", candidate);
                int count = Convert.ToInt32(checkCmd.ExecuteScalar());
                if (count == 0) return candidate;

                // Truncate base to make room for suffix digit(s)
                int suffixLen = suffix.ToString().Length;
                string truncBase = baseCode.Substring(0, Math.Min(4 - suffixLen, baseCode.Length));
                candidate = truncBase + suffix.ToString();
                suffix++;
            }

            // Fallback: use timestamp-based code
            return ("SU" + DateTime.Now.ToString("MMdd")).Substring(0, 4).ToUpperInvariant();
        }

        // ─── Helper: Insert Branch ─────────────────────────────────────────────
        private int InsertBranch(SqlConnection con, string branchCode, string branchName, int locationId, ClientBrowserInfo clientInfo)
        {
            bool hasIpCol = false;
            using (var chk = new SqlCommand(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Branches' AND COLUMN_NAME='CreatedIpAddress'", con))
            {
                hasIpCol = Convert.ToInt32(chk.ExecuteScalar()) > 0;
            }

            var cols = new StringBuilder("BranchCode, BranchName, BranchLocationId, Is_MainBranch, IsActive, CreatedAt, UpdatedAt, from_Signup");
            var vals = new StringBuilder("@Code, @Name, @LocationId, 0, 1, GETDATE(), NULL, 1");

            if (hasIpCol && clientInfo != null)
            {
                cols.Append(", CreatedIpAddress, CreatedUserAgent, CreatedBrowser, CreatedDevice");
                vals.Append(", @IpAddress, @UserAgent, @Browser, @Device");
            }

            using var cmd = new SqlCommand($@"
INSERT INTO dbo.Branches ({cols})
VALUES ({vals});
SELECT CAST(SCOPE_IDENTITY() AS INT);", con);

            cmd.Parameters.AddWithValue("@Code", branchCode);
            cmd.Parameters.AddWithValue("@Name", branchName);
            cmd.Parameters.AddWithValue("@LocationId", locationId);

            if (hasIpCol && clientInfo != null)
            {
                cmd.Parameters.AddWithValue("@IpAddress", string.IsNullOrWhiteSpace(clientInfo.IpAddress) ? (object)DBNull.Value : clientInfo.IpAddress);
                cmd.Parameters.AddWithValue("@UserAgent", string.IsNullOrWhiteSpace(clientInfo.UserAgent) ? (object)DBNull.Value : (clientInfo.UserAgent.Length > 500 ? clientInfo.UserAgent.Substring(0, 500) : clientInfo.UserAgent));
                cmd.Parameters.AddWithValue("@Browser", string.IsNullOrWhiteSpace(clientInfo.Browser) ? (object)DBNull.Value : clientInfo.Browser);
                cmd.Parameters.AddWithValue("@Device", string.IsNullOrWhiteSpace(clientInfo.DeviceType) ? (object)DBNull.Value : clientInfo.DeviceType);
            }

            var result = cmd.ExecuteScalar();
            return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
        }

        // ─── Helper: Copy settings for new branch (best-effort) ───────────────
        private void CopySettingsForNewBranch(SqlConnection con, int newBranchId, SignUpViewModel model, string logoPath)
        {
            try
            {
                // Check if BranchId column exists in RestaurantSettings
                using var checkCmd = new SqlCommand(@"
SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='RestaurantSettings' AND COLUMN_NAME='BranchId'", con);
                if (Convert.ToInt32(checkCmd.ExecuteScalar()) == 0) return;

                // Skip if settings already exist for this branch
                using var existsCmd = new SqlCommand(
                    "SELECT COUNT(1) FROM dbo.RestaurantSettings WHERE BranchId = @NewBranchId", con);
                existsCmd.Parameters.AddWithValue("@NewBranchId", newBranchId);
                if (Convert.ToInt32(existsCmd.ExecuteScalar()) > 0) return;

                // Build column list dynamically
                var columns = new System.Collections.Generic.List<string>();
                using (var colCmd = new SqlCommand(@"
SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='RestaurantSettings'
  AND COLUMN_NAME NOT IN ('Id','BranchId','CreatedAt','UpdatedAt')", con))
                using (var rdr = colCmd.ExecuteReader())
                {
                    while (rdr.Read()) columns.Add(rdr.GetString(0));
                }

                if (columns.Count == 0) return;

                string colList = string.Join(", ", columns);
                using var copyCmd = new SqlCommand($@"
IF NOT EXISTS (SELECT 1 FROM dbo.RestaurantSettings WHERE BranchId = @NewBranchId)
BEGIN
    INSERT INTO dbo.RestaurantSettings (BranchId, {colList}, CreatedAt, UpdatedAt)
    SELECT TOP 1 @NewBranchId, {colList}, GETDATE(), GETDATE()
    FROM dbo.RestaurantSettings rs
    WHERE rs.BranchId = 1
END", con);
                copyCmd.Parameters.AddWithValue("@NewBranchId", newBranchId);
                copyCmd.ExecuteNonQuery();

                // Update the newly inserted settings with the new inputs
                string updateSql = @"
UPDATE dbo.RestaurantSettings 
SET RestaurantName = @Name, 
    City = @City, 
    State = @State, 
    Pincode = @Pincode, 
    Country = @Country, 
    GSTCode = @GSTCode, 
    Website = @Website";

                if (!string.IsNullOrEmpty(logoPath))
                {
                    updateSql += ", LogoPath = @LogoPath";
                }
                updateSql += " WHERE BranchId = @NewBranchId";

                using var updateCmd = new SqlCommand(updateSql, con);
                updateCmd.Parameters.AddWithValue("@Name", model.RestaurantName?.Trim() ?? string.Empty);
                updateCmd.Parameters.AddWithValue("@City", model.City?.Trim() ?? string.Empty);
                updateCmd.Parameters.AddWithValue("@State", model.State?.Trim() ?? string.Empty);
                updateCmd.Parameters.AddWithValue("@Pincode", model.Pincode?.Trim() ?? string.Empty);
                updateCmd.Parameters.AddWithValue("@Country", model.Country?.Trim() ?? string.Empty);
                updateCmd.Parameters.AddWithValue("@GSTCode", model.GSTCode?.Trim() ?? string.Empty);
                updateCmd.Parameters.AddWithValue("@Website", model.Website?.Trim() ?? string.Empty);
                
                if (!string.IsNullOrEmpty(logoPath))
                {
                    updateCmd.Parameters.AddWithValue("@LogoPath", logoPath);
                }
                updateCmd.Parameters.AddWithValue("@NewBranchId", newBranchId);
                updateCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy or update restaurant settings for new branch.");
            }
        }

        // ─── Helper: Generate unique username: FirstName + Last4Phone ──────────
        private string GenerateUniqueUsername(SqlConnection con, string firstName, string phone)
        {
            // Clean first name: letters only
            string cleanFirst = Regex.Replace(firstName, @"[^a-zA-Z]", "");
            string last4 = phone.Length >= 4 ? phone.Substring(phone.Length - 4) : phone;
            string baseUsername = cleanFirst + last4;

            // Deduplicate
            string candidate = baseUsername;
            int suffix = 2;
            while (suffix <= 999)
            {
                using var cmd = new SqlCommand(
                    "SELECT COUNT(1) FROM dbo.Users WHERE LOWER(Username) = LOWER(@Username)", con);
                cmd.Parameters.AddWithValue("@Username", candidate);
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                if (count == 0) return candidate;
                candidate = baseUsername + "_" + suffix;
                suffix++;
            }

            return baseUsername + "_" + DateTime.Now.Ticks.ToString().Substring(0, 4);
        }

        // ─── Helper: Insert User with from_Signup = 1 & Client Tracking ────────
        private int InsertUser(SqlConnection con, string username, string passwordHash, string salt,
            string firstName, string lastName, string email, string phone, int createdBranchId, ClientBrowserInfo clientInfo)
        {
            // Check if from_Signup column exists (should, after EnsureFromSignupColumn)
            bool hasFromSignup = false;
            using (var chk = new SqlCommand(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Users' AND COLUMN_NAME='from_Signup'", con))
            {
                hasFromSignup = Convert.ToInt32(chk.ExecuteScalar()) > 0;
            }

            bool hasCreatedBranchId = false;
            using (var chk2 = new SqlCommand(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Users' AND COLUMN_NAME='CreatedBranchId'", con))
            {
                hasCreatedBranchId = Convert.ToInt32(chk2.ExecuteScalar()) > 0;
            }
            
            bool hasTermsAcceptedAt = false;
            using (var chk3 = new SqlCommand(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Users' AND COLUMN_NAME='TermsAcceptedAt'", con))
            {
                hasTermsAcceptedAt = Convert.ToInt32(chk3.ExecuteScalar()) > 0;
            }

            bool hasSignupIpCol = false;
            using (var chk4 = new SqlCommand(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Users' AND COLUMN_NAME='SignupIpAddress'", con))
            {
                hasSignupIpCol = Convert.ToInt32(chk4.ExecuteScalar()) > 0;
            }

            bool hasSetupWizardCol = false;
            using (var chk5 = new SqlCommand(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Users' AND COLUMN_NAME='SetupWizardCompleted'", con))
            {
                hasSetupWizardCol = Convert.ToInt32(chk5.ExecuteScalar()) > 0;
            }

            var insertCols = new StringBuilder("Username, PasswordHash, Salt, FirstName, LastName, Email, Phone, IsActive");
            var insertVals = new StringBuilder("@Username, @PasswordHash, @Salt, @FirstName, @LastName, @Email, @Phone, 1");

            if (hasFromSignup) { insertCols.Append(", from_Signup"); insertVals.Append(", 1"); }
            if (hasCreatedBranchId) { insertCols.Append(", CreatedBranchId"); insertVals.Append(", @CreatedBranchId"); }
            if (hasTermsAcceptedAt) { insertCols.Append(", TermsAcceptedAt"); insertVals.Append(", GETDATE()"); }

            if (hasSignupIpCol && clientInfo != null)
            {
                insertCols.Append(", SignupIpAddress, SignupUserAgent, SignupBrowser, SignupOS, SignupDevice, SignupDate");
                insertVals.Append(", @SignupIp, @SignupUserAgent, @SignupBrowser, @SignupOS, @SignupDevice, GETDATE()");
            }

            if (hasSetupWizardCol)
            {
                insertCols.Append(", SetupWizardCompleted");
                insertVals.Append(", 0");
            }

            using var cmd = new SqlCommand($@"
INSERT INTO dbo.Users ({insertCols})
VALUES ({insertVals});
SELECT CAST(SCOPE_IDENTITY() AS INT);", con);

            cmd.Parameters.AddWithValue("@Username", username);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@Salt", salt);
            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", (object)lastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(email) ? (object)DBNull.Value : email);
            cmd.Parameters.AddWithValue("@Phone", phone);
            if (hasCreatedBranchId)
                cmd.Parameters.AddWithValue("@CreatedBranchId", createdBranchId);

            if (hasSignupIpCol && clientInfo != null)
            {
                cmd.Parameters.AddWithValue("@SignupIp", string.IsNullOrWhiteSpace(clientInfo.IpAddress) ? (object)DBNull.Value : clientInfo.IpAddress);
                cmd.Parameters.AddWithValue("@SignupUserAgent", string.IsNullOrWhiteSpace(clientInfo.UserAgent) ? (object)DBNull.Value : (clientInfo.UserAgent.Length > 500 ? clientInfo.UserAgent.Substring(0, 500) : clientInfo.UserAgent));
                cmd.Parameters.AddWithValue("@SignupBrowser", string.IsNullOrWhiteSpace(clientInfo.Browser) ? (object)DBNull.Value : clientInfo.Browser);
                cmd.Parameters.AddWithValue("@SignupOS", string.IsNullOrWhiteSpace(clientInfo.OperatingSystem) ? (object)DBNull.Value : clientInfo.OperatingSystem);
                cmd.Parameters.AddWithValue("@SignupDevice", string.IsNullOrWhiteSpace(clientInfo.DeviceType) ? (object)DBNull.Value : clientInfo.DeviceType);
            }

            var result = cmd.ExecuteScalar();
            return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
        }

        // ─── Helper: Create Audit Log Entry ───────────────────────────────────
        private void CreateAuditLog(SqlConnection con, int userId, string action, string details, string ipAddress, string userAgent, string entityName, string entityId)
        {
            try
            {
                // Check if sp_CreateAuditLog stored procedure exists
                bool hasSp = false;
                using (var checkSp = new SqlCommand("SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[sp_CreateAuditLog]') AND type in (N'P', N'PC')", con))
                {
                    hasSp = Convert.ToInt32(checkSp.ExecuteScalar()) > 0;
                }

                if (hasSp)
                {
                    using var cmd = new SqlCommand("dbo.sp_CreateAuditLog", con);
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@Action", action);
                    cmd.Parameters.AddWithValue("@Details", (object)details ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IpAddress", (object)ipAddress ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserAgent", (object)userAgent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EntityName", (object)entityName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EntityId", (object)entityId ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    // Fallback to direct insert if AuditLog table exists
                    using var checkTable = new SqlCommand("SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='AuditLog'", con);
                    if (Convert.ToInt32(checkTable.ExecuteScalar()) > 0)
                    {
                        using var cmd = new SqlCommand(@"
INSERT INTO dbo.AuditLog (UserId, Action, Details, IpAddress, UserAgent, EntityName, EntityId, CreatedAt)
VALUES (@UserId, @Action, @Details, @IpAddress, @UserAgent, @EntityName, @EntityId, GETDATE())", con);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Action", action);
                        cmd.Parameters.AddWithValue("@Details", (object)details ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IpAddress", (object)ipAddress ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserAgent", (object)userAgent ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@EntityName", (object)entityName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@EntityId", (object)entityId ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write audit log for Signup user {UserId}", userId);
            }
        }

        // ─── Helper: Insert UserBranches ───────────────────────────────────────
        private void InsertUserBranch(SqlConnection con, int userId, int branchId)
        {
            using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM dbo.UserBranches WHERE UserId=@UserId AND BranchId=@BranchId)
    INSERT INTO dbo.UserBranches (UserId, BranchId, IsDefault, IsActive, CreatedAt, UpdatedAt)
    VALUES (@UserId, @BranchId, 1, 1, SYSUTCDATETIME(), NULL)", con);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            cmd.ExecuteNonQuery();
        }

        // ─── Helper: Get Administrator role ID ────────────────────────────────
        private int GetAdministratorRoleId(SqlConnection con)
        {
            using var cmd = new SqlCommand(
                "SELECT TOP 1 Id FROM dbo.Roles WHERE LOWER(LTRIM(RTRIM(Name))) = 'administrator'", con);
            var result = cmd.ExecuteScalar();
            return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
        }

        // ─── Helper: Insert UserBranchRoles ───────────────────────────────────
        private void InsertUserBranchRole(SqlConnection con, int userId, int branchId, int roleId)
        {
            using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM dbo.UserBranchRoles WHERE UserId=@UserId AND BranchId=@BranchId AND RoleId=@RoleId)
    INSERT INTO dbo.UserBranchRoles (UserId, BranchId, RoleId, IsActive, CreatedAt, UpdatedAt)
    VALUES (@UserId, @BranchId, @RoleId, 1, SYSUTCDATETIME(), NULL)", con);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            cmd.Parameters.AddWithValue("@RoleId", roleId);
            cmd.ExecuteNonQuery();
        }

        // ─── Helper: Insert UserRoles (legacy table) ───────────────────────────
        private void InsertUserRole(SqlConnection con, int userId, int roleId)
        {
            try
            {
                using var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
    IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId=@UserId AND RoleId=@RoleId)
        INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId)", con);
                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@RoleId", roleId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not insert into UserRoles for UserId={UserId}", userId);
            }
        }

        // ─── Helper: Build validation error message ────────────────────────────
        private string BuildValidationErrorMessage()
        {
            var sb = new StringBuilder();
            foreach (var kvp in ModelState)
            {
                foreach (var err in kvp.Value.Errors)
                    sb.AppendLine(err.ErrorMessage);
            }
            return sb.ToString().Trim();
        }

        // ─── Helper: Build welcome email HTML ─────────────────────────────────
        private string BuildWelcomeEmailHtml(string firstName, string restaurantName, string username, string password, string appUrl)
        {
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #f4f6fb; margin: 0; padding: 0; }}
        .container {{ max-width: 600px; margin: 40px auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 24px rgba(42,16,109,0.12); }}
        .header {{ background: linear-gradient(135deg, #2a106d 0%, #7c3aed 100%); padding: 36px 32px; text-align: center; }}
        .header h1 {{ color: #fff; margin: 0; font-size: 28px; letter-spacing: 1px; }}
        .header p {{ color: rgba(255,255,255,0.8); margin: 8px 0 0; font-size: 14px; }}
        .body {{ padding: 36px 32px; }}
        .body h2 {{ color: #2a106d; font-size: 20px; margin-top: 0; }}
        .body p {{ color: #444; font-size: 15px; line-height: 1.6; }}
        .credentials {{ background: #f5f3ff; border: 1px solid #e0d9ff; border-radius: 8px; padding: 20px 24px; margin: 24px 0; }}
        .credentials table {{ width: 100%; border-collapse: collapse; }}
        .credentials td {{ padding: 8px 4px; font-size: 15px; color: #333; }}
        .credentials td:first-child {{ font-weight: 600; color: #2a106d; width: 140px; }}
        .credentials .value {{ font-family: 'Courier New', monospace; font-size: 16px; background: #ede9fe; padding: 3px 10px; border-radius: 4px; }}
        .cta {{ text-align: center; margin: 28px 0 8px; }}
        .cta a {{ display: inline-block; background: linear-gradient(135deg, #7c3aed, #2a106d); color: #fff; padding: 14px 36px; border-radius: 30px; text-decoration: none; font-weight: 700; font-size: 15px; letter-spacing: 0.5px; }}
        .footer {{ background: #f9f8ff; border-top: 1px solid #e9e6ff; padding: 20px 32px; text-align: center; color: #888; font-size: 13px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ eRestoPOS</h1>
            <p>Restaurant Management System</p>
        </div>
        <div class='body'>
            <h2>Welcome aboard, {System.Net.WebUtility.HtmlEncode(firstName)}! 🎉</h2>
            <p>Your <strong>eRestoPOS</strong> account has been created successfully for <strong>{System.Net.WebUtility.HtmlEncode(restaurantName)}</strong>. You can now sign in and start managing your restaurant.</p>
            <div class='credentials'>
                <table>
                    <tr>
                        <td>Username:</td>
                        <td><span class='value'>{System.Net.WebUtility.HtmlEncode(username)}</span></td>
                    </tr>
                    <tr>
                        <td>Password:</td>
                        <td><span class='value'>{System.Net.WebUtility.HtmlEncode(password)}</span></td>
                    </tr>
                    <tr>
                        <td>Login URL:</td>
                        <td><a href='{System.Net.WebUtility.HtmlEncode(appUrl)}'>{System.Net.WebUtility.HtmlEncode(appUrl)}</a></td>
                    </tr>
                </table>
            </div>
            <p style='color:#e05;font-size:13px;'>⚠️ Please change your password after logging in for the first time.</p>
            <div class='cta'>
                <a href='{System.Net.WebUtility.HtmlEncode(appUrl)}/Account/Login'>Login to eRestoPOS →</a>
            </div>
        </div>
        <div class='footer'>
            This email was sent because you registered at eRestoPOS. &copy; {DateTime.Now.Year} Emeditech Plus LLP
        </div>
    </div>
</body>
</html>";
        }
    }
}
