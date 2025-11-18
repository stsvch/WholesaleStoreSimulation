using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WholesaleStoreSimulation
{
    public class ContinuousRunAnalysisResult
    {
        public List<double> IndependentRunData { get; set; } = new();
        public List<double> ContinuousRunData { get; set; } = new();
        
        public double IndependentMean { get; set; }
        public double IndependentVariance { get; set; }
        public double ContinuousMean { get; set; }
        public double ContinuousVariance { get; set; }
        
        public FTestResult FTest { get; set; }
        public TTestResult TTest { get; set; }
        public AutocorrelationResult Autocorrelation { get; set; }
        
        public bool IsContinuousRunAcceptable { get; set; }
        public string Conclusion { get; set; }
    }

    public class AutocorrelationResult
    {
        public double AutocorrelationCoefficient { get; set; }
        public double CriticalValue { get; set; }
        public bool IsSignificant { get; set; }
    }

    public class ContinuousRunAnalysisRunner
    {
        private readonly SimulatorConfig _baseConfig;
        private readonly int _replications;
        private const double Alpha = 0.05;
        private const double AutocorrelationCritical = 0.25;

        public ContinuousRunAnalysisRunner(SimulatorConfig baseConfig, int replications)
        {
            _baseConfig = baseConfig;
            _replications = replications;
        }

        public ContinuousRunAnalysisResult Run()
        {
            Console.WriteLine("\n" + new string('-', 60));
            Console.WriteLine("АНАЛИЗ ВОЗМОЖНОСТИ НЕПРЕРЫВНОГО ПРОГОНА");
            Console.WriteLine(new string('-', 60));

            Console.WriteLine($"\nШаг 1: Формирование выборок из {_replications} репликаций");

            Console.WriteLine("Шаг 1.1: Независимые прогоны (эталон)...");
            var independentData = RunIndependentReplications();

            Console.WriteLine("Шаг 1.2: Непрерывный прогон...");
            var continuousData = RunContinuousReplication();

            double independentMean = independentData.Average();
            double independentVar = CalculateVariance(independentData);
            double continuousMean = continuousData.Average();
            double continuousVar = CalculateVariance(continuousData);

            Console.WriteLine($"\nСтатистики выборок:");
            Console.WriteLine($"  Независимые: среднее = {independentMean:F3}, дисперсия = {independentVar:F3}");
            Console.WriteLine($"  Непрерывные: среднее = {continuousMean:F3}, дисперсия = {continuousVar:F3}");

            var fTestResult = PerformFTest(independentVar, independentData.Count, continuousVar, continuousData.Count);
            
            var tTestResult = fTestResult.VariancesAreEqual
                ? PerformStudentTTest(independentMean, independentVar, independentData.Count, continuousMean, continuousVar, continuousData.Count)
                : PerformWelchTTest(independentMean, independentVar, independentData.Count, continuousMean, continuousVar, continuousData.Count);

            var autocorrelationResult = CalculateAutocorrelation(continuousData);

            bool isStatisticallyHomogeneous = tTestResult.MeansAreEqual;
            bool hasInsignificantAutocorrelation = !autocorrelationResult.IsSignificant;
            bool isAcceptable = isStatisticallyHomogeneous && hasInsignificantAutocorrelation;

            string conclusion;
            if (isAcceptable)
            {
                conclusion = "Гипотеза о возможности использования непрерывного прогона ПРИНИМАЕТСЯ. " +
                           "Оба условия выполнены: статистическая однородность и незначительная автокорреляция.";
            }
            else
            {
                var reasons = new List<string>();
                if (!isStatisticallyHomogeneous) reasons.Add("отсутствие статистической однородности");
                if (!hasInsignificantAutocorrelation) reasons.Add("наличие значимой автокорреляции");
                
                conclusion = $"Гипотеза о возможности использования непрерывного прогона ОТВЕРГАЕТСЯ. " +
                           $"Причина: {string.Join(" и ", reasons)}.";
            }

            return new ContinuousRunAnalysisResult
            {
                IndependentRunData = independentData,
                ContinuousRunData = continuousData,
                IndependentMean = independentMean,
                IndependentVariance = independentVar,
                ContinuousMean = continuousMean,
                ContinuousVariance = continuousVar,
                FTest = fTestResult,
                TTest = tTestResult,
                Autocorrelation = autocorrelationResult,
                IsContinuousRunAcceptable = isAcceptable,
                Conclusion = conclusion
            };
        }

        private List<double> RunIndependentReplications()
        {
            var results = new List<double>();
            
            for (int i = 0; i < _replications; i++)
            {
                var config = new SimulatorConfig
                {
                    SimulationDuration = _baseConfig.SimulationDuration,
                    RandomSeed = _baseConfig.RandomSeed + i,
                    ClerkCount = _baseConfig.ClerkCount,
                    ClerkBatchSize = _baseConfig.ClerkBatchSize
                };
                
                var sim = new WholesaleStoreSimulator(config, trace: false);
                sim.Run();
                var result = sim.GetResult();
                
                results.Add(result.AverageSystemTime);
            }
            
            return results;
        }

        private List<double> RunContinuousReplication()
        {
            var results = new List<double>();
            var config = new SimulatorConfig
            {
                SimulationDuration = _baseConfig.SimulationDuration * _replications,
                RandomSeed = _baseConfig.RandomSeed,
                ClerkCount = _baseConfig.ClerkCount,
                ClerkBatchSize = _baseConfig.ClerkBatchSize
            };

            var sim = new WholesaleStoreSimulator(config, trace: false);
            sim.Run();
            
            var fullResult = sim.GetResult();
            
            double subReplicationDuration = _baseConfig.SimulationDuration;
            var timePoints = fullResult.PerCustomerSystemTime;
            
            for (int i = 0; i < _replications; i++)
            {
                double startTime = i * subReplicationDuration;
                double endTime = (i + 1) * subReplicationDuration;
                
                var subReplicationData = timePoints
                    .Where(t => t.Time >= startTime && t.Time < endTime)
                    .Select(t => t.Value)
                    .ToList();
                
                if (subReplicationData.Any())
                {
                    results.Add(subReplicationData.Average());
                }
            }
            
            return results;
        }

        private AutocorrelationResult CalculateAutocorrelation(List<double> data)
        {
            if (data.Count < 2)
            {
                return new AutocorrelationResult
                {
                    AutocorrelationCoefficient = 0,
                    CriticalValue = AutocorrelationCritical,
                    IsSignificant = false
                };
            }

            double mean = data.Average();
            double variance = data.Select(x => Math.Pow(x - mean, 2)).Sum() / (data.Count - 1);
            
            if (variance == 0) 
            {
                return new AutocorrelationResult
                {
                    AutocorrelationCoefficient = 0,
                    CriticalValue = AutocorrelationCritical,
                    IsSignificant = false
                };
            }

            double covariance = 0;
            for (int i = 0; i < data.Count - 1; i++)
            {
                covariance += (data[i] - mean) * (data[i + 1] - mean);
            }
            covariance /= (data.Count - 2);

            double autocorrelation = covariance / variance;
            bool isSignificant = Math.Abs(autocorrelation) >= AutocorrelationCritical;

            Console.WriteLine("\n--- Проверка автокорреляции ---");
            Console.WriteLine($"  Коэффициент автокорреляции r₁: {autocorrelation:F4}");
            Console.WriteLine($"  Критическое значение: ±{AutocorrelationCritical}");
            Console.WriteLine($"  Результат: автокорреляция {(isSignificant ? "ЗНАЧИМА" : "незначима")}");

            return new AutocorrelationResult
            {
                AutocorrelationCoefficient = autocorrelation,
                CriticalValue = AutocorrelationCritical,
                IsSignificant = isSignificant
            };
        }

        private FTestResult PerformFTest(double varA, int nA, double varB, int nB)
        {
            double fStat;
            int df1, df2;

            if (varA >= varB)
            {
                fStat = varA / varB;
                df1 = nA - 1;
                df2 = nB - 1;
            }
            else
            {
                fStat = varB / varA;
                df1 = nB - 1;
                df2 = nA - 1;
            }

            double fCritical = FDistributionProvider.GetCriticalValue(df1, df2, Alpha);
            bool areEqual = fStat < fCritical;
            
            Console.WriteLine("\n--- F-тест (равенство дисперсий) ---");
            Console.WriteLine($"  F-статистика: {fStat:F4}");
            Console.WriteLine($"  F-критическое (df1={df1}, df2={df2}): {fCritical:F4}");
            Console.WriteLine($"  Результат: Дисперсии {(areEqual ? "РАВНЫ" : "НЕ РАВНЫ")}");

            return new FTestResult
            {
                FStatistic = fStat,
                FCritical = fCritical,
                Df1 = df1,
                Df2 = df2,
                VariancesAreEqual = areEqual
            };
        }

        private TTestResult PerformStudentTTest(double mean0, double var0, int n0, double mean1, double var1, int n1)
        {
            int df = n0 + n1 - 2;
            double sp2 = ((n0 - 1) * var0 + (n1 - 1) * var1) / df;
            double se = Math.Sqrt(sp2 * (1.0 / n0 + 1.0 / n1));
            double tStat = Math.Abs(mean0 - mean1) / se;
            
            double tCritical = TDistributionProvider.GetCriticalValue(df, Alpha);
            bool areEqual = tStat < tCritical;
            
            Console.WriteLine("\n--- t-тест Стьюдента (для равных дисперсий) ---");
            Console.WriteLine($"  t-статистика: {tStat:F4}");
            Console.WriteLine($"  t-критическое (df={df}): {tCritical:F4}");
            Console.WriteLine($"  Результат: Средние {(areEqual ? "РАВНЫ" : "НЕ РАВНЫ")}");

            return new TTestResult
            {
                TestType = "Критерий Стьюдента (дисперсии равны)",
                TStatistic = tStat,
                TCritical = tCritical,
                Df = df,
                MeansAreEqual = areEqual
            };
        }

        private TTestResult PerformWelchTTest(double mean0, double var0, int n0, double mean1, double var1, int n1)
        {
            double tStat = Math.Abs(mean0 - mean1) / Math.Sqrt(var0 / n0 + var1 / n1);
            
            double tCrit0 = TDistributionProvider.GetCriticalValue(n0 - 1, Alpha);
            double tCrit1 = TDistributionProvider.GetCriticalValue(n1 - 1, Alpha);
            double term0 = var0 / n0;
            double term1 = var1 / n1;
            double tCriticalWeighted = (tCrit0 * term0 + tCrit1 * term1) / (term0 + term1);
            
            bool areEqual = tStat < tCriticalWeighted;

            Console.WriteLine("\n--- Взвешенный t-тест (для неравных дисперсий) ---");
            Console.WriteLine($"  t-статистика: {tStat:F4}");
            Console.WriteLine($"  t-критическое (взвешенное, df0={n0-1}, df1={n1-1}): {tCriticalWeighted:F4}");
            Console.WriteLine($"  Результат: Средние {(areEqual ? "РАВНЫ" : "НЕ РАВНЫ")}");

            return new TTestResult
            {
                TestType = "Взвешенный t-критерий (дисперсии не равны)",
                TStatistic = tStat,
                TCritical = tCriticalWeighted,
                Df = n0 + n1,
                MeansAreEqual = areEqual
            };
        }

        private static double CalculateVariance(IReadOnlyCollection<double> data)
        {
            if (data.Count < 2) return 0;
            double mean = data.Average();
            return data.Sum(d => Math.Pow(d - mean, 2)) / (data.Count - 1);
        }
    }

    public static class ContinuousRunAnalysis
    {
        public static ContinuousRunAnalysisResult RunAnalysis(SimulatorConfig baseConfig)
        {
            const int replications = 30;
            
            Console.WriteLine($"\nПроверка возможности использования непрерывного прогона на {replications} репликациях");
            
            try
            {
                var runner = new ContinuousRunAnalysisRunner(baseConfig, replications);
                var result = runner.Run();
                
                Console.WriteLine($"\nИтоговый вывод: {result.Conclusion}");
                return result;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nОШИБКА при выполнении анализа непрерывного прогона: {ex.Message}");
                Console.ResetColor();
                return null;
            }
        }
    }

    public static class ContinuousRunChartGenerator
    {
        public static void GenerateHtmlReport(ContinuousRunAnalysisResult result, string fileName = "continuous_run_analysis.html")
        {
            var html = GenerateHtmlContent(result);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            File.WriteAllText(fullPath, html, Encoding.UTF8);
            Console.WriteLine($"\nHTML отчет по анализу непрерывного прогона успешно сгенерирован: {fullPath}");
        }

        private static string GenerateHtmlContent(ContinuousRunAnalysisResult result)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            
            var htmlBuilder = new StringBuilder();
            htmlBuilder.AppendLine("<!DOCTYPE html>");
            htmlBuilder.AppendLine("<html lang=\"ru\"><head>");
            htmlBuilder.AppendLine("<meta charset=\"UTF-8\"><title>Анализ возможности непрерывного прогона</title>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            htmlBuilder.AppendLine("<style>");
            htmlBuilder.AppendLine("    body { font-family: 'Segoe UI', sans-serif; margin: 0; padding: 20px; background: #f0f2f5; color: #333; }");
            htmlBuilder.AppendLine("    .container { max-width: 1800px; margin: auto; background: white; border-radius: 20px; padding: 30px; box-shadow: 0 10px 30px rgba(0,0,0,0.1); }");
            htmlBuilder.AppendLine("    h1, h2 { text-align: center; color: #2c3e50; }");
            htmlBuilder.AppendLine("    h2 { margin-top: 50px; border-top: 1px solid #ddd; padding-top: 30px; }");
            htmlBuilder.AppendLine("    .grid-container { display: grid; grid-template-columns: repeat(auto-fit, minmax(400px, 1fr)); gap: 25px; margin-bottom: 40px; }");
            htmlBuilder.AppendLine("    .result-block { background: #f8f9fa; border-left: 4px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px;}");
            htmlBuilder.AppendLine("    .result-accepted { border-left-color: #27ae60; } .result-rejected { border-left-color: #e74c3c; }");
            htmlBuilder.AppendLine("    .chart-container { background: #fff; padding: 25px; border-radius: 15px; box-shadow: 0 5px 15px rgba(0,0,0,0.05); text-align: center; }");
            htmlBuilder.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 15px; } th, td { border: 1px solid #ddd; padding: 8px; text-align: left; } th { background-color: #f2f2f2; }");
            htmlBuilder.AppendLine("</style>");
            htmlBuilder.AppendLine("</head><body><div class=\"container\">");
            
            htmlBuilder.AppendLine("<h1>📊 1.5 Анализ возможности использования непрерывного прогона</h1>");
            
            htmlBuilder.AppendLine($"<div class=\"result-block {(result.IsContinuousRunAcceptable ? "result-accepted" : "result-rejected")}\">");
            htmlBuilder.AppendLine($"<h3>Итоговое заключение</h3><p>{result.Conclusion}</p></div>");

            htmlBuilder.AppendLine("<h2>Статистические тесты</h2>");
            htmlBuilder.AppendLine("<div class=\"grid-container\">");
            
            htmlBuilder.AppendLine("<div class=\"result-block\"><h4>F-тест (равенство дисперсий)</h4><table>");
            htmlBuilder.AppendLine($"<tr><td>F-статистика</td><td>{result.FTest.FStatistic:F4}</td></tr>");
            htmlBuilder.AppendLine($"<tr><td>F-критическое (df1={result.FTest.Df1}, df2={result.FTest.Df2})</td><td>{result.FTest.FCritical:F4}</td></tr>");
            htmlBuilder.AppendLine($"<tr><td><b>Вывод</b></td><td><b>Дисперсии {(result.FTest.VariancesAreEqual ? "РАВНЫ" : "НЕ РАВНЫ")}</b></td></tr>");
            htmlBuilder.AppendLine("</table></div>");

            htmlBuilder.AppendLine("<div class=\"result-block\"><h4>t-тест (равенство средних)</h4>");
            htmlBuilder.AppendLine($"<p><i>Тип критерия: {result.TTest.TestType}</i></p><table>");
            htmlBuilder.AppendLine($"<tr><td>|t-статистика|</td><td>{result.TTest.TStatistic:F4}</td></tr>");
            htmlBuilder.AppendLine($"<tr><td>t-критическое</td><td>{result.TTest.TCritical:F4}</td></tr>");
            htmlBuilder.AppendLine($"<tr><td><b>Вывод</b></td><td><b>Средние {(result.TTest.MeansAreEqual ? "РАВНЫ" : "НЕ РАВНЫ")}</b></td></tr>");
            htmlBuilder.AppendLine("</table></div>");

            htmlBuilder.AppendLine("<div class=\"result-block\"><h4>Проверка автокорреляции</h4><table>");
            htmlBuilder.AppendLine($"<tr><td>Коэффициент автокорреляции r₁</td><td>{result.Autocorrelation.AutocorrelationCoefficient:F4}</td></tr>");
            htmlBuilder.AppendLine($"<tr><td>Критическое значение</td><td>±{result.Autocorrelation.CriticalValue}</td></tr>");
            htmlBuilder.AppendLine($"<tr><td><b>Вывод</b></td><td><b>Автокорреляция {(result.Autocorrelation.IsSignificant ? "ЗНАЧИМА" : "незначима")}</b></td></tr>");
            htmlBuilder.AppendLine("</table></div>");

            htmlBuilder.AppendLine("</div>");

            htmlBuilder.AppendLine("<h2>Сравнение распределений</h2>");
            htmlBuilder.AppendLine("<div class=\"grid-container\">");
            htmlBuilder.AppendLine($"<div class=\"chart-container\"><h3>Независимые прогоны</h3><canvas id=\"independentHistogram\"></canvas></div>");
            htmlBuilder.AppendLine($"<div class=\"chart-container\"><h3>Непрерывный прогон</h3><canvas id=\"continuousHistogram\"></canvas></div>");
            htmlBuilder.AppendLine("</div>");

            /*htmlBuilder.AppendLine("<h2>Сравнение по репликациям</h2>");
            htmlBuilder.AppendLine("<div class=\"grid-container\">");
            htmlBuilder.AppendLine($"<div class=\"chart-container\"><h3>Значения откликов</h3><canvas id=\"comparisonChart\"></canvas></div>");
            htmlBuilder.AppendLine("</div>");*/

            htmlBuilder.AppendLine("</div>");

            string independentDataJson = JsonSerializer.Serialize(result.IndependentRunData);
            string continuousDataJson = JsonSerializer.Serialize(result.ContinuousRunData);

            htmlBuilder.AppendLine("<script>");
            htmlBuilder.AppendLine("    const createChart = (id, config) => new Chart(document.getElementById(id), config);");
            htmlBuilder.AppendLine($"    const independentData = {independentDataJson};");
            htmlBuilder.AppendLine($"    const continuousData = {continuousDataJson};");
            
            htmlBuilder.AppendLine("    const createHistogram = (canvasId, rawData, label, color) => {");
            htmlBuilder.AppendLine("        if (!rawData || rawData.length === 0) return;");
            htmlBuilder.AppendLine("        const values = rawData.sort((a, b) => a - b);");
            htmlBuilder.AppendLine("        const min = values[0], max = values[values.length - 1];");
            htmlBuilder.AppendLine("        const binCount = Math.ceil(1 + 3.322 * Math.log10(values.length));");
            htmlBuilder.AppendLine("        const binWidth = (max - min) / binCount;");
            htmlBuilder.AppendLine("        const bins = Array(binCount).fill(0);");
            htmlBuilder.AppendLine("        const labels = [];");
            htmlBuilder.AppendLine("        for (let i = 0; i < binCount; i++) {");
            htmlBuilder.AppendLine("            labels.push(`[${(min + i * binWidth).toFixed(1)}, ${(min + (i + 1) * binWidth).toFixed(1)})`);");
            htmlBuilder.AppendLine("        }");
            htmlBuilder.AppendLine("        values.forEach(v => {");
            htmlBuilder.AppendLine("            let binIndex = Math.floor((v - min) / binWidth);");
            htmlBuilder.AppendLine("            if (binIndex >= binCount) binIndex = binCount - 1;");
            htmlBuilder.AppendLine("            bins[binIndex]++;");
            htmlBuilder.AppendLine("        });");
            htmlBuilder.AppendLine("        createChart(canvasId, {");
            htmlBuilder.AppendLine("            type: 'bar',");
            htmlBuilder.AppendLine("            data: {");
            htmlBuilder.AppendLine("                labels,");
            htmlBuilder.AppendLine("                datasets: [{");
            htmlBuilder.AppendLine("                    label,");
            htmlBuilder.AppendLine("                    data: bins,");
            htmlBuilder.AppendLine("                    backgroundColor: color + 'CC'");
            htmlBuilder.AppendLine("                }]");
            htmlBuilder.AppendLine("            },");
            htmlBuilder.AppendLine("            options: {");
            htmlBuilder.AppendLine("                responsive: true,");
            htmlBuilder.AppendLine("                scales: {");
            htmlBuilder.AppendLine("                    x: { title: { display: true, text: 'Значение отклика (мин)' } },");
            htmlBuilder.AppendLine("                    y: { beginAtZero: true, title: { display: true, text: 'Частота' } }");
            htmlBuilder.AppendLine("                }");
            htmlBuilder.AppendLine("            }");
            htmlBuilder.AppendLine("        });");
            htmlBuilder.AppendLine("    };");

            htmlBuilder.AppendLine("    createHistogram('independentHistogram', independentData, 'Независимые прогоны', '#3498db');");
            htmlBuilder.AppendLine("    createHistogram('continuousHistogram', continuousData, 'Непрерывный прогон', '#e74c3c');");

            htmlBuilder.AppendLine("    const labels = Array.from({length: Math.max(independentData.length, continuousData.length)}, (_, i) => `Репликация ${i + 1}`);");
            htmlBuilder.AppendLine("    createChart('comparisonChart', {");
            htmlBuilder.AppendLine("        type: 'line',");
            htmlBuilder.AppendLine("        data: {");
            htmlBuilder.AppendLine("            labels,");
            htmlBuilder.AppendLine("            datasets: [{");
            htmlBuilder.AppendLine("                label: 'Независимые прогоны',");
            htmlBuilder.AppendLine("                data: independentData,");
            htmlBuilder.AppendLine("                borderColor: '#3498db',");
            htmlBuilder.AppendLine("                backgroundColor: '#3498db33',");
            htmlBuilder.AppendLine("                fill: false");
            htmlBuilder.AppendLine("            }, {");
            htmlBuilder.AppendLine("                label: 'Непрерывный прогон',");
            htmlBuilder.AppendLine("                data: continuousData,");
            htmlBuilder.AppendLine("                borderColor: '#e74c3c',");
            htmlBuilder.AppendLine("                backgroundColor: '#e74c3c33',");
            htmlBuilder.AppendLine("                fill: false");
            htmlBuilder.AppendLine("            }]");
            htmlBuilder.AppendLine("        },");
            htmlBuilder.AppendLine("        options: {");
            htmlBuilder.AppendLine("            responsive: true,");
            htmlBuilder.AppendLine("            scales: {");
            htmlBuilder.AppendLine("                y: {");
            htmlBuilder.AppendLine("                    beginAtZero: false,");
            htmlBuilder.AppendLine("                    title: { display: true, text: 'Среднее время в системе (мин)' }");
            htmlBuilder.AppendLine("                }");
            htmlBuilder.AppendLine("            }");
            htmlBuilder.AppendLine("        }");
            htmlBuilder.AppendLine("    });");

            htmlBuilder.AppendLine("</script>");
            htmlBuilder.AppendLine("</body></html>");

            return htmlBuilder.ToString();
        }
    }
}