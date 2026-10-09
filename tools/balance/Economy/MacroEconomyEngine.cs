namespace AirlineCEO.Balance.Economy;

/// <summary>
/// 离线版宏观引擎，公式对齐 Unity MacroEconomyManager（Phase 13）。
/// 使用 System.Random，避免 UnityEngine.Random 依赖。
/// </summary>
public sealed class MacroEconomyEngine
{
    private readonly Random _rng;

    public float GdpGrowthRate { get; private set; }
    public string EconomicPhase { get; private set; }
    public int PhaseDuration { get; private set; }
    public float FuelPriceUsdPerTon { get; private set; }

    public MacroEconomyEngine(
        float initialGdp = 3f,
        string initialPhase = "boom",
        float initialFuelUsdPerTon = 850f,
        int? seed = null)
    {
        GdpGrowthRate = initialGdp;
        EconomicPhase = initialPhase;
        PhaseDuration = 0;
        FuelPriceUsdPerTon = initialFuelUsdPerTon;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random(42);
    }

    /// <summary>每财年（或模拟中每季）推进一次 GDP / 阶段。</summary>
    public string TickYear(int year)
    {
        float prev = GdpGrowthRate;
        float fluctuation;
        float meanTarget;

        switch (EconomicPhase)
        {
            case "boom":
                fluctuation = NextFloat(-0.5f, 0.6f);
                meanTarget = 4f;
                break;
            case "recession":
                fluctuation = NextFloat(-0.8f, 0.4f);
                meanTarget = -1.5f;
                break;
            default:
                fluctuation = NextFloat(-0.4f, 0.45f);
                meanTarget = 2f;
                break;
        }

        float reversion = (meanTarget - prev) * 0.2f;
        GdpGrowthRate = Clamp(prev + fluctuation + reversion, -10f, 10f);

        string prevPhase = EconomicPhase;
        PhaseDuration++;

        if (GdpGrowthRate >= 3f)
        {
            if (prevPhase != "boom") PhaseDuration = 0;
            EconomicPhase = "boom";
        }
        else if (GdpGrowthRate < 0f)
        {
            if (prevPhase == "recession" || prev < 0f)
            {
                if (prevPhase != "recession") PhaseDuration = 0;
                EconomicPhase = "recession";
            }
            else
            {
                EconomicPhase = prevPhase;
            }
        }
        else
        {
            if (prevPhase != "recovery") PhaseDuration = 0;
            EconomicPhase = "recovery";
        }

        // 油价：基线随机游走 + 宏观倾向
        float oilTendency = GetOilTendency();
        float oilNoise = NextFloat(-0.04f, 0.04f);
        FuelPriceUsdPerTon = Clamp(
            FuelPriceUsdPerTon * (1f + oilTendency * 0.25f + oilNoise),
            400f,
            1600f);

        string changed = prevPhase != EconomicPhase ? $" → {PhaseLabel(EconomicPhase)}" : "";
        return $"Y{year}: GDP {GdpGrowthRate:F1}% · {PhaseLabel(EconomicPhase)}{changed} · 油价 {FuelPriceUsdPerTon:F0} $/t";
    }

    /// <summary>对齐 MacroEconomyManager.GetDemandMultiplier。</summary>
    public float GetDemandMultiplier(string passengerType)
    {
        return EconomicPhase switch
        {
            "boom" => passengerType == "business" ? 1.25f : 1.15f,
            "recession" => 1f - Clamp(Math.Abs(GdpGrowthRate) * 3f, 0.20f, 0.30f),
            "recovery" => passengerType == "leisure" ? 1.12f : 1.05f,
            _ => 1f
        };
    }

    public float GetOilTendency() => EconomicPhase switch
    {
        "boom" => 0.05f,
        "recession" => -NextFloat(0.20f, 0.30f),
        _ => 0f
    };

    /// <summary>季度级油价小幅漂移（±pct，默认 ±3%）。</summary>
    public void ApplyFuelDrift(float maxAbsPct = 0.03f)
    {
        float factor = 1f + NextFloat(-maxAbsPct, maxAbsPct);
        FuelPriceUsdPerTon = Clamp(FuelPriceUsdPerTon * factor, 400f, 1600f);
    }

    public static string PhaseLabel(string phase) => phase switch
    {
        "boom" => "繁荣期",
        "recession" => "衰退期",
        "recovery" => "复苏期",
        _ => "不明"
    };

    private float NextFloat(float min, float max) =>
        (float)(_rng.NextDouble() * (max - min) + min);

    private static float Clamp(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }
}
