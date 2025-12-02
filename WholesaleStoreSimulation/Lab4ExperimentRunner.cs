using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WholesaleStoreSimulation;

public record ResponseCurvePoint(double ParameterValue, double MeanResponse, double StdDev);

public class RegressionSummary
{
    public string Name { get; init; } = string.Empty;
    public double[] Coefficients { get; init; } = Array.Empty<double>();
    public double R2 { get; init; }
    public double Rmse { get; init; }
    public List<double> Predicted { get; init; } = new();
}

public class ResponseCurveExperimentResult
{
    public string ParameterName { get; init; } = string.Empty;
    public string ResponseName { get; init; } = string.Empty;
    public List<ResponseCurvePoint> Points { get; init; } = new();
    public RegressionSummary LinearModel { get; init; } = new();
    public RegressionSummary NonLinearModel { get; init; } = new();
    public string BestApproximation => LinearModel.Rmse <= NonLinearModel.Rmse ? LinearModel.Name : NonLinearModel.Name;
}

public class ResourceFailureOutcome
{
    public int FailedClerks { get; init; }
    public int ActiveClerks { get; init; }
    public double MeanQueueLength { get; init; }
    public double MeanSystemTime { get; init; }
    public double ServedArrivalRatio { get; init; }
    public bool IsStable { get; init; }
}

public class ResourceResilienceResult
{
    public int BaseClerks { get; init; }
    public List<ResourceFailureOutcome> Outcomes { get; init; } = new();
    public int MaxFailedWhileStable => Outcomes.Where(o => o.IsStable).Select(o => o.FailedClerks).DefaultIfEmpty(0).Max();
    public double StabilityQueueThreshold { get; init; }
    public double StabilityRatioThreshold { get; init; }
}

public class AlternativeScenarioResult
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Replications { get; init; }
    public double MeanSystemTime { get; init; }
    public double MeanQueueLength { get; init; }
    public double MeanUtilization { get; init; }
}

public class TwoFactorExperimentResult
{
    public string FactorAName { get; init; } = string.Empty;
    public string FactorBName { get; init; } = string.Empty;
    public List<double> FactorALevels { get; init; } = new();
    public List<double> FactorBLevels { get; init; } = new();
    public double[][] ResponseMatrix { get; init; } = Array.Empty<double[]>();
    public double FactorAEffect { get; init; }
    public double FactorBEffect { get; init; }
    public string DominantFactor => FactorAEffect > FactorBEffect ? FactorAName : FactorBName;
}

public class Lab4ExperimentResult
{
    public ResponseCurveExperimentResult ResponseCurve { get; init; } = new();
    public ResourceResilienceResult Resilience { get; init; } = new();
    public List<AlternativeScenarioResult> Alternatives { get; init; } = new();
    public TwoFactorExperimentResult TwoFactor { get; init; } = new();
}

public static class Lab4ExperimentRunner
{
    public static Lab4ExperimentResult Run(SimulatorConfig baseConfig)
    {
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("ЛАБОРАТОРНАЯ 4: ПОСТАНОВКА ЭКСПЕРИМЕНТОВ С МОДЕЛЬЮ");
        Console.WriteLine(new string('=', 70));

        var responseCurve = RunResponseCurveExperiment(baseConfig);
        var resilience = EvaluateResourceResilience(baseConfig);
        var alternatives = CompareAlternatives(baseConfig);
        var twoFactor = RunTwoFactorExperiment(baseConfig);

        var result = new Lab4ExperimentResult
        {
            ResponseCurve = responseCurve,
            Resilience = resilience,
            Alternatives = alternatives,
            TwoFactor = twoFactor
        };

        Lab4ReportGenerator.GenerateHtmlReport(result);
        return result;
    }

    private static ResponseCurveExperimentResult RunResponseCurveExperiment(SimulatorConfig baseConfig)
    {
        const int replicationsPerLevel = 8;
        var points = new List<ResponseCurvePoint>();
        var parameterLevels = Enumerable.Range(0, 8).Select(i => 1.4 + 0.15 * i).ToList();

        Console.WriteLine("\n1) Однофакторный эксперимент: зависимость AverageSystemTime от MeanInterArrivalTime");
        foreach (var level in parameterLevels)
        {
            var cfg = CloneConfig(baseConfig);
            cfg.MeanInterArrivalTime = level;
            var agg = WholesaleStoreSimulator.VerifyWithReplications(cfg, replicationsPerLevel);
            points.Add(new ResponseCurvePoint(level, agg.MeanAvgSystem, agg.StdDevAvgSystem));
            Console.WriteLine($"  λ={level:0.00} -> AvgSystemTime={agg.MeanAvgSystem:0.00} ± {agg.StdDevAvgSystem:0.00}");
        }

        var x = points.Select(p => p.ParameterValue).ToList();
        var y = points.Select(p => p.MeanResponse).ToList();

        var linear = FitLinear(x, y);
        var quadratic = FitQuadratic(x, y);

        return new ResponseCurveExperimentResult
        {
            ParameterName = "Средний интервал между прибытиями (λ)",
            ResponseName = "Среднее время в системе",
            Points = points,
            LinearModel = linear,
            NonLinearModel = quadratic
        };
    }

    private static ResourceResilienceResult EvaluateResourceResilience(SimulatorConfig baseConfig)
    {
        const int replications = 10;
        var outcomes = new List<ResourceFailureOutcome>();
        double queueThreshold = 12.0;
        double ratioThreshold = 0.97;

        Console.WriteLine("\n2) Поиск максимального числа отказанных ресурсов с сохранением стационарности");
        for (int failed = 0; failed <= baseConfig.ClerkCount; failed++)
        {
            int active = Math.Max(1, baseConfig.ClerkCount - failed);
            var cfg = CloneConfig(baseConfig);
            cfg.ClerkCount = active;
            var agg = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications);
            double meanArrived = agg.ReplicationResults.Average(r => r.CustomersArrived);
            double meanServed = agg.ReplicationResults.Average(r => r.CustomersServed);
            double ratio = meanArrived > 0 ? meanServed / meanArrived : 0;
            bool stable = agg.MeanAvgQueueLength <= queueThreshold && ratio >= ratioThreshold;

            outcomes.Add(new ResourceFailureOutcome
            {
                FailedClerks = failed,
                ActiveClerks = active,
                MeanQueueLength = agg.MeanAvgQueueLength,
                MeanSystemTime = agg.MeanAvgSystem,
                ServedArrivalRatio = ratio,
                IsStable = stable
            });
            Console.WriteLine($"  Потеряно {failed} клерков (осталось {active}): queue={agg.MeanAvgQueueLength:0.0}, ratio={ratio:0.000} -> {(stable ? "стационарно" : "нестабильно")}");
        }

        return new ResourceResilienceResult
        {
            BaseClerks = baseConfig.ClerkCount,
            Outcomes = outcomes,
            StabilityQueueThreshold = queueThreshold,
            StabilityRatioThreshold = ratioThreshold
        };
    }

    private static List<AlternativeScenarioResult> CompareAlternatives(SimulatorConfig baseConfig)
    {
        const int replications = 12;
        Console.WriteLine("\n3) Сравнение трёх альтернатив использования объекта моделирования");

        var scenarios = new List<(string Name, string Description, Func<SimulatorConfig, SimulatorConfig> Builder)>
        {
            ("Базовый", "Исходная конфигурация без изменений", cfg => CloneConfig(cfg)),
            (
                "Ускоренный склад",
                "Сокращение времени похода на склад (6-10-15 мин) при неизменном штате",
                cfg =>
                {
                    var copy = CloneConfig(cfg);
                    copy.WarehouseTripMin = 6.0;
                    copy.WarehouseTripMode = 10.0;
                    copy.WarehouseTripMax = 15.0;
                    return copy;
                }
            ),
            (
                "Дополнительный клерк",
                "Увеличение числа клерков до 4 при уменьшенной партии до 5 единиц",
                cfg =>
                {
                    var copy = CloneConfig(cfg);
                    copy.ClerkCount = 4;
                    copy.ClerkBatchSize = 5;
                    return copy;
                }
            )
        };

        var results = new List<AlternativeScenarioResult>();
        foreach (var scenario in scenarios)
        {
            var cfg = scenario.Builder(baseConfig);
            var agg = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications);
            results.Add(new AlternativeScenarioResult
            {
                Name = scenario.Name,
                Description = scenario.Description,
                Replications = replications,
                MeanSystemTime = agg.MeanAvgSystem,
                MeanQueueLength = agg.MeanAvgQueueLength,
                MeanUtilization = agg.MeanAvgUtil
            });
            Console.WriteLine($"  {scenario.Name}: AvgSystem={agg.MeanAvgSystem:0.00}, Queue={agg.MeanAvgQueueLength:0.00}, Util={agg.MeanAvgUtil:P1}");
        }

        return results;
    }

    private static TwoFactorExperimentResult RunTwoFactorExperiment(SimulatorConfig baseConfig)
    {
        const int replicationsPerCell = 6;
        var factorALevels = new List<double> { 1.6, 1.8, 2.0, 2.2 };
        var factorBLevels = new List<double> { 2, 3, 4, 5 };
        var matrix = new double[factorALevels.Count][];

        Console.WriteLine("\n4) Двухфакторный эксперимент: (λ, количество клерков) -> AverageSystemTime");
        for (int i = 0; i < factorALevels.Count; i++)
        {
            matrix[i] = new double[factorBLevels.Count];
            for (int j = 0; j < factorBLevels.Count; j++)
            {
                var cfg = CloneConfig(baseConfig);
                cfg.MeanInterArrivalTime = factorALevels[i];
                cfg.ClerkCount = (int)factorBLevels[j];
                var agg = WholesaleStoreSimulator.VerifyWithReplications(cfg, replicationsPerCell);
                matrix[i][j] = agg.MeanAvgSystem;
                Console.WriteLine($"  λ={factorALevels[i]:0.00}, клерков={factorBLevels[j]} -> AvgSystem={agg.MeanAvgSystem:0.00}");
            }
        }

        double factorAEffect = matrix.Select(row => row.Average()).Max() - matrix.Select(row => row.Average()).Min();
        double factorBEffect = Enumerable.Range(0, factorBLevels.Count)
            .Select(col => matrix.Select(row => row[col]).Average())
            .Max() - Enumerable.Range(0, factorBLevels.Count)
            .Select(col => matrix.Select(row => row[col]).Average())
            .Min();

        return new TwoFactorExperimentResult
        {
            FactorAName = "Средний интервал между прибытиями (λ)",
            FactorBName = "Количество клерков",
            FactorALevels = factorALevels,
            FactorBLevels = factorBLevels,
            ResponseMatrix = matrix,
            FactorAEffect = factorAEffect,
            FactorBEffect = factorBEffect
        };
    }

    private static RegressionSummary FitLinear(List<double> x, List<double> y)
    {
        var (slope, intercept, r) = CalculateLinearRegression(x, y);
        var predictions = x.Select(v => intercept + slope * v).ToList();
        var rmse = CalculateRmse(y, predictions);
        double r2 = CalculateR2(y, predictions);
        return new RegressionSummary
        {
            Name = "Линейная аппроксимация",
            Coefficients = new[] { intercept, slope },
            R2 = r2,
            Rmse = rmse,
            Predicted = predictions
        };
    }

    private static RegressionSummary FitQuadratic(List<double> x, List<double> y)
    {
        var coefficients = CalculateQuadraticRegression(x, y);
        var predictions = x.Select(v => coefficients[0] + coefficients[1] * v + coefficients[2] * v * v).ToList();
        var rmse = CalculateRmse(y, predictions);
        double r2 = CalculateR2(y, predictions);
        return new RegressionSummary
        {
            Name = "Квадратичная аппроксимация",
            Coefficients = coefficients,
            R2 = r2,
            Rmse = rmse,
            Predicted = predictions
        };
    }

    private static (double slope, double intercept, double r) CalculateLinearRegression(List<double> x, List<double> y)
    {
        if (x.Count != y.Count || x.Count == 0)
            return (0, 0, 0);

        double n = x.Count;
        double sumX = x.Sum();
        double sumY = y.Sum();
        double sumXY = x.Zip(y, (a, b) => a * b).Sum();
        double sumX2 = x.Sum(a => a * a);
        double sumY2 = y.Sum(a => a * a);

        double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        double intercept = (sumY - slope * sumX) / n;

        double r = (n * sumXY - sumX * sumY) /
                  Math.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));

        return (slope, intercept, r);
    }

    private static double[] CalculateQuadraticRegression(List<double> x, List<double> y)
    {
        double n = x.Count;
        double sumX = x.Sum();
        double sumX2 = x.Sum(v => v * v);
        double sumX3 = x.Sum(v => v * v * v);
        double sumX4 = x.Sum(v => v * v * v * v);
        double sumY = y.Sum();
        double sumXY = x.Zip(y, (a, b) => a * b).Sum();
        double sumX2Y = x.Zip(y, (a, b) => a * a * b).Sum();

        double[,] m =
        {
            { n, sumX, sumX2 },
            { sumX, sumX2, sumX3 },
            { sumX2, sumX3, sumX4 }
        };
        double[] b = { sumY, sumXY, sumX2Y };

        return Solve3x3(m, b);
    }

    private static double[] Solve3x3(double[,] m, double[] b)
    {
        double[,] a = (double[,])m.Clone();
        double[] rhs = (double[])b.Clone();
        int n = 3;

        for (int i = 0; i < n; i++)
        {
            int maxRow = i;
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(a[k, i]) > Math.Abs(a[maxRow, i]))
                    maxRow = k;
            }

            for (int k = i; k < n; k++)
            {
                (a[maxRow, k], a[i, k]) = (a[i, k], a[maxRow, k]);
            }
            (rhs[maxRow], rhs[i]) = (rhs[i], rhs[maxRow]);

            double pivot = a[i, i];
            if (Math.Abs(pivot) < 1e-9)
                continue;
            for (int k = i; k < n; k++)
                a[i, k] /= pivot;
            rhs[i] /= pivot;

            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                double factor = a[j, i];
                for (int k = i; k < n; k++)
                    a[j, k] -= factor * a[i, k];
                rhs[j] -= factor * rhs[i];
            }
        }

        return rhs;
    }

    private static double CalculateRmse(List<double> actual, List<double> predicted)
    {
        if (actual.Count == 0) return 0;
        double mse = actual.Zip(predicted, (a, p) => Math.Pow(a - p, 2)).Average();
        return Math.Sqrt(mse);
    }

    private static double CalculateR2(List<double> actual, List<double> predicted)
    {
        if (actual.Count == 0) return 0;
        double mean = actual.Average();
        double ssTot = actual.Sum(v => Math.Pow(v - mean, 2));
        double ssRes = actual.Zip(predicted, (a, p) => Math.Pow(a - p, 2)).Sum();
        return ssTot == 0 ? 0 : 1 - ssRes / ssTot;
    }

    private static SimulatorConfig CloneConfig(SimulatorConfig original)
    {
        return JsonSerializer.Deserialize<SimulatorConfig>(
            JsonSerializer.Serialize(original)) ?? new SimulatorConfig();
    }
}

public static class Lab4ReportGenerator
{
    public static void GenerateHtmlReport(Lab4ExperimentResult result, string fileName = "lab4_experiments.html")
    {
        var html = BuildHtml(result);
        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        File.WriteAllText(fullPath, html, Encoding.UTF8);
        Console.WriteLine($"\nОтчет по лабораторной работе 4 сохранен: {fullPath}");
    }

    private static string BuildHtml(Lab4ExperimentResult result)
    {
        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"UTF-8\"><title>Лабораторная 4 — эксперименты</title>");
        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
        sb.AppendLine("<script src=\"https://cdn.plot.ly/plotly-latest.min.js\"></script>");
        sb.AppendLine("<style>body{font-family:'Segoe UI',sans-serif;background:#f3f4f6;margin:0;padding:20px;} .container{max-width:1600px;margin:auto;background:white;padding:30px;border-radius:16px;box-shadow:0 10px 30px rgba(0,0,0,0.08);} h1,h2{text-align:center;color:#1f2937;} h3{color:#111827;} .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:24px;} .card{background:#fff;padding:18px;border-radius:12px;box-shadow:0 4px 14px rgba(0,0,0,0.05);} table{width:100%;border-collapse:collapse;margin-top:10px;} th,td{border:1px solid #e5e7eb;padding:8px;text-align:left;} th{background:#f9fafb;} .pill{display:inline-block;padding:4px 10px;border-radius:9999px;background:#eef2ff;color:#4338ca;font-weight:600;} .metric{font-size:18px;font-weight:700;color:#2563eb;} .ok{color:#16a34a;font-weight:700;} .bad{color:#dc2626;font-weight:700;} </style></head><body><div class='container'>");
        sb.AppendLine("<h1>Лабораторная работа №4 — постановка экспериментов</h1>");

        AppendResponseCurveSection(sb, result.ResponseCurve, culture);
        AppendResilienceSection(sb, result.Resilience, culture);
        AppendAlternativesSection(sb, result.Alternatives, culture);
        AppendTwoFactorSection(sb, result.TwoFactor, culture);

        sb.AppendLine("</div><script>const fmt=v=>Number.parseFloat(v).toFixed(2);</script></body></html>");
        return sb.ToString();
    }

    private static void AppendResponseCurveSection(StringBuilder sb, ResponseCurveExperimentResult result, CultureInfo culture)
    {
        var labels = result.Points.Select(p => p.ParameterValue).ToList();
        var responses = result.Points.Select(p => p.MeanResponse).ToList();
        var linear = result.LinearModel.Predicted;
        var nonLinear = result.NonLinearModel.Predicted;

        sb.AppendLine("<h2>1. Влияние одного параметра (7+ уровней) с аппроксимацией</h2>");
        sb.AppendLine("<div class='grid'><div class='card'><h3>Таблица результатов</h3><table><tr><th>Уровень λ</th><th>AvgSystemTime</th><th>StdDev</th></tr>");
        foreach (var p in result.Points)
        {
            sb.AppendLine($"<tr><td>{p.ParameterValue.ToString("F2", culture)}</td><td>{p.MeanResponse.ToString("F2", culture)}</td><td>{p.StdDev.ToString("F2", culture)}</td></tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine($"<p class='metric'>Лучшая аппроксимация: {result.BestApproximation} (RMSE {Math.Min(result.LinearModel.Rmse, result.NonLinearModel.Rmse):F3})</p>");
        sb.AppendLine("</div><div class='card'><h3>Кривая отклика с аппроксимациями</h3><canvas id='responseCurveChart'></canvas></div></div>");
        sb.AppendLine("<script>");
        sb.AppendLine($"const rcLabels = {JsonSerializer.Serialize(labels, new JsonSerializerOptions{WriteIndented=false})};");
        sb.AppendLine($"const rcData = {JsonSerializer.Serialize(responses, new JsonSerializerOptions{WriteIndented=false})};");
        sb.AppendLine($"const rcLinear = {JsonSerializer.Serialize(linear, new JsonSerializerOptions{WriteIndented=false})};");
        sb.AppendLine($"const rcPoly = {JsonSerializer.Serialize(nonLinear, new JsonSerializerOptions{WriteIndented=false})};");
        sb.AppendLine($"new Chart(document.getElementById('responseCurveChart'), {{ type: 'line', data: {{ labels: rcLabels, datasets: [{{ label: 'Наблюдаемые', data: rcData, borderColor: '#2563eb', backgroundColor:'#2563eb33', tension:0.2, pointRadius:4 }}, {{ label: 'Линейная', data: rcLinear, borderColor:'#16a34a', fill:false, tension:0.1 }}, {{ label: 'Квадратичная', data: rcPoly, borderColor:'#f59e0b', fill:false, tension:0.1 }}] }}, options: {{ responsive:true, plugins: {{ legend: {{ position:'top' }} }}, scales: {{ x: {{ title: {{ display:true, text:'MeanInterArrivalTime (мин)' }} }}, y: {{ title: {{ display:true, text:'AverageSystemTime (мин)' }} }} }} }} }});");
        sb.AppendLine("</script>");
    }

    private static void AppendResilienceSection(StringBuilder sb, ResourceResilienceResult result, CultureInfo culture)
    {
        var labels = result.Outcomes.Select(o => o.FailedClerks).ToList();
        var queues = result.Outcomes.Select(o => o.MeanQueueLength).ToList();
        var ratios = result.Outcomes.Select(o => o.ServedArrivalRatio).ToList();
        sb.AppendLine("<h2>2. Устойчивость к отказам ресурсов</h2>");
        sb.AppendLine($"<p>Стационарность считается сохраненной при очереди ≤ {result.StabilityQueueThreshold} и доле обслуженных ≥ {result.StabilityRatioThreshold:P1}.</p>");
        sb.AppendLine($"<p class='metric'>Максимум отказанных клерков без потери стационарности: {result.MaxFailedWhileStable} из {result.BaseClerks}</p>");
        sb.AppendLine("<div class='grid'><div class='card'><h3>Показатели</h3><table><tr><th>Отказано</th><th>Активно</th><th>AvgQueue</th><th>AvgSystem</th><th>Доля обслуженных</th><th>Состояние</th></tr>");
        foreach (var o in result.Outcomes)
        {
            var state = o.IsStable ? "<span class='ok'>стационарно</span>" : "<span class='bad'>нестабильно</span>";
            sb.AppendLine($"<tr><td>{o.FailedClerks}</td><td>{o.ActiveClerks}</td><td>{o.MeanQueueLength.ToString("F2", culture)}</td><td>{o.MeanSystemTime.ToString("F2", culture)}</td><td>{o.ServedArrivalRatio.ToString("P1", culture)}</td><td>{state}</td></tr>");
        }
        sb.AppendLine("</table></div><div class='card'><h3>Очередь и доля обслуженных</h3><canvas id='resilienceChart'></canvas></div></div>");
        sb.AppendLine("<script>");
        sb.AppendLine($"const resLabels = {JsonSerializer.Serialize(labels)}; const resQueue = {JsonSerializer.Serialize(queues)}; const resRatio = {JsonSerializer.Serialize(ratios)};\nnew Chart(document.getElementById('resilienceChart'), {{ type:'line', data: {{ labels: resLabels, datasets: [{{ label:'Средняя очередь', data: resQueue, borderColor:'#2563eb', backgroundColor:'#2563eb33', tension:0.2 }}, {{ label:'Доля обслуженных', data: resRatio, borderColor:'#16a34a', backgroundColor:'#16a34a33', tension:0.2, yAxisID:'y1' }}] }}, options: {{ responsive:true, scales: {{ y: {{ title:{display:true,text:'AvgQueue'}, suggestedMax: Math.max(...resQueue, {result.StabilityQueueThreshold})+1 }}, y1: {{ position:'right', min:0.9, max:1.01, title:{display:true,text:'Served/Arrived'}, grid:{{ drawOnChartArea:false }} }} }}, plugins: {{ annotation: {{ annotations:{} }} }} }} });");
        sb.AppendLine("</script>");
    }

    private static void AppendAlternativesSection(StringBuilder sb, List<AlternativeScenarioResult> alternatives, CultureInfo culture)
    {
        sb.AppendLine("<h2>3. Сравнение трех альтернатив</h2>");
        sb.AppendLine("<div class='card'><table><tr><th>Сценарий</th><th>Описание</th><th>AvgSystemTime</th><th>AvgQueue</th><th>Utilization</th></tr>");
        foreach (var alt in alternatives)
        {
            sb.AppendLine($"<tr><td><span class='pill'>{alt.Name}</span></td><td>{alt.Description}</td><td>{alt.MeanSystemTime.ToString("F2", culture)}</td><td>{alt.MeanQueueLength.ToString("F2", culture)}</td><td>{alt.MeanUtilization.ToString("P1", culture)}</td></tr>");
        }
        sb.AppendLine("</table><div style='height:420px'><canvas id='alternativesChart'></canvas></div></div>");

        sb.AppendLine("<script>");
        sb.AppendLine($"const altLabels = {JsonSerializer.Serialize(alternatives.Select(a=>a.Name).ToList())};");
        sb.AppendLine($"const altSystem = {JsonSerializer.Serialize(alternatives.Select(a=>a.MeanSystemTime).ToList())};");
        sb.AppendLine($"const altQueue = {JsonSerializer.Serialize(alternatives.Select(a=>a.MeanQueueLength).ToList())};");
        sb.AppendLine($"const altUtil = {JsonSerializer.Serialize(alternatives.Select(a=>a.MeanUtilization).ToList())};");
        sb.AppendLine("new Chart(document.getElementById('alternativesChart'), { type:'bar', data:{ labels: altLabels, datasets:[{ label:'AvgSystemTime', data: altSystem, backgroundColor:'#2563ebaa' },{ label:'AvgQueueLength', data: altQueue, backgroundColor:'#f59e0baa' },{ label:'Utilization', data: altUtil, backgroundColor:'#16a34aaa', yAxisID:'y1' }]}, options:{ responsive:true, scales:{ y:{ beginAtZero:true, title:{display:true,text:'Время / Очередь'}}, y1:{ position:'right', min:0, max:1.1, grid:{ drawOnChartArea:false }, title:{display:true,text:'Utilization'} }}} });");
        sb.AppendLine("</script>");
    }

    private static void AppendTwoFactorSection(StringBuilder sb, TwoFactorExperimentResult result, CultureInfo culture)
    {
        sb.AppendLine("<h2>4. Двухфакторный эксперимент</h2>");
        sb.AppendLine($"<p>Доминирующий фактор по амплитуде влияния: <b>{result.DominantFactor}</b> (ΔA={result.FactorAEffect:F2}, ΔB={result.FactorBEffect:F2}).</p>");
        sb.AppendLine("<div class='grid'><div class='card'><h3>Матрица отклика (AvgSystemTime)</h3><table><tr><th>λ / Клерков</th>");
        foreach (var level in result.FactorBLevels)
            sb.AppendLine($"<th>{level}</th>");
        sb.AppendLine("</tr>");
        for (int i = 0; i < result.FactorALevels.Count; i++)
        {
            sb.AppendLine($"<tr><th>{result.FactorALevels[i].ToString("F1", culture)}</th>");
            for (int j = 0; j < result.FactorBLevels.Count; j++)
            {
                sb.AppendLine($"<td>{result.ResponseMatrix[i][j].ToString("F2", culture)}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table></div><div class='card'><h3>Поверхность отклика</h3><div id='surfacePlot' style='height:500px;'></div></div></div>");

        sb.AppendLine("<script>");
        sb.AppendLine($"const tfX = {JsonSerializer.Serialize(result.FactorBLevels)}; const tfY = {JsonSerializer.Serialize(result.FactorALevels)}; const tfZ = {JsonSerializer.Serialize(result.ResponseMatrix)}; Plotly.newPlot('surfacePlot', [{{ type:'surface', x: tfX, y: tfY, z: tfZ, colorscale:'Viridis' }}], {{ scene: {{ xaxis: {{ title:'Количество клерков' }}, yaxis: {{ title:'λ (мин)' }}, zaxis: {{ title:'AverageSystemTime' }} }}, margin: {{ l:0, r:0, b:0, t:0 }} }});");
        sb.AppendLine("</script>");
    }
}
