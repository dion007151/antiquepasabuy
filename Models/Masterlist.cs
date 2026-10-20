namespace AntiquePasabuyPOS.Models;

/// <summary>
/// Represents a weekly masterlist batch — a named, persistent record that groups
/// customer orders for a specific week and stores aggregate purchasing information.
/// </summary>
public class Masterlist
{
    public int Id { get; set; }

    /// <summary>
    /// Unique sequential control number, e.g. OCT-2026-01
    /// </summary>
    public string ControlNumber { get; set; } = "";

    /// <summary>
    /// Display name / week label, e.g. "Week 1 – October 2026"
    /// </summary>
    public string Name { get; set; } = "";

    public int Month { get; set; }   // 1–12
    public int Year  { get; set; }

    /// <summary>
    /// Actual start date of this weekly batch (not assumed from week number).
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Actual end date of this weekly batch.
    /// </summary>
    public DateTime EndDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Active | Closed | Archived
    /// </summary>
    public string Status { get; set; } = "Active";

    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Navigation — orders assigned to this batch
    public ICollection<Order> Orders { get; set; } = new List<Order>();

    // ── Computed display helpers ──────────────────────────────────────────────

    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM");

    public string DateRangeDisplay =>
        $"{StartDate:MMM dd} – {EndDate:MMM dd, yyyy}";

    public string StatusDisplay => Status switch
    {
        "Active"   => "Active",
        "Closed"   => "Closed",
        "Archived" => "Archived",
        _          => Status
    };

    public bool IsActive   => Status == "Active";
    public bool IsClosed   => Status == "Closed";
    public bool IsArchived => Status == "Archived";
}
