using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace RestaurantManagementSystem.Services
{
    public class LicensingService : ILicensingService
    {
        private const string LocalLicenseTableName = "dbo.ClientAppLicense";
        private const string ValidationLogTableName = "dbo.ClientAppLicenseValidationLog";
        private const string RemoteValidationHistoryTableName = "dbo.LicenseValidationHistory";
        private const string RemoteOtpValidationHistoryTableName = "dbo.ClientOTPValidationHistory";
        private const string CentralMailConfigurationTableName = "dbo.tbl_centralmailconfiguration";
        private const string DefaultCentralLicenseRemoteServer = "198.38.81.123,1433";
        private const string DefaultCentralLicenseRemoteUsername = "sa";
        private const string DefaultCentralLicenseRemotePassword = "Ehospit@lity@#1926";
        private const string DefaultCentralLicenseRemoteDatabase = "Central_Lic_DB";
        private const string RegistrationOtpSessionKey = "Licensing:PendingRegistrationOtp";
        private const string HardwareRenewalOtpSessionKey = "Licensing:PendingHardwareRenewalOtp";
        private const int RegistrationOtpLength = 6;
        private const int RegistrationOtpLifetimeSeconds = 120;
        private const string DailyGateCacheKeyPrefix = "LicenseGate:Daily:";
        private static readonly SemaphoreSlim LocalSchemaLock = new(1, 1);
        private static readonly SemaphoreSlim FingerprintLock = new(1, 1);
        private static readonly HashSet<string> ApprovedRegistrationOtpEmails = new(StringComparer.OrdinalIgnoreCase)
        {
            "ap.porel27@gmail.com",
            "purojit2010@gmail.com"
        };
        private static bool _localSchemaEnsured;
        private static LicenseMachineFingerprint? _cachedMachineFingerprint;

        private readonly IConfiguration _configuration;
        private readonly string _localConnectionString;
        private readonly ILogger<LicensingService> _logger;
        private readonly UrlEncryptionService _urlEncryptionService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMemoryCache _memoryCache;
        private readonly int _remoteConnectionTimeoutSeconds;

        public LicensingService(
            IConfiguration configuration,
            ILogger<LicensingService> logger,
            UrlEncryptionService urlEncryptionService,
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache)
        {
            _configuration = configuration;
            _localConnectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");
            _logger = logger;
            _urlEncryptionService = urlEncryptionService;
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _memoryCache = memoryCache;

            if (!int.TryParse(configuration["Licensing:RemoteConnectionTimeoutSeconds"], out _remoteConnectionTimeoutSeconds) || _remoteConnectionTimeoutSeconds <= 0)
            {
                _remoteConnectionTimeoutSeconds = 10;
            }
        }

        private string GetCurrentAppUrl()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null)
            {
                return string.Empty;
            }

            var request = context.Request;
            return $"{request.Scheme}://{request.Host}";
        }

        public async Task<LicenseRegistrationViewModel> BuildRegistrationViewModelAsync(LicenseRegistrationViewModel? source = null)
        {
            await EnsureLocalSchemaAsync();

            var machine = GetCurrentMachineFingerprint();

            var model = source ?? new LicenseRegistrationViewModel();
            model.ServerMacID = machine.ServerMacID;
            model.HardDiskNumber = machine.HardDiskNumber;
            model.MotherboardNumber = machine.MotherboardNumber;
            model.StartDate = DateTime.Now;
            model.ClientCodePreview = string.IsNullOrWhiteSpace(model.ClientCodePreview) ? "Generated on registration" : model.ClientCodePreview;
            model.LicenseKeyPreview = string.IsNullOrWhiteSpace(model.LicenseKeyPreview) ? "Generated on registration" : model.LicenseKeyPreview;

            if (model.ExpiryDate == default)
            {
                model.ExpiryDate = DateTime.Today.AddYears(1);
            }

            return model;
        }

        public async Task<(bool Success, string Message, int ExpiresInSeconds, string? TargetEmail)> SendRegistrationOtpAsync(LicenseRegistrationViewModel model, string? requestIp = null)
        {
            await EnsureLocalSchemaAsync();

            var session = GetSession();
            if (session == null)
            {
                return (false, "OTP session storage is unavailable for the current request.", 0, null);
            }

            var existingLocalLicense = await GetLocalLicenseAsync();
            if (existingLocalLicense != null)
            {
                return (false, "This application is already registered. Re-registration is blocked while a local license exists.", 0, null);
            }

            var validationError = ValidateRegistrationRequest(model);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return (false, validationError, 0, null);
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out var configurationErrorMessage))
            {
                return (false, configurationErrorMessage, 0, null);
            }

            var normalizedModel = CloneRegistrationModel(model);

            var databaseName = NormalizeDatabaseName(centralConnection.RemoteDatabase);
            var publicIpAddress = await ResolvePublicIpAddressAsync(requestIp);

            try
            {
                await EnsureRemoteDatabaseAndSchemaAsync(
                    centralConnection.RemoteServer,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword,
                    databaseName);

                var remoteConnectionString = BuildConnectionString(
                    centralConnection.RemoteServer,
                    databaseName,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword);

                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                var mailConfiguration = await GetCentralMailConfigurationAsync(remoteConnection, null);
                if (mailConfiguration == null || !mailConfiguration.IsActive)
                {
                    return (false, "Central mail configuration is missing or inactive.", 0, null);
                }

                var otpCode = GenerateOtpCode();
                var generatedAt = DateTime.Now;
                var pendingRegistration = new PendingLicenseRegistrationOtp
                {
                    ChallengeId = Guid.NewGuid(),
                    Model = normalizedModel,
                    OtpCode = otpCode,
                    GeneratedAt = generatedAt,
                    ExpiresAt = generatedAt.AddSeconds(RegistrationOtpLifetimeSeconds),
                    RequestIp = publicIpAddress,
                    FailedAttempts = 0
                };

                await InsertClientOtpValidationHistoryAsync(remoteConnection, null, pendingRegistration, ComputeOtpHash(otpCode));

                // Send the same OTP to every approved authorizer email internally
                var emailErrors = new List<string>();
                foreach (var approverEmail in ApprovedRegistrationOtpEmails)
                {
                    var emailResult = await SendRegistrationOtpEmailAsync(mailConfiguration, normalizedModel, approverEmail, otpCode, pendingRegistration.ExpiresAt);
                    if (!emailResult.Success)
                    {
                        emailErrors.Add($"{approverEmail}: {emailResult.Message}");
                        _logger.LogWarning("OTP email to approver {ApproverEmail} failed: {Message}", approverEmail, emailResult.Message);
                    }
                }

                // Block registration only if ALL approver deliveries failed
                if (emailErrors.Count == ApprovedRegistrationOtpEmails.Count)
                {
                    var combinedError = string.Join("; ", emailErrors);
                    await UpdateClientOtpValidationHistoryAsync(remoteConnection, null, pendingRegistration.ChallengeId, false, null, combinedError);
                    ClearPendingRegistrationOtp(session);
                    return (false, "OTP email delivery failed. " + combinedError, 0, null);
                }

                SavePendingRegistrationOtp(session, pendingRegistration);

                return (
                    true,
                    $"OTP sent for authorization. Enter the 6-digit code within {RegistrationOtpLifetimeSeconds} seconds to complete license registration.",
                    RegistrationOtpLifetimeSeconds,
                    null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send registration OTP for client {ClientName}", normalizedModel.ClientName);
                return (false, ex.Message, 0, null);
            }
        }

        public async Task<(bool Success, string Message, ClientAppLicense? License)> VerifyRegistrationOtpAsync(string otpCode, string? requestIp = null)
        {
            await EnsureLocalSchemaAsync();

            var session = GetSession();
            if (session == null)
            {
                return (false, "OTP session storage is unavailable for the current request.", null);
            }

            var pendingRegistration = GetPendingRegistrationOtp(session);
            if (pendingRegistration == null)
            {
                return (false, "OTP session expired. Click Register License again to request a new OTP.", null);
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out var configurationErrorMessage))
            {
                return (false, configurationErrorMessage, null);
            }

            var databaseName = NormalizeDatabaseName(centralConnection.RemoteDatabase);

            try
            {
                await EnsureRemoteDatabaseAndSchemaAsync(
                    centralConnection.RemoteServer,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword,
                    databaseName);

                var remoteConnectionString = BuildConnectionString(
                    centralConnection.RemoteServer,
                    databaseName,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword);

                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                if (!pendingRegistration.IsVerified)
                {
                    if (pendingRegistration.ExpiresAt <= DateTime.Now)
                    {
                        await UpdateClientOtpValidationHistoryAsync(remoteConnection, null, pendingRegistration.ChallengeId, false, null, "OTP expired.");
                        ClearPendingRegistrationOtp(session);
                        return (false, "OTP expired. Click Register License again to request a new OTP.", null);
                    }

                    var normalizedOtp = NormalizeOtpCode(otpCode);
                    if (normalizedOtp == null)
                    {
                        return (false, "Enter the 6-digit OTP sent to the approved email address.", null);
                    }

                    if (!OtpCodesMatch(pendingRegistration.OtpCode, normalizedOtp))
                    {
                        pendingRegistration.FailedAttempts++;
                        SavePendingRegistrationOtp(session, pendingRegistration);
                        await UpdateClientOtpValidationHistoryAsync(remoteConnection, null, pendingRegistration.ChallengeId, false, null, "Invalid OTP entered.");
                        return (false, "Invalid OTP. Enter the correct 6-digit OTP.", null);
                    }

                    pendingRegistration.IsVerified = true;
                    pendingRegistration.VerifiedAt = DateTime.Now;
                    SavePendingRegistrationOtp(session, pendingRegistration);

                    await UpdateClientOtpValidationHistoryAsync(
                        remoteConnection,
                        null,
                        pendingRegistration.ChallengeId,
                        true,
                        pendingRegistration.VerifiedAt,
                        null);
                }

                var registrationResult = await RegisterClientAsync(pendingRegistration.Model, requestIp);
                if (!registrationResult.Success)
                {
                    await UpdateClientOtpValidationHistoryAsync(
                        remoteConnection,
                        null,
                        pendingRegistration.ChallengeId,
                        pendingRegistration.IsVerified,
                        pendingRegistration.VerifiedAt,
                        registrationResult.Message,
                        registrationResult.License?.ClientCode,
                        registrationResult.License?.LicenseKey);

                    return registrationResult;
                }

                await UpdateClientOtpValidationHistoryAsync(
                    remoteConnection,
                    null,
                    pendingRegistration.ChallengeId,
                    true,
                    pendingRegistration.VerifiedAt ?? DateTime.Now,
                    null,
                    registrationResult.License?.ClientCode,
                    registrationResult.License?.LicenseKey);

                // Send welcome email — registration is not blocked if this fails
                if (registrationResult.License != null && !string.IsNullOrWhiteSpace(registrationResult.License.EmailID))
                {
                    try
                    {
                        var mailConfig = await GetCentralMailConfigurationAsync(remoteConnection, null);
                        if (mailConfig != null && mailConfig.IsActive)
                        {
                            var welcomeResult = await SendWelcomeEmailAsync(mailConfig, registrationResult.License);
                            if (!welcomeResult.Success)
                            {
                                _logger.LogWarning(
                                    "Welcome email could not be delivered to {EmailID} for client {ClientCode}: {Message}",
                                    registrationResult.License.EmailID,
                                    registrationResult.License.ClientCode,
                                    welcomeResult.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Welcome email delivery failed for client {ClientCode}", registrationResult.License.ClientCode);
                    }
                }

                ClearPendingRegistrationOtp(session);
                return registrationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify registration OTP for challenge {ChallengeId}", pendingRegistration.ChallengeId);
                return (false, ex.Message, null);
            }
        }

        public async Task<(bool Success, string Message, ClientAppLicense? License)> RegisterClientAsync(LicenseRegistrationViewModel model, string? requestIp = null)
        {
            await EnsureLocalSchemaAsync();

            var existingLocalLicense = await GetLocalLicenseAsync();
            if (existingLocalLicense != null)
            {
                return (false, "This application is already registered. Re-registration is blocked while a local license exists.", existingLocalLicense);
            }

            if (string.IsNullOrWhiteSpace(model.ClientName) || string.IsNullOrWhiteSpace(model.ContactNumber) || string.IsNullOrWhiteSpace(model.EmailID))
            {
                return (false, "Client name, contact number, and client email ID are required.", null);
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out var configurationErrorMessage))
            {
                return (false, configurationErrorMessage, null);
            }

            if (model.ExpiryDate.Date < DateTime.Today)
            {
                return (false, "End date cannot be earlier than today.", null);
            }

            var machine = GetCurrentMachineFingerprint();
            var databaseName = NormalizeDatabaseName(centralConnection.RemoteDatabase);
            var remoteServer = centralConnection.RemoteServer;
            var remoteUsername = centralConnection.RemoteUsername;
            var remotePassword = centralConnection.RemotePassword;
            var publicIpAddress = await ResolvePublicIpAddressAsync(requestIp);

            try
            {
                await EnsureRemoteDatabaseAndSchemaAsync(remoteServer, remoteUsername, remotePassword, databaseName);

                var remoteConnectionString = BuildConnectionString(remoteServer, databaseName, remoteUsername, remotePassword);
                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                var remoteLicense = await GetRemoteLicenseByFingerprintAsync(remoteConnection, null, machine, GetCurrentAppUrl());
                if (remoteLicense == null)
                {
                    var transaction = (SqlTransaction)await remoteConnection.BeginTransactionAsync(IsolationLevel.Serializable);
                    await using (transaction)
                    {
                        remoteLicense = new ClientAppLicense
                        {
                            ClientCode = await GenerateNextClientCodeAsync(remoteConnection, transaction),
                            ClientName = model.ClientName.Trim(),
                            ContactNumber = model.ContactNumber.Trim(),
                            EmailID = string.IsNullOrWhiteSpace(model.EmailID) ? null : model.EmailID.Trim(),
                            LicenseKey = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                            HardDiskNumber = machine.HardDiskNumber,
                            ServerMacID = machine.ServerMacID,
                            MotherboardNumber = machine.MotherboardNumber,
                            PublicIPAddress = publicIpAddress,
                            StartDate = DateTime.Now,
                            ExpiryDate = model.ExpiryDate.Date.AddDays(1).AddTicks(-1),
                            LastLoginDate = null,
                            IsActive = true,
                            CreatedAt = DateTime.Now,
                            OTP_Verified = true,
                            AMC_Expireddate = model.AmcExpiryDate,
                            AppUrl = GetCurrentAppUrl(),
                            ProductType = string.IsNullOrWhiteSpace(model.ProductType) ? null : model.ProductType.Trim()
                        };

                        remoteLicense.Id = await InsertLicenseAsync(remoteConnection, transaction, remoteLicense);
                        await transaction.CommitAsync();
                    }
                }

                remoteLicense.PublicIPAddress = publicIpAddress;
                await UpdateRemoteLicenseTrackingAsync(remoteConnection, null, remoteLicense, updateLastLoginDate: false);

                await UpsertLocalLicenseAsync(remoteLicense);

                return (true, $"License registration completed. Client code {remoteLicense.ClientCode} is now active.", remoteLicense);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "License registration failed");
                return (false, ex.Message, null);
            }
        }

        /// <summary>
        /// When no local license matches the current machine's hardware, checks the remote DB
        /// for a license registered with the current AppUrl. If found and hardware differs,
        /// returns a HardwareMismatch gate result to block re-registration from the same URL.
        /// Returns null if no such remote record exists (safe to allow fresh registration).
        /// </summary>
        private async Task<LicenseGateResult?> TryGetHardwareMismatchForCurrentUrlAsync(string? requestIp)
        {
            var currentUrl = GetCurrentAppUrl();
            if (string.IsNullOrWhiteSpace(currentUrl))
            {
                return null;
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out _))
            {
                return null;
            }

            try
            {
                var remoteConnectionString = BuildConnectionString(
                    centralConnection.RemoteServer,
                    NormalizeDatabaseName(centralConnection.RemoteDatabase),
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword);

                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                var remoteLicense = await GetRemoteLicenseByAppUrlAsync(remoteConnection, null, currentUrl);
                if (remoteLicense == null)
                {
                    return null;
                }

                var machine = GetCurrentMachineFingerprint();
                var publicIpForUrl = await ResolvePublicIpAddressAsync(requestIp);

                if (FingerprintsMatch(machine, remoteLicense))
                {
                    // Hardware matches the remote record — sync locally and allow access.
                    remoteLicense.PublicIPAddress = publicIpForUrl;
                    await UpsertLocalLicenseAsync(remoteLicense);

                    // Always log every remote hit so the history table is complete.
                    await InsertRemoteValidationHistoryAsync(
                        remoteConnection,
                        null,
                        CreateRemoteValidationHistory(remoteLicense, machine, true, null, publicIpForUrl, GetCurrentAppUrl()));

                    return null;
                }

                // Same URL but different hardware — hardware was changed on this server.
                var mismatchReason = BuildHardwareMismatchReason(machine, remoteLicense);

                await InsertRemoteValidationHistoryAsync(
                    remoteConnection,
                    null,
                    CreateRemoteValidationHistory(remoteLicense, machine, false, mismatchReason, publicIpForUrl, GetCurrentAppUrl()));

                return CreateGateResult(
                    LicenseGateStatus.HardwareMismatch,
                    remoteLicense,
                    string.Empty,
                    string.Empty,
                    mismatchReason);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not check remote for AppUrl hardware mismatch.");
                return null;
            }
        }

        public async Task ClearLocalLicenseAsync()
        {
            await EnsureLocalSchemaAsync();

            // Evict the daily in-process cache so the next request performs a fresh
            // validation rather than serving a stale "valid" result.
            var dailyCacheKey = DailyGateCacheKeyPrefix + DateTime.Today.ToString("yyyyMMdd");
            _memoryCache.Remove(dailyCacheKey);

            // Also clear the cached hardware fingerprint so registration from new hardware
            // computes the correct values.
            _cachedMachineFingerprint = null;

            // Only delete THIS machine's license record — other servers sharing the same DB
            // must keep their own records intact.
            var license = await GetLocalLicenseAsync();
            if (license == null)
            {
                _logger.LogInformation("No local license found for current machine hardware — nothing to clear.");
                return;
            }

            await using var connection = new SqlConnection(_localConnectionString);
            await connection.OpenAsync();
            const string sql = "DELETE FROM dbo.ClientAppLicense WHERE Id = @Id;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", license.Id);
            await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Local license record Id={Id} (ClientCode={ClientCode}) cleared to allow re-registration on new hardware.", license.Id, license.ClientCode);
        }

        // ── Hardware Renewal via OTP ──────────────────────────────────────────────────
        // Allows a server with changed hardware to re-associate the existing remote
        // license to the new hardware identifiers, gated by a 6-digit OTP sent to
        // approved internal email addresses. The existing ClientCode and LicenseKey
        // are preserved; only HardDiskNumber, ServerMacID, and MotherboardNumber change.

        public async Task<(bool Success, string Message, int ExpiresInSeconds)> SendHardwareRenewalOtpAsync(string licenseKey, string? requestIp = null)
        {
            await EnsureLocalSchemaAsync();

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                return (false, "License key is required.", 0);
            }

            // Sanitize: license keys are plain ASCII GUIDs without HTML chars
            var normalizedKey = licenseKey.Trim();
            if (normalizedKey.Length > 100)
            {
                return (false, "Invalid license key format.", 0);
            }

            var session = GetSession();
            if (session == null)
            {
                return (false, "OTP session storage is unavailable for the current request.", 0);
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out var configurationErrorMessage))
            {
                return (false, configurationErrorMessage, 0);
            }

            var databaseName = NormalizeDatabaseName(centralConnection.RemoteDatabase);
            var publicIpAddress = await ResolvePublicIpAddressAsync(requestIp);

            try
            {
                await EnsureRemoteDatabaseAndSchemaAsync(
                    centralConnection.RemoteServer,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword,
                    databaseName);

                var remoteConnectionString = BuildConnectionString(
                    centralConnection.RemoteServer,
                    databaseName,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword);

                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                // Validate the license key against the remote table
                var remoteLicense = await GetRemoteLicenseByKeyAsync(remoteConnection, null, normalizedKey);
                if (remoteLicense == null || !remoteLicense.IsActive)
                {
                    _logger.LogWarning("Hardware renewal OTP requested for unknown or inactive license key from IP {Ip}.", publicIpAddress);
                    // Return a generic error to avoid enumeration of license keys
                    return (false, "License key not found or inactive. Verify the key and try again.", 0);
                }

                var mailConfiguration = await GetCentralMailConfigurationAsync(remoteConnection, null);
                if (mailConfiguration == null || !mailConfiguration.IsActive)
                {
                    return (false, "Central mail configuration is missing or inactive.", 0);
                }

                var otpCode = GenerateOtpCode();
                var generatedAt = DateTime.Now;
                var pendingRenewal = new PendingHardwareRenewalOtp
                {
                    ChallengeId = Guid.NewGuid(),
                    LicenseKey = remoteLicense.LicenseKey,
                    ClientCode = remoteLicense.ClientCode,
                    OtpCode = otpCode,
                    GeneratedAt = generatedAt,
                    ExpiresAt = generatedAt.AddSeconds(RegistrationOtpLifetimeSeconds),
                    RequestIp = publicIpAddress,
                    FailedAttempts = 0
                };

                await InsertHardwareRenewalOtpHistoryAsync(remoteConnection, null, pendingRenewal, ComputeOtpHash(otpCode), remoteLicense);

                var emailErrors = new List<string>();
                foreach (var approverEmail in ApprovedRegistrationOtpEmails)
                {
                    var emailResult = await SendHardwareRenewalOtpEmailAsync(mailConfiguration, pendingRenewal, remoteLicense, approverEmail, otpCode, pendingRenewal.ExpiresAt);
                    if (!emailResult.Success)
                    {
                        emailErrors.Add($"{approverEmail}: {emailResult.Message}");
                        _logger.LogWarning("Hardware renewal OTP email to approver {ApproverEmail} failed: {Message}", approverEmail, emailResult.Message);
                    }
                }

                if (emailErrors.Count == ApprovedRegistrationOtpEmails.Count)
                {
                    var combinedError = string.Join("; ", emailErrors);
                    await UpdateHardwareRenewalOtpHistoryAsync(remoteConnection, null, pendingRenewal.ChallengeId, false, null, combinedError);
                    ClearPendingHardwareRenewalOtp(session);
                    return (false, "OTP email delivery failed. " + combinedError, 0);
                }

                SavePendingHardwareRenewalOtp(session, pendingRenewal);
                return (true, $"OTP sent. Enter the 6-digit code within {RegistrationOtpLifetimeSeconds} seconds.", RegistrationOtpLifetimeSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send hardware renewal OTP for license key (redacted)");
                return (false, ex.Message, 0);
            }
        }

        public async Task<(bool Success, string Message)> VerifyHardwareRenewalOtpAsync(string otpCode, string? requestIp = null)
        {
            await EnsureLocalSchemaAsync();

            var session = GetSession();
            if (session == null)
            {
                return (false, "OTP session storage is unavailable for the current request.");
            }

            var pendingRenewal = GetPendingHardwareRenewalOtp(session);
            if (pendingRenewal == null)
            {
                return (false, "OTP session expired. Click Re-New License again to request a new OTP.");
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out var configurationErrorMessage))
            {
                return (false, configurationErrorMessage);
            }

            var databaseName = NormalizeDatabaseName(centralConnection.RemoteDatabase);

            try
            {
                await EnsureRemoteDatabaseAndSchemaAsync(
                    centralConnection.RemoteServer,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword,
                    databaseName);

                var remoteConnectionString = BuildConnectionString(
                    centralConnection.RemoteServer,
                    databaseName,
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword);

                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                if (!pendingRenewal.IsVerified)
                {
                    if (pendingRenewal.ExpiresAt <= DateTime.Now)
                    {
                        await UpdateHardwareRenewalOtpHistoryAsync(remoteConnection, null, pendingRenewal.ChallengeId, false, null, "OTP expired.");
                        ClearPendingHardwareRenewalOtp(session);
                        return (false, "OTP expired. Click Re-New License again to request a new OTP.");
                    }

                    var normalizedOtp = NormalizeOtpCode(otpCode);
                    if (normalizedOtp == null)
                    {
                        return (false, "Enter the 6-digit OTP sent to the approved email address.");
                    }

                    if (!OtpCodesMatch(pendingRenewal.OtpCode, normalizedOtp))
                    {
                        pendingRenewal.FailedAttempts++;
                        SavePendingHardwareRenewalOtp(session, pendingRenewal);
                        await UpdateHardwareRenewalOtpHistoryAsync(remoteConnection, null, pendingRenewal.ChallengeId, false, null, "Invalid OTP entered.");
                        return (false, "Invalid OTP. Enter the correct 6-digit OTP.");
                    }

                    pendingRenewal.IsVerified = true;
                    pendingRenewal.VerifiedAt = DateTime.Now;
                    SavePendingHardwareRenewalOtp(session, pendingRenewal);
                    await UpdateHardwareRenewalOtpHistoryAsync(remoteConnection, null, pendingRenewal.ChallengeId, true, pendingRenewal.VerifiedAt, null);
                }

                // Update remote hardware to current machine identifiers
                var machine = GetCurrentMachineFingerprint();
                await UpdateRemoteHardwareAsync(remoteConnection, null, pendingRenewal.LicenseKey, pendingRenewal.ClientCode, machine);

                // Fetch the updated remote license and sync locally
                var updatedRemoteLicense = await GetRemoteLicenseByKeyAsync(remoteConnection, null, pendingRenewal.LicenseKey);
                if (updatedRemoteLicense != null)
                {
                    var publicIp = await ResolvePublicIpAddressAsync(requestIp);
                    updatedRemoteLicense.PublicIPAddress = publicIp;
                    await UpdateRemoteLicenseTrackingAsync(remoteConnection, null, updatedRemoteLicense, updateLastLoginDate: false);
                    await UpsertLocalLicenseAsync(updatedRemoteLicense);

                    // Log the hardware renewal as a successful validation event so history
                    // reflects every remote contact, including hardware re-association.
                    await InsertRemoteValidationHistoryAsync(
                        remoteConnection,
                        null,
                        CreateRemoteValidationHistory(updatedRemoteLicense, machine, true, null, publicIp, GetCurrentAppUrl()));
                }

                // Invalidate caches so the next EvaluateAccessAsync does a clean check
                var dailyCacheKey = DailyGateCacheKeyPrefix + DateTime.Today.ToString("yyyyMMdd");
                _memoryCache.Remove(dailyCacheKey);
                _cachedMachineFingerprint = null;

                await UpdateHardwareRenewalOtpHistoryAsync(remoteConnection, null, pendingRenewal.ChallengeId, true, pendingRenewal.VerifiedAt ?? DateTime.Now, null);
                ClearPendingHardwareRenewalOtp(session);

                _logger.LogInformation("Hardware renewal completed for ClientCode={ClientCode}.", pendingRenewal.ClientCode);
                return (true, "Hardware updated successfully. The license has been re-associated with this server.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify hardware renewal OTP for ChallengeId={ChallengeId}", pendingRenewal.ChallengeId);
                return (false, ex.Message);
            }
        }

        public async Task<LicenseGateResult> EvaluateAccessAsync(bool forceRemoteValidation = false, string? requestIp = null)
        {
            await EnsureLocalSchemaAsync();

            var dailyCacheKey = DailyGateCacheKeyPrefix + DateTime.Today.ToString("yyyyMMdd");

            // If a forced re-validation is requested (e.g. RetryValidation action), remove
            // any cached result so the full remote check runs below.
            if (forceRemoteValidation)
            {
                _memoryCache.Remove(dailyCacheKey);
            }
            else if (_memoryCache.TryGetValue(dailyCacheKey, out LicenseGateResult? cachedResult) && cachedResult != null)
            {
                // Even on a cache hit, do a quick local existence check so that a manual
                // deletion of the ClientAppLicense row (from either DB tool or admin action)
                // is detected immediately — evict the stale "Valid" entry and fall through
                // to full re-evaluation rather than serving a cached grant indefinitely.
                var localCheck = await GetLocalLicenseAsync();
                if (localCheck != null)
                {
                    _logger.LogDebug("License gate served from daily in-process cache.");
                    return cachedResult;
                }

                // Local record no longer exists — purge the cache and the fingerprint
                // cache so the system re-evaluates cleanly from scratch.
                _memoryCache.Remove(dailyCacheKey);
                _cachedMachineFingerprint = null;
                _logger.LogInformation("Local license record no longer exists; evicting daily cache and re-evaluating.");
            }

            var localLicense = await GetLocalLicenseAsync();
            if (localLicense == null)
            {
                // No local record matches the current machine's hardware fingerprint.
                // Before allowing fresh registration, check the remote DB for a license
                // registered against the same AppUrl. If found with different hardware,
                // that means the hardware on this server changed — show HardwareMismatch
                // instead of the registration form (prevents unauthorized re-registration).
                var urlMismatchResult = await TryGetHardwareMismatchForCurrentUrlAsync(requestIp);
                if (urlMismatchResult != null)
                {
                    return urlMismatchResult;
                }

                return CreateGateResult(
                    LicenseGateStatus.Unregistered,
                    null,
                    "License Registration Required",
                    "This application has not been registered yet. Complete the license registration before login.");
            }

            // Do NOT short-circuit on Expired or Inactive from local cache.
            // Always go to remote so that a renewed/reactivated license is detected,
            // local is updated, and the user can log in without manual intervention.

            var machine = GetCurrentMachineFingerprint();
            var localHardwareMatchesLiveMachine = FingerprintsMatch(machine, localLicense);

            if (localLicense.LastLoginDate.HasValue && localLicense.LastLoginDate.Value.Date == DateTime.Today)
            {
                if (localHardwareMatchesLiveMachine)
                {
                    var localCacheResult = CreateGateResult(
                        LicenseGateStatus.Valid,
                        localLicense,
                        "License Valid",
                        "License already validated for today from local cache.");

                    // Store in fast in-process cache until midnight so subsequent requests
                    // within the same day never reach the DB or shell commands.
                    CacheDailyGateResult(dailyCacheKey, localCacheResult);
                    return localCacheResult;
                }
            }

            if (!TryGetCentralLicenseConnection(out var centralConnection, out var configurationErrorMessage))
            {
                return CreateGateResult(
                    LicenseGateStatus.ConfigurationMissing,
                    localLicense,
                    string.Empty,
                    string.Empty,
                    configurationErrorMessage);
            }

            var publicIpAddress = await ResolvePublicIpAddressAsync(requestIp);

            try
            {
                var remoteConnectionString = BuildConnectionString(
                    centralConnection.RemoteServer,
                    NormalizeDatabaseName(centralConnection.RemoteDatabase),
                    centralConnection.RemoteUsername,
                    centralConnection.RemotePassword);
                await using var remoteConnection = new SqlConnection(remoteConnectionString);
                await remoteConnection.OpenAsync();

                var remoteLicense = await GetRemoteLicenseAsync(remoteConnection, null, localLicense.ClientCode, localLicense.LicenseKey);
                if (remoteLicense == null)
                {
                    var remoteNotFoundResult = CreateGateResult(
                        LicenseGateStatus.RemoteNotFound,
                        localLicense,
                        string.Empty,
                        string.Empty,
                        "No matching license record exists in the remote licensing database.");

                    await InsertRemoteValidationHistoryAsync(
                        remoteConnection,
                        null,
                        CreateRemoteValidationHistory(localLicense, machine, false, remoteNotFoundResult.Message, publicIpAddress, GetCurrentAppUrl()));

                    await TryInsertLocalValidationLogAsync(CreateValidationLog(
                        localLicense,
                        machine,
                        LicenseGateStatus.RemoteNotFound.ToString(),
                        isMatch: false,
                        isExpired: false,
                        isRemoteReachable: true,
                        failureReason: remoteNotFoundResult.Message,
                        requestIp: publicIpAddress,
                        appUrl: GetCurrentAppUrl()));

                    return remoteNotFoundResult;
                }

                remoteLicense.PublicIPAddress = publicIpAddress;

                var isExpired = remoteLicense.ExpiryDate <= DateTime.Now;
                var liveHardwareMatchesRemote = FingerprintsMatch(machine, remoteLicense);
                var localRemoteMismatchReason = BuildLocalRemoteMismatchReason(localLicense, remoteLicense);
                var status = LicenseGateStatus.Valid;
                string? failureReason = null;

                if (!remoteLicense.OTP_Verified)
                {
                    status = LicenseGateStatus.PendingActivation;
                    failureReason = "OTP or activation verification has not been completed for this license.";
                }
                else if (!remoteLicense.IsActive)
                {
                    status = LicenseGateStatus.Inactive;
                    failureReason = "Client Deactivated for activate contact vendor 8617280732";
                }
                else if (isExpired)
                {
                    status = LicenseGateStatus.Expired;
                    failureReason = $"Remote expiry date {remoteLicense.ExpiryDate:dd-MMM-yyyy HH:mm:ss} has already passed.";
                }
                else if (!string.IsNullOrWhiteSpace(localRemoteMismatchReason))
                {
                    status = LicenseGateStatus.DataMismatch;
                    failureReason = localRemoteMismatchReason;
                }
                else if (!liveHardwareMatchesRemote)
                {
                    status = LicenseGateStatus.HardwareMismatch;
                    failureReason = BuildHardwareMismatchReason(machine, remoteLicense);
                }

                var gateResult = CreateGateResult(status, remoteLicense, string.Empty, string.Empty, failureReason);

                if (gateResult.IsAllowed)
                {
                    remoteLicense.LastLoginDate = DateTime.Now;
                }
                else
                {
                    remoteLicense.LastLoginDate = localLicense.LastLoginDate;
                }

                await UpdateRemoteLicenseTrackingAsync(remoteConnection, null, remoteLicense, updateLastLoginDate: gateResult.IsAllowed);
                await UpsertLocalLicenseAsync(remoteLicense);

                await InsertRemoteValidationHistoryAsync(
                    remoteConnection,
                    null,
                    CreateRemoteValidationHistory(remoteLicense, machine, gateResult.IsAllowed, gateResult.IsAllowed ? null : gateResult.Message, publicIpAddress, GetCurrentAppUrl()));

                await TryInsertLocalValidationLogAsync(CreateValidationLog(
                    remoteLicense,
                    machine,
                    gateResult.Status.ToString(),
                    liveHardwareMatchesRemote,
                    isExpired,
                    isRemoteReachable: true,
                    failureReason: gateResult.IsAllowed ? null : gateResult.Message,
                    requestIp: publicIpAddress,
                    appUrl: GetCurrentAppUrl()));

                // After a successful remote validation, store the result in the fast
                // in-process daily cache so every subsequent request within the same day
                // is served instantly without any DB query or hardware fingerprint work.
                if (gateResult.IsAllowed)
                {
                    CacheDailyGateResult(dailyCacheKey, gateResult);
                }

                return gateResult;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote licensing validation failed for client code {ClientCode}", localLicense.ClientCode);

                await TryInsertLocalValidationLogAsync(CreateValidationLog(
                    localLicense,
                    machine,
                    LicenseGateStatus.RemoteUnavailable.ToString(),
                    isMatch: false,
                    isExpired: localLicense.ExpiryDate <= DateTime.Now,
                    isRemoteReachable: false,
                    failureReason: ex.Message,
                    requestIp: publicIpAddress,
                    appUrl: GetCurrentAppUrl()));

                return CreateGateResult(
                    LicenseGateStatus.RemoteUnavailable,
                    localLicense,
                    string.Empty,
                    string.Empty,
                    ex.Message);
            }
        }

        /// <summary>
        /// Stores a valid gate result in the in-process memory cache until midnight so
        /// subsequent requests within the same day are served instantly.
        /// </summary>
        private void CacheDailyGateResult(string cacheKey, LicenseGateResult result)
        {
            var midnight = DateTime.Today.AddDays(1);
            var ttl = midnight - DateTime.Now;
            if (ttl <= TimeSpan.Zero)
            {
                ttl = TimeSpan.FromMinutes(1);
            }

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.Now.Add(ttl),
                Priority = CacheItemPriority.High
            };
            _memoryCache.Set(cacheKey, result, options);
        }

        public async Task<LicenseBlockedViewModel> BuildBlockedViewModelAsync(LicenseGateStatus? statusOverride = null)
        {
            await EnsureLocalSchemaAsync();

            var localLicense = await GetLocalLicenseAsync();
            var latestLocalValidationLog = await GetLatestLocalValidationLogAsync();
            var resolvedStatus = statusOverride
                ?? (localLicense == null ? LicenseGateStatus.Unregistered : LicenseGateStatus.UnknownError);

            var gateResult = CreateGateResult(
                resolvedStatus,
                localLicense,
                title: string.Empty,
                message: string.Empty,
                failureReason: null);

            return new LicenseBlockedViewModel
            {
                Status = resolvedStatus,
                Title = gateResult.Title,
                Message = gateResult.Message,
                FailureReason = gateResult.FailureReason,
                ClientCode = localLicense?.ClientCode,
                LicenseKey = localLicense?.LicenseKey,
                ExpiryDate = localLicense?.ExpiryDate,
                LastValidatedAt = latestLocalValidationLog?.ValidatedAt,
                ShowRetryAction = resolvedStatus == LicenseGateStatus.RemoteUnavailable || resolvedStatus == LicenseGateStatus.UnknownError
            };
        }

        private async Task EnsureLocalSchemaAsync()
        {
            if (_localSchemaEnsured)
            {
                return;
            }

            await LocalSchemaLock.WaitAsync();
            try
            {
                if (_localSchemaEnsured)
                {
                    return;
                }

                await using var connection = new SqlConnection(_localConnectionString);
                await connection.OpenAsync();

                var sql = @"
IF OBJECT_ID('dbo.ClientAppLicense', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ClientAppLicense]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ClientCode] VARCHAR(32) NOT NULL,
        [ClientName] VARCHAR(200) NOT NULL,
        [ContactNumber] VARCHAR(30) NULL,
        [EmailID] NVARCHAR(200) NULL,
        [LicenseKey] NVARCHAR(100) NOT NULL,
        [HardDiskNumber] NVARCHAR(256) NOT NULL,
        [ServerMacID] NVARCHAR(256) NOT NULL,
        [MotherboardNumber] NVARCHAR(256) NOT NULL,
        [PublicIPAddress] VARCHAR(60) NULL,
        [StartDate] DATETIME NOT NULL,
        [ExpiryDate] DATETIME NOT NULL,
        [LastLoginDate] DATETIME NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_ClientAppLicense_IsActive] DEFAULT ((1)),
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_ClientAppLicense_CreatedAt] DEFAULT (GETDATE()),
        [OTP_Verified] BIT NOT NULL CONSTRAINT [DF_ClientAppLicense_OTP_Verified] DEFAULT ((1)),
        [AMC_Expireddate] DATETIME NULL,
        [AppUrl] NVARCHAR(500) NULL,
        [ProductType] NVARCHAR(100) NULL
    );
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ClientAppLicense')
      AND [name] = 'HardDiskNumber'
      AND max_length < 512
)
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ALTER COLUMN [HardDiskNumber] NVARCHAR(256) NOT NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ClientAppLicense')
      AND [name] = 'ServerMacID'
      AND max_length < 512
)
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ALTER COLUMN [ServerMacID] NVARCHAR(256) NOT NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ClientAppLicense')
      AND [name] = 'MotherboardNumber'
      AND max_length < 512
)
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ALTER COLUMN [MotherboardNumber] NVARCHAR(256) NOT NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'PublicIPAddress') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [PublicIPAddress] VARCHAR(60) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'LastLoginDate') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [LastLoginDate] DATETIME NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'EmailID') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [EmailID] NVARCHAR(200) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'OTP_Verified') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [OTP_Verified] BIT NOT NULL CONSTRAINT [DF_ClientAppLicense_OTP_Verified_Backfill] DEFAULT ((1));
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AMC_Expireddate') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AMC_Expireddate] DATETIME NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AppUrl') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AppUrl] NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'ProductType') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [ProductType] NVARCHAR(100) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'IsDisplayAlerts') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [IsDisplayAlerts] BIT NOT NULL CONSTRAINT [DF_ClientAppLicense_IsDisplayAlerts] DEFAULT ((0));
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AlertStartdate') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AlertStartdate] DATE NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AlertStartTime') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AlertStartTime] TIME(0) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AlertEnddate') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AlertEnddate] DATE NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AlertEndTime') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AlertEndTime] TIME(0) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AlertMessage') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AlertMessage] NVARCHAR(MAX) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ClientAppLicense_ClientCode' AND object_id = OBJECT_ID('dbo.ClientAppLicense'))
BEGIN
    CREATE UNIQUE INDEX [UX_ClientAppLicense_ClientCode] ON [dbo].[ClientAppLicense]([ClientCode]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ClientAppLicense_LicenseKey' AND object_id = OBJECT_ID('dbo.ClientAppLicense'))
BEGIN
    CREATE UNIQUE INDEX [UX_ClientAppLicense_LicenseKey] ON [dbo].[ClientAppLicense]([LicenseKey]);
END;

IF OBJECT_ID('dbo.ClientAppLicenseValidationLog', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ClientAppLicenseValidationLog]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ClientCode] VARCHAR(32) NULL,
        [LicenseKey] NVARCHAR(100) NULL,
        [ValidatedAt] DATETIME NOT NULL,
        [ServerMacID] NVARCHAR(120) NULL,
        [HardDiskNumber] VARCHAR(120) NULL,
        [MotherboardNumber] NVARCHAR(120) NULL,
        [IsMatch] BIT NOT NULL,
        [IsExpired] BIT NOT NULL,
        [IsRemoteReachable] BIT NOT NULL,
        [Result] VARCHAR(40) NOT NULL,
        [FailureReason] NVARCHAR(500) NULL,
        [RequestIp] VARCHAR(60) NULL,
        [AppUrl] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_ClientAppLicenseValidationLog_CreatedAt] DEFAULT (GETDATE())
    );
END;

IF COL_LENGTH('dbo.ClientAppLicenseValidationLog', 'AppUrl') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicenseValidationLog] ADD [AppUrl] NVARCHAR(500) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientAppLicenseValidationLog_ClientCode_ValidatedAt' AND object_id = OBJECT_ID('dbo.ClientAppLicenseValidationLog'))
BEGIN
    CREATE INDEX [IX_ClientAppLicenseValidationLog_ClientCode_ValidatedAt]
    ON [dbo].[ClientAppLicenseValidationLog]([ClientCode], [ValidatedAt] DESC);
END;

IF OBJECT_ID('dbo.ClientLicenseRemoteConfig', 'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[ClientLicenseRemoteConfig];
END;";

                await using var command = new SqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
                _localSchemaEnsured = true;
            }
            finally
            {
                LocalSchemaLock.Release();
            }
        }

        private async Task EnsureRemoteDatabaseAndSchemaAsync(string remoteServer, string remoteUsername, string remotePassword, string remoteDatabase)
        {
            var masterConnectionString = BuildConnectionString(remoteServer, "master", remoteUsername, remotePassword);
            await using var masterConnection = new SqlConnection(masterConnectionString);
            await masterConnection.OpenAsync();

            var quotedDatabaseName = QuoteSqlIdentifier(remoteDatabase);
            var createDatabaseSql = $@"
IF DB_ID(N'{EscapeSqlStringLiteral(remoteDatabase)}') IS NULL
BEGIN
    EXEC('CREATE DATABASE {quotedDatabaseName}');
END;";
            await using (var createDatabaseCommand = new SqlCommand(createDatabaseSql, masterConnection))
            {
                await createDatabaseCommand.ExecuteNonQueryAsync();
            }

            var remoteConnectionString = BuildConnectionString(remoteServer, remoteDatabase, remoteUsername, remotePassword);
            await using var remoteConnection = new SqlConnection(remoteConnectionString);
            await remoteConnection.OpenAsync();

            var sql = @"
IF OBJECT_ID('dbo.ClientAppLicense', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ClientAppLicense]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ClientCode] VARCHAR(32) NOT NULL,
        [ClientName] VARCHAR(200) NOT NULL,
        [ContactNumber] VARCHAR(30) NULL,
        [EmailID] NVARCHAR(200) NULL,
        [LicenseKey] NVARCHAR(100) NOT NULL,
        [HardDiskNumber] VARCHAR(120) NOT NULL,
        [ServerMacID] NVARCHAR(120) NOT NULL,
        [MotherboardNumber] NVARCHAR(120) NOT NULL,
        [PublicIPAddress] VARCHAR(60) NULL,
        [StartDate] DATETIME NOT NULL,
        [ExpiryDate] DATETIME NOT NULL,
        [LastLoginDate] DATETIME NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_RemoteClientAppLicense_IsActive] DEFAULT ((1)),
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_RemoteClientAppLicense_CreatedAt] DEFAULT (GETDATE()),
        [OTP_Verified] BIT NOT NULL CONSTRAINT [DF_RemoteClientAppLicense_OTP_Verified] DEFAULT ((1)),
        [AMC_Expireddate] DATETIME NULL,
        [AppUrl] NVARCHAR(500) NULL,
        [ProductType] NVARCHAR(100) NULL
    );
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'PublicIPAddress') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [PublicIPAddress] VARCHAR(60) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'LastLoginDate') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [LastLoginDate] DATETIME NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'EmailID') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [EmailID] NVARCHAR(200) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'OTP_Verified') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [OTP_Verified] BIT NOT NULL CONSTRAINT [DF_RemoteClientAppLicense_OTP_Verified_Backfill] DEFAULT ((1));
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AMC_Expireddate') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AMC_Expireddate] DATETIME NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'AppUrl') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [AppUrl] NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.ClientAppLicense', 'ProductType') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientAppLicense] ADD [ProductType] NVARCHAR(100) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RemoteClientAppLicense_ClientCode' AND object_id = OBJECT_ID('dbo.ClientAppLicense'))
BEGIN
    CREATE UNIQUE INDEX [UX_RemoteClientAppLicense_ClientCode] ON [dbo].[ClientAppLicense]([ClientCode]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RemoteClientAppLicense_LicenseKey' AND object_id = OBJECT_ID('dbo.ClientAppLicense'))
BEGIN
    CREATE UNIQUE INDEX [UX_RemoteClientAppLicense_LicenseKey] ON [dbo].[ClientAppLicense]([LicenseKey]);
END;

IF OBJECT_ID('dbo.ClientAppLicenseValidationLog', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ClientAppLicenseValidationLog]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ClientCode] VARCHAR(32) NULL,
        [LicenseKey] NVARCHAR(100) NULL,
        [ValidatedAt] DATETIME NOT NULL,
        [ServerMacID] NVARCHAR(120) NULL,
        [HardDiskNumber] VARCHAR(120) NULL,
        [MotherboardNumber] NVARCHAR(120) NULL,
        [IsMatch] BIT NOT NULL,
        [IsExpired] BIT NOT NULL,
        [IsRemoteReachable] BIT NOT NULL,
        [Result] VARCHAR(40) NOT NULL,
        [FailureReason] NVARCHAR(500) NULL,
        [RequestIp] VARCHAR(60) NULL,
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_RemoteClientAppLicenseValidationLog_CreatedAt] DEFAULT (GETDATE())
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RemoteClientAppLicenseValidationLog_ClientCode_ValidatedAt' AND object_id = OBJECT_ID('dbo.ClientAppLicenseValidationLog'))
BEGIN
    CREATE INDEX [IX_RemoteClientAppLicenseValidationLog_ClientCode_ValidatedAt]
    ON [dbo].[ClientAppLicenseValidationLog]([ClientCode], [ValidatedAt] DESC);
END;

IF OBJECT_ID('dbo.LicenseValidationHistory', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseValidationHistory]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ClientCode] VARCHAR(32) NOT NULL,
        [LicenseKey] VARCHAR(100) NOT NULL,
        [IsValid] BIT NOT NULL,
        [FailureReason] VARCHAR(500) NULL,
        [PublicIPAddress] VARCHAR(60) NULL,
        [DeviceInfo] VARCHAR(1000) NULL,
        [AppUrl] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_LicenseValidationHistory_CreatedAt] DEFAULT (GETDATE())
    );
END;

IF COL_LENGTH('dbo.LicenseValidationHistory', 'AppUrl') IS NULL
BEGIN
    ALTER TABLE [dbo].[LicenseValidationHistory] ADD [AppUrl] NVARCHAR(500) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LicenseValidationHistory_ClientCode_LicenseKey_CreatedAt' AND object_id = OBJECT_ID('dbo.LicenseValidationHistory'))
BEGIN
    CREATE INDEX [IX_LicenseValidationHistory_ClientCode_LicenseKey_CreatedAt]
    ON [dbo].[LicenseValidationHistory]([ClientCode], [LicenseKey], [CreatedAt] DESC)
    INCLUDE ([IsValid], [FailureReason]);
END;";

            sql += @"

IF OBJECT_ID('dbo.ClientOTPValidationHistory', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ClientOTPValidationHistory]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ChallengeId] UNIQUEIDENTIFIER NOT NULL,
        [ClientName] NVARCHAR(200) NULL,
        [ContactNumber] VARCHAR(30) NULL,
        [EmailID] NVARCHAR(200) NOT NULL,
        [OTPCodeHash] NVARCHAR(256) NOT NULL,
        [ClientCode] VARCHAR(32) NULL,
        [LicenseKey] NVARCHAR(100) NULL,
        [IsValidated] BIT NOT NULL CONSTRAINT [DF_ClientOTPValidationHistory_IsValidated] DEFAULT ((0)),
        [GeneratedAt] DATETIME NOT NULL,
        [ExpiresAt] DATETIME NOT NULL,
        [ValidatedAt] DATETIME NULL,
        [RequestIp] VARCHAR(60) NULL,
        [FailureReason] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_ClientOTPValidationHistory_CreatedAt] DEFAULT (GETDATE())
    );
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'ChallengeId') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [ChallengeId] UNIQUEIDENTIFIER NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'ClientName') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [ClientName] NVARCHAR(200) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'ContactNumber') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [ContactNumber] VARCHAR(30) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'EmailID') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [EmailID] NVARCHAR(200) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'OTPCodeHash') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [OTPCodeHash] NVARCHAR(256) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'ClientCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [ClientCode] VARCHAR(32) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'LicenseKey') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [LicenseKey] NVARCHAR(100) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'IsValidated') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [IsValidated] BIT NOT NULL CONSTRAINT [DF_ClientOTPValidationHistory_IsValidated_Backfill] DEFAULT ((0));
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'GeneratedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [GeneratedAt] DATETIME NOT NULL CONSTRAINT [DF_ClientOTPValidationHistory_GeneratedAt] DEFAULT (GETDATE());
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'ExpiresAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [ExpiresAt] DATETIME NOT NULL CONSTRAINT [DF_ClientOTPValidationHistory_ExpiresAt] DEFAULT (GETDATE());
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'ValidatedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [ValidatedAt] DATETIME NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'RequestIp') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [RequestIp] VARCHAR(60) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'FailureReason') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [FailureReason] NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.ClientOTPValidationHistory', 'CreatedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ClientOTPValidationHistory] ADD [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_ClientOTPValidationHistory_CreatedAt_Backfill] DEFAULT (GETDATE());
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ClientOTPValidationHistory_ChallengeId' AND object_id = OBJECT_ID('dbo.ClientOTPValidationHistory'))
BEGIN
    EXEC('CREATE UNIQUE INDEX [UX_ClientOTPValidationHistory_ChallengeId] ON [dbo].[ClientOTPValidationHistory]([ChallengeId]) WHERE [ChallengeId] IS NOT NULL;');
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientOTPValidationHistory_EmailID_CreatedAt' AND object_id = OBJECT_ID('dbo.ClientOTPValidationHistory'))
BEGIN
    EXEC('CREATE INDEX [IX_ClientOTPValidationHistory_EmailID_CreatedAt] ON [dbo].[ClientOTPValidationHistory]([EmailID], [CreatedAt] DESC);');
END;";

            await using var command = new SqlCommand(sql, remoteConnection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<ClientAppLicense?> GetLocalLicenseAsync()
        {
            var machine = GetCurrentMachineFingerprint();

            await using var connection = new SqlConnection(_localConnectionString);
            await connection.OpenAsync();

            const string sql = @"
SELECT
    Id,
    ClientCode,
    ClientName,
    ContactNumber,
    EmailID,
    LicenseKey,
    HardDiskNumber,
    ServerMacID,
    MotherboardNumber,
    PublicIPAddress,
    StartDate,
    ExpiryDate,
    LastLoginDate,
    IsActive,
    CreatedAt,
    OTP_Verified,
    AMC_Expireddate,
    AppUrl,
    ProductType,
    ISNULL(IsDisplayAlerts, 0) AS IsDisplayAlerts,
    AlertStartdate,
    AlertStartTime,
    AlertEnddate,
    AlertEndTime,
    AlertMessage
FROM dbo.ClientAppLicense
ORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection);
            var currentAppUrl = GetCurrentAppUrl();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var license = MapLocalLicense(reader);
                if (FingerprintsMatch(machine, license) &&
                    string.Equals(license.AppUrl, currentAppUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return license;
                }
            }

            return null;
        }

        public async Task<string?> GetActiveAlertMessageAsync()
        {
            try
            {
                var currentAppUrl = GetCurrentAppUrl();

                await using var connection = new SqlConnection(_localConnectionString);
                await connection.OpenAsync();

                const string sql = @"
SELECT TOP 1 AlertMessage
FROM dbo.ClientAppLicense
WHERE IsDisplayAlerts = 1
  AND AlertMessage IS NOT NULL
  AND AlertMessage <> ''
  AND GETDATE() >= CAST(AlertStartdate AS datetime) + CAST(AlertStartTime AS datetime)
  AND GETDATE() <= CAST(AlertEnddate AS datetime) + CAST(AlertEndTime AS datetime)
  AND AppUrl = @AppUrl
ORDER BY Id DESC;";

                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@AppUrl", currentAppUrl ?? (object)DBNull.Value);

                var result = await command.ExecuteScalarAsync();
                return result as string;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to fetch alert message from ClientAppLicense");
                return null;
            }
        }

        private async Task<LicenseValidationHistoryEntry?> GetLatestRemoteValidationHistoryForTodayAsync(SqlConnection connection, SqlTransaction? transaction, string clientCode, string licenseKey)
        {
            const string sql = @"
SELECT TOP 1
    Id,
    ClientCode,
    LicenseKey,
    IsValid,
    FailureReason,
    PublicIPAddress,
    DeviceInfo,
    CreatedAt
FROM dbo.LicenseValidationHistory
WHERE ClientCode = @ClientCode
  AND LicenseKey = @LicenseKey
  AND CAST(CreatedAt AS date) = CAST(GETDATE() AS date)
ORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ClientCode", clientCode);
            command.Parameters.AddWithValue("@LicenseKey", licenseKey);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new LicenseValidationHistoryEntry
            {
                Id = reader.GetInt64(0),
                ClientCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                LicenseKey = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                IsValid = !reader.IsDBNull(3) && reader.GetBoolean(3),
                FailureReason = reader.IsDBNull(4) ? null : reader.GetString(4),
                PublicIPAddress = reader.IsDBNull(5) ? null : reader.GetString(5),
                DeviceInfo = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.IsDBNull(7) ? DateTime.Now : reader.GetDateTime(7)
            };
        }

        private async Task InsertRemoteValidationHistoryAsync(SqlConnection connection, SqlTransaction? transaction, LicenseValidationHistoryEntry entry)
        {
            const string sql = @"
INSERT INTO dbo.LicenseValidationHistory
(
    ClientCode,
    LicenseKey,
    IsValid,
    FailureReason,
    PublicIPAddress,
    DeviceInfo,
    AppUrl,
    CreatedAt
)
VALUES
(
    @ClientCode,
    @LicenseKey,
    @IsValid,
    @FailureReason,
    @PublicIPAddress,
    @DeviceInfo,
    @AppUrl,
    @CreatedAt
);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ClientCode", entry.ClientCode);
            command.Parameters.AddWithValue("@LicenseKey", entry.LicenseKey);
            command.Parameters.AddWithValue("@IsValid", entry.IsValid);
            command.Parameters.AddWithValue("@FailureReason", (object?)entry.FailureReason ?? DBNull.Value);
            command.Parameters.AddWithValue("@PublicIPAddress", (object?)entry.PublicIPAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@DeviceInfo", (object?)entry.DeviceInfo ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppUrl", (object?)entry.AppUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<CentralMailConfiguration?> GetCentralMailConfigurationAsync(SqlConnection connection, SqlTransaction? transaction)
        {
            if (!await TableExistsAsync(connection, transaction, CentralMailConfigurationTableName))
            {
                return null;
            }

            var sql = $@"
SELECT TOP 1
    Id,
    SmtpServer,
    SmtpPort,
    SmtpUsername,
    SmtpPassword,
    EnableSSL,
    FromEmail,
    FromName,
    IsActive
FROM {CentralMailConfigurationTableName}
WHERE IsActive = 1
ORDER BY Id DESC;";

            await using var command = new SqlCommand(sql, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
            {
                return null;
            }

            var rawPassword = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

            return new CentralMailConfiguration
            {
                Id = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0)),
                SmtpServer = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                SmtpPort = reader.IsDBNull(2) ? 587 : Convert.ToInt32(reader.GetValue(2)),
                SmtpUsername = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                SmtpPassword = ResolveMailPassword(rawPassword),
                EnableSsl = !reader.IsDBNull(5) && reader.GetBoolean(5),
                FromEmail = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                FromName = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                IsActive = !reader.IsDBNull(8) && reader.GetBoolean(8)
            };
        }

        private async Task InsertClientOtpValidationHistoryAsync(SqlConnection connection, SqlTransaction? transaction, PendingLicenseRegistrationOtp pendingRegistration, string otpCodeHash)
        {
            var sql = $@"
INSERT INTO {RemoteOtpValidationHistoryTableName}
(
    ChallengeId,
    ClientName,
    ContactNumber,
    EmailID,
    OTPCodeHash,
    ClientCode,
    LicenseKey,
    IsValidated,
    GeneratedAt,
    ExpiresAt,
    ValidatedAt,
    RequestIp,
    FailureReason,
    CreatedAt
)
VALUES
(
    @ChallengeId,
    @ClientName,
    @ContactNumber,
    @EmailID,
    @OTPCodeHash,
    @ClientCode,
    @LicenseKey,
    @IsValidated,
    @GeneratedAt,
    @ExpiresAt,
    @ValidatedAt,
    @RequestIp,
    @FailureReason,
    @CreatedAt
);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ChallengeId", pendingRegistration.ChallengeId);
            command.Parameters.AddWithValue("@ClientName", pendingRegistration.Model.ClientName.Trim());
            command.Parameters.AddWithValue("@ContactNumber", pendingRegistration.Model.ContactNumber.Trim());
            command.Parameters.AddWithValue("@EmailID", pendingRegistration.Model.EmailID?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("@OTPCodeHash", otpCodeHash);
            command.Parameters.AddWithValue("@ClientCode", DBNull.Value);
            command.Parameters.AddWithValue("@LicenseKey", DBNull.Value);
            command.Parameters.AddWithValue("@IsValidated", pendingRegistration.IsVerified);
            command.Parameters.AddWithValue("@GeneratedAt", pendingRegistration.GeneratedAt);
            command.Parameters.AddWithValue("@ExpiresAt", pendingRegistration.ExpiresAt);
            command.Parameters.AddWithValue("@ValidatedAt", DBNull.Value);
            command.Parameters.AddWithValue("@RequestIp", (object?)pendingRegistration.RequestIp ?? DBNull.Value);
            command.Parameters.AddWithValue("@FailureReason", DBNull.Value);
            command.Parameters.AddWithValue("@CreatedAt", pendingRegistration.GeneratedAt);
            await command.ExecuteNonQueryAsync();
        }

        private async Task UpdateClientOtpValidationHistoryAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            Guid challengeId,
            bool isValidated,
            DateTime? validatedAt,
            string? failureReason,
            string? clientCode = null,
            string? licenseKey = null)
        {
            var sql = $@"
UPDATE {RemoteOtpValidationHistoryTableName}
SET IsValidated = @IsValidated,
    ValidatedAt = @ValidatedAt,
    FailureReason = @FailureReason,
    ClientCode = COALESCE(@ClientCode, ClientCode),
    LicenseKey = COALESCE(@LicenseKey, LicenseKey)
WHERE ChallengeId = @ChallengeId;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ChallengeId", challengeId);
            command.Parameters.AddWithValue("@IsValidated", isValidated);
            command.Parameters.AddWithValue("@ValidatedAt", (object?)validatedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@FailureReason", (object?)failureReason ?? DBNull.Value);
            command.Parameters.AddWithValue("@ClientCode", (object?)clientCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@LicenseKey", (object?)licenseKey ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<(bool Success, string Message)> SendRegistrationOtpEmailAsync(CentralMailConfiguration configuration, LicenseRegistrationViewModel model, string toEmail, string otpCode, DateTime expiresAt)
        {
            try
            {
                var smtpServer = NormalizeSmtpServer(configuration.SmtpServer);

                using var client = new SmtpClient(smtpServer, configuration.SmtpPort)
                {
                    EnableSsl = configuration.EnableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(configuration.SmtpUsername, configuration.SmtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(configuration.FromEmail, configuration.FromName),
                    Subject = $"eRestoPOS License OTP - {otpCode}",
                    Body = BuildRegistrationOtpEmailBody(model, otpCode, expiresAt),
                    IsBodyHtml = true,
                    Priority = MailPriority.High
                };

                message.To.Add(toEmail.Trim());
                await client.SendMailAsync(message);

                return (true, "OTP sent successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send registration OTP email to {ToEmail}", toEmail);
                return (false, $"Unable to send OTP email. {ex.Message}");
            }
        }

        private async Task<(bool Success, string Message)> SendWelcomeEmailAsync(CentralMailConfiguration configuration, ClientAppLicense license)
        {
            try
            {
                var smtpServer = NormalizeSmtpServer(configuration.SmtpServer);

                using var client = new SmtpClient(smtpServer, configuration.SmtpPort)
                {
                    EnableSsl = configuration.EnableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(configuration.SmtpUsername, configuration.SmtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(configuration.FromEmail, configuration.FromName),
                    Subject = $"Welcome to eRestoPOS — License Registered ({license.ClientCode})",
                    Body = BuildWelcomeEmailBody(license),
                    IsBodyHtml = true,
                    Priority = MailPriority.Normal
                };

                message.To.Add(license.EmailID?.Trim() ?? string.Empty);
                await client.SendMailAsync(message);

                _logger.LogInformation("Welcome email sent to {EmailID} for client {ClientCode}", license.EmailID, license.ClientCode);
                return (true, "Welcome email sent successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome email to {EmailID} for client {ClientCode}", license.EmailID, license.ClientCode);
                return (false, $"Unable to send welcome email. {ex.Message}");
            }
        }

        private static string BuildWelcomeEmailBody(ClientAppLicense license)
        {
            var clientName    = WebUtility.HtmlEncode(license.ClientName.Trim());
            var emailId       = WebUtility.HtmlEncode(license.EmailID?.Trim() ?? string.Empty);
            var contactNumber = WebUtility.HtmlEncode(license.ContactNumber?.Trim() ?? "—");
            var clientCode    = WebUtility.HtmlEncode(license.ClientCode.Trim());
            var licenseKey    = WebUtility.HtmlEncode(license.LicenseKey.Trim());
            var startDate     = license.StartDate.ToString("dd-MMM-yyyy");
            var expiryDate    = license.ExpiryDate.ToString("dd-MMM-yyyy");
            var amcExpiry     = license.AMC_Expireddate.HasValue
                                    ? license.AMC_Expireddate.Value.ToString("dd-MMM-yyyy")
                                    : "Not Applicable";
            var macId         = WebUtility.HtmlEncode(license.ServerMacID.Trim());
            var hddSerial     = WebUtility.HtmlEncode(license.HardDiskNumber.Trim());
            var motherboard   = WebUtility.HtmlEncode(license.MotherboardNumber.Trim());
            var publicIp      = WebUtility.HtmlEncode(license.PublicIPAddress?.Trim() ?? "—");
            var registeredAt  = license.CreatedAt.ToString("dd-MMM-yyyy HH:mm:ss");

            return $$"""
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8" /><meta name="viewport" content="width=device-width,initial-scale=1.0" /></head>
<body style="margin:0;padding:0;background:#f0f4f8;font-family:Segoe UI,Arial,sans-serif;">
<table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f0f4f8;padding:32px 16px;">
  <tr><td align="center">
    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:620px;">

      <!-- Header -->
      <tr>
        <td style="background:linear-gradient(135deg,#111827 0%,#991b1b 58%,#ea580c 100%);border-radius:20px 20px 0 0;padding:32px 36px 28px;">
          <div style="font-size:11px;font-weight:700;letter-spacing:0.18em;text-transform:uppercase;color:rgba(255,255,255,0.75);margin-bottom:10px;">eRestoPOS Licensing System</div>
          <h1 style="margin:0 0 8px;font-size:26px;font-weight:800;color:#ffffff;line-height:1.2;">License Registration Successful</h1>
          <p style="margin:0;font-size:14px;color:rgba(255,255,255,0.88);line-height:1.6;">Your eRestoPOS license has been activated and is ready to use.</p>
        </td>
      </tr>

      <!-- Body -->
      <tr>
        <td style="background:#ffffff;padding:32px 36px 0;border-left:1px solid #e5e7eb;border-right:1px solid #e5e7eb;">

          <!-- Greeting -->
          <p style="margin:0 0 20px;font-size:16px;color:#111827;line-height:1.7;">
            Dear <strong>{{clientName}}</strong>,<br />
            Thank you for registering with <strong>eRestoPOS</strong>. Your license has been successfully created and bound to your server. Below are your complete registration details for your records.
          </p>

          <!-- Client Information -->
          <div style="margin-bottom:24px;">
            <div style="font-size:11px;font-weight:700;letter-spacing:0.14em;text-transform:uppercase;color:#991b1b;margin-bottom:10px;padding-bottom:6px;border-bottom:2px solid #fee2e2;">Client Information</div>
            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
              <tr>
                <td width="44%" style="padding:8px 0;font-size:13px;color:#6b7280;vertical-align:top;">Client Name</td>
                <td style="padding:8px 0;font-size:13px;color:#111827;font-weight:600;vertical-align:top;">{{clientName}}</td>
              </tr>
              <tr style="background:#f9fafb;">
                <td style="padding:8px 10px;font-size:13px;color:#6b7280;vertical-align:top;border-radius:6px 0 0 6px;">Email Address</td>
                <td style="padding:8px 10px;font-size:13px;color:#111827;font-weight:600;vertical-align:top;border-radius:0 6px 6px 0;">{{emailId}}</td>
              </tr>
              <tr>
                <td style="padding:8px 0;font-size:13px;color:#6b7280;vertical-align:top;">Contact Number</td>
                <td style="padding:8px 0;font-size:13px;color:#111827;font-weight:600;vertical-align:top;">{{contactNumber}}</td>
              </tr>
              <tr style="background:#f9fafb;">
                <td style="padding:8px 10px;font-size:13px;color:#6b7280;vertical-align:top;border-radius:6px 0 0 6px;">Registration Date</td>
                <td style="padding:8px 10px;font-size:13px;color:#111827;font-weight:600;vertical-align:top;border-radius:0 6px 6px 0;">{{registeredAt}}</td>
              </tr>
            </table>
          </div>

          <!-- License Details -->
          <div style="margin-bottom:24px;">
            <div style="font-size:11px;font-weight:700;letter-spacing:0.14em;text-transform:uppercase;color:#991b1b;margin-bottom:10px;padding-bottom:6px;border-bottom:2px solid #fee2e2;">License Details</div>
            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
              <tr>
                <td width="44%" style="padding:8px 0;font-size:13px;color:#6b7280;vertical-align:top;">Client Code</td>
                <td style="padding:8px 0;vertical-align:top;">
                  <span style="display:inline-block;background:#fef3c7;color:#92400e;font-size:13px;font-weight:700;padding:3px 10px;border-radius:6px;letter-spacing:0.06em;">{{clientCode}}</span>
                </td>
              </tr>
              <tr style="background:#f9fafb;">
                <td style="padding:8px 10px;font-size:13px;color:#6b7280;vertical-align:top;border-radius:6px 0 0 6px;">License Key</td>
                <td style="padding:8px 10px;font-size:12px;color:#374151;font-weight:600;font-family:Consolas,monospace;word-break:break-all;vertical-align:top;border-radius:0 6px 6px 0;">{{licenseKey}}</td>
              </tr>
              <tr>
                <td style="padding:8px 0;font-size:13px;color:#6b7280;vertical-align:top;">Start Date</td>
                <td style="padding:8px 0;font-size:13px;color:#111827;font-weight:600;vertical-align:top;">{{startDate}}</td>
              </tr>
              <tr style="background:#f9fafb;">
                <td style="padding:8px 10px;font-size:13px;color:#6b7280;vertical-align:top;border-radius:6px 0 0 6px;">Expiry Date</td>
                <td style="padding:8px 10px;font-size:13px;font-weight:700;vertical-align:top;border-radius:0 6px 6px 0;">
                  <span style="color:#15803d;">{{expiryDate}}</span>
                </td>
              </tr>
              <tr>
                <td style="padding:8px 0;font-size:13px;color:#6b7280;vertical-align:top;">AMC Expiry Date</td>
                <td style="padding:8px 0;font-size:13px;color:#111827;font-weight:600;vertical-align:top;">{{amcExpiry}}</td>
              </tr>
              <tr style="background:#f9fafb;">
                <td style="padding:8px 10px;font-size:13px;color:#6b7280;vertical-align:top;border-radius:6px 0 0 6px;">Public IP Address</td>
                <td style="padding:8px 0;font-size:13px;color:#111827;font-weight:600;vertical-align:top;">{{publicIp}}</td>
              </tr>
            </table>
          </div>

          <!-- Hardware Binding -->
          <div style="margin-bottom:24px;">
            <div style="font-size:11px;font-weight:700;letter-spacing:0.14em;text-transform:uppercase;color:#991b1b;margin-bottom:10px;padding-bottom:6px;border-bottom:2px solid #fee2e2;">Hardware Binding</div>
            <p style="margin:0 0 10px;font-size:12px;color:#6b7280;line-height:1.6;">Your license is bound to the following hardware identifiers. Contact support if you migrate to a new server.</p>
            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f9fafb;border-radius:10px;">
              <tr>
                <td width="44%" style="padding:9px 12px;font-size:12px;color:#6b7280;border-bottom:1px solid #f0f0f0;">MAC Address</td>
                <td style="padding:9px 12px;font-size:12px;color:#374151;font-weight:600;font-family:Consolas,monospace;word-break:break-all;border-bottom:1px solid #f0f0f0;">{{macId}}</td>
              </tr>
              <tr>
                <td style="padding:9px 12px;font-size:12px;color:#6b7280;border-bottom:1px solid #f0f0f0;">Hard Disk Serial</td>
                <td style="padding:9px 12px;font-size:12px;color:#374151;font-weight:600;font-family:Consolas,monospace;word-break:break-all;border-bottom:1px solid #f0f0f0;">{{hddSerial}}</td>
              </tr>
              <tr>
                <td style="padding:9px 12px;font-size:12px;color:#6b7280;">Motherboard Serial</td>
                <td style="padding:9px 12px;font-size:12px;color:#374151;font-weight:600;font-family:Consolas,monospace;word-break:break-all;">{{motherboard}}</td>
              </tr>
            </table>
          </div>

        </td>
      </tr>

      <!-- Important Notes -->
      <tr>
        <td style="background:#fffbeb;border:1px solid #fde68a;border-top:none;padding:18px 36px;">
          <div style="font-size:12px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:#92400e;margin-bottom:8px;">Important Notes</div>
          <ul style="margin:0;padding-left:18px;font-size:13px;color:#78350f;line-height:1.8;">
            <li>Keep your <strong>Client Code</strong> and <strong>License Key</strong> confidential — do not share them.</li>
            <li>Your license is hardware-bound. Changing the server hardware requires re-validation.</li>
            <li>License renewal must be completed before the expiry date to avoid service interruption.</li>
            <li>For support, contact us at <strong>+91 86172 80732</strong> or reply to this email.</li>
          </ul>
        </td>
      </tr>

      <!-- Footer -->
      <tr>
        <td style="background:#111827;border-radius:0 0 20px 20px;padding:24px 36px;text-align:center;">
          <div style="font-size:13px;font-weight:700;color:#f97316;letter-spacing:0.12em;margin-bottom:6px;">eRestoPOS</div>
          <div style="font-size:12px;color:rgba(255,255,255,0.6);line-height:1.7;">
            This is an automated message from eRestoPOS Licensing System.<br />
            Please do not reply to this email unless you need support.<br />
            Support: +91 86172 80732
          </div>
          <hr style="margin:16px 0;border:none;border-top:1px solid rgba(255,255,255,0.1);" />
          <div style="font-size:11px;color:rgba(255,255,255,0.35);">© {{DateTime.Now.Year}} eRestoPOS. All rights reserved.</div>
        </td>
      </tr>

    </table>
  </td></tr>
</table>
</body>
</html>
""";
        }

        private async Task<ClientAppLicense?> GetRemoteLicenseAsync(SqlConnection connection, SqlTransaction? transaction, string clientCode, string licenseKey)
        {
            const string sql = @"
SELECT TOP 1
    Id,
    ClientCode,
    ClientName,
    ContactNumber,
    EmailID,
    LicenseKey,
    HardDiskNumber,
    ServerMacID,
    MotherboardNumber,
    PublicIPAddress,
    StartDate,
    ExpiryDate,
    LastLoginDate,
    IsActive,
    CreatedAt,
    OTP_Verified,
    AMC_Expireddate,
    AppUrl,
    ProductType,
    ISNULL(IsDisplayAlerts, 0) AS IsDisplayAlerts,
    AlertStartdate,
    AlertStartTime,
    AlertEnddate,
    AlertEndTime,
    AlertMessage
FROM dbo.ClientAppLicense
WHERE ClientCode = @ClientCode
  AND LicenseKey = @LicenseKey
ORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ClientCode", clientCode);
            command.Parameters.AddWithValue("@LicenseKey", licenseKey);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            return await reader.ReadAsync() ? MapLicense(reader) : null;
        }

        private async Task<ClientAppLicense?> GetRemoteLicenseByFingerprintAsync(SqlConnection connection, SqlTransaction? transaction, LicenseMachineFingerprint machine, string? appUrl = null)
        {
            var sql = @"
SELECT TOP 1
    Id,
    ClientCode,
    ClientName,
    ContactNumber,
    EmailID,
    LicenseKey,
    HardDiskNumber,
    ServerMacID,
    MotherboardNumber,
    PublicIPAddress,
    StartDate,
    ExpiryDate,
    LastLoginDate,
    IsActive,
    CreatedAt,
    OTP_Verified,
    AMC_Expireddate,
    AppUrl,
    ProductType,
    ISNULL(IsDisplayAlerts, 0) AS IsDisplayAlerts,
    AlertStartdate,
    AlertStartTime,
    AlertEnddate,
    AlertEndTime,
    AlertMessage
FROM dbo.ClientAppLicense
WHERE ServerMacID = @ServerMacID
  AND HardDiskNumber = @HardDiskNumber
  AND MotherboardNumber = @MotherboardNumber";

            if (!string.IsNullOrWhiteSpace(appUrl))
                sql += "\n  AND AppUrl = @AppUrl";

            sql += "\nORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ServerMacID", machine.ServerMacID);
            command.Parameters.AddWithValue("@HardDiskNumber", machine.HardDiskNumber);
            command.Parameters.AddWithValue("@MotherboardNumber", machine.MotherboardNumber);
            if (!string.IsNullOrWhiteSpace(appUrl))
                command.Parameters.AddWithValue("@AppUrl", appUrl);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            return await reader.ReadAsync() ? MapLicense(reader) : null;
        }

        private async Task<ClientAppLicense?> GetRemoteLicenseByAppUrlAsync(SqlConnection connection, SqlTransaction? transaction, string appUrl)
        {
            const string sql = @"
SELECT TOP 1
    Id,
    ClientCode,
    ClientName,
    ContactNumber,
    EmailID,
    LicenseKey,
    HardDiskNumber,
    ServerMacID,
    MotherboardNumber,
    PublicIPAddress,
    StartDate,
    ExpiryDate,
    LastLoginDate,
    IsActive,
    CreatedAt,
    OTP_Verified,
    AMC_Expireddate,
    AppUrl,
    ProductType,
    ISNULL(IsDisplayAlerts, 0) AS IsDisplayAlerts,
    AlertStartdate,
    AlertStartTime,
    AlertEnddate,
    AlertEndTime,
    AlertMessage
FROM dbo.ClientAppLicense
WHERE AppUrl = @AppUrl
ORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@AppUrl", appUrl);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            return await reader.ReadAsync() ? MapLicense(reader) : null;
        }

        private async Task<long> InsertLicenseAsync(SqlConnection connection, SqlTransaction transaction, ClientAppLicense license)
        {
            const string sql = @"
INSERT INTO dbo.ClientAppLicense
(
    ClientCode,
    ClientName,
    ContactNumber,
    EmailID,
    LicenseKey,
    HardDiskNumber,
    ServerMacID,
    MotherboardNumber,
    PublicIPAddress,
    StartDate,
    ExpiryDate,
    LastLoginDate,
    IsActive,
    CreatedAt,
    OTP_Verified,
    AMC_Expireddate,
    AppUrl,
    ProductType,
    IsDisplayAlerts,
    AlertStartdate,
    AlertStartTime,
    AlertEnddate,
    AlertEndTime,
    AlertMessage
)
VALUES
(
    @ClientCode,
    @ClientName,
    @ContactNumber,
    @EmailID,
    @LicenseKey,
    @HardDiskNumber,
    @ServerMacID,
    @MotherboardNumber,
    @PublicIPAddress,
    @StartDate,
    @ExpiryDate,
    @LastLoginDate,
    @IsActive,
    @CreatedAt,
    @OTP_Verified,
    @AMC_Expireddate,
    @AppUrl,
    @ProductType,
    @IsDisplayAlerts,
    @AlertStartdate,
    @AlertStartTime,
    @AlertEnddate,
    @AlertEndTime,
    @AlertMessage
);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

            await using var command = new SqlCommand(sql, connection, transaction);
            AddRemoteLicenseParameters(command, license);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0L : Convert.ToInt64(result);
        }

        private async Task<string> GenerateNextClientCodeAsync(SqlConnection connection, SqlTransaction transaction)
        {
            var financialYearCode = GetFinancialYearCode(DateTime.Now);
            var prefix = $"Cl-{financialYearCode}";

            const string sql = @"
SELECT ISNULL(MAX(TRY_CAST(RIGHT(ClientCode, 4) AS INT)), 0)
FROM dbo.ClientAppLicense WITH (UPDLOCK, HOLDLOCK)
WHERE ClientCode LIKE @Prefix + '%';";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Prefix", prefix);
            var result = await command.ExecuteScalarAsync();
            var lastSequence = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            var nextSequence = lastSequence + 1;
            if (nextSequence > 9999)
            {
                throw new InvalidOperationException($"Client code sequence overflow for financial year {financialYearCode}.");
            }

            return $"{prefix}{nextSequence:D4}";
        }

        private async Task UpsertLocalLicenseAsync(ClientAppLicense license)
        {
            await using var connection = new SqlConnection(_localConnectionString);
            await connection.OpenAsync();

            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.ClientAppLicense WHERE ClientCode = @ClientCode OR LicenseKey = @LicenseKey)
BEGIN
    UPDATE dbo.ClientAppLicense
    SET ClientName = @ClientName,
        ContactNumber = @ContactNumber,
        EmailID = @EmailID,
        HardDiskNumber = @HardDiskNumber,
        ServerMacID = @ServerMacID,
        MotherboardNumber = @MotherboardNumber,
        PublicIPAddress = @PublicIPAddress,
        StartDate = @StartDate,
        ExpiryDate = @ExpiryDate,
        LastLoginDate = @LastLoginDate,
        IsActive = @IsActive,
        CreatedAt = @CreatedAt,
        OTP_Verified = @OTP_Verified,
        AMC_Expireddate = @AMC_Expireddate,
        AppUrl = @AppUrl,
        ProductType = @ProductType,
        IsDisplayAlerts = @IsDisplayAlerts,
        AlertStartdate = @AlertStartdate,
        AlertStartTime = @AlertStartTime,
        AlertEnddate = @AlertEnddate,
        AlertEndTime = @AlertEndTime,
        AlertMessage = @AlertMessage
    WHERE ClientCode = @ClientCode OR LicenseKey = @LicenseKey;
END
ELSE
BEGIN
    INSERT INTO dbo.ClientAppLicense
    (
        ClientCode,
        ClientName,
        ContactNumber,
        EmailID,
        LicenseKey,
        HardDiskNumber,
        ServerMacID,
        MotherboardNumber,
        PublicIPAddress,
        StartDate,
        ExpiryDate,
        LastLoginDate,
        IsActive,
        CreatedAt,
        OTP_Verified,
        AMC_Expireddate,
        AppUrl,
        ProductType,
        IsDisplayAlerts,
        AlertStartdate,
        AlertStartTime,
        AlertEnddate,
        AlertEndTime,
        AlertMessage
    )
    VALUES
    (
        @ClientCode,
        @ClientName,
        @ContactNumber,
        @EmailID,
        @LicenseKey,
        @HardDiskNumber,
        @ServerMacID,
        @MotherboardNumber,
        @PublicIPAddress,
        @StartDate,
        @ExpiryDate,
        @LastLoginDate,
        @IsActive,
        @CreatedAt,
        @OTP_Verified,
        @AMC_Expireddate,
        @AppUrl,
        @ProductType,
        @IsDisplayAlerts,
        @AlertStartdate,
        @AlertStartTime,
        @AlertEnddate,
        @AlertEndTime,
        @AlertMessage
    );
END;";

            await using var command = new SqlCommand(sql, connection);
            AddLocalLicenseParameters(command, license);
            await command.ExecuteNonQueryAsync();
        }

        private async Task UpdateRemoteLicenseTrackingAsync(SqlConnection connection, SqlTransaction? transaction, ClientAppLicense license, bool updateLastLoginDate)
        {
            const string sql = @"
UPDATE dbo.ClientAppLicense
SET PublicIPAddress = @PublicIPAddress,
    LastLoginDate = CASE WHEN @UpdateLastLoginDate = 1 THEN @LastLoginDate ELSE LastLoginDate END
WHERE ClientCode = @ClientCode
  AND LicenseKey = @LicenseKey;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@PublicIPAddress", (object?)license.PublicIPAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@UpdateLastLoginDate", updateLastLoginDate);
            command.Parameters.AddWithValue("@LastLoginDate", (object?)license.LastLoginDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@ClientCode", license.ClientCode);
            command.Parameters.AddWithValue("@LicenseKey", license.LicenseKey);
            await command.ExecuteNonQueryAsync();
        }

        private async Task InsertLocalValidationLogAsync(ClientAppLicenseValidationLog log)
        {
            await using var connection = new SqlConnection(_localConnectionString);
            await connection.OpenAsync();
            await InsertValidationLogAsync(connection, null, log);
        }

        private async Task TryInsertLocalValidationLogAsync(ClientAppLicenseValidationLog log)
        {
            try
            {
                await InsertLocalValidationLogAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to write local license validation log for client code {ClientCode}", log.ClientCode);
            }
        }

        private async Task InsertValidationLogAsync(SqlConnection connection, SqlTransaction? transaction, ClientAppLicenseValidationLog log)
        {
            const string sql = @"
INSERT INTO dbo.ClientAppLicenseValidationLog
(
    ClientCode,
    LicenseKey,
    ValidatedAt,
    ServerMacID,
    HardDiskNumber,
    MotherboardNumber,
    IsMatch,
    IsExpired,
    IsRemoteReachable,
    Result,
    FailureReason,
    RequestIp,
    AppUrl,
    CreatedAt
)
VALUES
(
    @ClientCode,
    @LicenseKey,
    @ValidatedAt,
    @ServerMacID,
    @HardDiskNumber,
    @MotherboardNumber,
    @IsMatch,
    @IsExpired,
    @IsRemoteReachable,
    @Result,
    @FailureReason,
    @RequestIp,
    @AppUrl,
    @CreatedAt
);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ClientCode", (object?)log.ClientCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@LicenseKey", (object?)log.LicenseKey ?? DBNull.Value);
            command.Parameters.AddWithValue("@ValidatedAt", log.ValidatedAt);
            command.Parameters.AddWithValue("@ServerMacID", (object?)log.ServerMacID ?? DBNull.Value);
            command.Parameters.AddWithValue("@HardDiskNumber", (object?)log.HardDiskNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@MotherboardNumber", (object?)log.MotherboardNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsMatch", log.IsMatch);
            command.Parameters.AddWithValue("@IsExpired", log.IsExpired);
            command.Parameters.AddWithValue("@IsRemoteReachable", log.IsRemoteReachable);
            command.Parameters.AddWithValue("@Result", log.Result);
            command.Parameters.AddWithValue("@FailureReason", (object?)log.FailureReason ?? DBNull.Value);
            command.Parameters.AddWithValue("@RequestIp", (object?)log.RequestIp ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppUrl", (object?)log.AppUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatedAt", log.CreatedAt);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<ClientAppLicenseValidationLog?> GetLatestLocalValidationLogAsync()
        {
            await using var connection = new SqlConnection(_localConnectionString);
            await connection.OpenAsync();

            const string sql = @"
SELECT TOP 1
    Id,
    ClientCode,
    LicenseKey,
    ValidatedAt,
    ServerMacID,
    HardDiskNumber,
    MotherboardNumber,
    IsMatch,
    IsExpired,
    IsRemoteReachable,
    Result,
    FailureReason,
    RequestIp,
    CreatedAt
FROM dbo.ClientAppLicenseValidationLog
ORDER BY ValidatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new ClientAppLicenseValidationLog
            {
                Id = reader.GetInt64(0),
                ClientCode = reader.IsDBNull(1) ? null : reader.GetString(1),
                LicenseKey = reader.IsDBNull(2) ? null : reader.GetString(2),
                ValidatedAt = reader.GetDateTime(3),
                ServerMacID = reader.IsDBNull(4) ? null : reader.GetString(4),
                HardDiskNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                MotherboardNumber = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsMatch = !reader.IsDBNull(7) && reader.GetBoolean(7),
                IsExpired = !reader.IsDBNull(8) && reader.GetBoolean(8),
                IsRemoteReachable = !reader.IsDBNull(9) && reader.GetBoolean(9),
                Result = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                FailureReason = reader.IsDBNull(11) ? null : reader.GetString(11),
                RequestIp = reader.IsDBNull(12) ? null : reader.GetString(12),
                CreatedAt = reader.IsDBNull(13) ? DateTime.Now : reader.GetDateTime(13)
            };
        }

        private LicenseMachineFingerprint GetCurrentMachineFingerprint()
        {
            // Hardware does not change while the application is running.
            // Computing the fingerprint requires spawning external processes (diskutil,
            // ioreg, powershell, etc.) which is expensive. Cache the result for the
            // lifetime of the application process so every request does not pay that cost.
            if (_cachedMachineFingerprint != null)
            {
                return _cachedMachineFingerprint;
            }

            FingerprintLock.Wait();
            try
            {
                if (_cachedMachineFingerprint != null)
                {
                    return _cachedMachineFingerprint;
                }

                LicenseMachineFingerprint fingerprint;

                if (OperatingSystem.IsWindows())
                {
                    fingerprint = GetWindowsMachineFingerprint();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    fingerprint = GetMacMachineFingerprint();
                }
                else if (OperatingSystem.IsLinux())
                {
                    fingerprint = GetLinuxMachineFingerprint();
                }
                else
                {
                    fingerprint = new LicenseMachineFingerprint();
                }

                fingerprint.ServerMacID = NormalizeHardwareValue(string.IsNullOrWhiteSpace(fingerprint.ServerMacID) ? GetPrimaryMacAddress() : fingerprint.ServerMacID);
                fingerprint.HardDiskNumber = NormalizeHardwareValue(fingerprint.HardDiskNumber);
                fingerprint.MotherboardNumber = NormalizeHardwareValue(fingerprint.MotherboardNumber);
                fingerprint.CapturedAt = DateTime.Now;

                _cachedMachineFingerprint = fingerprint;
                return fingerprint;
            }
            finally
            {
                FingerprintLock.Release();
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static LicenseMachineFingerprint GetWindowsMachineFingerprint()
        {
            // Hard disk serial — try multiple approaches to handle restricted IIS app pool identities
            var hardDiskNumber = ExecuteProcess("powershell.exe",
                "-NoProfile -NonInteractive -Command \"try { (Get-CimInstance Win32_DiskDrive | Where-Object { $_.SerialNumber } | Select-Object -First 1 -ExpandProperty SerialNumber).Trim() } catch {}\"");

            if (string.IsNullOrWhiteSpace(hardDiskNumber))
            {
                hardDiskNumber = ExecuteProcess("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"try { (Get-CimInstance Win32_PhysicalMedia | Where-Object { $_.SerialNumber } | Select-Object -First 1 -ExpandProperty SerialNumber).Trim() } catch {}\"");
            }

            if (string.IsNullOrWhiteSpace(hardDiskNumber))
            {
                hardDiskNumber = GetWindowsHardwareIdentifier(
                    "powershell.exe",
                    "-NoProfile -Command \"(Get-CimInstance Win32_PhysicalMedia | Where-Object { $_.SerialNumber } | Select-Object -First 1 -ExpandProperty SerialNumber)\"",
                    "wmic",
                    "diskdrive get serialnumber");
            }

            // Fallback: use Windows Machine GUID which is always readable by any IIS process
            if (string.IsNullOrWhiteSpace(hardDiskNumber))
            {
                hardDiskNumber = GetWindowsMachineGuid();
            }

            // Motherboard serial — try multiple approaches
            var motherboardNumber = ExecuteProcess("powershell.exe",
                "-NoProfile -NonInteractive -Command \"try { (Get-CimInstance Win32_BaseBoard | Select-Object -First 1 -ExpandProperty SerialNumber).Trim() } catch {}\"");

            if (string.IsNullOrWhiteSpace(motherboardNumber))
            {
                motherboardNumber = GetWindowsHardwareIdentifier(
                    "powershell.exe",
                    "-NoProfile -Command \"(Get-CimInstance Win32_BaseBoard | Select-Object -First 1 -ExpandProperty SerialNumber)\"",
                    "wmic",
                    "baseboard get serialnumber");
            }

            // Fallback: Machine GUID (unique per Windows install, always readable)
            if (string.IsNullOrWhiteSpace(motherboardNumber))
            {
                motherboardNumber = GetWindowsMachineGuid();
            }

            return new LicenseMachineFingerprint
            {
                ServerMacID = GetPrimaryMacAddress(),
                HardDiskNumber = hardDiskNumber,
                MotherboardNumber = motherboardNumber
            };
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string GetWindowsMachineGuid()
        {
            try
            {
                var output = ExecuteProcessRaw("reg",
                    @"query ""HKLM\SOFTWARE\Microsoft\Cryptography"" /v MachineGuid");
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    var regSzIdx = trimmed.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
                    if (regSzIdx >= 0)
                    {
                        var guid = trimmed.Substring(regSzIdx + "REG_SZ".Length).Trim().Trim('{', '}');
                        if (!string.IsNullOrWhiteSpace(guid))
                        {
                            return guid;
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static LicenseMachineFingerprint GetMacMachineFingerprint()
        {
            var macAddress = GetMacPrimaryMacAddress();
            var diskInfo = ExecuteProcessRaw("diskutil", "info /");
            var hardDiskNumber = ExtractValueByLabel(diskInfo, "Disk / Partition UUID", "Volume UUID");
            if (string.IsNullOrWhiteSpace(hardDiskNumber))
            {
                var storageOutput = ExecuteProcessRaw("system_profiler", "SPNVMeDataType SPSerialATADataType");
                hardDiskNumber = ExtractValueByLabel(storageOutput, "Serial Number");
            }

            var platformOutput = ExecuteProcessRaw("ioreg", "-rd1 -c IOPlatformExpertDevice");
            var motherboardNumber = ExtractValueByKey(platformOutput, "IOPlatformUUID", "IOPlatformSerialNumber");
            if (string.IsNullOrWhiteSpace(motherboardNumber))
            {
                var hardwareOutput = ExecuteProcessRaw("system_profiler", "SPHardwareDataType");
                motherboardNumber = ExtractValueByLabel(hardwareOutput, "Hardware UUID", "Serial Number (system)");
            }

            return new LicenseMachineFingerprint
            {
                ServerMacID = macAddress,
                HardDiskNumber = hardDiskNumber,
                MotherboardNumber = motherboardNumber
            };
        }

        private static LicenseMachineFingerprint GetLinuxMachineFingerprint()
        {
            var macAddress = GetPrimaryMacAddress();
            var hardDiskNumber = ExecuteProcess("lsblk", "-ndo SERIAL");
            if (string.IsNullOrWhiteSpace(hardDiskNumber))
            {
                hardDiskNumber = ReadFirstNonEmptyFile("/sys/class/dmi/id/product_uuid", "/etc/machine-id");
            }

            var motherboardNumber = ReadFirstNonEmptyFile("/sys/class/dmi/id/board_serial", "/sys/class/dmi/id/product_serial", "/sys/class/dmi/id/product_uuid");

            return new LicenseMachineFingerprint
            {
                ServerMacID = macAddress,
                HardDiskNumber = hardDiskNumber,
                MotherboardNumber = motherboardNumber
            };
        }

        private static string GetPrimaryMacAddress()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                    .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .Select(nic => new
                    {
                        Address = nic.GetPhysicalAddress()?.ToString(),
                        Score = GetMacAddressPriority(nic)
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Address))
                    .OrderByDescending(item => item.Score)
                    .ToList();

                return interfaces.FirstOrDefault()?.Address ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int GetMacAddressPriority(NetworkInterface networkInterface)
        {
            var name = networkInterface.Name ?? string.Empty;

            if (string.Equals(name, "en0", StringComparison.OrdinalIgnoreCase)) return 500;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet) return 450;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return 400;
            if (name.StartsWith("eth", StringComparison.OrdinalIgnoreCase)) return 350;
            if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return 300;
            return 100;
        }

        private static string GetWindowsHardwareIdentifier(string primaryCommand, string primaryArguments, string fallbackCommand, string fallbackArguments)
        {
            var primary = ExecuteProcess(primaryCommand, primaryArguments);
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary;
            }

            return ExecuteProcess(fallbackCommand, fallbackArguments);
        }

        private static string GetMacPrimaryMacAddress()
        {
            var output = ExecuteProcessRaw("networksetup", "-listallhardwareports");
            if (string.IsNullOrWhiteSpace(output))
            {
                return GetPrimaryMacAddress();
            }

            var candidates = new List<(string Device, string Port, string Address)>();
            string currentPort = string.Empty;
            string currentDevice = string.Empty;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Hardware Port:", StringComparison.OrdinalIgnoreCase))
                {
                    currentPort = trimmed.Substring("Hardware Port:".Length).Trim();
                    continue;
                }

                if (trimmed.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                {
                    currentDevice = trimmed.Substring("Device:".Length).Trim();
                    continue;
                }

                if (trimmed.StartsWith("Ethernet Address:", StringComparison.OrdinalIgnoreCase))
                {
                    var address = trimmed.Substring("Ethernet Address:".Length).Trim();
                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        candidates.Add((currentDevice, currentPort, address));
                    }
                }
            }

            var candidate = candidates.FirstOrDefault(item => string.Equals(item.Device, "en0", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(candidate.Address))
            {
                candidate = candidates.FirstOrDefault(item => item.Port.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                    || item.Port.Contains("Ethernet", StringComparison.OrdinalIgnoreCase));
            }

            return string.IsNullOrWhiteSpace(candidate.Address)
                ? GetPrimaryMacAddress()
                : candidate.Address;
        }

        private static string ExecuteProcess(string fileName, string arguments)
        {
            return ExtractFirstValue(ExecuteProcessRaw(fileName, arguments));
        }

        private static string ExecuteProcessRaw(string fileName, string arguments)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return string.Empty;
                }

                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(10000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    return string.Empty;
                }

                Task.WaitAll(new[] { standardOutputTask, standardErrorTask }, 1000);

                var output = standardOutputTask.Result;
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = standardErrorTask.Result;
                }

                return output.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractValueByLabel(string? output, params string[] labels)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                foreach (var label in labels)
                {
                    var prefix = label + ":";
                    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring(prefix.Length).Trim().Trim('"');
                    }
                }
            }

            return string.Empty;
        }

        private static string ExtractValueByKey(string? output, params string[] keys)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                foreach (var key in keys)
                {
                    var marker = $"\"{key}\" = ";
                    var index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        return trimmed.Substring(index + marker.Length).Trim().Trim('"');
                    }
                }
            }

            return string.Empty;
        }

        private static string ReadFirstNonEmptyFile(params string[] paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return content;
                    }
                }
                catch
                {
                }
            }

            return string.Empty;
        }

        private static string ExtractFirstValue(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            return output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) &&
                                        !line.Equals("SerialNumber", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        private static string NormalizeHardwareValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNAVAILABLE";
            }

            var normalized = new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(normalized) ? "UNAVAILABLE" : normalized;
        }

        private static bool FingerprintsMatch(LicenseMachineFingerprint machine, ClientAppLicense license)
        {
            return string.Equals(machine.ServerMacID, NormalizeHardwareValue(license.ServerMacID), StringComparison.OrdinalIgnoreCase)
                && string.Equals(machine.HardDiskNumber, NormalizeHardwareValue(license.HardDiskNumber), StringComparison.OrdinalIgnoreCase)
                && string.Equals(machine.MotherboardNumber, NormalizeHardwareValue(license.MotherboardNumber), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildHardwareMismatchReason(LicenseMachineFingerprint machine, ClientAppLicense license)
        {
            var mismatches = new List<string>();

            if (!string.Equals(machine.ServerMacID, NormalizeHardwareValue(license.ServerMacID), StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add("Server MAC ID does not match");
            }

            if (!string.Equals(machine.HardDiskNumber, NormalizeHardwareValue(license.HardDiskNumber), StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add("Hard disk number does not match");
            }

            if (!string.Equals(machine.MotherboardNumber, NormalizeHardwareValue(license.MotherboardNumber), StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add("Motherboard number does not match");
            }

            return mismatches.Count == 0 ? "Current hardware does not match the registered license." : string.Join("; ", mismatches);
        }

        private static string? BuildLocalRemoteMismatchReason(ClientAppLicense localLicense, ClientAppLicense remoteLicense)
        {
            var mismatches = new List<string>();

            if (!string.Equals(localLicense.LicenseKey?.Trim(), remoteLicense.LicenseKey?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add("License key does not match the remote license record");
            }

            if (!string.Equals(localLicense.ClientName?.Trim(), remoteLicense.ClientName?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add("Client name does not match the remote license record");
            }

            // ExpiryDate and IsActive are intentionally excluded: the remote DB is the
            // authoritative source for both fields (admins extend expiry / reactivate
            // remotely). A stale local copy is normal and is always refreshed by
            // UpsertLocalLicenseAsync after every successful remote validation.

            return mismatches.Count == 0 ? null : string.Join("; ", mismatches);
        }

        private static LicenseValidationHistoryEntry CreateRemoteValidationHistory(
            ClientAppLicense license,
            LicenseMachineFingerprint machine,
            bool isValid,
            string? failureReason,
            string? requestIp,
            string? appUrl = null)
        {
            return new LicenseValidationHistoryEntry
            {
                ClientCode = license.ClientCode,
                LicenseKey = license.LicenseKey,
                IsValid = isValid,
                FailureReason = isValid ? null : failureReason,
                PublicIPAddress = requestIp,
                DeviceInfo = BuildDeviceInfo(machine),
                AppUrl = appUrl,
                CreatedAt = DateTime.Now
            };
        }

        private static string BuildDeviceInfo(LicenseMachineFingerprint machine)
        {
            var operatingSystem = OperatingSystem.IsWindows()
                ? "Windows"
                : OperatingSystem.IsMacOS()
                    ? "macOS"
                    : OperatingSystem.IsLinux()
                        ? "Linux"
                        : "Unknown";

            return $"Host={Environment.MachineName};OS={operatingSystem};MAC={machine.ServerMacID};HardDisk={machine.HardDiskNumber};Motherboard={machine.MotherboardNumber}";
        }

        private async Task<string?> ResolvePublicIpAddressAsync(string? requestIp)
        {
            if (IsPublicIpAddress(requestIp))
            {
                return requestIp;
            }

            var resolvedFromService = await GetPublicIpFromExternalServiceAsync();
            if (IsPublicIpAddress(resolvedFromService))
            {
                return resolvedFromService;
            }

            return requestIp;
        }

        private async Task<string?> GetPublicIpFromExternalServiceAsync()
        {
            var endpoints = new[]
            {
                "https://api64.ipify.org",
                "https://api.ipify.org",
                "https://icanhazip.com"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(5);

                    var response = await client.GetStringAsync(endpoint);
                    var value = response.Trim();
                    if (IsPublicIpAddress(value))
                    {
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Unable to resolve public IP from {Endpoint}", endpoint);
                }
            }

            return null;
        }

        private static bool IsPublicIpAddress(string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress.Trim(), out var parsedAddress))
            {
                return false;
            }

            if (IPAddress.IsLoopback(parsedAddress))
            {
                return false;
            }

            if (parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = parsedAddress.GetAddressBytes();
                if (bytes[0] == 10) return false;
                if (bytes[0] == 127) return false;
                if (bytes[0] == 169 && bytes[1] == 254) return false;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                return true;
            }

            if (parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (parsedAddress.IsIPv6LinkLocal || parsedAddress.IsIPv6Multicast || parsedAddress.IsIPv6SiteLocal)
                {
                    return false;
                }

                var bytes = parsedAddress.GetAddressBytes();
                if ((bytes[0] & 0xFE) == 0xFC)
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static LicenseGateStatus MapFailureReasonToStatus(string? failureReason)
        {
            if (string.IsNullOrWhiteSpace(failureReason))
            {
                return LicenseGateStatus.UnknownError;
            }

            var value = failureReason.Trim();

            if (value.Contains("software expired", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.Expired;
            if (value.Contains("hardware mismatch", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.HardwareMismatch;
            if (value.Contains("pending activation", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.PendingActivation;
            if (value.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                || value.Contains("inactive", StringComparison.OrdinalIgnoreCase)
                || value.Contains("deactivated", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.Inactive;
            if (value.Contains("does not match the remote license record", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.DataMismatch;
            if (value.Contains("unreachable", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.RemoteUnavailable;
            if (value.Contains("not found", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.RemoteNotFound;
            if (value.Contains("configuration missing", StringComparison.OrdinalIgnoreCase)) return LicenseGateStatus.ConfigurationMissing;

            return LicenseGateStatus.UnknownError;
        }

        private static ClientAppLicenseValidationLog CreateValidationLog(
            ClientAppLicense license,
            LicenseMachineFingerprint machine,
            string result,
            bool isMatch,
            bool isExpired,
            bool isRemoteReachable,
            string? failureReason,
            string? requestIp,
            string? appUrl = null)
        {
            return new ClientAppLicenseValidationLog
            {
                ClientCode = license.ClientCode,
                LicenseKey = license.LicenseKey,
                ValidatedAt = DateTime.Now,
                ServerMacID = machine.ServerMacID,
                HardDiskNumber = machine.HardDiskNumber,
                MotherboardNumber = machine.MotherboardNumber,
                IsMatch = isMatch,
                IsExpired = isExpired,
                IsRemoteReachable = isRemoteReachable,
                Result = result,
                FailureReason = failureReason,
                RequestIp = requestIp,
                AppUrl = appUrl,
                CreatedAt = DateTime.Now
            };
        }

        private void AddLocalLicenseParameters(SqlCommand command, ClientAppLicense license)
        {
            command.Parameters.AddWithValue("@ClientCode", license.ClientCode);
            command.Parameters.AddWithValue("@ClientName", license.ClientName);
            command.Parameters.AddWithValue("@ContactNumber", (object?)license.ContactNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@EmailID", (object?)license.EmailID ?? DBNull.Value);
            command.Parameters.AddWithValue("@LicenseKey", license.LicenseKey);
            command.Parameters.AddWithValue("@HardDiskNumber", EncryptLocalHardwareValue(license.HardDiskNumber));
            command.Parameters.AddWithValue("@ServerMacID", EncryptLocalHardwareValue(license.ServerMacID));
            command.Parameters.AddWithValue("@MotherboardNumber", EncryptLocalHardwareValue(license.MotherboardNumber));
            command.Parameters.AddWithValue("@PublicIPAddress", (object?)license.PublicIPAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@StartDate", license.StartDate);
            command.Parameters.AddWithValue("@ExpiryDate", license.ExpiryDate);
            command.Parameters.AddWithValue("@LastLoginDate", (object?)license.LastLoginDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsActive", license.IsActive);
            command.Parameters.AddWithValue("@CreatedAt", license.CreatedAt);
            command.Parameters.AddWithValue("@OTP_Verified", license.OTP_Verified);
            command.Parameters.AddWithValue("@AMC_Expireddate", (object?)license.AMC_Expireddate ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppUrl", (object?)license.AppUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("@ProductType", (object?)license.ProductType ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsDisplayAlerts", license.IsDisplayAlerts);
            command.Parameters.AddWithValue("@AlertStartdate", (object?)license.AlertStartDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertStartTime", (object?)license.AlertStartTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertEnddate", (object?)license.AlertEndDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertEndTime", (object?)license.AlertEndTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertMessage", (object?)license.AlertMessage ?? DBNull.Value);
        }

        private static void AddRemoteLicenseParameters(SqlCommand command, ClientAppLicense license)
        {
            command.Parameters.AddWithValue("@ClientCode", license.ClientCode);
            command.Parameters.AddWithValue("@ClientName", license.ClientName);
            command.Parameters.AddWithValue("@ContactNumber", (object?)license.ContactNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@EmailID", (object?)license.EmailID ?? DBNull.Value);
            command.Parameters.AddWithValue("@LicenseKey", license.LicenseKey);
            command.Parameters.AddWithValue("@HardDiskNumber", license.HardDiskNumber);
            command.Parameters.AddWithValue("@ServerMacID", license.ServerMacID);
            command.Parameters.AddWithValue("@MotherboardNumber", license.MotherboardNumber);
            command.Parameters.AddWithValue("@PublicIPAddress", (object?)license.PublicIPAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@StartDate", license.StartDate);
            command.Parameters.AddWithValue("@ExpiryDate", license.ExpiryDate);
            command.Parameters.AddWithValue("@LastLoginDate", (object?)license.LastLoginDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsActive", license.IsActive);
            command.Parameters.AddWithValue("@CreatedAt", license.CreatedAt);
            command.Parameters.AddWithValue("@OTP_Verified", license.OTP_Verified);
            command.Parameters.AddWithValue("@AMC_Expireddate", (object?)license.AMC_Expireddate ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppUrl", (object?)license.AppUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("@ProductType", (object?)license.ProductType ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsDisplayAlerts", license.IsDisplayAlerts);
            command.Parameters.AddWithValue("@AlertStartdate", (object?)license.AlertStartDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertStartTime", (object?)license.AlertStartTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertEnddate", (object?)license.AlertEndDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertEndTime", (object?)license.AlertEndTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@AlertMessage", (object?)license.AlertMessage ?? DBNull.Value);
        }

        private ClientAppLicense MapLocalLicense(SqlDataReader reader)
        {
            var license = MapLicense(reader);
            license.HardDiskNumber = DecryptLocalHardwareValue(license.HardDiskNumber);
            license.ServerMacID = DecryptLocalHardwareValue(license.ServerMacID);
            license.MotherboardNumber = DecryptLocalHardwareValue(license.MotherboardNumber);
            return license;
        }

        private static ClientAppLicense MapLicense(SqlDataReader reader)
        {
            return new ClientAppLicense
            {
                Id = reader.GetInt64(0),
                ClientCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                ClientName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ContactNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                EmailID = reader.IsDBNull(4) ? null : reader.GetString(4),
                LicenseKey = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                HardDiskNumber = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                ServerMacID = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                MotherboardNumber = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                PublicIPAddress = reader.IsDBNull(9) ? null : reader.GetString(9),
                StartDate = reader.IsDBNull(10) ? DateTime.Now : reader.GetDateTime(10),
                ExpiryDate = reader.IsDBNull(11) ? DateTime.Now : reader.GetDateTime(11),
                LastLoginDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                IsActive = !reader.IsDBNull(13) && reader.GetBoolean(13),
                CreatedAt = reader.IsDBNull(14) ? DateTime.Now : reader.GetDateTime(14),
                OTP_Verified = !reader.IsDBNull(15) && reader.GetBoolean(15),
                AMC_Expireddate = reader.IsDBNull(16) ? null : reader.GetDateTime(16),
                AppUrl = reader.IsDBNull(17) ? null : reader.GetString(17),
                ProductType = reader.IsDBNull(18) ? null : reader.GetString(18),
                IsDisplayAlerts = reader.FieldCount > 19 && !reader.IsDBNull(19) && reader.GetBoolean(19),
                AlertStartDate = reader.FieldCount > 20 && !reader.IsDBNull(20) ? reader.GetDateTime(20) : null,
                AlertStartTime = reader.FieldCount > 21 && !reader.IsDBNull(21) ? reader.GetTimeSpan(21) : null,
                AlertEndDate = reader.FieldCount > 22 && !reader.IsDBNull(22) ? reader.GetDateTime(22) : null,
                AlertEndTime = reader.FieldCount > 23 && !reader.IsDBNull(23) ? reader.GetTimeSpan(23) : null,
                AlertMessage = reader.FieldCount > 24 && !reader.IsDBNull(24) ? reader.GetString(24) : null
            };
        }

        private LicenseGateResult CreateGateResult(LicenseGateStatus status, ClientAppLicense? license, string title, string message, string? failureReason = null)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                (title, message) = GetStatusPresentation(status, license);
            }

            return new LicenseGateResult
            {
                Status = status,
                Title = title,
                Message = message,
                FailureReason = failureReason,
                ClientCode = license?.ClientCode,
                LicenseKey = license?.LicenseKey,
                ExpiryDate = license?.ExpiryDate,
                EvaluatedAt = DateTime.Now
            };
        }

        private static (string Title, string Message) GetStatusPresentation(LicenseGateStatus status, ClientAppLicense? license)
        {
            return status switch
            {
                LicenseGateStatus.Valid => ("License Valid", "License validation succeeded. Access to the login page is allowed."),
                LicenseGateStatus.Unregistered => ("License Registration Required", "This application has not been registered yet. Complete the license registration before login."),
                LicenseGateStatus.PendingActivation => ("License Pending Activation", "License pending activation unable to open"),
                LicenseGateStatus.Inactive => ("Client Deactivated", "Client Deactivated for activate contact vendor 8617280732"),
                LicenseGateStatus.Expired => ("Software Expired", "Software Expired Contact with Vendor"),
                LicenseGateStatus.HardwareMismatch => ("Hardware Mismatch", "hardware Mismatch unable to open"),
                LicenseGateStatus.DataMismatch => ("License Data Mismatch", "License data mismatch unable to open"),
                LicenseGateStatus.RemoteUnavailable => ("License Server Unreachable", "License server unreachable unable to open"),
                LicenseGateStatus.RemoteNotFound => ("License Not Found", "License record not found on vendor server"),
                LicenseGateStatus.ConfigurationMissing => ("License Configuration Missing", "License configuration missing unable to open"),
                _ => ("License Validation Failed", "License validation failed unable to open")
            };
        }

        private string EncryptLocalHardwareValue(string value)
        {
            return _urlEncryptionService.EncryptParameters(new Dictionary<string, string>
            {
                ["value"] = NormalizeHardwareValue(value)
            });
        }

        private string DecryptLocalHardwareValue(string encryptedValue)
        {
            if (string.IsNullOrWhiteSpace(encryptedValue))
            {
                return NormalizeHardwareValue(encryptedValue);
            }

            try
            {
                var payload = _urlEncryptionService.DecryptParameters(encryptedValue);
                if (payload.TryGetValue("value", out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return NormalizeHardwareValue(value);
                }
            }
            catch
            {
            }

            return NormalizeHardwareValue(encryptedValue);
        }

        private async Task<bool> TableExistsAsync(SqlConnection connection, SqlTransaction? transaction, string tableName)
        {
            const string sql = "SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END;";
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@TableName", tableName);
            var result = await command.ExecuteScalarAsync();
            return result != null && result != DBNull.Value && Convert.ToInt32(result) == 1;
        }

        private static string? ValidateRegistrationRequest(LicenseRegistrationViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.ClientName))
            {
                return "Client name is required.";
            }

            if (string.IsNullOrWhiteSpace(model.ContactNumber))
            {
                return "Contact number is required.";
            }

            if (string.IsNullOrWhiteSpace(model.EmailID))
            {
                return "Client email ID is required.";
            }

            if (model.ExpiryDate.Date < DateTime.Today)
            {
                return "End date cannot be earlier than today.";
            }

            return null;
        }

        private static LicenseRegistrationViewModel CloneRegistrationModel(LicenseRegistrationViewModel model)
        {
            return new LicenseRegistrationViewModel
            {
                ClientName = model.ClientName.Trim(),
                ContactNumber = model.ContactNumber.Trim(),
                EmailID = NormalizeEmailAddress(model.EmailID),
                ClientCodePreview = model.ClientCodePreview,
                LicenseKeyPreview = model.LicenseKeyPreview,
                ServerMacID = model.ServerMacID,
                HardDiskNumber = model.HardDiskNumber,
                MotherboardNumber = model.MotherboardNumber,
                StartDate = model.StartDate,
                ExpiryDate = model.ExpiryDate,
                AmcExpiryDate = model.AmcExpiryDate,
                ProductType = model.ProductType
            };
        }

        private static string? NormalizeEmailAddress(string? emailAddress)
        {
            return string.IsNullOrWhiteSpace(emailAddress) ? null : emailAddress.Trim();
        }

        private static string GenerateOtpCode()
        {
            return RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, RegistrationOtpLength)).ToString($"D{RegistrationOtpLength}");
        }

        private static string? NormalizeOtpCode(string? otpCode)
        {
            if (string.IsNullOrWhiteSpace(otpCode))
            {
                return null;
            }

            var normalized = new string(otpCode.Where(char.IsDigit).ToArray());
            return normalized.Length == RegistrationOtpLength ? normalized : null;
        }

        private static bool OtpCodesMatch(string expectedOtp, string providedOtp)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expectedOtp);
            var providedBytes = Encoding.UTF8.GetBytes(providedOtp);

            return expectedBytes.Length == providedBytes.Length
                && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }

        private static string ComputeOtpHash(string otpCode)
        {
            var otpBytes = Encoding.UTF8.GetBytes(otpCode);
            var hash = SHA256.HashData(otpBytes);
            return Convert.ToHexString(hash);
        }

        private string ResolveMailPassword(string encryptedOrPlainPassword)
        {
            if (string.IsNullOrWhiteSpace(encryptedOrPlainPassword))
            {
                return string.Empty;
            }

            try
            {
                var encryptionKey = Convert.FromBase64String(_configuration["Encryption:Key"] ?? string.Empty);
                var encryptionIV = Convert.FromBase64String(_configuration["Encryption:IV"] ?? string.Empty);

                using var aes = Aes.Create();
                aes.Key = encryptionKey;
                aes.IV = encryptionIV;

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                var encryptedBytes = Convert.FromBase64String(encryptedOrPlainPassword);

                using var ms = new MemoryStream(encryptedBytes);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch
            {
                return encryptedOrPlainPassword;
            }
        }

        private static string BuildRegistrationOtpEmailBody(LicenseRegistrationViewModel model, string otpCode, DateTime expiresAt)
        {
            var clientName = WebUtility.HtmlEncode(model.ClientName.Trim());
            var emailAddress = WebUtility.HtmlEncode(model.EmailID?.Trim() ?? string.Empty);

            return $$"""
<div style="font-family:Segoe UI,Arial,sans-serif;background:#f5f7fb;padding:24px;color:#111827;">
    <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;overflow:hidden;box-shadow:0 18px 40px rgba(15,23,42,0.08);">
        <div style="padding:24px 28px;background:linear-gradient(135deg,#111827 0%,#991b1b 58%,#ea580c 100%);color:#ffffff;">
            <div style="font-size:14px;letter-spacing:0.14em;text-transform:uppercase;opacity:0.9;">eRestoPOS Licensing</div>
            <h2 style="margin:10px 0 6px;font-size:24px;line-height:1.2;">Registration OTP</h2>
            <p style="margin:0;font-size:14px;line-height:1.6;opacity:0.9;">Use the OTP below to complete license registration for {{clientName}}.</p>
        </div>
        <div style="padding:28px;">
            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#374151;">An OTP was requested for the approved email address {{emailAddress}}. This code is valid for 60 seconds only.</p>
            <div style="margin:0 0 18px;padding:18px 20px;border-radius:14px;border:1px solid #fed7aa;background:#fff7ed;text-align:center;">
                <div style="font-size:12px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:#c2410c;margin-bottom:8px;">One Time Password</div>
                <div style="font-size:34px;font-weight:800;letter-spacing:0.32em;color:#7c2d12;">{{WebUtility.HtmlEncode(otpCode)}}</div>
            </div>
            <p style="margin:0 0 8px;font-size:14px;color:#4b5563;">Expiry time: <strong>{{expiresAt:dd-MMM-yyyy HH:mm:ss}}</strong></p>
            <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">If you did not request this OTP, you can ignore this email. No license will be registered unless the correct OTP is entered on the registration page.</p>
        </div>
    </div>
</div>
""";
        }

        private static string NormalizeSmtpServer(string smtpServer)
        {
            if (string.IsNullOrWhiteSpace(smtpServer))
            {
                return smtpServer;
            }

            if (!smtpServer.StartsWith("smtp.", StringComparison.OrdinalIgnoreCase)
                && !smtpServer.StartsWith("mail.", StringComparison.OrdinalIgnoreCase))
            {
                if (smtpServer.Contains("gmail.com", StringComparison.OrdinalIgnoreCase))
                {
                    return "smtp.gmail.com";
                }

                if (smtpServer.Contains("outlook.com", StringComparison.OrdinalIgnoreCase)
                    || smtpServer.Contains("hotmail.com", StringComparison.OrdinalIgnoreCase))
                {
                    return "smtp.office365.com";
                }
            }

            return smtpServer;
        }

        private ISession? GetSession()
        {
            return _httpContextAccessor.HttpContext?.Session;
        }

        private static void SavePendingRegistrationOtp(ISession session, PendingLicenseRegistrationOtp pendingRegistration)
        {
            session.SetString(RegistrationOtpSessionKey, JsonSerializer.Serialize(pendingRegistration));
        }

        private PendingLicenseRegistrationOtp? GetPendingRegistrationOtp(ISession session)
        {
            var payload = session.GetString(RegistrationOtpSessionKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PendingLicenseRegistrationOtp>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to deserialize pending registration OTP state.");
                ClearPendingRegistrationOtp(session);
                return null;
            }
        }

        private static void ClearPendingRegistrationOtp(ISession session)
        {
            session.Remove(RegistrationOtpSessionKey);
        }

        private static string MaskEmailAddress(string? emailAddress)
        {
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                return string.Empty;
            }

            var parts = emailAddress.Split('@', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return emailAddress;
            }

            var localPart = parts[0];
            var domainPart = parts[1];
            if (localPart.Length <= 2)
            {
                return $"{localPart[0]}***@{domainPart}";
            }

            return $"{localPart[..2]}***@{domainPart}";
        }

        private bool TryGetCentralLicenseConnection(out (string RemoteServer, string RemoteUsername, string RemotePassword, string RemoteDatabase) connection, out string errorMessage)
        {
            var remoteServer = GetConfiguredValue("CentralLicensing:RemoteServer", "CENTRAL_LICENSE_REMOTE_SERVER")
                ?? DefaultCentralLicenseRemoteServer;
            var remoteUsername = GetConfiguredValue("CentralLicensing:RemoteUsername", "CENTRAL_LICENSE_REMOTE_USERNAME")
                ?? DefaultCentralLicenseRemoteUsername;
            var remotePassword = GetConfiguredValue("CentralLicensing:RemotePassword", "CENTRAL_LICENSE_REMOTE_PASSWORD")
                ?? DefaultCentralLicenseRemotePassword;
            var remoteDatabase = GetConfiguredValue("CentralLicensing:RemoteDatabase", "CENTRAL_LICENSE_REMOTE_DATABASE")
                ?? DefaultCentralLicenseRemoteDatabase;

            if (string.IsNullOrWhiteSpace(remoteServer)
                || string.IsNullOrWhiteSpace(remoteUsername)
                || string.IsNullOrWhiteSpace(remotePassword)
                || string.IsNullOrWhiteSpace(remoteDatabase))
            {
                connection = default;
                errorMessage = "Central licensing server configuration is unavailable.";
                return false;
            }

            connection = (
                remoteServer.Trim(),
                remoteUsername.Trim(),
                remotePassword,
                remoteDatabase.Trim());
            errorMessage = string.Empty;
            return true;
        }

        private string? GetConfiguredValue(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = _configuration[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private string BuildConnectionString(string server, string database, string username, string password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = username,
                Password = password,
                Encrypt = false,
                TrustServerCertificate = true,
                ConnectTimeout = _remoteConnectionTimeoutSeconds,
                MultipleActiveResultSets = false,
                ConnectRetryCount = 3,
                ConnectRetryInterval = 3
            };

            return builder.ConnectionString;
        }

        private string NormalizeDatabaseName(string? databaseName)
        {
            return string.IsNullOrWhiteSpace(databaseName) ? string.Empty : databaseName.Trim();
        }

        private static string QuoteSqlIdentifier(string identifier)
        {
            return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
        }

        private static string EscapeSqlStringLiteral(string value)
        {
            return value.Replace("'", "''", StringComparison.Ordinal);
        }

        private static string GetFinancialYearCode(DateTime now)
        {
            var fyStart = now.Month >= 4 ? now.Year : now.Year - 1;
            return $"{fyStart % 100:D2}{(fyStart + 1) % 100:D2}";
        }

        private sealed class PendingLicenseRegistrationOtp
        {
            public Guid ChallengeId { get; set; }

            public LicenseRegistrationViewModel Model { get; set; } = new();

            public string OtpCode { get; set; } = string.Empty;

            public DateTime GeneratedAt { get; set; }

            public DateTime ExpiresAt { get; set; }

            public bool IsVerified { get; set; }

            public DateTime? VerifiedAt { get; set; }

            public string? RequestIp { get; set; }

            public int FailedAttempts { get; set; }
        }

        private sealed class PendingHardwareRenewalOtp
        {
            public Guid ChallengeId { get; set; }

            public string LicenseKey { get; set; } = string.Empty;

            public string ClientCode { get; set; } = string.Empty;

            public string OtpCode { get; set; } = string.Empty;

            public DateTime GeneratedAt { get; set; }

            public DateTime ExpiresAt { get; set; }

            public bool IsVerified { get; set; }

            public DateTime? VerifiedAt { get; set; }

            public string? RequestIp { get; set; }

            public int FailedAttempts { get; set; }
        }

        // ── Hardware Renewal OTP session helpers ──────────────────────────────────────

        private static void SavePendingHardwareRenewalOtp(ISession session, PendingHardwareRenewalOtp pending)
        {
            session.SetString(HardwareRenewalOtpSessionKey, JsonSerializer.Serialize(pending));
        }

        private PendingHardwareRenewalOtp? GetPendingHardwareRenewalOtp(ISession session)
        {
            var payload = session.GetString(HardwareRenewalOtpSessionKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PendingHardwareRenewalOtp>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to deserialize pending hardware renewal OTP state.");
                ClearPendingHardwareRenewalOtp(session);
                return null;
            }
        }

        private static void ClearPendingHardwareRenewalOtp(ISession session)
        {
            session.Remove(HardwareRenewalOtpSessionKey);
        }

        // ── Remote DB helpers for hardware renewal ────────────────────────────────────

        private async Task<ClientAppLicense?> GetRemoteLicenseByKeyAsync(SqlConnection connection, SqlTransaction? transaction, string licenseKey)
        {
            const string sql = @"
SELECT TOP 1
    Id,
    ClientCode,
    ClientName,
    ContactNumber,
    EmailID,
    LicenseKey,
    HardDiskNumber,
    ServerMacID,
    MotherboardNumber,
    PublicIPAddress,
    StartDate,
    ExpiryDate,
    LastLoginDate,
    IsActive,
    CreatedAt,
    OTP_Verified,
    AMC_Expireddate,
    AppUrl,
    ProductType,
    ISNULL(IsDisplayAlerts, 0) AS IsDisplayAlerts,
    AlertStartdate,
    AlertStartTime,
    AlertEnddate,
    AlertEndTime,
    AlertMessage
FROM dbo.ClientAppLicense
WHERE LicenseKey = @LicenseKey
ORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@LicenseKey", licenseKey);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            return await reader.ReadAsync() ? MapLicense(reader) : null;
        }

        private async Task UpdateRemoteHardwareAsync(SqlConnection connection, SqlTransaction? transaction, string licenseKey, string clientCode, LicenseMachineFingerprint machine)
        {
            const string sql = @"
UPDATE dbo.ClientAppLicense
SET HardDiskNumber = @HardDiskNumber,
    ServerMacID    = @ServerMacID,
    MotherboardNumber = @MotherboardNumber
WHERE LicenseKey = @LicenseKey
  AND ClientCode = @ClientCode;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@HardDiskNumber", machine.HardDiskNumber);
            command.Parameters.AddWithValue("@ServerMacID", machine.ServerMacID);
            command.Parameters.AddWithValue("@MotherboardNumber", machine.MotherboardNumber);
            command.Parameters.AddWithValue("@LicenseKey", licenseKey);
            command.Parameters.AddWithValue("@ClientCode", clientCode);
            await command.ExecuteNonQueryAsync();
        }

        private async Task InsertHardwareRenewalOtpHistoryAsync(SqlConnection connection, SqlTransaction? transaction, PendingHardwareRenewalOtp pending, string otpCodeHash, ClientAppLicense remoteLicense)
        {
            var sql = $@"
INSERT INTO {RemoteOtpValidationHistoryTableName}
(
    ChallengeId,
    ClientName,
    ContactNumber,
    EmailID,
    OTPCodeHash,
    ClientCode,
    LicenseKey,
    IsValidated,
    GeneratedAt,
    ExpiresAt,
    ValidatedAt,
    RequestIp,
    FailureReason,
    CreatedAt
)
VALUES
(
    @ChallengeId,
    @ClientName,
    @ContactNumber,
    @EmailID,
    @OTPCodeHash,
    @ClientCode,
    @LicenseKey,
    @IsValidated,
    @GeneratedAt,
    @ExpiresAt,
    @ValidatedAt,
    @RequestIp,
    @FailureReason,
    @CreatedAt
);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ChallengeId", pending.ChallengeId);
            command.Parameters.AddWithValue("@ClientName", remoteLicense.ClientName ?? string.Empty);
            command.Parameters.AddWithValue("@ContactNumber", (object?)remoteLicense.ContactNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@EmailID", (object?)remoteLicense.EmailID ?? DBNull.Value);
            command.Parameters.AddWithValue("@OTPCodeHash", otpCodeHash);
            command.Parameters.AddWithValue("@ClientCode", pending.ClientCode);
            command.Parameters.AddWithValue("@LicenseKey", pending.LicenseKey);
            command.Parameters.AddWithValue("@IsValidated", pending.IsVerified);
            command.Parameters.AddWithValue("@GeneratedAt", pending.GeneratedAt);
            command.Parameters.AddWithValue("@ExpiresAt", pending.ExpiresAt);
            command.Parameters.AddWithValue("@ValidatedAt", DBNull.Value);
            command.Parameters.AddWithValue("@RequestIp", (object?)pending.RequestIp ?? DBNull.Value);
            command.Parameters.AddWithValue("@FailureReason", DBNull.Value);
            command.Parameters.AddWithValue("@CreatedAt", pending.GeneratedAt);
            await command.ExecuteNonQueryAsync();
        }

        private async Task UpdateHardwareRenewalOtpHistoryAsync(SqlConnection connection, SqlTransaction? transaction, Guid challengeId, bool isValidated, DateTime? validatedAt, string? failureReason)
        {
            var sql = $@"
UPDATE {RemoteOtpValidationHistoryTableName}
SET IsValidated   = @IsValidated,
    ValidatedAt   = @ValidatedAt,
    FailureReason = @FailureReason
WHERE ChallengeId = @ChallengeId;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@ChallengeId", challengeId);
            command.Parameters.AddWithValue("@IsValidated", isValidated);
            command.Parameters.AddWithValue("@ValidatedAt", (object?)validatedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@FailureReason", (object?)failureReason ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<(bool Success, string Message)> SendHardwareRenewalOtpEmailAsync(CentralMailConfiguration configuration, PendingHardwareRenewalOtp pending, ClientAppLicense remoteLicense, string toEmail, string otpCode, DateTime expiresAt)
        {
            try
            {
                var smtpServer = NormalizeSmtpServer(configuration.SmtpServer);

                using var client = new SmtpClient(smtpServer, configuration.SmtpPort)
                {
                    EnableSsl = configuration.EnableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(configuration.SmtpUsername, configuration.SmtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(configuration.FromEmail, configuration.FromName),
                    Subject = $"eRestoPOS Hardware Renewal OTP - {otpCode}",
                    Body = BuildHardwareRenewalOtpEmailBody(remoteLicense, otpCode, expiresAt),
                    IsBodyHtml = true,
                    Priority = MailPriority.High
                };

                message.To.Add(toEmail.Trim());
                await client.SendMailAsync(message);
                return (true, "OTP sent successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send hardware renewal OTP email to {ToEmail}", toEmail);
                return (false, $"Unable to send OTP email. {ex.Message}");
            }
        }

        private static string BuildHardwareRenewalOtpEmailBody(ClientAppLicense license, string otpCode, DateTime expiresAt)
        {
            var clientName = WebUtility.HtmlEncode(license.ClientName ?? string.Empty);
            var clientCode = WebUtility.HtmlEncode(license.ClientCode ?? string.Empty);

            return $$"""
<div style="font-family:Segoe UI,Arial,sans-serif;background:#f5f7fb;padding:24px;color:#111827;">
    <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;overflow:hidden;box-shadow:0 18px 40px rgba(15,23,42,0.08);">
        <div style="padding:24px 28px;background:linear-gradient(135deg,#111827 0%,#991b1b 58%,#ea580c 100%);color:#ffffff;">
            <div style="font-size:14px;letter-spacing:0.14em;text-transform:uppercase;opacity:0.9;">eRestoPOS Licensing</div>
            <h2 style="margin:10px 0 6px;font-size:24px;line-height:1.2;">Hardware Renewal OTP</h2>
            <p style="margin:0;font-size:14px;line-height:1.6;opacity:0.9;">Use the OTP below to authorize hardware re-association for client <strong>{{WebUtility.HtmlEncode(clientName)}}</strong> ({{WebUtility.HtmlEncode(clientCode)}}).</p>
        </div>
        <div style="padding:28px;">
            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#374151;">An OTP was requested to update the server hardware identifiers for client {{WebUtility.HtmlEncode(clientName)}}. This code is valid for {{RegistrationOtpLifetimeSeconds}} seconds only.</p>
            <div style="margin:0 0 18px;padding:18px 20px;border-radius:14px;border:1px solid #fed7aa;background:#fff7ed;text-align:center;">
                <div style="font-size:12px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:#c2410c;margin-bottom:8px;">One Time Password</div>
                <div style="font-size:34px;font-weight:800;letter-spacing:0.32em;color:#7c2d12;">{{WebUtility.HtmlEncode(otpCode)}}</div>
            </div>
            <p style="margin:0 0 8px;font-size:14px;color:#4b5563;">Expiry time: <strong>{{expiresAt:dd-MMM-yyyy HH:mm:ss}}</strong></p>
            <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">If you did not request this OTP, ignore this email. No hardware change will occur unless the correct OTP is entered.</p>
        </div>
    </div>
</div>
""";
        }

        private sealed class CentralMailConfiguration
        {
            public long Id { get; set; }

            public string SmtpServer { get; set; } = string.Empty;

            public int SmtpPort { get; set; }

            public string SmtpUsername { get; set; } = string.Empty;

            public string SmtpPassword { get; set; } = string.Empty;

            public bool EnableSsl { get; set; }

            public string FromEmail { get; set; } = string.Empty;

            public string FromName { get; set; } = string.Empty;

            public bool IsActive { get; set; }
        }
    }
}