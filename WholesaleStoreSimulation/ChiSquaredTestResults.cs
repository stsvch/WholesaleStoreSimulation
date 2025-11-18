using System.Collections.Generic;
using System.Linq;

namespace WholesaleStoreSimulation;

/// <summary>
/// Базовый результат выполнения теста хи-квадрат.
/// </summary>
public class ChiSquaredTestResult
{
    public double ChiSquaredStatistic { get; set; }
    public double CriticalValue { get; set; }
    public int DegreesOfFreedom { get; set; }
    public bool IsNormal { get; set; }
    public List<ChiSquaredIntervalInfo> Intervals { get; set; } = new();
    public double Mean { get; set; }
    public double StdDev { get; set; }
}

/// <summary>
/// Результат теста хи-квадрат для конкретного отклика с указанием его имени.
/// </summary>
public class ChiSquaredMetricTestResult
{
    public string MetricName { get; set; } = string.Empty;
    public ChiSquaredTestResult Result { get; set; } = new();

    public ChiSquaredMetricTestResult()
    {
    }

    public ChiSquaredMetricTestResult(string metricName, ChiSquaredTestResult baseResult)
    {
        MetricName = metricName;
        Result = new ChiSquaredTestResult
        {
            ChiSquaredStatistic = baseResult.ChiSquaredStatistic,
            CriticalValue = baseResult.CriticalValue,
            DegreesOfFreedom = baseResult.DegreesOfFreedom,
            IsNormal = baseResult.IsNormal,
            Mean = baseResult.Mean,
            StdDev = baseResult.StdDev,
            Intervals = baseResult.Intervals
                .Select(i => new ChiSquaredIntervalInfo
                {
                    LowerBound = i.LowerBound,
                    UpperBound = i.UpperBound,
                    ObservedFrequency = i.ObservedFrequency,
                    ExpectedFrequency = i.ExpectedFrequency
                })
                .ToList()
        };
    }
}

/// <summary>
/// Хранит информацию об одном интервале гистограммы.
/// </summary>
public class ChiSquaredIntervalInfo
{
    public double LowerBound { get; set; }
    public double UpperBound { get; set; }
    public int ObservedFrequency { get; set; }
    public double ExpectedFrequency { get; set; }
}
