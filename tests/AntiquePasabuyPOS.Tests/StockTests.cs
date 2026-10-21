using System;
using System.IO;
using System.Linq;
using Xunit;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Tests;

public class StockTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly AppDbContext _db;

    public StockTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"antiquepasabuy_test_{Guid.NewGuid():N}.db");
        _db = new AppDbContext(_tempDbPath);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            if (File.Exists(_tempDbPath))
                File.Delete(_tempDbPath);
        }
        catch { }
    }

    [Fact]
    public void AddStock_IncreasesQuantity_And_LogsMovement()
    {
        var product = new Product
        {
            Name = "Organic Milk",
            Price = 120m,
            CostPrice = 90m,
            IsStockItem = true,
            StockQuantity = 5,
            OpeningStock = 5,
            IsActive = true
        };
        _db.Products.Add(product);
        _db.SaveChanges();

        var stockSvc = new StockService(_db);
        var newQty = stockSvc.AddStock(product.Id, 7, "Delivery from supplier");

        Assert.Equal(12, newQty);

        var refreshed = _db.Products.Find(product.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(12, refreshed.StockQuantity);

        var movements = stockSvc.GetMovements(product.Id);
        Assert.Single(movements);
        Assert.Equal(7, movements[0].ChangeQty);
        Assert.Equal(12, movements[0].NewQuantity);
        Assert.Equal("Delivery from supplier", movements[0].Note);
    }

    [Fact]
    public void AdjustStock_WithoutNote_ThrowsArgumentException()
    {
        var product = new Product
        {
            Name = "Brown Sugar",
            Price = 60m,
            CostPrice = 45m,
            IsStockItem = true,
            StockQuantity = 10,
            IsActive = true
        };
        _db.Products.Add(product);
        _db.SaveChanges();

        var stockSvc = new StockService(_db);
        Assert.Throws<ArgumentException>(() => stockSvc.AdjustStock(product.Id, 8, ""));
        Assert.Throws<ArgumentException>(() => stockSvc.AdjustStock(product.Id, 8, "   "));
    }

    [Fact]
    public void AdjustStock_ValidNewQuantityAndNote_UpdatesQuantity_And_LogsMovement()
    {
        var product = new Product
        {
            Name = "Fresh Eggs Large",
            Price = 10m,
            CostPrice = 7.5m,
            IsStockItem = true,
            StockQuantity = 30,
            IsActive = true
        };
        _db.Products.Add(product);
        _db.SaveChanges();

        var stockSvc = new StockService(_db);
        var adjusted = stockSvc.AdjustStock(product.Id, 25, "5 cracked during storage");

        Assert.Equal(25, adjusted);

        var refreshed = _db.Products.Find(product.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(25, refreshed.StockQuantity);

        var movements = stockSvc.GetMovements(product.Id);
        Assert.Single(movements);
        Assert.Equal(-5, movements[0].ChangeQty);
        Assert.Equal(25, movements[0].NewQuantity);
        Assert.Equal("5 cracked during storage", movements[0].Note);
    }

    [Fact]
    public void BoughtFromStock_ReducesQuantity_And_LogsMovement()
    {
        var product = new Product
        {
            Name = "Canned Tuna",
            Price = 45m,
            CostPrice = 35m,
            IsStockItem = true,
            StockQuantity = 15,
            IsActive = true
        };
        _db.Products.Add(product);

        var order = new Order
        {
            OrderNo = "TEST-101",
            Status = OrderStatus.WAITING_CONFIRMATION,
            CreatedAt = DateTime.Now
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        var item = new OrderItem
        {
            OrderId = order.Id,
            ProductName = "Canned Tuna",
            Qty = 4,
            EstimatedPrice = 45m,
            Status = ItemStatus.PENDING
        };
        _db.OrderItems.Add(item);
        _db.SaveChanges();

        var buyingSvc = new BuyingStateService(_db);
        var (ok, msg) = buyingSvc.MarkItemBoughtFromStock(item.Id);

        Assert.True(ok);

        var refreshedItem = _db.OrderItems.Find(item.Id);
        Assert.NotNull(refreshedItem);
        Assert.Equal(ItemStatus.BOUGHT_FROM_STOCK, refreshedItem.Status);

        var refreshedProd = _db.Products.Find(product.Id);
        Assert.NotNull(refreshedProd);
        Assert.Equal(11, refreshedProd.StockQuantity);

        var stockSvc = new StockService(_db);
        var movements = stockSvc.GetMovements(product.Id);
        Assert.Single(movements);
        Assert.Equal(-4, movements[0].ChangeQty);
        Assert.Equal(11, movements[0].NewQuantity);
        Assert.Equal("Order TEST-101", movements[0].Note);
    }

    [Fact]
    public void ResetItemToPending_RestoresQuantity_And_LogsMovement()
    {
        var product = new Product
        {
            Name = "Cooking Oil 1L",
            Price = 95m,
            CostPrice = 75m,
            IsStockItem = true,
            StockQuantity = 20,
            IsActive = true
        };
        _db.Products.Add(product);

        var order = new Order
        {
            OrderNo = "TEST-102",
            Status = OrderStatus.WAITING_CONFIRMATION,
            CreatedAt = DateTime.Now
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        var item = new OrderItem
        {
            OrderId = order.Id,
            ProductName = "Cooking Oil 1L",
            Qty = 5,
            EstimatedPrice = 95m,
            Status = ItemStatus.PENDING
        };
        _db.OrderItems.Add(item);
        _db.SaveChanges();

        var buyingSvc = new BuyingStateService(_db);
        buyingSvc.MarkItemBoughtFromStock(item.Id);

        // Stock decreased from 20 to 15
        var midProd = _db.Products.Find(product.Id);
        Assert.Equal(15, midProd!.StockQuantity);

        // Revert back to Pending
        var (resetOk, _) = buyingSvc.ResetItemToPending(item.Id);
        Assert.True(resetOk);

        var restoredProd = _db.Products.Find(product.Id);
        Assert.Equal(20, restoredProd!.StockQuantity);

        var stockSvc = new StockService(_db);
        var movements = stockSvc.GetMovements(product.Id);
        Assert.Equal(2, movements.Count);
        // Latest movement is restoration (+5)
        Assert.Equal(5, movements[0].ChangeQty);
        Assert.Equal(20, movements[0].NewQuantity);
    }

    [Fact]
    public void CancellingOrder_RestoresQuantity_ForBoughtFromStockItems()
    {
        var product = new Product
        {
            Name = "Instant Coffee",
            Price = 15m,
            CostPrice = 11m,
            IsStockItem = true,
            StockQuantity = 50,
            IsActive = true
        };
        _db.Products.Add(product);

        var order = new Order
        {
            OrderNo = "TEST-103",
            Status = OrderStatus.WAITING_CONFIRMATION,
            CreatedAt = DateTime.Now
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        var item = new OrderItem
        {
            OrderId = order.Id,
            ProductName = "Instant Coffee",
            Qty = 10,
            EstimatedPrice = 15m,
            Status = ItemStatus.PENDING
        };
        _db.OrderItems.Add(item);
        _db.SaveChanges();

        var buyingSvc = new BuyingStateService(_db);
        buyingSvc.MarkItemBoughtFromStock(item.Id);

        var midProd = _db.Products.Find(product.Id);
        Assert.Equal(40, midProd!.StockQuantity);

        // Cancel the order through OrderStatusService
        var statusSvc = new OrderStatusService(_db);
        var (ok, _) = statusSvc.Transition(order.Id, OrderStatus.CANCELLED, reason: "Customer changed mind");
        Assert.True(ok);

        var restoredProd = _db.Products.Find(product.Id);
        Assert.Equal(50, restoredProd!.StockQuantity);
    }

    [Fact]
    public void TotalStockValue_EqualsSumOfQuantityTimesCostPrice()
    {
        _db.Products.AddRange(
            new Product { Name = "Item A", Price = 100m, CostPrice = 80m, IsStockItem = true, StockQuantity = 10, IsActive = true }, // 800
            new Product { Name = "Item B", Price = 50m, CostPrice = 40m, IsStockItem = true, StockQuantity = 5.5, IsActive = true }, // 220
            new Product { Name = "Item C", Price = 200m, CostPrice = 150m, IsStockItem = false, StockQuantity = 20, IsActive = true } // not a stock item: ignored
        );
        _db.SaveChanges();

        var stockSvc = new StockService(_db);
        var totalValue = stockSvc.GetTotalStockValue();

        // 10 * 80 + 5.5 * 40 = 800 + 220 = 1020
        Assert.Equal(1020m, totalValue);
    }

    [Fact]
    public void QuantityEqualsSumOfMovements()
    {
        var product = new Product
        {
            Name = "Flour 1kg",
            Price = 65m,
            CostPrice = 50m,
            IsStockItem = true,
            StockQuantity = 0,
            IsActive = true
        };
        _db.Products.Add(product);
        _db.SaveChanges();

        var stockSvc = new StockService(_db);

        // Movement 1: Add 20
        stockSvc.AddStock(product.Id, 20, "Initial batch");
        // Movement 2: Adjust down to 18
        stockSvc.AdjustStock(product.Id, 18, "2 bags water damaged");
        // Movement 3: Sale from stock (deduct 6)
        stockSvc.RecordSaleFromStock("Flour 1kg", 6, "ORD-999", 1);
        // Movement 4: Restore 2 from cancelled partial order
        stockSvc.RestoreStock("Flour 1kg", 2, "ORD-999", 1);

        var refreshedProd = _db.Products.Find(product.Id);
        Assert.NotNull(refreshedProd);
        Assert.Equal(14, refreshedProd.StockQuantity);

        var movements = stockSvc.GetMovements(product.Id);
        var movementSum = movements.Sum(m => m.ChangeQty);

        // Initial 0 + sum of changes = final quantity
        Assert.Equal(refreshedProd.StockQuantity, movementSum);
    }
}
