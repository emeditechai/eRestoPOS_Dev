namespace RestaurantManagementSystem.Services
{
    public interface ILicensingService
    {
        Task<LicenseRegistrationViewModel> BuildRegistrationViewModelAsync(LicenseRegistrationViewModel? source = null);

        Task<(bool Success, string Message, int ExpiresInSeconds, string? TargetEmail)> SendRegistrationOtpAsync(LicenseRegistrationViewModel model, string? requestIp = null);

        Task<(bool Success, string Message, ClientAppLicense? License)> VerifyRegistrationOtpAsync(string otpCode, string? requestIp = null);

        Task<(bool Success, string Message, ClientAppLicense? License)> RegisterClientAsync(LicenseRegistrationViewModel model, string? requestIp = null);

        Task<LicenseGateResult> EvaluateAccessAsync(bool forceRemoteValidation = false, string? requestIp = null);

        Task<LicenseBlockedViewModel> BuildBlockedViewModelAsync(LicenseGateStatus? statusOverride = null);

        Task ClearLocalLicenseAsync();

        Task<(bool Success, string Message, int ExpiresInSeconds)> SendHardwareRenewalOtpAsync(string licenseKey, string? requestIp = null);

        Task<(bool Success, string Message)> VerifyHardwareRenewalOtpAsync(string otpCode, string? requestIp = null);
    }
}