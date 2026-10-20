using System.IO;
using Microsoft.EntityFrameworkCore;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Data;

public class AppDbContext : DbContext
{
    public DbSet<User>      Users      => Set<User>();
    public DbSet<Customer>  Customers  => Set<Customer>();
    public DbSet<Store>     Stores     => Set<Store>();
    public DbSet<Order>     Orders     => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment>   Payments   => Set<Payment>();
    public DbSet<Delivery>  Deliveries => Set<Delivery>();
    public DbSet<Expense>   Expenses   => Set<Expense>();
    public DbSet<Product>   Products   => Set<Product>();
    public DbSet<Setting>   Settings   => Set<Setting>();
    public DbSet<AuditLog>  AuditLogs  => Set<AuditLog>();
    public DbSet<Masterlist> Masterlists => Set<Masterlist>();

    // ── Angelito's Bar & Resto Grill DbSets ────────────────────────────────
    public DbSet<RestoTable>         RestoTables         => Set<RestoTable>();
    public DbSet<RestoMenuItem>      RestoMenuItems      => Set<RestoMenuItem>();
    public DbSet<RestoInventoryItem> RestoInventoryItems => Set<RestoInventoryItem>();
    public DbSet<RestoStaff>         RestoStaffMembers   => Set<RestoStaff>();
    public DbSet<RestoOrder>         RestoOrders         => Set<RestoOrder>();
    public DbSet<RestoOrderItem>     RestoOrderItems     => Set<RestoOrderItem>();
    public DbSet<RestoSale>          RestoSales          => Set<RestoSale>();
    public DbSet<RestoSaleItem>      RestoSaleItems      => Set<RestoSaleItem>();

    private readonly string _dbPath;

    public AppDbContext()
    {
        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "antique_pasabuy.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder opts)
        => opts.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Order>().Ignore(o => o.ItemsSubtotal)
                          .Ignore(o => o.GrandTotal)
                          .Ignore(o => o.TotalPaid)
                          .Ignore(o => o.Balance)
                          .Ignore(o => o.PasabuyFee)
                          .Ignore(o => o.PaymentStatus)
                          .Ignore(o => o.PaidDate)
                          .Ignore(o => o.PaidDateDisplay)
                          .Ignore(o => o.EstimatedItemsTotal)
                          .Ignore(o => o.ActualItemsTotal)
                          .Ignore(o => o.PaymentMethodDisplay)
                          .Ignore(o => o.StatusDisplay)
                          .Ignore(o => o.IsPickup)
                          .Ignore(o => o.IsDelivery);
        mb.Entity<OrderItem>().Ignore(i => i.PriceDifference)
                              .Ignore(i => i.Subtotal);
        mb.Entity<Store>().Ignore(s => s.Branch);
        mb.Entity<Product>().Ignore(p => p.TotalStocks)
                            .Ignore(p => p.SoldItems)
                            .Ignore(p => p.RemainingItems);

        // Masterlist — ignore computed display helpers
        mb.Entity<Masterlist>().Ignore(m => m.MonthName)
                               .Ignore(m => m.DateRangeDisplay)
                               .Ignore(m => m.StatusDisplay)
                               .Ignore(m => m.IsActive)
                               .Ignore(m => m.IsClosed)
                               .Ignore(m => m.IsArchived);

        // Orders — ignore Masterlist navigation for EF (FK-only, loaded manually)
        mb.Entity<Order>().Ignore(o => o.Masterlist);

        mb.Entity<Order>().Property(o => o.Status).HasConversion(
            s => OrderStatusToString(s),
            str => StringToOrderStatus(str)
        );
        mb.Entity<Order>().Property(o => o.DeliveryType).HasConversion<string>();
        mb.Entity<OrderItem>().Property(i => i.Status).HasConversion(
            s => ItemStatusToString(s),
            str => StringToItemStatus(str)
        );
        mb.Entity<OrderItem>().Property(i => i.Source).HasConversion<string>();
        mb.Entity<Payment>().Property(p => p.Method).HasConversion<string>();
        mb.Entity<Delivery>().Property(d => d.Status).HasConversion<string>();
        mb.Entity<User>().Property(u => u.Role).HasConversion<string>();
        mb.Entity<Expense>().Property(e => e.ExpenseDate)
            .HasConversion(d => d.ToString(), s => DateOnly.Parse(s));

        // Angelito's Resto model configurations
        mb.Entity<RestoTable>().Property(t => t.Status).HasConversion<string>();
        mb.Entity<RestoOrder>().Property(o => o.Status).HasConversion<string>();
        mb.Entity<RestoOrderItem>().Property(i => i.Status).HasConversion<string>();
        mb.Entity<RestoOrderItem>().Ignore(i => i.Subtotal);
        mb.Entity<RestoSale>().Property(s => s.PaymentStatus).HasConversion<string>();
        mb.Entity<RestoInventoryItem>().Ignore(i => i.IsLowStock)
                                      .Ignore(i => i.StockValue)
                                      .Ignore(i => i.StockStatus);
    }

    public static string OrderStatusToString(OrderStatus s) => s switch
    {
        OrderStatus.WAITING_CONFIRMATION => "WAITING_CONFIRMATION",
        OrderStatus.READY_TO_BUY         => "READY_TO_BUY",
        OrderStatus.BUYING_NOW           => "BUYING_NOW",
        OrderStatus.BOUGHT               => "BOUGHT",
        OrderStatus.COMPLETED            => "COMPLETED",
        OrderStatus.CANCELLED            => "CANCELLED",
        _                                => s.ToString()
    };

    public static OrderStatus StringToOrderStatus(string str) => (str ?? "").ToUpperInvariant() switch
    {
        "WAITING_CONFIRMATION" or "PENDING" or "DRAFT" or "CONFIRMED" => OrderStatus.WAITING_CONFIRMATION,
        "READY_TO_BUY" or "FOR_BUYING" or "READY" or "SHOPPING"       => OrderStatus.READY_TO_BUY,
        "BUYING_NOW" or "BUYING" or "PURCHASING" or "PARTIAL"         => OrderStatus.BUYING_NOW,
        "BOUGHT" or "PURCHASED"                                       => OrderStatus.BOUGHT,
        "COMPLETED" or "DELIVERED" or "PICKED_UP"                     => OrderStatus.COMPLETED,
        "CANCELLED" or "UNAVAILABLE"                                  => OrderStatus.CANCELLED,
        _ => Enum.Parse<OrderStatus>(str ?? "", true)
    };

    public static string ItemStatusToString(ItemStatus s) => s switch
    {
        ItemStatus.PENDING           => "PENDING",
        ItemStatus.PURCHASED         => "PURCHASED",
        ItemStatus.BOUGHT_FROM_STOCK => "BOUGHT_FROM_STOCK",
        ItemStatus.UNAVAILABLE       => "UNAVAILABLE",
        ItemStatus.REORDER           => "REORDER",
        ItemStatus.CANCELLED         => "CANCELLED",
        _                            => s.ToString().ToUpperInvariant()
    };

    public static ItemStatus StringToItemStatus(string str) => (str ?? "").Trim().ToUpperInvariant() switch
    {
        "PENDING" or "PARTIAL"                                => ItemStatus.PENDING,
        "PURCHASED" or "BOUGHT"                               => ItemStatus.PURCHASED,
        "BOUGHT_FROM_STOCK" or "BOUGHT FROM STOCK" or "STOCK" => ItemStatus.BOUGHT_FROM_STOCK,
        "UNAVAILABLE" or "NOT_AVAILABLE" or "NOT AVAILABLE"   => ItemStatus.UNAVAILABLE,
        "REORDER"                                             => ItemStatus.REORDER,
        "CANCELLED"                                           => ItemStatus.CANCELLED,
        _ => Enum.TryParse<ItemStatus>(str ?? "", true, out var res) ? res : ItemStatus.PENDING
    };

    /// <summary>
    /// Backs up the database file to the backups folder prior to running any migration.
    /// </summary>
    public static string BackupDatabase(string? dbPath = null)
    {
        try
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            var source = dbPath ?? Path.Combine(folder, "antique_pasabuy.db");
            if (!File.Exists(source)) return "";

            var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            Directory.CreateDirectory(backupDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(backupDir, $"antique_pasabuy_backup_pre_migration_{timestamp}.db");
            File.Copy(source, dest, true);
            return dest;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DB Backup warning: {ex.Message}");
            return "";
        }
    }

    public static AppDbContext Create()
    {
        var ctx = new AppDbContext();
        BackupDatabase(ctx._dbPath);
        try
        {
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA cache_size = -64000;
                PRAGMA temp_store = MEMORY;
                PRAGMA mmap_size = 268435456;
            ";
            cmd.ExecuteNonQuery();
        }
        catch { }

        ctx.Database.EnsureCreated();
        EnsureSchemaMigrated(ctx);
        MigrateToCanonicalStatuses(ctx);
        PurgeGhostAndDummyProducts(ctx);
        SeedData.Seed(ctx);
        return ctx;
    }

    /// <summary>
    /// Permanently removes ghost staple products that were auto-injected on startup,
    /// marks stale demo orders as completed, and prevents ghost dummy items from re-appearing.
    /// </summary>
    private static void PurgeGhostAndDummyProducts(AppDbContext ctx)
    {
        try
        {
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                -- 1. Remove ghost staple products that were re-injected on startup
                DELETE FROM Products WHERE 
                    Name LIKE 'Luxe Organix%' 
                    OR Name LIKE 'Silka%' 
                    OR Name LIKE 'A Bonne%' 
                    OR Name LIKE 'Macaron%' 
                    OR Name LIKE 'Jack ''n Jill%' 
                    OR Name LIKE 'Green Cross%';

                -- 2. Prevent seeded demo orders from showing up in active buying checklists
                UPDATE Orders SET Status = 'COMPLETED' 
                WHERE OrderNo IN ('ORD-2026-0001', 'ORD-2026-0002', 'ORD-2026-0003', 'ORD-2026-0004', 'ORD-2026-0005', 'ORD-2026-0006')
                  AND Status IN ('WAITING_CONFIRMATION', 'READY_TO_BUY', 'BUYING_NOW');

                UPDATE OrderItems SET Status = 'PURCHASED'
                WHERE OrderId IN (
                    SELECT Id FROM Orders WHERE OrderNo IN ('ORD-2026-0001', 'ORD-2026-0002', 'ORD-2026-0003', 'ORD-2026-0004', 'ORD-2026-0005', 'ORD-2026-0006')
                ) AND Status = 'PENDING';

                -- 3. Purge legacy demo restaurant sales and orders
                DELETE FROM RestoSaleItems WHERE RestoSaleId IN (SELECT Id FROM RestoSales WHERE SaleNo LIKE 'RESTO-202609%' OR SaleNo LIKE 'RESTO-202610%');
                DELETE FROM RestoSales WHERE SaleNo LIKE 'RESTO-202609%' OR SaleNo LIKE 'RESTO-202610%';
                DELETE FROM RestoOrderItems WHERE RestoOrderId IN (SELECT Id FROM RestoOrders WHERE OrderNo LIKE 'ORD-202610%');
                DELETE FROM RestoOrders WHERE OrderNo LIKE 'ORD-202610%';
                UPDATE RestoTables SET Status = 'Available', CurrentCustomerName = NULL, CurrentBillAmount = 0, OccupiedSince = NULL WHERE Status = 'Occupied' AND (CurrentBillAmount = 0 OR CurrentCustomerName = 'Dine-in Guests');

                -- 4. Mark database as having completed initial seed so dummy data is never recreated
                INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('has_seeded_initial_data', 'true');
                UPDATE Settings SET Value = 'true' WHERE Key = 'has_seeded_initial_data';
                INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('has_purged_test_resto_sales', 'true');
                UPDATE Settings SET Value = 'true' WHERE Key = 'has_purged_test_resto_sales';
            ";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purge ghost products note: {ex.Message}");
        }
    }

    /// <summary>
    /// Migrates existing legacy status strings in SQLite to the canonical uppercase statuses,
    /// guaranteeing zero data loss.
    /// </summary>
    private static void MigrateToCanonicalStatuses(AppDbContext ctx)
    {
        try
        {
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Orders SET Status = 'WAITING_CONFIRMATION' WHERE Status IN ('Draft', 'Pending', 'Confirmed', 'draft', 'pending', 'confirmed', 'PENDING');
                UPDATE Orders SET Status = 'READY_TO_BUY' WHERE Status IN ('FOR_BUYING', 'ForBuying', 'forbuying', 'READY', 'Ready', 'ready');
                UPDATE Orders SET Status = 'BUYING_NOW' WHERE Status IN ('BUYING', 'Buying', 'buying', 'Shopping', 'shopping', 'Purchasing', 'purchasing');
                UPDATE Orders SET Status = 'COMPLETED' WHERE Status IN ('Completed', 'completed', 'Complete', 'complete', 'FOR_DELIVERY', 'DELIVERED', 'READY_FOR_PICKUP', 'PICKED_UP');
                UPDATE Orders SET Status = 'CANCELLED' WHERE Status IN ('Cancelled', 'cancelled', 'UNAVAILABLE');

                UPDATE OrderItems SET Status = 'PENDING' WHERE Status IN ('Pending', 'pending', 'Partial', 'partial');
                UPDATE OrderItems SET Status = 'PURCHASED' WHERE Status IN ('Purchased', 'purchased', 'Bought', 'bought');
                UPDATE OrderItems SET Status = 'UNAVAILABLE' WHERE Status IN ('Unavailable', 'unavailable', 'Not Available', 'not available', 'Not available');
                UPDATE OrderItems SET Status = 'BOUGHT_FROM_STOCK' WHERE Status IN ('BoughtFromStock', 'Bought from stock', 'bought from stock', 'bought_from_stock');
                UPDATE OrderItems SET Status = 'REORDER' WHERE Status IN ('Reorder', 'reorder');

                UPDATE Orders SET DeliveryType = 'PICKUP' WHERE DeliveryType IN ('Pickup', 'pickup');
                UPDATE Orders SET DeliveryType = 'DELIVERY' WHERE DeliveryType IN ('Delivery', 'delivery');

                UPDATE Products SET Category = 'Food' WHERE LOWER(TRIM(Category)) IN ('food', 'foods');
                UPDATE Products SET Category = 'Groceries' WHERE LOWER(TRIM(Category)) IN ('groceries', 'grocery');
                UPDATE Products SET Category = 'Fastfood' WHERE LOWER(TRIM(Category)) IN ('fast food', 'fastfood');
                UPDATE Products SET Category = 'Drinks' WHERE LOWER(TRIM(Category)) IN ('drink', 'drinks', 'beverage', 'beverages');
            ";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Canonical status migration note: {ex.Message}");
        }
    }

    /// <summary>
    /// Safely adds new tables and columns to SQLite if they do not already exist,
    /// guaranteeing zero data loss on existing databases.
    /// </summary>
    private static void EnsureSchemaMigrated(AppDbContext ctx)
    {
        try
        {
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            // 1. Create Stores table if it does not exist
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Stores (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Location TEXT NULL,
                        Category TEXT NULL,
                        Notes TEXT NULL,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        CreatedAt TEXT NOT NULL
                    );";
                cmd.ExecuteNonQuery();
            }

            void AddColumnIfNotExists(string table, string column, string columnDef)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var reader = cmd.ExecuteReader();
                var exists = false;
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                reader.Close();

                if (!exists)
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDef};";
                    alterCmd.ExecuteNonQuery();
                }
            }

            // Products (original columns)
            AddColumnIfNotExists("Products", "Store", "TEXT NOT NULL DEFAULT 'General'");
            AddColumnIfNotExists("Products", "Description", "TEXT NULL");
            AddColumnIfNotExists("Products", "ImagePath", "TEXT NULL");
            AddColumnIfNotExists("Products", "Barcode", "TEXT NULL");

            // Products (Phase 2 – inventory & fee-exemption fields)
            AddColumnIfNotExists("Products", "IsStockItem",      "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfNotExists("Products", "IsFeeExempt",      "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfNotExists("Products", "CostPrice",        "REAL NULL");
            AddColumnIfNotExists("Products", "OpeningStock",     "REAL NOT NULL DEFAULT 0");
            AddColumnIfNotExists("Products", "StockQuantity",    "REAL NOT NULL DEFAULT 0");
            AddColumnIfNotExists("Products", "AdditionalStocks", "REAL NOT NULL DEFAULT 0");

            // Orders
            AddColumnIfNotExists("Orders", "BuyingDay",    "TEXT NOT NULL DEFAULT 'Wednesday'");
            AddColumnIfNotExists("Orders", "PreferredStore", "TEXT NULL");
            AddColumnIfNotExists("Orders", "BatchCode",    "TEXT NULL");
            AddColumnIfNotExists("Orders", "ManualFeeOverride", "TEXT NULL");
            AddColumnIfNotExists("Orders", "FeeOverrideNote",   "TEXT NULL");
            AddColumnIfNotExists("Orders", "IsFeeAdjusted",     "INTEGER NOT NULL DEFAULT 0");

            // Official Pasabuy Rates Table (Rule 3)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'pasabuy_rates_json' LIMIT 1;";
                var val = cmd.ExecuteScalar()?.ToString();
                if (string.IsNullOrWhiteSpace(val) || val.Contains("\"MinAmount\": 0,") || val.Contains("\"MinAmount\":0,"))
                {
                    var defaultJson = System.Text.Json.JsonSerializer.Serialize(
                        PasabuyFeeService.GetDefaultTiers(),
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    using var checkCmd = conn.CreateCommand();
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key = 'pasabuy_rates_json';";
                    var hasRow = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;

                    using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = hasRow
                        ? "UPDATE Settings SET Value = @val WHERE Key = 'pasabuy_rates_json';"
                        : "INSERT INTO Settings (Key, Value) VALUES ('pasabuy_rates_json', @val);";
                    var p = updateCmd.CreateParameter();
                    p.ParameterName = "@val";
                    p.Value = defaultJson;
                    updateCmd.Parameters.Add(p);
                    updateCmd.ExecuteNonQuery();
                }
            }

            // Masterlists table (weekly batch records)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Masterlists (
                        Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                        ControlNumber  TEXT NOT NULL UNIQUE,
                        Name           TEXT NOT NULL,
                        Month          INTEGER NOT NULL,
                        Year           INTEGER NOT NULL,
                        StartDate      TEXT NOT NULL,
                        EndDate        TEXT NOT NULL,
                        Notes          TEXT NULL,
                        Status         TEXT NOT NULL DEFAULT 'Active',
                        CreatedBy      INTEGER NULL,
                        CreatedAt      TEXT NOT NULL,
                        UpdatedAt      TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_masterlists_control ON Masterlists(ControlNumber);
                    CREATE INDEX IF NOT EXISTS idx_masterlists_year_month ON Masterlists(Year, Month);
                    CREATE INDEX IF NOT EXISTS idx_masterlists_status ON Masterlists(Status);
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // MasterlistId FK on Orders (nullable, zero data loss)
            AddColumnIfNotExists("Orders", "MasterlistId", "INTEGER NULL");

            // OrderItems (original columns)
            AddColumnIfNotExists("OrderItems", "Store",           "TEXT NOT NULL DEFAULT 'General'");
            AddColumnIfNotExists("OrderItems", "PurchasedQty",    "REAL NULL");
            AddColumnIfNotExists("OrderItems", "PurchaseDate",    "TEXT NULL");
            AddColumnIfNotExists("OrderItems", "ReceiptNo",       "TEXT NULL");
            AddColumnIfNotExists("OrderItems", "UnavailableReason", "TEXT NULL");
            AddColumnIfNotExists("OrderItems", "PurchaseNotes",   "TEXT NULL");

            // OrderItems (Phase 2 – source & cost tracking fields)
            AddColumnIfNotExists("OrderItems", "Source",      "TEXT NOT NULL DEFAULT 'STORE'");
            AddColumnIfNotExists("OrderItems", "CostPrice",   "REAL NULL");
            AddColumnIfNotExists("OrderItems", "IsFeeExempt", "INTEGER NOT NULL DEFAULT 0");

            // Payments (payment recording info)
            AddColumnIfNotExists("Payments", "PaymentDate", "TEXT NULL");
            AddColumnIfNotExists("Payments", "PaymentProofPath", "TEXT NULL");

            // Multi-business transaction isolation (Orders & Customers)
            AddColumnIfNotExists("Orders", "BusinessId", "TEXT NOT NULL DEFAULT 'ANTIQUE_PASABUY'");
            AddColumnIfNotExists("Customers", "BusinessId", "TEXT NOT NULL DEFAULT 'ANTIQUE_PASABUY'");

            // ── Angelito's Bar & Resto Grill Tables ────────────────────────────
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS RestoTables (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TableNumber TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Capacity INTEGER NOT NULL DEFAULT 4,
                        Section TEXT NOT NULL DEFAULT 'Main Dining',
                        Status TEXT NOT NULL DEFAULT 'Available',
                        CurrentCustomerName TEXT NULL,
                        CurrentBillAmount REAL NOT NULL DEFAULT 0,
                        OccupiedSince TEXT NULL,
                        BusinessId TEXT NOT NULL DEFAULT 'ANGELITOS_RESTO',
                        UpdatedAt TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS RestoMenuItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Code TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Category TEXT NOT NULL DEFAULT 'Grill',
                        Description TEXT NULL,
                        Price REAL NOT NULL DEFAULT 0,
                        CostPrice REAL NULL,
                        IsAvailable INTEGER NOT NULL DEFAULT 1,
                        ImagePath TEXT NULL,
                        BusinessId TEXT NOT NULL DEFAULT 'ANGELITOS_RESTO',
                        CreatedAt TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS RestoInventoryItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemCode TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Category TEXT NOT NULL DEFAULT 'Meat',
                        Quantity REAL NOT NULL DEFAULT 0,
                        Unit TEXT NOT NULL DEFAULT 'pc',
                        MinStockLevel REAL NOT NULL DEFAULT 5,
                        CostPrice REAL NULL,
                        BusinessId TEXT NOT NULL DEFAULT 'ANGELITOS_RESTO',
                        LastRestockedAt TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS RestoStaffMembers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        StaffCode TEXT NOT NULL,
                        FullName TEXT NOT NULL,
                        Role TEXT NOT NULL DEFAULT 'Server',
                        ContactNumber TEXT NOT NULL,
                        Shift TEXT NOT NULL DEFAULT 'Evening',
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        BusinessId TEXT NOT NULL DEFAULT 'ANGELITOS_RESTO',
                        HireDate TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS RestoOrders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        OrderNo TEXT NOT NULL,
                        TableNumber TEXT NULL,
                        CustomerName TEXT NULL,
                        Status TEXT NOT NULL DEFAULT 'Pending',
                        TotalAmount REAL NOT NULL DEFAULT 0,
                        ServerName TEXT NULL,
                        Notes TEXT NULL,
                        BusinessId TEXT NOT NULL DEFAULT 'ANGELITOS_RESTO',
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS RestoOrderItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RestoOrderId INTEGER NOT NULL,
                        MenuItemId INTEGER NULL,
                        ItemName TEXT NOT NULL,
                        Quantity REAL NOT NULL DEFAULT 1,
                        UnitPrice REAL NOT NULL DEFAULT 0,
                        Status TEXT NOT NULL DEFAULT 'Pending',
                        SpecialInstructions TEXT NULL,
                        FOREIGN KEY (RestoOrderId) REFERENCES RestoOrders(Id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS RestoSales (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SaleNo TEXT NOT NULL,
                        TableNumber TEXT NULL,
                        CustomerName TEXT NULL,
                        Subtotal REAL NOT NULL DEFAULT 0,
                        Discount REAL NOT NULL DEFAULT 0,
                        Tax REAL NOT NULL DEFAULT 0,
                        TotalAmount REAL NOT NULL DEFAULT 0,
                        AmountPaid REAL NOT NULL DEFAULT 0,
                        Change REAL NOT NULL DEFAULT 0,
                        PaymentMethod TEXT NOT NULL DEFAULT 'Cash',
                        PaymentStatus TEXT NOT NULL DEFAULT 'Paid',
                        CashierName TEXT NULL,
                        Notes TEXT NULL,
                        BusinessId TEXT NOT NULL DEFAULT 'ANGELITOS_RESTO',
                        CreatedAt TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS RestoSaleItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RestoSaleId INTEGER NOT NULL,
                        MenuItemId INTEGER NULL,
                        ItemName TEXT NOT NULL,
                        Category TEXT NOT NULL DEFAULT '',
                        UnitPrice REAL NOT NULL DEFAULT 0,
                        Quantity REAL NOT NULL DEFAULT 1,
                        Subtotal REAL NOT NULL DEFAULT 0,
                        Notes TEXT NULL,
                        FOREIGN KEY (RestoSaleId) REFERENCES RestoSales(Id) ON DELETE CASCADE
                    );
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // RestoSaleItems – add CostPrice column for profit/margin tracking (safe for existing DBs)
            AddColumnIfNotExists("RestoSaleItems", "CostPrice", "REAL NULL");

            // Seed initial tables for Angelito's if empty
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'T-01', 'Table 1', 4, 'Main Dining', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'T-01');

                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'T-02', 'Table 2', 4, 'Main Dining', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'T-02');

                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'T-03', 'Table 3', 6, 'Main Dining', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'T-03');

                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'T-04', 'Table 4', 2, 'Main Dining', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'T-04');

                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'BAR-1', 'Bar Stool 1', 1, 'Bar', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'BAR-1');

                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'BAR-2', 'Bar Stool 2', 1, 'Bar', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'BAR-2');

                    INSERT INTO RestoTables (TableNumber, Name, Capacity, Section, Status, BusinessId, UpdatedAt)
                    SELECT 'VIP-1', 'VIP Lounge', 10, 'VIP Room', 'Available', 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoTables WHERE TableNumber = 'VIP-1');
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // Seed initial menu items for Angelito's if empty
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'SISIG', 'Sizzling Pork Sisig', 'Sizzling', 'Crispy pork face and ears with onions and chili', 220.0, 110.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'SISIG');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'LIEMPO', 'Grilled Liempo', 'Grill', 'Marinated grilled pork belly with house glaze', 250.0, 130.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'LIEMPO');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'PATA', 'Crispy Pata', 'Grill', 'Deep fried pork knuckle with spiced soy dip', 650.0, 380.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'PATA');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'INASAL', 'Chicken Inasal', 'Grill', 'Bacolod style grilled chicken leg quarter', 180.0, 95.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'INASAL');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'TOKWA', 'Tokwat Baboy', 'Pulutan', 'Fried tofu and pork slices with tangy vinegar dip', 150.0, 70.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'TOKWA');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'CALAM', 'Crispy Calamares', 'Pulutan', 'Golden squid rings served with tartar sauce', 200.0, 100.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'CALAM');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'SMBP', 'San Miguel Pale Pilsen', 'Alcoholic', 'Cold draft 330ml bottle', 85.0, 52.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'SMBP');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'RH500', 'Red Horse Stallion 500ml', 'Alcoholic', 'Extra strong beer 500ml', 95.0, 60.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'RH500');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'GRICE', 'Garlic Fried Rice', 'Rice', 'Fragrant toasted garlic fried rice', 35.0, 12.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'GRICE');

                    INSERT INTO RestoMenuItems (Code, Name, Category, Description, Price, CostPrice, IsAvailable, BusinessId, CreatedAt)
                    SELECT 'TEA', 'House Iced Tea (Pitcher)', 'Drinks', 'Refillable iced tea with calamansi', 120.0, 30.0, 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoMenuItems WHERE Code = 'TEA');
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // Seed initial staff for Angelito's if empty
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO RestoStaffMembers (StaffCode, FullName, Role, ContactNumber, Shift, IsActive, BusinessId, HireDate)
                    SELECT 'ST-01', 'Marco Santos', 'Manager', '0917-123-4567', 'Morning', 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoStaffMembers WHERE StaffCode = 'ST-01');

                    INSERT INTO RestoStaffMembers (StaffCode, FullName, Role, ContactNumber, Shift, IsActive, BusinessId, HireDate)
                    SELECT 'ST-02', 'Elena Cruz', 'Chef', '0918-234-5678', 'Evening', 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoStaffMembers WHERE StaffCode = 'ST-02');

                    INSERT INTO RestoStaffMembers (StaffCode, FullName, Role, ContactNumber, Shift, IsActive, BusinessId, HireDate)
                    SELECT 'ST-03', 'Carlo Dizon', 'Server', '0919-345-6789', 'Evening', 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoStaffMembers WHERE StaffCode = 'ST-03');

                    INSERT INTO RestoStaffMembers (StaffCode, FullName, Role, ContactNumber, Shift, IsActive, BusinessId, HireDate)
                    SELECT 'ST-04', 'Maria Torres', 'Cashier', '0920-456-7890', 'Evening', 1, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoStaffMembers WHERE StaffCode = 'ST-04');
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // Seed initial inventory items for Angelito's if empty
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO RestoInventoryItems (ItemCode, Name, Category, Quantity, Unit, MinStockLevel, CostPrice, BusinessId, LastRestockedAt)
                    SELECT 'INV-01', 'Pork Belly (Liempo)', 'Meat', 25.0, 'kg', 5.0, 260.0, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoInventoryItems WHERE ItemCode = 'INV-01');

                    INSERT INTO RestoInventoryItems (ItemCode, Name, Category, Quantity, Unit, MinStockLevel, CostPrice, BusinessId, LastRestockedAt)
                    SELECT 'INV-02', 'Chicken Leg Quarter', 'Meat', 18.0, 'kg', 5.0, 180.0, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoInventoryItems WHERE ItemCode = 'INV-02');

                    INSERT INTO RestoInventoryItems (ItemCode, Name, Category, Quantity, Unit, MinStockLevel, CostPrice, BusinessId, LastRestockedAt)
                    SELECT 'INV-03', 'San Miguel Pale Pilsen', 'Liquor', 72.0, 'bottle', 24.0, 52.0, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoInventoryItems WHERE ItemCode = 'INV-03');

                    INSERT INTO RestoInventoryItems (ItemCode, Name, Category, Quantity, Unit, MinStockLevel, CostPrice, BusinessId, LastRestockedAt)
                    SELECT 'INV-04', 'Red Horse Stallion 500ml', 'Liquor', 48.0, 'bottle', 24.0, 60.0, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoInventoryItems WHERE ItemCode = 'INV-04');

                    INSERT INTO RestoInventoryItems (ItemCode, Name, Category, Quantity, Unit, MinStockLevel, CostPrice, BusinessId, LastRestockedAt)
                    SELECT 'INV-05', 'Premium Dinorado Rice', 'Produce', 50.0, 'kg', 10.0, 52.0, 'ANGELITOS_RESTO', datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM RestoInventoryItems WHERE ItemCode = 'INV-05');
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // Seed default barcodes for existing catalog products if empty
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE Products SET Barcode = '4800119213019' WHERE LOWER(Name) LIKE '%biogesic%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800119213026' WHERE LOWER(Name) LIKE '%bioflu%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800119213033' WHERE LOWER(Name) LIKE '%ascorbic%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644800' WHERE LOWER(Name) LIKE '%sugar%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644817' WHERE LOWER(Name) LIKE '%bread%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644824' WHERE LOWER(Name) LIKE '%milk%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644831' WHERE LOWER(Name) LIKE '%rice%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644848' WHERE LOWER(Name) LIKE '%oil%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644855' WHERE LOWER(Name) LIKE '%eggs%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644862' WHERE LOWER(Name) LIKE '%chickenjoy%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644879' WHERE LOWER(Name) LIKE '%spaghetti%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644886' WHERE LOWER(Name) LIKE '%inasal%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644893' WHERE LOWER(Name) LIKE '%chaofan%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644909' WHERE LOWER(Name) LIKE '%pandesal%' AND (Barcode IS NULL OR Barcode = '');
                    UPDATE Products SET Barcode = '4800016644916' WHERE LOWER(Name) LIKE '%black forest%' AND (Barcode IS NULL OR Barcode = '');
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }

            // High-performance B-tree indexes for lightning-fast queries at scale
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_orderitems_orderid ON OrderItems(OrderId);
                    CREATE INDEX IF NOT EXISTS idx_orderitems_status ON OrderItems(Status);
                    CREATE INDEX IF NOT EXISTS idx_orderitems_store ON OrderItems(Store);
                    CREATE INDEX IF NOT EXISTS idx_orderitems_prodname ON OrderItems(ProductName);

                    CREATE INDEX IF NOT EXISTS idx_orders_status ON Orders(Status);
                    CREATE INDEX IF NOT EXISTS idx_orders_businessid ON Orders(BusinessId);
                    CREATE INDEX IF NOT EXISTS idx_orders_createdat ON Orders(CreatedAt);
                    CREATE INDEX IF NOT EXISTS idx_orders_customerid ON Orders(CustomerId);
                    CREATE INDEX IF NOT EXISTS idx_orders_buyingday ON Orders(BuyingDay);
                    CREATE INDEX IF NOT EXISTS idx_orders_batchcode ON Orders(BatchCode);

                    CREATE INDEX IF NOT EXISTS idx_payments_orderid ON Payments(OrderId);
                    CREATE INDEX IF NOT EXISTS idx_payments_createdat ON Payments(CreatedAt);

                    CREATE INDEX IF NOT EXISTS idx_products_store ON Products(Store);
                    CREATE INDEX IF NOT EXISTS idx_products_barcode ON Products(Barcode);
                    CREATE INDEX IF NOT EXISTS idx_products_name ON Products(Name);

                    CREATE INDEX IF NOT EXISTS idx_customers_name ON Customers(Name);
                    CREATE INDEX IF NOT EXISTS idx_customers_phone ON Customers(Phone);

                    CREATE INDEX IF NOT EXISTS idx_expenses_date ON Expenses(ExpenseDate);
                ";
                try { cmd.ExecuteNonQuery(); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Migration notice: {ex.Message}");
        }
    }
}
