using System;
using System.Collections.Generic;
using System.Linq;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

public class StockService
{
    private readonly AppDbContext _db;

    public StockService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Adds stock to a product, increments StockQuantity, and records an audit movement.
    /// </summary>
    public double AddStock(int productId, double qty, string note = "Added")
    {
        if (qty <= 0) throw new ArgumentException("Quantity to add must be greater than zero.", nameof(qty));

        var product = _db.Products.Find(productId)
            ?? throw new ArgumentException($"Product #{productId} not found.");

        product.IsStockItem = true;
        product.StockQuantity += qty;
        product.AdditionalStocks += qty;

        var movement = new StockMovement
        {
            ProductId = productId,
            Timestamp = DateTime.Now,
            ChangeQty = qty,
            NewQuantity = product.StockQuantity,
            Note = string.IsNullOrWhiteSpace(note) ? "Added" : note.Trim(),
            OrderItemId = null
        };

        _db.StockMovements.Add(movement);
        _db.SaveChanges();
        return product.StockQuantity;
    }

    /// <summary>
    /// Sets the on-hand stock quantity directly, requiring a reason note (e.g. "damaged", "found more", "spoilage").
    /// </summary>
    public double AdjustStock(int productId, double newQty, string note)
    {
        if (newQty < 0) throw new ArgumentException("Stock quantity cannot be negative.", nameof(newQty));
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("A reason note is required for adjustments.", nameof(note));

        var product = _db.Products.Find(productId)
            ?? throw new ArgumentException($"Product #{productId} not found.");

        var oldQty = product.StockQuantity;
        var diff = newQty - oldQty;

        product.IsStockItem = true;
        product.StockQuantity = newQty;

        var movement = new StockMovement
        {
            ProductId = productId,
            Timestamp = DateTime.Now,
            ChangeQty = diff,
            NewQuantity = newQty,
            Note = note.Trim(),
            OrderItemId = null
        };

        _db.StockMovements.Add(movement);
        _db.SaveChanges();
        return newQty;
    }

    /// <summary>
    /// Decrements stock when an order item is marked Bought from Stock.
    /// </summary>
    public bool RecordSaleFromStock(string productName, double qty, string orderNo, int orderItemId)
    {
        if (string.IsNullOrWhiteSpace(productName) || qty <= 0) return false;

        var clean = productName.Trim().ToLowerInvariant();
        var product = _db.Products.FirstOrDefault(p => p.Name.ToLower() == clean && p.IsStockItem);
        if (product == null) return false;

        product.StockQuantity -= qty;

        var movement = new StockMovement
        {
            ProductId = product.Id,
            Timestamp = DateTime.Now,
            ChangeQty = -qty,
            NewQuantity = product.StockQuantity,
            Note = $"Order {orderNo}",
            OrderItemId = orderItemId
        };

        _db.StockMovements.Add(movement);
        _db.SaveChanges();
        return true;
    }

    /// <summary>
    /// Restores stock when an order item previously bought from stock is reverted or cancelled.
    /// </summary>
    public bool RestoreStock(string productName, double qty, string orderNo, int orderItemId)
    {
        if (string.IsNullOrWhiteSpace(productName) || qty <= 0) return false;

        var clean = productName.Trim().ToLowerInvariant();
        var product = _db.Products.FirstOrDefault(p => p.Name.ToLower() == clean && p.IsStockItem);
        if (product == null) return false;

        product.StockQuantity += qty;

        var movement = new StockMovement
        {
            ProductId = product.Id,
            Timestamp = DateTime.Now,
            ChangeQty = qty,
            NewQuantity = product.StockQuantity,
            Note = $"Order {orderNo} cancelled",
            OrderItemId = orderItemId
        };

        _db.StockMovements.Add(movement);
        _db.SaveChanges();
        return true;
    }

    /// <summary>
    /// Returns all movements for a product ordered by timestamp descending.
    /// </summary>
    public List<StockMovement> GetMovements(int productId)
    {
        return _db.StockMovements
            .AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.Timestamp)
            .ThenByDescending(m => m.Id)
            .ToList();
    }

    /// <summary>
    /// Total inventory value for all active stock items (Quantity * CostPrice).
    /// </summary>
    public decimal GetTotalStockValue()
    {
        return _db.Products
            .AsNoTracking()
            .Where(p => p.IsStockItem && p.IsActive && p.CostPrice.HasValue && p.StockQuantity > 0)
            .AsEnumerable()
            .Sum(p => (decimal)p.StockQuantity * (p.CostPrice ?? 0m));
    }
}
