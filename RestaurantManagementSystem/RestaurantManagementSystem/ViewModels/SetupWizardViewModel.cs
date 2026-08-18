using System;
using System.Collections.Generic;

namespace RestaurantManagementSystem.ViewModels
{
    public class SetupWizardStep
    {
        public int StepNumber { get; set; }
        public string StepKey { get; set; } = string.Empty;
        public string Phase { get; set; } = "Foundation"; // Foundation, Menu Catalog, Payments & POS
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconCss { get; set; } = string.Empty;
        public string ThemeColor { get; set; } = "#7c3aed";
        public string TargetUrl { get; set; } = string.Empty;
        public string ActionButtonText { get; set; } = "Configure";
        public bool IsConfigured { get; set; }
        public int CurrentCount { get; set; }
        public string CountBadgeText { get; set; } = string.Empty;
        public bool IsUnlocked { get; set; } = true;
        public string DependencyNote { get; set; } = string.Empty;
        public List<int> DependsOnStepNumbers { get; set; } = new List<int>();
    }

    public class SetupWizardViewModel
    {
        public int UserId { get; set; }
        public bool IsSignupUser { get; set; }
        public bool ShowWizard { get; set; }
        public int ReadinessPercentage { get; set; }
        public int CompletedStepsCount { get; set; }
        public int TotalStepsCount { get; set; }
        public int PendingStepsCount => Math.Max(0, TotalStepsCount - CompletedStepsCount);
        public string CurrentBranchName { get; set; } = "Current Branch";
        public int? CurrentBranchId { get; set; }
        public List<SetupWizardStep> Steps { get; set; } = new List<SetupWizardStep>();

        public bool IsAllCoreMastersCompleted => TotalStepsCount > 0 && CompletedStepsCount >= TotalStepsCount;
    }
}
