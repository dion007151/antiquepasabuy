using Microsoft.EntityFrameworkCore;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Service providing business logic for Angelito's Bar & Resto Grill.
/// Handles restaurant tables, menu, inventory, staff, active orders, and sales transactions.
/// </summary>
public static class RestaurantService
{
    // ── Tables ────────────────────────────────────────────────────────────

    public static async Task<List<RestoTable>> GetTablesAsync()
    {
        using var db = new AppDbContext();
        return await db.RestoTables
            .Where(t => t.BusinessId == BusinessProfileService.ANGELITOS_RESTO)
            .OrderBy(t => t.TableNumber)
            .ToListAsync();
    }

    public static async Task<bool> UpdateTableStatusAsync(int tableId, RestoTableStatus status, string? customer = null, decimal bill = 0)
    {
        using var db = new AppDbContext();
        var table = await db.RestoTables.FindAsync(tableId);
        if (table == null) return false;

        table.Status = status;
        table.CurrentCustomerName = (status == RestoTableStatus.Available) ? null : customer;
        table.CurrentBillAmount = (status == RestoTableStatus.Available) ? 0m : bill;
        table.OccupiedSince = (status == RestoTableStatus.Occupied || status == RestoTableStatus.Billing) 
            ? (table.OccupiedSince ?? DateTime.Now) 
            : null;
        table.UpdatedAt = DateTime.Now;

        // If marked available, also close any active open orders for this table
        if (status == RestoTableStatus.Available && !string.IsNullOrWhiteSpace(table.TableNumber))
        {
            var openOrders = await db.RestoOrders
                .Where(o => o.TableNumber == table.TableNumber &&
                            o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                            o.Status != RestoOrderStatus.Completed &&
                            o.Status != RestoOrderStatus.Cancelled)
                .ToListAsync();

            foreach (var o in openOrders)
            {
                o.Status = RestoOrderStatus.Completed;
                o.UpdatedAt = DateTime.Now;
            }
        }

        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Gets the current active (uncompleted) dining order/ticket for a table.
    /// </summary>
    public static async Task<RestoOrder?> GetActiveOrderByTableAsync(string tableNumber)
    {
        if (string.IsNullOrWhiteSpace(tableNumber)) return null;
        using var db = new AppDbContext();
        return await db.RestoOrders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                        o.TableNumber == tableNumber &&
                        o.Status != RestoOrderStatus.Completed &&
                        o.Status != RestoOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Saves or updates the active dining order / running tab for a table.
    /// Updates the table's CurrentBillAmount, CustomerName, and Occupied status automatically.
    /// </summary>
    public static async Task<RestoOrder> SaveOrUpdateTableOrderAsync(
        string tableNumber,
        string customerName,
        string? serverName,
        List<RestoOrderItem> items,
        string notes = "")
    {
        using var db = new AppDbContext();
        var order = await db.RestoOrders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                        o.TableNumber == tableNumber &&
                        o.Status != RestoOrderStatus.Completed &&
                        o.Status != RestoOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        var cust = string.IsNullOrWhiteSpace(customerName) ? "Dine-in Guests" : customerName.Trim();

        if (order == null)
        {
            var count = await db.RestoOrders.CountAsync() + 1;
            order = new RestoOrder
            {
                OrderNo = $"ORD-{DateTime.Now:yyyyMMdd}-{count:D4}",
                TableNumber = tableNumber,
                CustomerName = cust,
                ServerName = serverName,
                Status = RestoOrderStatus.Pending,
                Notes = notes,
                BusinessId = BusinessProfileService.ANGELITOS_RESTO,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            db.RestoOrders.Add(order);
            await db.SaveChangesAsync(); // Persist to obtain order.Id
        }
        else
        {
            order.CustomerName = cust;
            if (!string.IsNullOrWhiteSpace(serverName)) order.ServerName = serverName;
            order.Notes = notes;
            order.UpdatedAt = DateTime.Now;

            // Remove existing items and refresh with new line items
            db.RestoOrderItems.RemoveRange(order.Items);
            order.Items.Clear();
        }

        foreach (var it in items)
        {
            it.Id = 0;
            it.RestoOrderId = order.Id;
            db.RestoOrderItems.Add(it);
        }

        var total = items.Sum(i => i.Quantity * i.UnitPrice);
        order.TotalAmount = total;
        await db.SaveChangesAsync();

        // Update corresponding Table record so floor plan immediately displays the running bill
        var table = await db.RestoTables.FirstOrDefaultAsync(t =>
            t.TableNumber == tableNumber &&
            t.BusinessId == BusinessProfileService.ANGELITOS_RESTO);

        if (table != null)
        {
            table.Status = RestoTableStatus.Occupied;
            table.CurrentCustomerName = cust;
            table.CurrentBillAmount = total;
            table.OccupiedSince ??= DateTime.Now;
            table.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }

        return order;
    }

    /// <summary>
    /// Synchronizes all table bill totals with any active open orders in the database.
    /// Ensures running bills are 100% accurate on the floor plan.
    /// </summary>
    public static async Task SyncAllTableBillsAsync()
    {
        try
        {
            using var db = new AppDbContext();
            var tables = await db.RestoTables
                .Where(t => t.BusinessId == BusinessProfileService.ANGELITOS_RESTO)
                .ToListAsync();

            var activeOrders = await db.RestoOrders
                .Include(o => o.Items)
                .Where(o => o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                            o.Status != RestoOrderStatus.Completed &&
                            o.Status != RestoOrderStatus.Cancelled &&
                            !string.IsNullOrEmpty(o.TableNumber))
                .ToListAsync();

            bool changed = false;
            foreach (var t in tables)
            {
                var activeOrder = activeOrders
                    .Where(o => string.Equals(o.TableNumber, t.TableNumber, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(o => o.CreatedAt)
                    .FirstOrDefault();

                if (activeOrder != null)
                {
                    var calcTotal = activeOrder.Items.Sum(i => i.Quantity * i.UnitPrice);
                    if (t.Status == RestoTableStatus.Available)
                    {
                        t.Status = RestoTableStatus.Occupied;
                        t.OccupiedSince = activeOrder.CreatedAt;
                        changed = true;
                    }
                    if (t.CurrentBillAmount != calcTotal)
                    {
                        t.CurrentBillAmount = calcTotal;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(activeOrder.CustomerName) && t.CurrentCustomerName != activeOrder.CustomerName)
                    {
                        t.CurrentCustomerName = activeOrder.CustomerName;
                        changed = true;
                    }
                }
                else if (t.Status == RestoTableStatus.Available && t.CurrentBillAmount != 0m)
                {
                    t.CurrentBillAmount = 0m;
                    t.CurrentCustomerName = null;
                    changed = true;
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync();
            }
        }
        catch { }
    }

    // ── Menu ──────────────────────────────────────────────────────────────

    public static async Task<List<RestoMenuItem>> GetMenuItemsAsync(string? category = null)
    {
        using var db = new AppDbContext();
        var query = db.RestoMenuItems
            .Where(m => m.BusinessId == BusinessProfileService.ANGELITOS_RESTO);

        if (!string.IsNullOrWhiteSpace(category) && category != "All")
            query = query.Where(m => m.Category == category);

        return await query.OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync();
    }

    public static async Task<bool> SaveMenuItemAsync(RestoMenuItem item)
    {
        using var db = new AppDbContext();
        if (item.Id == 0)
        {
            item.BusinessId = BusinessProfileService.ANGELITOS_RESTO;
            item.CreatedAt = DateTime.Now;
            db.RestoMenuItems.Add(item);
        }
        else
        {
            var existing = await db.RestoMenuItems.FindAsync(item.Id);
            if (existing == null) return false;
            existing.Name = item.Name;
            existing.Code = item.Code;
            existing.Category = item.Category;
            existing.Price = item.Price;
            existing.CostPrice = item.CostPrice;
            existing.Description = item.Description;
            existing.IsAvailable = item.IsAvailable;
            existing.ImagePath = item.ImagePath;
        }

        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> DeleteMenuItemAsync(int id)
    {
        using var db = new AppDbContext();
        var existing = await db.RestoMenuItems.FindAsync(id);
        if (existing == null) return false;
        db.RestoMenuItems.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Inventory ─────────────────────────────────────────────────────────

    public static async Task<List<RestoInventoryItem>> GetInventoryItemsAsync()
    {
        using var db = new AppDbContext();
        return await db.RestoInventoryItems
            .Where(i => i.BusinessId == BusinessProfileService.ANGELITOS_RESTO)
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .ToListAsync();
    }

    public static async Task<bool> UpdateStockAsync(int itemId, decimal addedQuantity, decimal? newCost = null)
    {
        using var db = new AppDbContext();
        var item = await db.RestoInventoryItems.FindAsync(itemId);
        if (item == null) return false;

        item.Quantity += addedQuantity;
        if (newCost.HasValue && newCost.Value > 0)
            item.CostPrice = newCost.Value;

        item.LastRestockedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> SaveInventoryItemAsync(RestoInventoryItem item)
    {
        using var db = new AppDbContext();
        if (item.Id == 0)
        {
            item.BusinessId = BusinessProfileService.ANGELITOS_RESTO;
            item.LastRestockedAt = DateTime.Now;
            db.RestoInventoryItems.Add(item);
        }
        else
        {
            var existing = await db.RestoInventoryItems.FindAsync(item.Id);
            if (existing == null) return false;
            existing.ItemCode = item.ItemCode;
            existing.Name = item.Name;
            existing.Category = item.Category;
            existing.Quantity = item.Quantity;
            existing.Unit = item.Unit;
            existing.MinStockLevel = item.MinStockLevel;
            existing.CostPrice = item.CostPrice;
            existing.LastRestockedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> DeleteInventoryItemAsync(int id)
    {
        using var db = new AppDbContext();
        var existing = await db.RestoInventoryItems.FindAsync(id);
        if (existing == null) return false;
        db.RestoInventoryItems.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Staff ─────────────────────────────────────────────────────────────

    public static async Task<List<RestoStaff>> GetStaffMembersAsync()
    {
        using var db = new AppDbContext();
        return await db.RestoStaffMembers
            .Where(s => s.BusinessId == BusinessProfileService.ANGELITOS_RESTO)
            .OrderBy(s => s.FullName)
            .ToListAsync();
    }

    public static async Task<bool> SaveStaffAsync(RestoStaff staff)
    {
        using var db = new AppDbContext();
        if (staff.Id == 0)
        {
            staff.BusinessId = BusinessProfileService.ANGELITOS_RESTO;
            if (staff.HireDate == default) staff.HireDate = DateTime.Now;
            db.RestoStaffMembers.Add(staff);
        }
        else
        {
            var existing = await db.RestoStaffMembers.FindAsync(staff.Id);
            if (existing == null) return false;
            existing.StaffCode = staff.StaffCode;
            existing.FullName = staff.FullName;
            existing.Role = staff.Role;
            existing.ContactNumber = staff.ContactNumber;
            existing.Shift = staff.Shift;
            existing.IsActive = staff.IsActive;
        }

        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> DeleteStaffAsync(int id)
    {
        using var db = new AppDbContext();
        var existing = await db.RestoStaffMembers.FindAsync(id);
        if (existing == null) return false;
        db.RestoStaffMembers.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Tables Management ──────────────────────────────────────────────────

    public static async Task<bool> SaveTableAsync(RestoTable table)
    {
        using var db = new AppDbContext();
        if (table.Id == 0)
        {
            table.BusinessId = BusinessProfileService.ANGELITOS_RESTO;
            table.UpdatedAt = DateTime.Now;
            db.RestoTables.Add(table);
        }
        else
        {
            var existing = await db.RestoTables.FindAsync(table.Id);
            if (existing == null) return false;
            existing.TableNumber = table.TableNumber;
            existing.Name = table.Name;
            existing.Capacity = table.Capacity;
            existing.Section = table.Section;
            existing.Status = table.Status;
            existing.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> DeleteTableAsync(int id)
    {
        using var db = new AppDbContext();
        var existing = await db.RestoTables.FindAsync(id);
        if (existing == null) return false;
        db.RestoTables.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Orders & Kitchen ──────────────────────────────────────────────────

    public static async Task<List<RestoOrder>> GetActiveKitchenOrdersAsync()
    {
        using var db = new AppDbContext();
        return await db.RestoOrders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                        o.Status != RestoOrderStatus.Completed &&
                        o.Status != RestoOrderStatus.Cancelled)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public static async Task<List<RestoOrder>> GetAllOrdersAsync()
    {
        using var db = new AppDbContext();
        return await db.RestoOrders
            .Include(o => o.Items)
            .Where(o => o.BusinessId == BusinessProfileService.ANGELITOS_RESTO)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public static async Task<bool> UpdateOrderStatusAsync(int orderId, RestoOrderStatus status)
    {
        using var db = new AppDbContext();
        var order = await db.RestoOrders.FindAsync(orderId);
        if (order == null) return false;

        order.Status = status;
        order.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return true;
    }

    // ── Sales ─────────────────────────────────────────────────────────────

    public static async Task<List<RestoSale>> GetSalesAsync(DateTime? from = null, DateTime? to = null, string? search = null)
    {
        using var db = new AppDbContext();
        var query = db.RestoSales
            .Include(s => s.Items)
            .Where(s => s.BusinessId == BusinessProfileService.ANGELITOS_RESTO);

        if (from.HasValue)
            query = query.Where(s => s.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(s => s.CreatedAt <= to.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var sLower = search.Trim().ToLower();
            query = query.Where(s =>
                (s.SaleNo != null && s.SaleNo.ToLower().Contains(sLower)) ||
                (s.TableNumber != null && s.TableNumber.ToLower().Contains(sLower)) ||
                (s.CustomerName != null && s.CustomerName.ToLower().Contains(sLower)) ||
                (s.PaymentMethod != null && s.PaymentMethod.ToLower().Contains(sLower)) ||
                (s.CashierName != null && s.CashierName.ToLower().Contains(sLower))
            );
        }

        return await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public static async Task<RestoSale> CreateSaleAsync(RestoSale sale)
    {
        using var db = new AppDbContext();

        sale.BusinessId = BusinessProfileService.ANGELITOS_RESTO;
        sale.CreatedAt = DateTime.Now;

        if (string.IsNullOrWhiteSpace(sale.SaleNo))
        {
            var count = await db.RestoSales.CountAsync() + 1;
            sale.SaleNo = $"RESTO-{DateTime.Now:yyyyMMdd}-{count:D4}";
        }

        db.RestoSales.Add(sale);
        await db.SaveChangesAsync();

        // If associated with a table, close any open orders and free up the table (mark Available)
        if (!string.IsNullOrWhiteSpace(sale.TableNumber))
        {
            var activeOrders = await db.RestoOrders
                .Where(o => o.TableNumber == sale.TableNumber &&
                            o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                            o.Status != RestoOrderStatus.Completed &&
                            o.Status != RestoOrderStatus.Cancelled)
                .ToListAsync();

            foreach (var o in activeOrders)
            {
                o.Status = RestoOrderStatus.Completed;
                o.UpdatedAt = DateTime.Now;
            }

            var table = await db.RestoTables.FirstOrDefaultAsync(t =>
                t.TableNumber == sale.TableNumber &&
                t.BusinessId == BusinessProfileService.ANGELITOS_RESTO);

            if (table != null)
            {
                table.Status = RestoTableStatus.Available;
                table.CurrentCustomerName = null;
                table.CurrentBillAmount = 0m;
                table.OccupiedSince = null;
                table.UpdatedAt = DateTime.Now;
            }

            await db.SaveChangesAsync();
        }

        return sale;
    }

    public static async Task<string> GenerateNextSaleNoAsync()
    {
        using var db = new AppDbContext();
        var count = await db.RestoSales.CountAsync() + 1;
        return $"RESTO-{DateTime.Now:yyyyMMdd}-{count:D4}";
    }

    /// <summary>
    /// Permanently deletes a restaurant sale transaction and its line items.
    /// </summary>
    public static async Task<bool> DeleteSaleAsync(int saleId)
    {
        using var db = new AppDbContext();
        var sale = await db.RestoSales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId);

        if (sale == null) return false;

        db.RestoSaleItems.RemoveRange(sale.Items);
        db.RestoSales.Remove(sale);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Permanently deletes an active restaurant dining order/ticket and its line items.
    /// Automatically updates table balance and status if applicable.
    /// </summary>
    public static async Task<bool> DeleteOrderAsync(int orderId)
    {
        using var db = new AppDbContext();
        var order = await db.RestoOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return false;

        var tableNumber = order.TableNumber;
        db.RestoOrderItems.RemoveRange(order.Items);
        db.RestoOrders.Remove(order);
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(tableNumber))
        {
            var otherActive = await db.RestoOrders
                .Include(o => o.Items)
                .Where(o => o.TableNumber == tableNumber &&
                            o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                            o.Status != RestoOrderStatus.Completed &&
                            o.Status != RestoOrderStatus.Cancelled)
                .ToListAsync();

            var table = await db.RestoTables.FirstOrDefaultAsync(t =>
                t.TableNumber == tableNumber &&
                t.BusinessId == BusinessProfileService.ANGELITOS_RESTO);

            if (table != null)
            {
                if (otherActive.Count == 0)
                {
                    table.Status = RestoTableStatus.Available;
                    table.CurrentCustomerName = null;
                    table.CurrentBillAmount = 0m;
                    table.OccupiedSince = null;
                }
                else
                {
                    table.CurrentBillAmount = otherActive.Sum(o => o.Items.Sum(i => i.Quantity * i.UnitPrice));
                }
                table.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }

        return true;
    }

    /// <summary>
    /// Voids/cancels all active orders and running tabs for a table, making it available again.
    /// </summary>
    public static async Task<bool> VoidTableTabAsync(string tableNumber)
    {
        if (string.IsNullOrWhiteSpace(tableNumber)) return false;
        using var db = new AppDbContext();
        var activeOrders = await db.RestoOrders
            .Include(o => o.Items)
            .Where(o => o.TableNumber == tableNumber &&
                        o.BusinessId == BusinessProfileService.ANGELITOS_RESTO &&
                        o.Status != RestoOrderStatus.Completed &&
                        o.Status != RestoOrderStatus.Cancelled)
            .ToListAsync();

        foreach (var order in activeOrders)
        {
            db.RestoOrderItems.RemoveRange(order.Items);
            db.RestoOrders.Remove(order);
        }

        var table = await db.RestoTables.FirstOrDefaultAsync(t =>
            t.TableNumber == tableNumber &&
            t.BusinessId == BusinessProfileService.ANGELITOS_RESTO);

        if (table != null)
        {
            table.Status = RestoTableStatus.Available;
            table.CurrentCustomerName = null;
            table.CurrentBillAmount = 0m;
            table.OccupiedSince = null;
            table.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        return true;
    }
}
