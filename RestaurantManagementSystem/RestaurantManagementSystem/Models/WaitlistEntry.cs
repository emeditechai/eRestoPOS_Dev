using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    public class WaitlistEntry
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Guest Name")]
        public string GuestName { get; set; }
        
        // Property that controller expects
        public string CustomerName
        {
            get { return GuestName; }
            set { GuestName = value; }
        }

        [Required]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Phone number must be exactly 10 digits.")]
        [StringLength(10, MinimumLength = 10, ErrorMessage = "Phone number must be exactly 10 digits.")]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }

        [Required]
        [Display(Name = "Party Size")]
        [Range(1, 50, ErrorMessage = "Party size must be between 1 and 50")]
        public int PartySize { get; set; }

        [Required]
        [Display(Name = "Added At")]
        public DateTime AddedAt { get; set; } = DateTime.Now;
        
        // Property that controller expects
        public DateTime CheckInTime
        {
            get { return AddedAt; }
            set { AddedAt = value; }
        }

        [Display(Name = "Quoted Wait Time (minutes)")]
        public int QuotedWaitTime { get; set; } = 30; // Default 30 minutes
        
        // Property that controller expects
        public int EstimatedWaitMinutes
        {
            get { return QuotedWaitTime; }
            set { QuotedWaitTime = value; }
        }

        [Display(Name = "Notify When Ready")]
        public bool NotifyWhenReady { get; set; } = true;

        [StringLength(200)]
        public string? Notes { get; set; }
        
        // Property that controller expects
        public string? SpecialRequests
        {
            get { return Notes; }
            set { Notes = value; }
        }

        [Display(Name = "Status")]
        public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;

        [Display(Name = "Notified At")]
        public DateTime? NotifiedAt { get; set; }

        [Display(Name = "Seated At")]
        public DateTime? SeatedAt { get; set; }

        public int? TableId { get; set; }

        // Calculate expected ready time
        [Display(Name = "Expected Ready Time")]
        public DateTime ExpectedReadyTime
        {
            get
            {
                return AddedAt.AddMinutes(QuotedWaitTime);
            }
        }

        // Calculate elapsed wait time in minutes
        [Display(Name = "Elapsed Wait Time (minutes)")]
        public int ElapsedWaitTime
        {
            get
            {
                TimeSpan waitTime = DateTime.Now - AddedAt;
                return (int)waitTime.TotalMinutes;
            }
        }

        // Check if the quoted wait time has been exceeded
        public bool IsWaitTimeExceeded
        {
            get
            {
                return ElapsedWaitTime > QuotedWaitTime;
            }
        }
    }

    public enum WaitlistStatus
    {
        Waiting = 0,
        Notified = 1,
        Seated = 2,
        Left = 3,
        NoResponse = 4
    }
}
