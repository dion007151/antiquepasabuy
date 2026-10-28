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
    /// Deletes a masterlist from the database.
    /// Safely unassigns any orders previously linked to this masterlist (sets MasterlistId to NULL)
    /// and resets the active_batch_code setting if this was the active masterlist.
    /// Zero order data is lost.
    /// </summary>
    public static void Delete(AppDbContext db, int id)
    {
        var m = db.Masterlists.Find(id);
        if (m is null) return;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Orders SET MasterlistId = NULL WHERE MasterlistId = @mid";
        var p = cmd.CreateParameter(); p.ParameterName = "@mid"; p.Value = id; cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();

        // If this was active and setting active_batch_code matches, update setting
        var activeSetting = db.Settings.FirstOrDefault(s => s.Key == "active_batch_code");
        if (activeSetting != null && (string.Equals(activeSetting.Value, m.Name, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(activeSetting.Value, m.ControlNumber, StringComparison.OrdinalIgnoreCase)))
        {
            var nextActive = db.Masterlists.FirstOrDefault(other => other.Id != id && other.Status == "Active");
            activeSetting.Value = nextActive?.Name ?? "";
        }

        db.Masterlists.Remove(m);
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
                COALESCE(SUM(CASE WHEN oi.PurchasedQty > oi.Qty THEN oi.PurchasedQty - oi.Qty ELSE 0 END), 0) AS TotalExcess,
                0                                                      AS TotalReorder
            FROM Masterlists m
            LEFT JOIN Orders o ON (o.MasterlistId = m.Id OR (o.MasterlistId IS NULL AND (UPPER(o.BatchCode) = UPPER(m.Name) OR UPPER(o.BatchCode) = UPPER(m.ControlNumber)))) AND o.Status != 'CANCELLED'
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
                OrderCount         = reader.IsDBNull(10) ? 0 : Convert.ToInt32(reader[10]),
                TotalOrderedItems  = reader.IsDBNull(11) ? 0 : Convert.ToDouble(reader[11]),
                TotalPurchased     = reader.IsDBNull(12) ? 0 : Convert.ToDouble(reader[12]),
                TotalBoughtOnHand  = reader.IsDBNull(13) ? 0 : Convert.ToDouble(reader[13]),
                TotalUnavailable   = reader.IsDBNull(14) ? 0 : Convert.ToDouble(reader[14]),
                TotalExcess        = reader.IsDBNull(15) ? 0 : Convert.ToDouble(reader[15]),
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

    // ── UI-facing helpers ─────────────────────────────────────────────────────

    /// <summary>Returns the current Active masterlist, or null if none exists.</summary>
    public static Masterlist? GetActive(AppDbContext db)
        => db.Masterlists.FirstOrDefault(m => m.Status == "Active");

    /// <summary>
    /// Ordered list for the Masterlist page dropdown:
    ///   1. Active masterlist (if any) — shown first
    ///   2. Closed masterlists alphabetically by name
    ///   3. A sentinel entry with Id = -1 and Name = "All masterlists"
    /// </summary>
    public static List<MasterlistDropdownItem> GetDisplayList(AppDbContext db)
    {
        var items = new List<MasterlistDropdownItem>();

        var all = db.Masterlists
            .OrderBy(m => m.Status == "Active" ? 0 : 1)
            .ThenBy(m => m.Name)
            .ToList();

        foreach (var m in all)
        {
            items.Add(new MasterlistDropdownItem
            {
                Id            = m.Id,
                Name          = m.Status == "Active" ? $"{m.Name} (active)" : m.Name,
                RawName       = m.Name,
                Status        = m.Status,
                BatchCode     = m.Name,
                ControlNumber = m.ControlNumber,
                StartDate     = m.StartDate
            });
        }

        items.Add(new MasterlistDropdownItem { Id = -1, Name = "All masterlists", RawName = "All masterlists", Status = "All", BatchCode = null });
        return items;
    }

    /// <summary>
    /// Returns summary counts for the specified masterlist (or active if null, or all if id == -1).
    /// </summary>
    public static MasterlistSummary GetSummary(AppDbContext db, int? masterlistId = null)
    {
        Masterlist? target = null;
        if (masterlistId.HasValue && masterlistId.Value != -1)
        {
            target = db.Masterlists.Find(masterlistId.Value);
        }
        else if (!masterlistId.HasValue)
        {
            target = GetActive(db);
        }

        var isAll = masterlistId == -1;
        if (!isAll && target == null)
        {
            return new MasterlistSummary();
        }

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        if (isAll)
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(DISTINCT o.Id)                                       AS OrderCount,
                    COUNT(DISTINCT CASE WHEN EXISTS(
                        SELECT 1 FROM OrderItems oi2
                        WHERE oi2.OrderId = o.Id AND oi2.Status = 'PENDING'
                    ) THEN o.Id END)                                           AS OrdersWithPendingItems,
                    COALESCE((
                        SELECT SUM(oi.Qty)
                        FROM OrderItems oi
                        JOIN Orders o2 ON oi.OrderId = o2.Id
                        WHERE o2.Status != 'CANCELLED'
                    ), 0)                                                      AS TotalClientQty,
                    COALESCE((
                        SELECT SUM(oi.PurchasedQty)
                        FROM OrderItems oi
                        JOIN Orders o2 ON oi.OrderId = o2.Id
                        WHERE o2.Status != 'CANCELLED'
                    ), 0)                                                      AS TotalSupplierQty,
                    COALESCE((
                        SELECT SUM(CASE WHEN oi.PurchasedQty > oi.Qty THEN oi.PurchasedQty - oi.Qty ELSE 0 END)
                        FROM OrderItems oi
                        JOIN Orders o2 ON oi.OrderId = o2.Id
                        WHERE o2.Status != 'CANCELLED'
                    ), 0)                                                      AS TotalExcess,
                    COALESCE(SUM(o.ServiceFee + o.DeliveryFee - o.Discount
                        + COALESCE((
                            SELECT SUM(oi.EstimatedPrice * oi.Qty)
                            FROM OrderItems oi WHERE oi.OrderId = o.Id
                            AND oi.Status != 'UNAVAILABLE' AND oi.Status != 'CANCELLED'
                        ), 0)
                        - COALESCE((SELECT SUM(p.Amount) FROM Payments p WHERE p.OrderId = o.Id), 0)
                    ), 0)                                                      AS TotalUnpaid
                FROM Orders o
                WHERE o.Status != 'CANCELLED'";
        }
        else
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(DISTINCT o.Id)                                       AS OrderCount,
                    COUNT(DISTINCT CASE WHEN EXISTS(
                        SELECT 1 FROM OrderItems oi2
                        WHERE oi2.OrderId = o.Id AND oi2.Status = 'PENDING'
                    ) THEN o.Id END)                                           AS OrdersWithPendingItems,
                    COALESCE((
                        SELECT SUM(oi.Qty)
                        FROM OrderItems oi
                        JOIN Orders o2 ON oi.OrderId = o2.Id
                        WHERE (o2.MasterlistId = @mid OR (o2.MasterlistId IS NULL AND (UPPER(o2.BatchCode) = UPPER(@name) OR UPPER(o2.BatchCode) = UPPER(@ctrl))))
                          AND o2.Status != 'CANCELLED'
                    ), 0)                                                      AS TotalClientQty,
                    COALESCE((
                        SELECT SUM(oi.PurchasedQty)
                        FROM OrderItems oi
                        JOIN Orders o2 ON oi.OrderId = o2.Id
                        WHERE (o2.MasterlistId = @mid OR (o2.MasterlistId IS NULL AND (UPPER(o2.BatchCode) = UPPER(@name) OR UPPER(o2.BatchCode) = UPPER(@ctrl))))
                          AND o2.Status != 'CANCELLED'
                    ), 0)                                                      AS TotalSupplierQty,
                    COALESCE((
                        SELECT SUM(CASE WHEN oi.PurchasedQty > oi.Qty THEN oi.PurchasedQty - oi.Qty ELSE 0 END)
                        FROM OrderItems oi
                        JOIN Orders o2 ON oi.OrderId = o2.Id
                        WHERE (o2.MasterlistId = @mid OR (o2.MasterlistId IS NULL AND (UPPER(o2.BatchCode) = UPPER(@name) OR UPPER(o2.BatchCode) = UPPER(@ctrl))))
                          AND o2.Status != 'CANCELLED'
                    ), 0)                                                      AS TotalExcess,
                    COALESCE(SUM(o.ServiceFee + o.DeliveryFee - o.Discount
                        + COALESCE((
                            SELECT SUM(oi.EstimatedPrice * oi.Qty)
                            FROM OrderItems oi WHERE oi.OrderId = o.Id
                            AND oi.Status != 'UNAVAILABLE' AND oi.Status != 'CANCELLED'
                        ), 0)
                        - COALESCE((SELECT SUM(p.Amount) FROM Payments p WHERE p.OrderId = o.Id), 0)
                    ), 0)                                                      AS TotalUnpaid
                FROM Orders o
                WHERE (o.MasterlistId = @mid OR (o.MasterlistId IS NULL AND (UPPER(o.BatchCode) = UPPER(@name) OR UPPER(o.BatchCode) = UPPER(@ctrl))))
                  AND o.Status != 'CANCELLED'";

            var pMid = cmd.CreateParameter(); pMid.ParameterName = "@mid"; pMid.Value = target!.Id; cmd.Parameters.Add(pMid);
            var pName = cmd.CreateParameter(); pName.ParameterName = "@name"; pName.Value = target.Name; cmd.Parameters.Add(pName);
            var pCtrl = cmd.CreateParameter(); pCtrl.ParameterName = "@ctrl"; pCtrl.Value = target.ControlNumber; cmd.Parameters.Add(pCtrl);
        }

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new MasterlistSummary
            {
                MasterlistId           = target?.Id ?? -1,
                ControlNumber          = target?.ControlNumber ?? "ALL",
                Name                   = target?.Name ?? "All masterlists",
                StartDate              = target?.StartDate ?? DateTime.MinValue,
                OrderCount             = Convert.ToInt32(reader["OrderCount"]),
                OrdersWithPendingItems = Convert.ToInt32(reader["OrdersWithPendingItems"]),
                TotalClientQty         = Convert.ToDouble(reader["TotalClientQty"]),
                TotalSupplierQty       = Convert.ToDouble(reader["TotalSupplierQty"]),
                TotalExcess            = Convert.ToDouble(reader["TotalExcess"]),
                TotalUnpaidBalance     = Convert.ToDecimal(reader["TotalUnpaid"])
            };
        }

        return new MasterlistSummary
        {
            MasterlistId  = target?.Id ?? -1,
            ControlNumber = target?.ControlNumber ?? "ALL",
            Name          = target?.Name ?? "All masterlists",
            StartDate     = target?.StartDate ?? DateTime.MinValue
        };
    }

    /// <summary>
    /// Returns summary counts for the currently active masterlist (for the info line).
    /// </summary>
    public static MasterlistSummary GetActiveSummary(AppDbContext db) => GetSummary(db, null);

    /// <summary>
    /// Closes the current active masterlist, creates a new one, updates the active_batch_code
    /// setting, and assigns all future orders to the new masterlist.
    /// </summary>
    public static Masterlist CloseAndCreate(AppDbContext db, string newName, DateTime startDate, int? userId = null)
    {
        // Close any currently active masterlist
        var current = GetActive(db);
        if (current != null)
        {
            current.Status    = "Closed";
            current.UpdatedAt = DateTime.Now;
            db.SaveChanges();
        }

        var now = DateTime.Now;
        var controlNo = GenerateControlNumber(db, startDate.Month, startDate.Year);

        var newMasterlist = new Masterlist
        {
            ControlNumber = controlNo,
            Name          = newName.Trim(),
            Month         = startDate.Month,
            Year          = startDate.Year,
            StartDate     = startDate,
            EndDate       = startDate.AddDays(365), // open-ended; closed on next creation
            Status        = "Active",
            CreatedBy     = userId,
            CreatedAt     = now,
            UpdatedAt     = now
        };
        db.Masterlists.Add(newMasterlist);

        // Update the active_batch_code setting so NewOrderPage picks it up
        var setting = db.Settings.FirstOrDefault(s => s.Key == "active_batch_code");
        if (setting is null)
        {
            db.Settings.Add(new AntiquePasabuyPOS.Models.Setting { Key = "active_batch_code", Value = newName.Trim() });
        }
        else
        {
            setting.Value = newName.Trim();
        }

        db.SaveChanges();
        return newMasterlist;
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

    public string StatusBadgeColor => Status switch
    {
        "Active"   => "#16A34A",
        "Closed"   => "#64748B",
        "Archived" => "#D97706",
        _          => "#6B7280"
    };

    public string StatusBadgeBg => Status switch
    {
        "Active"   => "#DCFCE7",
        "Closed"   => "#F1F5F9",
        "Archived" => "#FEF3C7",
        _          => "#F3F4F6"
    };

    public string FolderIcon => Status switch
    {
        "Active"   => "FolderOpenOutline",
        "Closed"   => "FolderOutline",
        "Archived" => "FolderZipOutline",
        _          => "FolderOutline"
    };

    public string FolderColor => Status switch
    {
        "Active"   => "#4F46E5",
        "Closed"   => "#64748B",
        "Archived" => "#D97706",
        _          => "#6B7280"
    };
}

public class MasterlistDetailVm
{
    public Masterlist            Masterlist { get; set; } = null!;
    public List<MasterlistOrderVm> Orders  { get; set; } = [];

    public int    TotalOrders      => Orders.Count;
    public int    TotalItems       => Orders.Sum(o => o.Items.Count);
    public double TotalClientQty   => Orders.SelectMany(o => o.Items).Sum(i => i.Qty);
    public double TotalSupplierQty => Orders.SelectMany(o => o.Items).Sum(i => i.PurchasedQty ?? 0);
    public double TotalExcess      => Orders.SelectMany(o => o.Items).Sum(i => i.ExcessQty);
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
    public double   ExcessQty         => PurchasedQty.HasValue && PurchasedQty.Value > Qty ? (PurchasedQty.Value - Qty) : 0;
    public bool     HasExcess         => ExcessQty > 0;
    public string   ExcessDisplay     => HasExcess ? $"+{ExcessQty:0.##} EXCESS" : "";
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

public class MasterlistDropdownItem
{
    public int       Id            { get; set; }
    public string    Name          { get; set; } = "";
    public string    RawName       { get; set; } = "";
    public string    Status        { get; set; } = "";
    public string?   BatchCode     { get; set; }
    public string?   ControlNumber { get; set; }
    public DateTime? StartDate     { get; set; }
    public bool IsAll => Id == -1;
    public override string ToString() => Name;
}

public class MasterlistSummary
{
    public int      MasterlistId           { get; set; }
    public string   ControlNumber          { get; set; } = "";
    public string   Name                   { get; set; } = "";
    public DateTime StartDate              { get; set; }
    public int      OrderCount             { get; set; }
    public int      OrdersWithPendingItems { get; set; }
    public double   TotalClientQty         { get; set; }
    public double   TotalSupplierQty       { get; set; }
    public double   TotalExcess            { get; set; }
    public decimal  TotalUnpaidBalance     { get; set; }
}
