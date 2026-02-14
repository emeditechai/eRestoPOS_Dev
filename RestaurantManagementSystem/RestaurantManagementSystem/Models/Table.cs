using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    public class Table
    {
        public int Id { get; set; }

        public int? BranchId { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Table Number")]
        public string TableNumber { get; set; } = string.Empty;
        
        // Property that controller expects
        public string TableName
        {
            get { return TableNumber; }
            set { TableNumber = value; }
        }

        [Required]
        [Display(Name = "Seating Capacity")]
        [Range(1, 20, ErrorMessage = "Seating capacity must be between 1 and 20")]
        public int Capacity { get; set; }

        [Display(Name = "Area/Section")]
        [StringLength(50)]
        public string? Section { get; set; }

        [Display(Name = "Is Available")]
        public bool IsAvailable { get; set; } = true;

        [Display(Name = "Current Status")]
        public TableStatus Status { get; set; } = TableStatus.Available;

        // Minimum party size this table is suitable for
        [Display(Name = "Minimum Party Size")]
        public int MinPartySize { get; set; } = 1;

        [Display(Name = "Last Occupied At")]
        public DateTime? LastOccupiedAt { get; set; }

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        // Merged table properties
        public string? MergedTableNames { get; set; }
        public bool IsPartOfMergedOrder { get; set; } = false;

    // Populated at controller level for UI: list of other tables in the merged set (excludes self)
    public string? DisplayMergedWith { get; set; }

        // Calculated property to check if the table is suitable for a given party size
        public bool IsSuitableFor(int partySize)
        {
            // A table is suitable if the party size is >= the minimum size and <= the capacity
            return partySize >= MinPartySize && partySize <= Capacity;
        }

        // Calculated property to get the status description
        public string StatusDescription
        {
            get
            {
                return Status switch
                {
                    TableStatus.Available => "Available",
                    TableStatus.Reserved => "Reserved",
                    TableStatus.Occupied => "Occupied",
                    TableStatus.Dirty => "Needs Cleaning",
                    TableStatus.Maintenance => "Under Maintenance",
                    _ => "Unknown"
                };
            }
        }
    }

    public enum TableStatus
    {
        Available = 0,
        Reserved = 1,
        Occupied = 2,
        Dirty = 3,
        Maintenance = 4
    }
}
