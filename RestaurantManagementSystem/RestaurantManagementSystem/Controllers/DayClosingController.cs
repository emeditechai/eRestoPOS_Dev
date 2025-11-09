using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RestaurantManagementSystem.Controllers
{
    [Authorize]
    public class DayClosingController : Controller
    {
        private readonly IDayClosingService _dayClosingService;

        public DayClosingController(IDayClosingService dayClosingService)
        {
            _dayClosingService = dayClosingService;
        }

        /// <summary>
        /// Day Closing Dashboard - Main entry point
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? date)
        {
            try
            {
                var businessDate = date ?? DateTime.Today;
                
                var model = new DayClosingDashboardViewModel
                {
                    BusinessDate = businessDate
                };

                // Get cashier closing details
                model.CashierClosings = await _dayClosingService.GetDayClosingSummaryAsync(businessDate);

                // Get lock status
                model.LockStatus = await _dayClosingService.GetDayLockStatusAsync(businessDate);

                // Calculate summary
                model.Summary = new DaySummary
                {
                    TotalCashiers = model.CashierClosings.Count,
                    PendingCount = model.CashierClosings.Count(c => c.Status == "PENDING"),
                    OkCount = model.CashierClosings.Count(c => c.Status == "OK"),
                    CheckCount = model.CashierClosings.Count(c => c.Status == "CHECK"),
                    LockedCount = model.CashierClosings.Count(c => c.Status == "LOCKED"),
                    TotalOpeningFloat = model.CashierClosings.Sum(c => c.OpeningFloat),
                    TotalSystemAmount = model.CashierClosings.Sum(c => c.SystemAmount),
                    TotalDeclaredAmount = model.CashierClosings.Sum(c => c.DeclaredAmount ?? 0),
                    TotalVariance = model.CashierClosings.Sum(c => c.Variance ?? 0),
                    TotalExpectedCash = model.CashierClosings.Sum(c => c.ExpectedCash)
                };

                // Check if day can be locked
                model.CanLockDay = model.Summary.CheckCount == 0 && 
                                   model.Summary.PendingCount == 0 && 
                                   model.LockStatus?.IsLocked != true;

                if (!model.CanLockDay && model.LockStatus?.IsLocked != true)
                {
                    if (model.Summary.CheckCount > 0)
                        model.LockMessage = $"{model.Summary.CheckCount} cashier(s) have unresolved variances requiring approval.";
                    else if (model.Summary.PendingCount > 0)
                        model.LockMessage = $"{model.Summary.PendingCount} cashier(s) have not declared cash yet.";
                }

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading day closing: {ex.Message}";
                return View(new DayClosingDashboardViewModel());
            }
        }

        /// <summary>
        /// Initialize opening float for a cashier - GET
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Administrator,Manager")]
        public async Task<IActionResult> OpenFloat(DateTime? date)
        {
            var businessDate = date ?? DateTime.Today;
            
            var model = new OpenFloatViewModel
            {
                BusinessDate = businessDate,
                AvailableCashiers = await _dayClosingService.GetAvailableCashiersAsync(businessDate)
            };

            return View(model);
        }

        /// <summary>
        /// Process Open Float form submission
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Administrator,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OpenFloat(OpenFloatViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.AvailableCashiers = await _dayClosingService.GetAvailableCashiersAsync(model.BusinessDate);
                return View(model);
            }

            try
            {
                var username = User.Identity?.Name ?? "System";
                var result = await _dayClosingService.InitializeDayOpeningAsync(
                    model.BusinessDate,
                    model.CashierId,
                    model.OpeningFloat,
                    username
                );

                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                    return RedirectToAction(nameof(Index), new { date = model.BusinessDate });
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                    model.AvailableCashiers = await _dayClosingService.GetAvailableCashiersAsync(model.BusinessDate);
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error initializing opening float: {ex.Message}";
                model.AvailableCashiers = await _dayClosingService.GetAvailableCashiersAsync(model.BusinessDate);
                return View(model);
            }
        }

        /// <summary>
        /// Show Declare Cash form
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DeclareCash(int cashierId, DateTime? date)
        {
            var businessDate = date ?? DateTime.Today;
            
            try
            {
                var closings = await _dayClosingService.GetDayClosingSummaryAsync(businessDate);
                var cashierClosing = closings.FirstOrDefault(c => c.CashierId == cashierId);

                if (cashierClosing == null)
                {
                    TempData["ErrorMessage"] = "Cashier closing record not found. Please ensure opening float is initialized.";
                    return RedirectToAction(nameof(Index), new { date = businessDate });
                }

                if (cashierClosing.LockedFlag)
                {
                    TempData["ErrorMessage"] = "Day is locked. Cannot declare cash.";
                    return RedirectToAction(nameof(Index), new { date = businessDate });
                }

                var model = new DeclaredCashViewModel
                {
                    CloseId = cashierClosing.Id,
                    BusinessDate = businessDate,
                    CashierId = cashierId,
                    CashierName = cashierClosing.CashierName,
                    OpeningFloat = cashierClosing.OpeningFloat,
                    SystemAmount = cashierClosing.SystemAmount,
                    ExpectedCash = cashierClosing.ExpectedCash,
                    DeclaredAmount = cashierClosing.DeclaredAmount ?? 0
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading declare cash form: {ex.Message}";
                return RedirectToAction(nameof(Index), new { date = businessDate });
            }
        }

        /// <summary>
        /// Process Declare Cash form submission
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclareCash(DeclaredCashViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var username = User.Identity?.Name ?? "System";
                var result = await _dayClosingService.SaveDeclaredCashAsync(
                    model.BusinessDate,
                    model.CashierId,
                    model.DeclaredAmount,
                    username
                );

                if (result.Success)
                {
                    if (Math.Abs(result.Variance) > 100)
                    {
                        TempData["WarningMessage"] = $"Cash declared successfully. Variance of ₹{Math.Abs(result.Variance):N2} {(result.Variance >= 0 ? "Over" : "Short")} requires manager approval.";
                    }
                    else
                    {
                        TempData["SuccessMessage"] = $"Cash declared successfully. Variance: ₹{Math.Abs(result.Variance):N2} {(result.Variance >= 0 ? "Over" : "Short")}";
                    }
                    
                    return RedirectToAction(nameof(Index), new { date = model.BusinessDate });
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error declaring cash: {ex.Message}";
                return View(model);
            }
        }

        /// <summary>
        /// Show Variance Approval form
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Administrator,Manager")]
        public async Task<IActionResult> ApproveVariance(int closeId, DateTime? date)
        {
            var businessDate = date ?? DateTime.Today;
            
            try
            {
                var closings = await _dayClosingService.GetDayClosingSummaryAsync(businessDate);
                var cashierClosing = closings.FirstOrDefault(c => c.Id == closeId);

                if (cashierClosing == null)
                {
                    TempData["ErrorMessage"] = "Cashier closing record not found.";
                    return RedirectToAction(nameof(Index), new { date = businessDate });
                }

                var model = new VarianceApprovalViewModel
                {
                    CloseId = closeId,
                    BusinessDate = businessDate,
                    CashierName = cashierClosing.CashierName,
                    OpeningFloat = cashierClosing.OpeningFloat,
                    SystemAmount = cashierClosing.SystemAmount,
                    DeclaredAmount = cashierClosing.DeclaredAmount ?? 0,
                    Variance = cashierClosing.Variance ?? 0,
                    ExpectedCash = cashierClosing.ExpectedCash
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading approval form: {ex.Message}";
                return RedirectToAction(nameof(Index), new { date = businessDate });
            }
        }

        /// <summary>
        /// Process Variance Approval form submission
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Administrator,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveVariance(VarianceApprovalViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var username = User.Identity?.Name ?? "System";
                var approved = model.ApprovalAction == "APPROVE";
                
                var result = await _dayClosingService.ApproveVarianceAsync(
                    model.CloseId,
                    username,
                    model.ApprovalComment,
                    approved
                );

                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                    return RedirectToAction(nameof(Index), new { date = model.BusinessDate });
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error approving variance: {ex.Message}";
                return View(model);
            }
        }

        /// <summary>
        /// Lock the business day
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Administrator,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LockDay(DateTime businessDate, string? remarks)
        {
            try
            {
                var username = User.Identity?.Name ?? "System";
                var result = await _dayClosingService.LockDayAsync(businessDate, username, remarks);

                if (result.Success)
                {
                    TempData["SuccessMessage"] = "✅ Day locked successfully! No further changes allowed.";
                    return RedirectToAction(nameof(Index), new { date = businessDate });
                }
                else
                {
                    TempData["ErrorMessage"] = $"Cannot lock day: {result.Message}";
                    return RedirectToAction(nameof(Index), new { date = businessDate });
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error locking day: {ex.Message}";
                return RedirectToAction(nameof(Index), new { date = businessDate });
            }
        }

        /// <summary>
        /// Generate and display EOD Report
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Administrator,Manager")]
        public async Task<IActionResult> EODReport(DateTime? date)
        {
            var businessDate = date ?? DateTime.Today;
            
            try
            {
                var username = User.Identity?.Name ?? "System";
                var model = await _dayClosingService.GenerateEODReportAsync(businessDate, username);
                
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error generating EOD report: {ex.Message}";
                return RedirectToAction(nameof(Index), new { date = businessDate });
            }
        }

        /// <summary>
        /// Print EOD Report
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Administrator,Manager")]
        public async Task<IActionResult> PrintEOD(DateTime? date)
        {
            var businessDate = date ?? DateTime.Today;
            
            try
            {
                var username = User.Identity?.Name ?? "System";
                var model = await _dayClosingService.GenerateEODReportAsync(businessDate, username);
                
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error generating print report: {ex.Message}";
                return RedirectToAction(nameof(Index), new { date = businessDate });
            }
        }

        /// <summary>
        /// Refresh system amounts for all cashiers
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Administrator,Manager")]
        public async Task<IActionResult> RefreshSystemAmounts(DateTime businessDate)
        {
            try
            {
                await _dayClosingService.UpdateCashierSystemAmountsAsync(businessDate);
                TempData["SuccessMessage"] = "System amounts refreshed successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error refreshing system amounts: {ex.Message}";
            }
            
            return RedirectToAction(nameof(Index), new { date = businessDate });
        }
    }
}
