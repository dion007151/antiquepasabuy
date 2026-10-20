using Microsoft.EntityFrameworkCore;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Business logic for creating, querying, and managing weekly Masterlists.
/// All DB access uses a dedicated background AppDbContext to keep the UI thread free.
/// </summary>
public static class MasterlistService
{
    // ── Control number generation ─────────────────────────────────────────────

    /// <summary>
    /// Generates the next sequential control number for a given month/year,
    /// e.g. OCT-2026-01, OCT-2026-02, …
    /// Thread-safe: reads the current max sequence from the DB each time.
    /// </summary>
    public static string GenerateControlNumber(AppDbContext db, int month, int year)
    {
        var monthCode = new DateTime(year, month, 1).ToString("MMM").ToUpperInvariant();
        var prefix    = $"{monthCode}-{year}-";

        // Find max existing sequence for this month/year
        var existing = db.Masterlists
            .Where(m => m.Month == month && m.Year == year)
            .Select(m => m.ControlNumber)
            .ToList();

        int next = 1;
        foreach (var ctrl in existing)
        {
            if (ctrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var seqPart = ctrl[prefix.Length..];
                if (int.TryParse(seqPart, out int seq) && seq >= next)
                    next = seq + 1;
            }
        }
        return $"{prefix}{next:D2}";
    }

    /// <summary>
    /// Returns true when a control number is already used (case-insensitive).
    /// </summary>
    public static bool ControlNumberExists(AppDbContext db, string controlNumber, int? excludeId = null)
    {
        var q = db.Masterlists.Where(m => m.ControlNumber.ToLower() == controlNumber.ToLower());
        if (excludeId.HasValue) q = q.Where(m => m.Id != excludeId.Value);
        return q.Any();
    }

    /// <summary>
    /// Returns true when a new batch's date range overlaps with any existing Active/Closed batch
    /// for the same month/year. Archived batches are excluded from overlap checks.
    /// </summary>
    public static bool HasOverlap(AppDbContext db, DateTime start, DateTime end,
                                  int month, int year, int? excludeId = null)
    {
        var q = db.Masterlists
            .Where(m => m.Month == month && m.Year == year && m.Status != "Archived");
        if (excludeId.HasValue) q = q.Where(m => m.Id != excludeId.Value);

        return q.Any(m => m.StartDate <= end && m.EndDate >= start);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    public static Masterlist Create(AppDbContext db, Masterlist masterlist)
    {
        masterlist.CreatedAt = DateTime.Now;
        masterlist.UpdatedAt = DateTime.Now;
        db.Masterlists.Add(masterlist);
        db.SaveChanges();
        return masterlist;
    }

    public static void Update(AppDbContext db, Masterlist masterlist)
    {
        masterlist.UpdatedAt = DateTime.Now;
        db.Masterlists.Update(masterlist);
        db.SaveChanges();
    }

    /// <summary>
    /// Sets Status to "Closed" — does NOT delete the masterlist.
    /// Only permitted when the masterlist is Active.
    /// </summary>
    public static void Close(AppDbContext db, int id)
    {
        var m = db.Masterlists.Find(id);
        if (m is null || m.Status != "Active") return;
        m.Status    = "Closed";
        m.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    /// <summary>
    /// Sets Status to "Archived" — historical record preserved, never physically deleted.
    /// Permitted from Active or Closed state.
    /// </summary>
    public static void Archive(AppDbContext db, int id)
    {
        var m = db.Masterlists.Find(id);
        if (m is null || m.Status == "Archived") return;
        m.Status    = "Archived";
        m.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    /// <summary>
    /// Assigns an existing Order to a masterlist (sets MasterlistId FK).
    /// The order itself is not duplicated or modified beyond the FK.
    /// </summary>
    public static void AssignOrder(AppDbContext db, int orderId, int masterlistId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Orders SET MasterlistId = @ml WHERE Id = @id";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@ml"; p1.Value = masterlistId; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = orderId;      cmd.Parameters.Add(p2);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes the masterlist assignment from an order (sets MasterlistId to NULL).
    /// </summary>
    public static void UnassignOrder(AppDbContext db, int orderId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Orders SET MasterlistId = NULL WHERE Id = @id";
        var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = orderId; cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all masterlists (no navigation props — use raw SQL aggregates for speed).
    /// Returns lightweight MasterlistRowVm instances ready for the table.
    /// </summary>
    public static List<MasterlistRowVm> GetAllRows(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                m.Id,
                m.ControlNumber,
                m.Name,
                m.Month,
                m.Year,
                m.StartDate,
                m.EndDate,
                m.Status,
                m.Notes,
                m.CreatedAt,
                COUNT(DISTINCT o.Id)                                   AS OrderCount,
                COALESCE(SUM(oi.Qty), 0)                               AS TotalOrderedItems,
                COALESCE(SUM(CASE WHEN oi.Status IN ('PURCHASED') THEN oi.PurchasedQty ELSE 0 END), 0) AS TotalPurchased,
                COALESCE(SUM(CASE WHEN oi.Source = 'STOCK' THEN oi.Qty ELSE 0 END), 0)               AS TotalBoughtOnHand,
                COALESCE(SUM(CASE WHEN oi.Status = 'UNAVAILABLE' THEN oi.Qty ELSE 0 END), 0)         AS TotalUnavailable,
                0                                                      AS TotalExcess,
                0                                                      AS TotalReorder
            FROM Masterlists m
            LEFT JOIN Orders o   ON o.MasterlistId = m.Id AND o.Status != 'CANCELLED'
            LEFT JOIN OrderItems oi ON oi.OrderId = o.Id
            GROUP BY m.Id
            ORDER BY m.Year DESC, m.Month DESC, m.Id DESC";

        var rows = new List<MasterlistRowVm>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new MasterlistRowVm
            {
                Id                 = reader.GetInt32(0),
                ControlNumber      = reader.GetString(1),
                Name               = reader.GetString(2),
                Month              = reader.GetInt32(3),
                Year               = reader.GetInt32(4),
                StartDate          = DateTime.Parse(reader.GetString(5)),
                EndDate            = DateTime.Parse(reader.GetString(6)),
                Status             = reader.GetString(7),
                Notes              = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt          = DateTime.Parse(reader.GetString(9)),
                OrderCount         = (int)(long)reader[10],
                TotalOrderedItems  = (double)(long)reader[11] + (reader.IsDBNull(11) ? 0 : 0),
                TotalPurchased     = Convert.ToDouble(reader[12]),
                TotalBoughtOnHand  = Convert.ToDouble(reader[13]),
                TotalUnavailable   = Convert.ToDouble(reader[14]),
                TotalExcess        = 0,
                TotalReorder       = 0,
            });
        }
        return rows;
    }

    /// <summary>
    /// Loads the full detail view for a single masterlist — all orders and items assigned to it.
    /// </summary>
    public static MasterlistDetailVm GetDetail(AppDbContext db, int masterlistId)
    {
        var m = db.Masterlists.Find(masterlistId)
            ?? throw new InvalidOperationException($"Masterlist {masterlistId} not found.");

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        // Load orders with customer + items + payments
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                o.Id, o.OrderNo, o.Status, o.CreatedAt,
                c.Id AS CustId, c.Name AS CustName, c.Phone AS CustPhone,
                oi.Id AS ItemId, oi.ProductName, oi.Qty, oi.Unit,
                oi.EstimatedPrice, oi.ActualPrice, oi.Status AS ItemStatus,
                oi.PurchasedQty, oi.Store, oi.Source, oi.CostPrice,
                oi.UnavailableReason, oi.PurchaseNotes
            FROM Orders o
            LEFT JOIN Customers c ON c.Id = o.CustomerId
            LEFT JOIN OrderItems oi ON oi.OrderId = o.Id
            WHERE o.MasterlistId = @mid AND o.Status != 'CANCELLED'
            ORDER BY c.Name, o.Id, oi.Id";

        var p = cmd.CreateParameter(); p.ParameterName = "@mid"; p.Value = masterlistId; cmd.Parameters.Add(p);

        var ordersDict = new Dictionary<int, MasterlistOrderVm>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var orderId = reader.GetInt32(0);
            if (!ordersDict.TryGetValue(orderId, out var ov))
            {
                ov = new MasterlistOrderVm
                {
                    OrderId      = orderId,
                    OrderNo      = reader.GetString(1),
                    OrderStatus  = reader.GetString(2),
                    CreatedAt    = DateTime.Parse(reader.GetString(3)),
                    CustomerId   = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    CustomerName = reader.IsDBNull(5) ? "(Unknown)" : reader.GetString(5),
                    CustomerPhone= reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Items        = []
                };
                ordersDict[orderId] = ov;
            }

            if (!reader.IsDBNull(7))
            {
                ov.Items.Add(new MasterlistItemVm
                {
                    ItemId           = reader.GetInt32(7),
                    ProductName      = reader.GetString(8),
                    Qty              = reader.GetDouble(9),
                    Unit             = reader.GetString(10),
                    EstimatedPrice   = reader.GetDecimal(11),
                    ActualPrice      = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                    ItemStatus       = reader.GetString(13),
                    PurchasedQty     = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                    Store            = reader.GetString(15),
                    Source           = reader.GetString(16),
                    CostPrice        = reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    UnavailableReason= reader.IsDBNull(18) ? null : reader.GetString(18),
                    PurchaseNotes    = reader.IsDBNull(19) ? null : reader.GetString(19),
                });
            }
        }

        return new MasterlistDetailVm
        {
            Masterlist = m,
            Orders     = [.. ordersDict.Values]
        };
    }
}

// ── View-Model helper classes ─────────────────────────────────────────────────

public class MasterlistRowVm
{
    public int       Id                { get; set; }
    public string    ControlNumber     { get; set; } = "";
    public string    Name              { get; set; } = "";
    public int       Month             { get; set; }
    public int       Year              { get; set; }
    public DateTime  StartDate         { get; set; }
    public DateTime  EndDate           { get; set; }
    public string    Status            { get; set; } = "Active";
    public string?   Notes             { get; set; }
    public DateTime  CreatedAt         { get; set; }

    public int    OrderCount           { get; set; }
    public double TotalOrderedItems    { get; set; }
    public double TotalPurchased       { get; set; }
    public double TotalBoughtOnHand    { get; set; }
    public double TotalUnavailable     { get; set; }
    public double TotalExcess          { get; set; }
    public double TotalReorder         { get; set; }

    // Computed display
    public string DateRangeDisplay =>
        $"{StartDate:MMM dd} – {EndDate:MMM dd, yyyy}";

    public string MonthYearDisplay =>
        new DateTime(Year, Month, 1).ToString("MMMM yyyy");

    public bool IsActive   => Status == "Active";
    public bool IsClosed   => Status == "Closed";
    public bool IsArchived => Status == "Archived";
}

public class MasterlistDetailVm
{
    public Masterlist            Masterlist { get; set; } = null!;
    public List<MasterlistOrderVm> Orders  { get; set; } = [];

    public int    TotalOrders      => Orders.Count;
    public int    TotalItems       => Orders.Sum(o => o.Items.Count);
    public double TotalPurchased   => Orders.SelectMany(o => o.Items)
                                           .Where(i => i.ItemStatus == "PURCHASED")
                                           .Sum(i => i.PurchasedQty ?? i.Qty);
    public double TotalBoughtOnHand=> Orders.SelectMany(o => o.Items)
                                           .Where(i => i.Source == "STOCK")
                                           .Sum(i => i.Qty);
    public double TotalUnavailable => Orders.SelectMany(o => o.Items)
                                           .Where(i => i.ItemStatus == "UNAVAILABLE")
                                           .Sum(i => i.Qty);
}

public class MasterlistOrderVm
{
    public int      OrderId       { get; set; }
    public string   OrderNo       { get; set; } = "";
    public string   OrderStatus   { get; set; } = "";
    public DateTime CreatedAt     { get; set; }
    public int      CustomerId    { get; set; }
    public string   CustomerName  { get; set; } = "";
    public string   CustomerPhone { get; set; } = "";
    public List<MasterlistItemVm> Items { get; set; } = [];

    public int    TotalItems      => Items.Count;
    public string CustomerDisplay => CustomerName;
}

public class MasterlistItemVm
{
    public int      ItemId            { get; set; }
    public string   ProductName       { get; set; } = "";
    public double   Qty               { get; set; }
    public string   Unit              { get; set; } = "pc";
    public decimal  EstimatedPrice    { get; set; }
    public decimal? ActualPrice       { get; set; }
    public string   ItemStatus        { get; set; } = "PENDING";
    public double?  PurchasedQty      { get; set; }
    public string   Store             { get; set; } = "General";
    public string   Source            { get; set; } = "STORE";
    public decimal? CostPrice         { get; set; }
    public string?  UnavailableReason { get; set; }
    public string?  PurchaseNotes     { get; set; }

    /// <summary>
    /// Returns the exact client status label (New, Bought, Bought On Hand, Not Available, Reorder).
    /// </summary>
    public string StatusLabel => (ItemStatus?.ToUpperInvariant(), Source?.ToUpperInvariant()) switch
    {
        ("PENDING",     _       ) => "New",
        ("PURCHASED",  "STOCK"  ) => "Bought On Hand",
        ("PURCHASED",   _       ) => "Bought",
        ("UNAVAILABLE", _       ) => "Not Available",
        ("CANCELLED",   _       ) => "Not Available",
        _                         => "New"
    };

    public string StatusColor => StatusLabel switch
    {
        "Bought"          => "#16A34A",
        "Bought On Hand"  => "#0891B2",
        "Not Available"   => "#DC2626",
        "Reorder"         => "#D97706",
        _                 => "#6366F1"     // New
    };
}
