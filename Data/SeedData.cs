using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Data;

public static class SeedData
{
    public static void Seed(AppDbContext ctx)
    {
        // 1. Users
        if (!ctx.Users.Any())
        {
            ctx.Users.AddRange(
                new User
                {
                    Username     = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                    FullName     = "Administrator",
                    Role         = UserRole.Admin,
                    IsActive     = true,
                },
                new User
                {
                    Username     = "cashier",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("cashier123"),
                    FullName     = "Maria Santos (Staff)",
                    Role         = UserRole.Staff,
                    IsActive     = true,
                },
                new User
                {
                    Username     = "rider",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("rider123"),
                    FullName     = "John Doe (Rider)",
                    Role         = UserRole.Shopper,
                    IsActive     = true,
                }
            );
            ctx.SaveChanges();
        }

        // 2. Settings
        if (!ctx.Settings.Any())
        {
            var defaults = new Dictionary<string, string>
            {
                ["business_name"]        = "DIONIX POS",
                ["theme"]                = "light",
                ["default_service_fee"]  = "30",
                ["default_delivery_fee"] = "50",
                ["receipt_footer"]       = "Thank you for trusting DIONIX POS!",
                ["default_printer"]      = "",
            };
            foreach (var kv in defaults)
                ctx.Settings.Add(new Setting { Key = kv.Key, Value = kv.Value });
            ctx.SaveChanges();
        }

        // 2b. Official Pasabuy Rates Table
        if (!ctx.Settings.Any(s => s.Key == Services.PasabuyFeeService.SettingsKey))
        {
            Services.PasabuyFeeService.ResetToDefaults(ctx);
        }

        // Once initial seeding has been executed once, NEVER re-seed dummy/example
        // products, orders, or items so user deletions are permanently respected.
        if (ctx.Settings.Any(s => s.Key == "has_seeded_initial_data" && s.Value == "true"))
        {
            return;
        }

        // 3. Customers
        if (!ctx.Customers.Any())
        {
            ctx.Customers.AddRange(
                new Customer
                {
                    Code     = "CUST-001",
                    Name     = "Dionimar Flores",
                    Phone    = "0917-888-1234",
                    Address  = "Brgy. Funda-Dalipe, San Jose de Buenavista, Antique",
                    Notes    = "Preferred delivery in the afternoon.",
                    IsActive = true
                },
                new Customer
                {
                    Code     = "CUST-002",
                    Name     = "Elena Ramos",
                    Phone    = "0928-555-4321",
                    Address  = "Brgy. District 1, Sibalom, Antique",
                    Notes    = "Near Sibalom Public Market.",
                    IsActive = true
                },
                new Customer
                {
                    Code     = "CUST-003",
                    Name     = "Roberto Gomez",
                    Phone    = "0919-333-8765",
                    Address  = "Brgy. Poblacion 3, Hamtic, Antique",
                    Notes    = "Landmark: Yellow gate near Chapel.",
                    IsActive = true
                },
                new Customer
                {
                    Code     = "CUST-004",
                    Name     = "Ana Reyes",
                    Phone    = "0920-111-2345",
                    Address  = "Brgy. Poblacion, Belison, Antique",
                    Notes    = "Regular medicine pasabuy client.",
                    IsActive = true
                },
                new Customer
                {
                    Code     = "CUST-005",
                    Name     = "Juan Dela Cruz",
                    Phone    = "0918-999-7788",
                    Address  = "Brgy. San Fernando, San Jose, Antique",
                    Notes    = "Call before arriving.",
                    IsActive = true
                }
            );
            ctx.SaveChanges();
        }

        // 3. Stores (Antique Pasabuy Stores)
        if (!ctx.Stores.Any())
        {
            ctx.Stores.AddRange(
                new Store { Name = "SM", Location = "San Jose de Buenavista", Category = "Department Store / Supermarket", Notes = "Major weekly pasabuy stop" },
                new Store { Name = "Robinsons", Location = "San Jose de Buenavista", Category = "Supermarket / Department Store", Notes = "Robinsons Supermarket & Mall" },
                new Store { Name = "Grocery Store", Location = "San Jose de Buenavista", Category = "Grocery", Notes = "Puregold & local supermarkets" },
                new Store { Name = "Mercury Drug", Location = "Poblacion, San Jose", Category = "Pharmacy", Notes = "Prescriptions & OTC vitamins" },
                new Store { Name = "Bakery", Location = "San Jose de Buenavista", Category = "Bakery", Notes = "Tibiao Bakery & Red Ribbon" },
                new Store { Name = "Fast Food", Location = "San Jose Town Center", Category = "Fast Food", Notes = "Jollibee, Mang Inasal, Chowking" },
                new Store { Name = "Other Stores in Antique", Location = "Antique", Category = "General Store", Notes = "Specialty shops & markets" }
            );
            ctx.SaveChanges();
        }

        // 4. Products (Popular Pasabuy staples in Antique with designated stores)
        if (!ctx.Products.Any())
        {
            ctx.Products.AddRange(
                new Product { Name = "Jollibee 6pc Chickenjoy Bucket", Category = "Fastfood", Store = "Fast Food", Price = 450.00m, Unit = "bucket", Barcode = "4800016644862" },
                new Product { Name = "Jollibee Jolly Spaghetti Family Pan", Category = "Fastfood", Store = "Fast Food", Price = 250.00m, Unit = "pan", Barcode = "4800016644879" },
                new Product { Name = "Mang Inasal Chicken Inasal Paa Meal", Category = "Fastfood", Store = "Fast Food", Price = 165.00m, Unit = "meal", Barcode = "4800016644886" },
                new Product { Name = "Chowking Pork Chaofan w/ Siomai", Category = "Fastfood", Store = "Fast Food", Price = 135.00m, Unit = "meal", Barcode = "4800016644893" },
                new Product { Name = "SM Bonus Sugar 1kg", Category = "Groceries", Store = "SM", Price = 78.00m, Unit = "pack", Barcode = "4800016644800" },
                new Product { Name = "SM Premium White Bread", Category = "Bakery", Store = "SM", Price = 65.00m, Unit = "pack", Barcode = "4800016644817" },
                new Product { Name = "Robinsons Supermarket Fresh Milk 1L", Category = "Groceries", Store = "Robinsons", Price = 98.00m, Unit = "carton", Barcode = "4800016644824" },
                new Product { Name = "Puregold Sinandomeng Rice 5kg", Category = "Groceries", Store = "Grocery Store", Price = 280.00m, Unit = "bag", Barcode = "4800016644831" },
                new Product { Name = "Puregold Cooking Oil 1L", Category = "Groceries", Store = "Grocery Store", Price = 95.00m, Unit = "bottle", Barcode = "4800016644848" },
                new Product { Name = "Fresh Farm Eggs (1 Dozen)", Category = "Groceries", Store = "Grocery Store", Price = 120.00m, Unit = "dozen", Barcode = "4800016644855" },
                new Product { Name = "Mercury Drug Biogesic 500mg (10s)", Category = "Pharmacy", Store = "Mercury Drug", Price = 85.00m, Unit = "pack", Barcode = "4800119213019" },
                new Product { Name = "Mercury Drug Bioflu 500mg (10s)", Category = "Pharmacy", Store = "Mercury Drug", Price = 95.00m, Unit = "pack", Barcode = "4800119213026" },
                new Product { Name = "Ascorbic Acid Vitamin C 500mg 100s", Category = "Pharmacy", Store = "Mercury Drug", Price = 280.00m, Unit = "box", Barcode = "4800119213033" },
                new Product { Name = "Tibiao Bakery Special Pandesal Bag", Category = "Bakery", Store = "Bakery", Price = 60.00m, Unit = "pack", Barcode = "4800016644909" },
                new Product { Name = "Red Ribbon Black Forest Cake", Category = "Bakery", Store = "Bakery", Price = 550.00m, Unit = "cake", Barcode = "4800016644916" }
            );
            ctx.SaveChanges();
        }



        // 5. Orders, Items, Payments, Deliveries
        if (!ctx.Orders.Any())
        {
            var custDionimar = ctx.Customers.FirstOrDefault(c => c.Name.Contains("Dionimar")) ?? ctx.Customers.First();
            var custElena    = ctx.Customers.FirstOrDefault(c => c.Name.Contains("Elena"))    ?? ctx.Customers.First();
            var custRoberto  = ctx.Customers.FirstOrDefault(c => c.Name.Contains("Roberto"))  ?? ctx.Customers.First();
            var custAna      = ctx.Customers.FirstOrDefault(c => c.Name.Contains("Ana"))      ?? ctx.Customers.First();
            var custJuan     = ctx.Customers.FirstOrDefault(c => c.Name.Contains("Juan"))     ?? ctx.Customers.First();
            var adminUser    = ctx.Users.FirstOrDefault(u => u.Role == UserRole.Admin);

            // Order 1 - Delivered
            var o1 = new Order
            {
                OrderNo      = "ORD-2026-0001",
                CustomerId   = custDionimar.Id,
                Status       = OrderStatus.Delivered,
                DeliveryType = DeliveryType.Delivery,
                Address      = custDionimar.Address,
                Notes        = "Include extra gravy and hot sauce.",
                ServiceFee   = 30.00m,
                DeliveryFee  = 50.00m,
                Discount     = 0.00m,
                CreatedBy    = adminUser?.Id,
                CreatedAt    = DateTime.Now.AddDays(-2).AddHours(3),
                UpdatedAt    = DateTime.Now.AddDays(-2).AddHours(4),
            };
            ctx.Orders.Add(o1);
            ctx.SaveChanges();

            ctx.OrderItems.AddRange(
                new OrderItem { OrderId = o1.Id, ProductName = "Jollibee 6pc Chickenjoy Bucket", Qty = 1, Unit = "bucket", EstimatedPrice = 450.00m, ActualPrice = 450.00m },
                new OrderItem { OrderId = o1.Id, ProductName = "Jollibee Jolly Spaghetti Family Pan", Qty = 1, Unit = "pan", EstimatedPrice = 250.00m, ActualPrice = 250.00m }
            );
            ctx.Payments.Add(new Payment { OrderId = o1.Id, Amount = 780.00m, Method = PaymentMethod.Cash, Reference = "CASH-PAID", CreatedAt = o1.CreatedAt.AddHours(1) });
            ctx.Deliveries.Add(new Delivery { OrderId = o1.Id, RiderName = "John Doe (Rider)", RiderPhone = "0917-111-2222", Status = DeliveryStatus.Delivered, PickedUpAt = o1.CreatedAt.AddMinutes(30), DeliveredAt = o1.CreatedAt.AddMinutes(55), CreatedAt = o1.CreatedAt });

            // Order 2 - Delivered
            var o2 = new Order
            {
                OrderNo      = "ORD-2026-0002",
                CustomerId   = custElena.Id,
                Status       = OrderStatus.Delivered,
                DeliveryType = DeliveryType.Delivery,
                Address      = custElena.Address,
                Notes        = "Heavy items, handle with care.",
                ServiceFee   = 30.00m,
                DeliveryFee  = 50.00m,
                Discount     = 0.00m,
                CreatedBy    = adminUser?.Id,
                CreatedAt    = DateTime.Now.AddDays(-1).AddHours(2),
                UpdatedAt    = DateTime.Now.AddDays(-1).AddHours(3),
            };
            ctx.Orders.Add(o2);
            ctx.SaveChanges();

            ctx.OrderItems.AddRange(
                new OrderItem { OrderId = o2.Id, ProductName = "Puregold Sinandomeng Rice 5kg", Qty = 1, Unit = "bag", EstimatedPrice = 280.00m, ActualPrice = 280.00m },
                new OrderItem { OrderId = o2.Id, ProductName = "Puregold Cooking Oil 1L", Qty = 1, Unit = "bottle", EstimatedPrice = 95.00m, ActualPrice = 95.00m }
            );
            ctx.Payments.Add(new Payment { OrderId = o2.Id, Amount = 455.00m, Method = PaymentMethod.GCash, Reference = "GCASH-987654", CreatedAt = o2.CreatedAt.AddMinutes(20) });
            ctx.Deliveries.Add(new Delivery { OrderId = o2.Id, RiderName = "John Doe (Rider)", RiderPhone = "0917-111-2222", Status = DeliveryStatus.Delivered, PickedUpAt = o2.CreatedAt.AddMinutes(30), DeliveredAt = o2.CreatedAt.AddMinutes(50), CreatedAt = o2.CreatedAt });

            // Order 3 - Ready for Pickup
            var o3 = new Order
            {
                OrderNo      = "ORD-2026-0003",
                CustomerId   = custRoberto.Id,
                Status       = OrderStatus.Ready,
                DeliveryType = DeliveryType.Pickup,
                Address      = "Store Counter",
                Notes        = "Customer will pick up by 4:00 PM.",
                ServiceFee   = 30.00m,
                DeliveryFee  = 0.00m,
                Discount     = 0.00m,
                CreatedBy    = adminUser?.Id,
                CreatedAt    = DateTime.Now.AddHours(-4),
                UpdatedAt    = DateTime.Now.AddHours(-1),
            };
            ctx.Orders.Add(o3);
            ctx.SaveChanges();

            ctx.OrderItems.AddRange(
                new OrderItem { OrderId = o3.Id, ProductName = "Dunkin' Famous Bundle (6 Donuts)", Qty = 1, Unit = "box", EstimatedPrice = 349.00m, ActualPrice = 349.00m },
                new OrderItem { OrderId = o3.Id, ProductName = "Tibiao Bakery Special Pandesal Bag", Qty = 1, Unit = "pack", EstimatedPrice = 60.00m, ActualPrice = 60.00m }
            );
            ctx.Payments.Add(new Payment { OrderId = o3.Id, Amount = 439.00m, Method = PaymentMethod.Cash, Reference = "PAID-COUNTER", CreatedAt = o3.CreatedAt });

            // Order 4 - Bought (medicine run completed)
            var o4 = new Order
            {
                OrderNo      = "ORD-2026-0004",
                CustomerId   = custAna.Id,
                Status       = OrderStatus.BOUGHT,
                DeliveryType = DeliveryType.Delivery,
                Address      = custAna.Address,
                Notes        = "Buy generic if Mercury brand not available.",
                ServiceFee   = 30.00m,
                DeliveryFee  = 50.00m,
                Discount     = 0.00m,
                CreatedBy    = adminUser?.Id,
                CreatedAt    = DateTime.Now.AddHours(-2),
                UpdatedAt    = DateTime.Now.AddHours(-1),
            };
            ctx.Orders.Add(o4);
            ctx.SaveChanges();

            ctx.OrderItems.AddRange(
                new OrderItem { OrderId = o4.Id, ProductName = "Mercury Drug Biogesic 500mg (10s)", Qty = 2, Unit = "pack", EstimatedPrice = 85.00m, ActualPrice = 85.00m, Status = ItemStatus.PURCHASED, PurchasedQty = 2, PurchaseDate = DateTime.Now.AddHours(-1) },
                new OrderItem { OrderId = o4.Id, ProductName = "Mercury Drug Bioflu 500mg (10s)", Qty = 1, Unit = "pack", EstimatedPrice = 95.00m, ActualPrice = 95.00m, Status = ItemStatus.PURCHASED, PurchasedQty = 1, PurchaseDate = DateTime.Now.AddHours(-1) },
                new OrderItem { OrderId = o4.Id, ProductName = "Ascorbic Acid Vitamin C 500mg 100s", Qty = 1, Unit = "box", EstimatedPrice = 280.00m, ActualPrice = 280.00m, Status = ItemStatus.PURCHASED, PurchasedQty = 1, PurchaseDate = DateTime.Now.AddHours(-1) }
            );

            // Order 5 - Confirmed (Ready to shop)
            var o5 = new Order
            {
                OrderNo      = "ORD-2026-0005",
                CustomerId   = custJuan.Id,
                Status       = OrderStatus.Confirmed,
                DeliveryType = DeliveryType.Delivery,
                Address      = custJuan.Address,
                Notes        = "Spicy inasal preferred.",
                ServiceFee   = 30.00m,
                DeliveryFee  = 50.00m,
                Discount     = 0.00m,
                CreatedBy    = adminUser?.Id,
                CreatedAt    = DateTime.Now.AddMinutes(-45),
                UpdatedAt    = DateTime.Now.AddMinutes(-30),
            };
            ctx.Orders.Add(o5);
            ctx.SaveChanges();

            ctx.OrderItems.AddRange(
                new OrderItem { OrderId = o5.Id, ProductName = "Mang Inasal Chicken Inasal Paa Meal", Qty = 2, Unit = "meal", EstimatedPrice = 165.00m, ActualPrice = 165.00m },
                new OrderItem { OrderId = o5.Id, ProductName = "Chowking Pork Chaofan w/ Siomai", Qty = 1, Unit = "meal", EstimatedPrice = 135.00m, ActualPrice = 135.00m }
            );

            // Order 6 - Completed (cake pickup done)
            var o6 = new Order
            {
                OrderNo      = "ORD-2026-0006",
                CustomerId   = custDionimar.Id,
                Status       = OrderStatus.COMPLETED,
                DeliveryType = DeliveryType.Pickup,
                Address      = "Store Pickup",
                Notes        = "Dedication on cake: Happy Birthday Nanay!",
                ServiceFee   = 30.00m,
                DeliveryFee  = 0.00m,
                Discount     = 0.00m,
                CreatedBy    = adminUser?.Id,
                CreatedAt    = DateTime.Now.AddMinutes(-15),
                UpdatedAt    = DateTime.Now.AddMinutes(-15),
            };
            ctx.Orders.Add(o6);
            ctx.SaveChanges();

            ctx.OrderItems.Add(new OrderItem { OrderId = o6.Id, ProductName = "Red Ribbon Black Forest Cake", Qty = 1, Unit = "cake", EstimatedPrice = 550.00m, ActualPrice = 550.00m, Status = ItemStatus.PURCHASED, PurchasedQty = 1, PurchaseDate = DateTime.Now.AddMinutes(-10) });

            ctx.SaveChanges();
        }

        // 6. Expenses (Operating expenses so Dashboard and Reports are never blank)
        if (!ctx.Expenses.Any())
        {
            var adminUser = ctx.Users.FirstOrDefault(u => u.Role == UserRole.Admin);
            var today = DateOnly.FromDateTime(DateTime.Today);
            ctx.Expenses.AddRange(
                new Expense
                {
                    Description = "Rider Motorcycle Gas / Fuel (San Jose to Sibalom)",
                    Amount      = 250.00m,
                    Category    = "Fuel",
                    ExpenseDate = today,
                    Notes       = "Full tank for shopping runs",
                    CreatedBy   = adminUser?.Id,
                    CreatedAt   = DateTime.Now.AddHours(-5)
                },
                new Expense
                {
                    Description = "Thermal Receipt Paper Roll (Pack of 5)",
                    Amount      = 150.00m,
                    Category    = "Supplies",
                    ExpenseDate = today,
                    Notes       = "80mm POS receipt paper",
                    CreatedBy   = adminUser?.Id,
                    CreatedAt   = DateTime.Now.AddHours(-3)
                },
                new Expense
                {
                    Description = "Mall & Supermarket Parking Fees",
                    Amount      = 60.00m,
                    Category    = "Transportation",
                    ExpenseDate = today,
                    Notes       = "Robinsons Antique & Gaisano Grand parking",
                    CreatedBy   = adminUser?.Id,
                    CreatedAt   = DateTime.Now.AddHours(-2)
                },
                new Expense
                {
                    Description = "Packaging Eco-Bags and Thermal Insulated Bag",
                    Amount      = 320.00m,
                    Category    = "Supplies",
                    ExpenseDate = today.AddDays(-1),
                    Notes       = "Reusable food containers and bags",
                    CreatedBy   = adminUser?.Id,
                    CreatedAt   = DateTime.Now.AddDays(-1)
                },
                new Expense
                {
                    Description = "Motorcycle Maintenance & Tire Check",
                    Amount      = 400.00m,
                    Category    = "Maintenance",
                    ExpenseDate = today.AddDays(-2),
                    Notes       = "Routine maintenance for delivery bike",
                    CreatedBy   = adminUser?.Id,
                    CreatedAt   = DateTime.Now.AddDays(-2)
                }
            );
            ctx.SaveChanges();
        }

        EnsureAngelitosSeedData(ctx);

        // Mark that initial seeding is complete so demo/example data is never recreated
        SetSetting(ctx, "has_seeded_initial_data", "true");
    }

    public static string GetSetting(AppDbContext ctx, string key, string def = "")
        => ctx.Settings.AsNoTracking().FirstOrDefault(s => s.Key == key)?.Value ?? def;

    public static void SetSetting(AppDbContext ctx, string key, string value)
    {
        var existing = ctx.Settings.Where(x => x.Key == key).ToList();
        if (existing.Count == 0)
        {
            ctx.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            existing[0].Value = value;
            for (int i = 1; i < existing.Count; i++)
            {
                ctx.Settings.Remove(existing[i]);
            }
        }
        ctx.SaveChanges();
    }



    private static void EnsureAngelitosSeedData(AppDbContext ctx)
    {
        // 1. Tables
        if (!ctx.RestoTables.Any())
        {
            ctx.RestoTables.AddRange(
                new RestoTable { TableNumber = "T-01", Name = "Table 1", Capacity = 4, Section = "Main Dining", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "T-02", Name = "Table 2", Capacity = 4, Section = "Main Dining", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "T-03", Name = "Table 3", Capacity = 6, Section = "Main Dining", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "T-04", Name = "Table 4", Capacity = 6, Section = "Main Dining", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "BAR-01", Name = "Bar Counter 1", Capacity = 2, Section = "Bar", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "BAR-02", Name = "Bar Counter 2", Capacity = 2, Section = "Bar", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "VIP-01", Name = "VIP Suite", Capacity = 10, Section = "VIP", Status = RestoTableStatus.Available },
                new RestoTable { TableNumber = "PATIO-01", Name = "Patio Garden", Capacity = 4, Section = "Patio", Status = RestoTableStatus.Available }
            );
            ctx.SaveChanges();
        }

        // 2. Menu Items
        if (!ctx.RestoMenuItems.Any())
        {
            ctx.RestoMenuItems.AddRange(
                new RestoMenuItem { Code = "GRL-01", Name = "Inihaw na Liempo (Pork Belly)", Category = "Grill", Price = 185.00m, CostPrice = 110.00m, IsAvailable = true },
                new RestoMenuItem { Code = "GRL-02", Name = "Chicken Inasal (Leg Quarter)", Category = "Grill", Price = 165.00m, CostPrice = 90.00m, IsAvailable = true },
                new RestoMenuItem { Code = "GRL-03", Name = "Grilled Tuna Panga (500g)", Category = "Grill", Price = 280.00m, CostPrice = 160.00m, IsAvailable = true },
                new RestoMenuItem { Code = "SIZ-01", Name = "Sizzling Pork Sisig with Egg", Category = "Sizzling", Price = 195.00m, CostPrice = 100.00m, IsAvailable = true },
                new RestoMenuItem { Code = "SIZ-02", Name = "Sizzling Spicy Gambas", Category = "Sizzling", Price = 240.00m, CostPrice = 135.00m, IsAvailable = true },
                new RestoMenuItem { Code = "SIZ-03", Name = "Sizzling Bulalo Steak", Category = "Sizzling", Price = 320.00m, CostPrice = 180.00m, IsAvailable = true },
                new RestoMenuItem { Code = "PUL-01", Name = "Crispy Pata (Jumbo)", Category = "Pulutan", Price = 580.00m, CostPrice = 330.00m, IsAvailable = true },
                new RestoMenuItem { Code = "PUL-02", Name = "Tokwa't Baboy Especial", Category = "Pulutan", Price = 145.00m, CostPrice = 70.00m, IsAvailable = true },
                new RestoMenuItem { Code = "PUL-03", Name = "Calamares Fritos", Category = "Pulutan", Price = 210.00m, CostPrice = 110.00m, IsAvailable = true },
                new RestoMenuItem { Code = "ALC-01", Name = "San Miguel Pale Pilsen 330ml", Category = "Alcoholic", Price = 75.00m, CostPrice = 48.00m, IsAvailable = true },
                new RestoMenuItem { Code = "ALC-02", Name = "San Mig Light 330ml", Category = "Alcoholic", Price = 80.00m, CostPrice = 52.00m, IsAvailable = true },
                new RestoMenuItem { Code = "ALC-03", Name = "Red Horse Beer 500ml", Category = "Alcoholic", Price = 95.00m, CostPrice = 62.00m, IsAvailable = true },
                new RestoMenuItem { Code = "DRK-01", Name = "House Blend Iced Tea (Pitcher)", Category = "Drinks", Price = 95.00m, CostPrice = 35.00m, IsAvailable = true },
                new RestoMenuItem { Code = "DRK-02", Name = "Fresh Calamansi Juice", Category = "Drinks", Price = 65.00m, CostPrice = 20.00m, IsAvailable = true },
                new RestoMenuItem { Code = "RCE-01", Name = "Steamed Plain Rice (Cup)", Category = "Rice", Price = 25.00m, CostPrice = 8.00m, IsAvailable = true },
                new RestoMenuItem { Code = "RCE-02", Name = "Garlic Fried Rice (Platter)", Category = "Rice", Price = 110.00m, CostPrice = 35.00m, IsAvailable = true }
            );
            ctx.SaveChanges();
        }

        // 3. Inventory Items
        if (!ctx.RestoInventoryItems.Any())
        {
            ctx.RestoInventoryItems.AddRange(
                new RestoInventoryItem { ItemCode = "INV-MT-01", Name = "Pork Liempo Slab", Category = "Meat", Quantity = 28m, Unit = "kg", MinStockLevel = 8m, CostPrice = 290.00m },
                new RestoInventoryItem { ItemCode = "INV-MT-02", Name = "Chicken Leg Quarter", Category = "Meat", Quantity = 35m, Unit = "kg", MinStockLevel = 10m, CostPrice = 185.00m },
                new RestoInventoryItem { ItemCode = "INV-SF-01", Name = "Fresh Tuna Panga", Category = "Meat", Quantity = 14m, Unit = "kg", MinStockLevel = 5m, CostPrice = 260.00m },
                new RestoInventoryItem { ItemCode = "INV-BV-01", Name = "San Miguel Pale Pilsen Case", Category = "Liquor", Quantity = 18m, Unit = "case", MinStockLevel = 4m, CostPrice = 1100.00m },
                new RestoInventoryItem { ItemCode = "INV-BV-02", Name = "Red Horse 500ml Case", Category = "Liquor", Quantity = 22m, Unit = "case", MinStockLevel = 5m, CostPrice = 1250.00m },
                new RestoInventoryItem { ItemCode = "INV-RC-01", Name = "Dinorado Rice (50kg sack)", Category = "Produce", Quantity = 6m, Unit = "sack", MinStockLevel = 2m, CostPrice = 2450.00m }
            );
            ctx.SaveChanges();
        }

        // 4. Staff Members
        if (!ctx.RestoStaffMembers.Any())
        {
            ctx.RestoStaffMembers.AddRange(
                new RestoStaff { StaffCode = "STF-01", FullName = "Chef Roberto Cruz", Role = "Head Chef", Shift = "Afternoon/Evening", ContactNumber = "0917-111-2233", IsActive = true, HireDate = DateTime.Now.AddMonths(-6) },
                new RestoStaff { StaffCode = "STF-02", FullName = "Angela Reyes", Role = "Head Server", Shift = "Evening", ContactNumber = "0918-222-3344", IsActive = true, HireDate = DateTime.Now.AddMonths(-4) },
                new RestoStaff { StaffCode = "STF-03", FullName = "Mark Anthony Tan", Role = "Bartender", Shift = "Night", ContactNumber = "0919-333-4455", IsActive = true, HireDate = DateTime.Now.AddMonths(-3) },
                new RestoStaff { StaffCode = "STF-04", FullName = "Grace Villanueva", Role = "Cashier", Shift = "Evening", ContactNumber = "0920-444-5566", IsActive = true, HireDate = DateTime.Now.AddMonths(-2) }
            );
            ctx.SaveChanges();
        }
    }
}
