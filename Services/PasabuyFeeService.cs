using System.Text.Json;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

public static class PasabuyFeeService
{
    public const string SettingsKey = "pasabuy_rates_json";

    /// <summary>
    /// The official default Antique Pasabuy rate table.
    /// </summary>
    /// <summary>
    /// The official default Antique Pasabuy rate table.
    /// Simplified whole numbers matching the official rate sheet:
    /// - 0 to 99 pesos = ₱50
    /// - 100 to 199 = ₱75
    /// - 200 to 299 = ₱100
    /// - 300 to 999 = ₱150
    /// - 1000 to 1499 = ₱250
    /// - 1500 to 1999 = ₱300
    /// - 2000 to 2499 = ₱350
    /// - 2500 and UP = 15%
    /// </summary>
    public static List<PasabuyRateTier> GetDefaultTiers() => new()
    {
        new PasabuyRateTier { MinAmount = 1m,    MaxAmount = 99m,   FixedFee = 50m },
        new PasabuyRateTier { MinAmount = 100m,  MaxAmount = 199m,  FixedFee = 75m },
        new PasabuyRateTier { MinAmount = 200m,  MaxAmount = 299m,  FixedFee = 100m },
        new PasabuyRateTier { MinAmount = 300m,  MaxAmount = 999m,  FixedFee = 150m },
        new PasabuyRateTier { MinAmount = 1000m, MaxAmount = 1499m, FixedFee = 250m },
        new PasabuyRateTier { MinAmount = 1500m, MaxAmount = 1999m, FixedFee = 300m },
        new PasabuyRateTier { MinAmount = 2000m, MaxAmount = 2499m, FixedFee = 350m },
        new PasabuyRateTier { MinAmount = 2500m, MaxAmount = null,  PercentageFee = 0.15m },
    };

    /// <summary>
    /// Loads configured rate tiers from database settings, or returns official defaults.
    /// Automatically simplifies any legacy decimal ranges (e.g. 99.99 -> 99).
    /// </summary>
    public static List<PasabuyRateTier> GetRateTiers(AppDbContext? db = null)
    {
        if (db == null) return GetDefaultTiers();

        try
        {
            var json = SeedData.GetSetting(db, SettingsKey, "");
            if (!string.IsNullOrWhiteSpace(json))
            {
                var tiers = JsonSerializer.Deserialize<List<PasabuyRateTier>>(json);
                if (tiers != null && tiers.Count > 0)
                {
                    // Clean up any legacy 99.99 -> 99
                    foreach (var t in tiers)
                    {
                        if (t.MaxAmount.HasValue && Math.Abs(t.MaxAmount.Value - Math.Floor(t.MaxAmount.Value) - 0.99m) < 0.005m)
                        {
                            t.MaxAmount = Math.Floor(t.MaxAmount.Value);
                        }
                    }
                    return tiers.OrderBy(t => t.MinAmount).ToList();
                }
            }
        }
        catch
        {
            // Fall back to default on any parsing issue
        }

        return GetDefaultTiers();
    }

    /// <summary>
    /// Saves custom rate tiers to database settings.
    /// </summary>
    public static void SaveRateTiers(AppDbContext db, List<PasabuyRateTier> tiers)
    {
        var json = JsonSerializer.Serialize(tiers, new JsonSerializerOptions { WriteIndented = true });
        SeedData.SetSetting(db, SettingsKey, json);
    }

    /// <summary>
    /// Resets rate tiers back to the official default table.
    /// </summary>
    public static void ResetToDefaults(AppDbContext db)
    {
        SaveRateTiers(db, GetDefaultTiers());
    }

    /// <summary>
    /// Calculates the official Antique Pasabuy service fee based on the order subtotal.
    /// Accurately checks each bracket (e.g. subtotal up to 99.99 falls in 0-99 bracket,
    /// 100 up to 199.99 falls in 100-199 bracket, 2500+ is 15%).
    /// </summary>
    public static decimal CalculateServiceFee(decimal subtotal, AppDbContext? db = null)
    {
        if (subtotal <= 0m) return 0m;

        var tiers = GetRateTiers(db);

        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            var matchesMin = subtotal >= tier.MinAmount;

            bool matchesMax;
            if (i < tiers.Count - 1 && tiers[i + 1].MinAmount > tier.MinAmount)
            {
                // Range seamlessly covers up to the next tier's minimum amount (e.g. < 100m covers 99.99)
                matchesMax = subtotal < tiers[i + 1].MinAmount;
            }
            else if (tier.MaxAmount.HasValue)
            {
                // If MaxAmount is a whole number like 99, it covers up to 99.999
                decimal maxBoundary = tier.MaxAmount.Value % 1 == 0 ? tier.MaxAmount.Value + 0.9999m : tier.MaxAmount.Value;
                matchesMax = subtotal <= maxBoundary;
            }
            else
            {
                matchesMax = true;
            }

            if (matchesMin && matchesMax)
            {
                if (tier.FixedFee.HasValue)
                {
                    return tier.FixedFee.Value;
                }

                if (tier.PercentageFee.HasValue)
                {
                    return Math.Round(subtotal * tier.PercentageFee.Value, 2, MidpointRounding.AwayFromZero);
                }
            }
        }

        // Hardcoded safety fallback using official rates if custom table was incomplete
        if (subtotal < 100m) return 50m;
        if (subtotal < 200m) return 75m;
        if (subtotal < 300m) return 100m;
        if (subtotal < 1000m) return 150m;
        if (subtotal < 1500m) return 250m;
        if (subtotal < 2000m) return 300m;
        if (subtotal < 2500m) return 350m;
        return Math.Round(subtotal * 0.15m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates the Pasabuy Fee for an order, excluding any items where IsFeeExempt = true
    /// from the taxable subtotal (e.g. SOLAR LIGHT NO PASABUY items).
    /// Fee-exempt items are still included in the grand total; they simply don't contribute
    /// to the bracket that determines the Pasabuy Fee amount.
    /// </summary>
    public static decimal CalculateServiceFeeForItems(IEnumerable<AntiquePasabuyPOS.Models.OrderItem> items, AppDbContext? db = null)
    {
        // Only non-exempt items contribute to the taxable subtotal
        var taxableSubtotal = items
            .Where(i => !i.IsFeeExempt)
            .Sum(i => (i.ActualPrice ?? i.EstimatedPrice) * (decimal)i.Qty);

        return CalculateServiceFee(taxableSubtotal, db);
    }
}
