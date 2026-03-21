namespace RestaurantManagementSystem.Controllers
{
    [AllowAnonymous]
    public class LicenseController : Controller
    {
        private readonly ILicensingService _licensingService;

        public LicenseController(ILicensingService licensingService)
        {
            _licensingService = licensingService;
        }

        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var gateResult = await _licensingService.EvaluateAccessAsync(false, ResolveRequestIp());
            if (gateResult.IsAllowed)
            {
                return RedirectToPostLicenseDestination();
            }

            if (gateResult.Status != LicenseGateStatus.Unregistered)
            {
                return RedirectToAction(nameof(Blocked), new { status = gateResult.Status.ToString() });
            }

            var viewModel = await _licensingService.BuildRegistrationViewModelAsync();
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(LicenseRegistrationViewModel model)
        {
            var gateResult = await _licensingService.EvaluateAccessAsync(false, ResolveRequestIp());
            if (gateResult.IsAllowed)
            {
                return RedirectToPostLicenseDestination();
            }

            if (gateResult.Status != LicenseGateStatus.Unregistered)
            {
                return RedirectToAction(nameof(Blocked), new { status = gateResult.Status.ToString() });
            }

            if (!ModelState.IsValid)
            {
                return View(await _licensingService.BuildRegistrationViewModelAsync(model));
            }

            ModelState.AddModelError(string.Empty, "OTP verification is required. Use the Register License action on this page to send the OTP and complete registration.");
            return View(await _licensingService.BuildRegistrationViewModelAsync(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartRegistrationOtp(LicenseRegistrationViewModel model)
        {
            var gateResult = await _licensingService.EvaluateAccessAsync(false, ResolveRequestIp());
            if (gateResult.IsAllowed)
            {
                return Json(new
                {
                    success = true,
                    message = "License is already registered.",
                    redirectUrl = Url.Action("Login", "Account")
                });
            }

            if (gateResult.Status != LicenseGateStatus.Unregistered)
            {
                return Json(new
                {
                    success = false,
                    message = gateResult.Message,
                    redirectUrl = Url.Action(nameof(Blocked), new { status = gateResult.Status.ToString() })
                });
            }

            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    success = false,
                    message = GetModelStateErrorMessage()
                });
            }

            var result = await _licensingService.SendRegistrationOtpAsync(model, ResolveRequestIp());
            return Json(new
            {
                success = result.Success,
                message = result.Message,
                expiresInSeconds = result.ExpiresInSeconds,
                targetEmail = result.TargetEmail
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyRegistrationOtp([FromForm] string otpCode)
        {
            var gateResult = await _licensingService.EvaluateAccessAsync(false, ResolveRequestIp());
            if (gateResult.IsAllowed)
            {
                TempData["SuccessMessage"] = "License is already registered.";
                return Json(new
                {
                    success = true,
                    message = "License is already registered.",
                    redirectUrl = Url.Action("Login", "Account")
                });
            }

            if (gateResult.Status != LicenseGateStatus.Unregistered)
            {
                return Json(new
                {
                    success = false,
                    message = gateResult.Message,
                    redirectUrl = Url.Action(nameof(Blocked), new { status = gateResult.Status.ToString() })
                });
            }

            var result = await _licensingService.VerifyRegistrationOtpAsync(otpCode, ResolveRequestIp());
            if (!result.Success)
            {
                return Json(new
                {
                    success = false,
                    message = result.Message
                });
            }

            TempData["SuccessMessage"] = $"License registered successfully. Client code: {result.License?.ClientCode}";
            return Json(new
            {
                success = true,
                message = result.Message,
                redirectUrl = Url.Action("Login", "Account")
            });
        }

        [HttpGet]
        public async Task<IActionResult> Blocked(string? status = null)
        {
            var parsedStatus = ParseStatus(status);
            var viewModel = await _licensingService.BuildBlockedViewModelAsync(parsedStatus);

            if (viewModel.Status == LicenseGateStatus.Valid)
            {
                return RedirectToPostLicenseDestination();
            }

            if (viewModel.Status == LicenseGateStatus.Unregistered)
            {
                return RedirectToAction(nameof(Register));
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryValidation()
        {
            var gateResult = await _licensingService.EvaluateAccessAsync(true, ResolveRequestIp());
            if (gateResult.IsAllowed)
            {
                TempData["SuccessMessage"] = "License validation succeeded.";
                return RedirectToPostLicenseDestination();
            }

            return RedirectToAction(nameof(Blocked), new { status = gateResult.Status.ToString() });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReRegister()
        {
            var gateResult = await _licensingService.EvaluateAccessAsync(false, ResolveRequestIp());
            if (gateResult.Status != LicenseGateStatus.HardwareMismatch)
            {
                return RedirectToAction(nameof(Blocked), new { status = gateResult.Status.ToString() });
            }

            await _licensingService.ClearLocalLicenseAsync();
            return RedirectToAction(nameof(Register));
        }

        private IActionResult RedirectToPostLicenseDestination()
        {
            return User.Identity?.IsAuthenticated == true
                ? RedirectToAction("Index", "Home")
                : RedirectToAction("Login", "Account");
        }

        private static LicenseGateStatus? ParseStatus(string? status)
        {
            return Enum.TryParse<LicenseGateStatus>(status, true, out var parsed) ? parsed : null;
        }

        private string GetModelStateErrorMessage()
        {
            return ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Enter all required registration details." : error.ErrorMessage)
                .FirstOrDefault()
                ?? "Enter all required registration details.";
        }

        private string? ResolveRequestIp()
        {
            if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) && !StringValues.IsNullOrEmpty(forwarded))
            {
                return forwarded.ToString().Split(',').Select(value => value.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }

            if (Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !StringValues.IsNullOrEmpty(realIp))
            {
                return realIp.ToString();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }
}