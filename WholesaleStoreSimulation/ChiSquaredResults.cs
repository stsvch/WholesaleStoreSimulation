using System.Collections.Generic;
using System.Linq;

namespace WholesaleStoreSimulation;

/// <summary>
/// Legacy models that existed in earlier versions of the project.  The new
/// implementation uses <see cref="ChiSquaredMetricTestResult"/> and
/// <see cref="ChiSquaredTestResult"/>, but some callers (for example Visual
/// Studio projects that still reference the old API) expect the historical
/// <c>ChiSquaredResult</c> and <c>ChiSquaredInterval</c> types.
///
/// The classes below act as a thin compatibility layer that simply proxies the
/// data to the new models.  This keeps existing code compiling while allowing
/// the new reporting pipeline to work with the richer metric-aware results.
/// </summary>
public class ChiSquaredResult
{
    public string MetricName { get; set; } = string.Empty;
    public double Mean { get; set; }
    public double StdDev { get; set; }
    public double ChiSquaredStatistic { get; set; }
    public int DegreesOfFreedom { get; set; }
    public double CriticalValue { get; set; }
    public bool IsNormal { get; set; }
    public List<ChiSquaredInterval> Intervals { get; set; } = new();

    public ChiSquaredMetricTestResult ToMetricTestResult()
    {
        var baseResult = new ChiSquaredTestResult
        {
            ChiSquaredStatistic = ChiSquaredStatistic,
            CriticalValue = CriticalValue,
            DegreesOfFreedom = DegreesOfFreedom,
            IsNormal = IsNormal,
            Mean = Mean,
            StdDev = StdDev,
            Intervals = Intervals.Select(i => i.ToIntervalInfo()).ToList()
        };

        return new ChiSquaredMetricTestResult(MetricName, baseResult);
    }

    public static ChiSquaredResult FromMetricResult(ChiSquaredMetricTestResult metricResult)
    {
        return new ChiSquaredResult
        {
            MetricName = metricResult.MetricName,
            Mean = metricResult.Result.Mean,
            StdDev = metricResult.Result.StdDev,
            ChiSquaredStatistic = metricResult.Result.ChiSquaredStatistic,
            DegreesOfFreedom = metricResult.Result.DegreesOfFreedom,
            CriticalValue = metricResult.Result.CriticalValue,
            IsNormal = metricResult.Result.IsNormal,
            Intervals = metricResult.Result.Intervals
                .Select(ChiSquaredInterval.FromIntervalInfo)
                .ToList()
        };
    }
}

public class ChiSquaredInterval
{
    public double LowerBound { get; set; }
    public double UpperBound { get; set; }
    public int ObservedFrequency { get; set; }
    public double ExpectedFrequency { get; set; }

    public ChiSquaredIntervalInfo ToIntervalInfo() => new()
    {
        LowerBound = LowerBound,
        UpperBound = UpperBound,
        ObservedFrequency = ObservedFrequency,
        ExpectedFrequency = ExpectedFrequency
    };

    public static ChiSquaredInterval FromIntervalInfo(ChiSquaredIntervalInfo info) => new()
    {
        LowerBound = info.LowerBound,
        UpperBound = info.UpperBound,
        ObservedFrequency = info.ObservedFrequency,
        ExpectedFrequency = info.ExpectedFrequency
    };
}
