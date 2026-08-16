namespace RestaurantManagementSystem.Middleware
{
    public class LicensingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LicensingMiddleware> _logger;

        public LicensingMiddleware(RequestDelegate next, ILogger<LicensingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ILicensingService licensingService)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (ShouldBypass(path))
            {
                await _next(context);
                return;
            }

            var requestIp = ResolveRequestIp(context);
            var gateResult = await licensingService.EvaluateAccessAsync(forceRemoteValidation: false, requestIp: requestIp);

            if (gateResult.Status == LicenseGateStatus.Unregistered)
            {
                if (!path.StartsWith("/License/Register", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect("/License/Register");
                    return;
                }

                await _next(context);
                return;
            }

            if (!gateResult.IsAllowed)
            {
                if (context.User.Identity?.IsAuthenticated == true)
                {
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }

                if (!path.StartsWith("/License/Blocked", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith("/License/RetryValidation", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Blocking request for path {Path} due to licensing status {Status}", path, gateResult.Status);
                    context.Response.Redirect($"/License/Blocked?status={WebUtility.UrlEncode(gateResult.Status.ToString())}");
                    return;
                }

                await _next(context);
                return;
            }

            if (path.StartsWith("/License/Register", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/License/Blocked", StringComparison.OrdinalIgnoreCase))
            {
                var destination = context.User.Identity?.IsAuthenticated == true ? "/Home/Index" : "/Account/Login";
                context.Response.Redirect(destination);
                return;
            }

            await _next(context);
        }

        private static bool ShouldBypass(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/images", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/License", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Home/Error", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Home/Maintenance", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/SignUp", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolveRequestIp(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) && !StringValues.IsNullOrEmpty(forwarded))
            {
                return forwarded.ToString().Split(',').Select(value => value.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }

            if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !StringValues.IsNullOrEmpty(realIp))
            {
                return realIp.ToString();
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }
}