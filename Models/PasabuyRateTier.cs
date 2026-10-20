namespace AntiquePasabuyPOS.Models;

public class PasabuyRateTier
{
    public decimal MinAmount { get; set; }
    public decimal? MaxAmount { get; set; } // null indicates open-ended (e.g. ₱2,500 and above)
    public decimal? FixedFee { get; set; }
    public decimal? PercentageFee { get; set; } // e.g., 0.15m for 15%

    public string DisplayRange
    {
        get
        {
            if (!MaxAmount.HasValue)
                return $"₱{(MinAmount % 1 == 0 ? MinAmount.ToString("N0") : MinAmount.ToString("N2"))} and UP";

            var minStr = MinAmount % 1 == 0 ? MinAmount.ToString("N0") : MinAmount.ToString("N2");
            var maxStr = MaxAmount.Value % 1 == 0 ? MaxAmount.Value.ToString("N0") : MaxAmount.Value.ToString("N2");
            return MinAmount == 0 ? $"0 – {maxStr} pesos" : $"₱{minStr} – ₱{maxStr}";
        }
    }

    public string DisplayFee => FixedFee.HasValue
        ? $"₱{(FixedFee.Value % 1 == 0 ? FixedFee.Value.ToString("N0") : FixedFee.Value.ToString("N2"))}"
        : $"{((PercentageFee ?? 0) * 100):0.#}%";

    public string RuleDescription => FixedFee.HasValue
        ? $"Fixed fee of ₱{(FixedFee.Value % 1 == 0 ? FixedFee.Value.ToString("N0") : FixedFee.Value.ToString("N2"))}"
        : $"{((PercentageFee ?? 0) * 100):0.#}% of applicable order total";
}
