using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WholesaleStoreSimulation
{
public class Program
{
    static void Main(string[] args)
    {
        var cfg = new SimulatorConfig();

        // Single run with trace for detailed analysis and time-series plot
        Console.WriteLine("--- Запуск симуляции с трассировкой ---");
        var sim = new WholesaleStoreSimulator(cfg, trace: false);
        sim.Run();
        var result = sim.GetResult();

        Console.WriteLine("\n--- Результаты одиночной симуляции ---");
        Console.WriteLine($"Прибыло клиентов: {result.CustomersArrived}");
        Console.WriteLine($"1. Обслужено клиентов: {result.CustomersServed}");
        Console.WriteLine($"2. Средняя длина очереди: {result.AverageQueueLength:0.##} чел.");
        Console.WriteLine($"3. Среднее время в системе: {result.AverageSystemTime:0.##} мин.");
        Console.WriteLine($"4. Среднее время ожидания: {result.AverageWaitingTime:0.##} мин.");
        Console.WriteLine($"5. Средняя загрузка клерков: {result.ClerkUtilizations.Average():P1}");
        
        // Запуск серии репликаций для получения статистически значимых результатов
        Console.WriteLine("\n--- Верификация модели: 10 репликаций ---");
        var agg = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications: 10, trace: false);
        
        Console.WriteLine("\n--- Агрегированные результаты по 10 репликациям ---");
        Console.WriteLine($"1. Среднее кол-во обслуженных: {agg.MeanCustomersServed:0.##} (Ст. откл: {agg.StdDevCustomersServed:0.##})");
        Console.WriteLine($"2. Средняя длина очереди: {agg.MeanAvgQueueLength:0.##} (Ст. откл: {agg.StdDevAvgQueueLength:0.##})");
        Console.WriteLine($"3. Среднее время в системе: {agg.MeanAvgSystem:0.##} (Ст. откл: {agg.StdDevAvgSystem:0.##})");
        Console.WriteLine($"4. Среднее время ожидания: {agg.MeanAvgWait:0.##} (Ст. откл: {agg.StdDevAvgWait:0.##})");
        Console.WriteLine($"5. Средняя загрузка клерков: {agg.MeanAvgUtil:P1} (Ст. откл: {agg.StdDevAvgUtil:P1})");

        // 1.1
        var chiSquaredResults = RunChiSquaredTests();
        
        // 1.2 - Точечные и интервальные оценки
        var confidenceResults = ConfidenceIntervalAnalyzer.PerformConfidenceIntervalAnalysis(cfg);

        // 1.3
        var convergenceResult = ConvergenceAnalyzer.AnalyzeConvergence(
            cfg, 
            metricName: "AverageSystemTime",
            targetPrecision: 5.0,
            maxReplications: 100,
            transientPeriod: 120
        );
        
        // 1.4
        var transientResult = RunTransientAnalysis(cfg);

        if (chiSquaredResults?.Any() == true)
        {
            ChartGenerator.GenerateHtmlReport(result, agg, chiSquaredResults);
        }

        if (confidenceResults is not null)
        {
            ConfidenceIntervalChartGenerator.GenerateHtmlReport(confidenceResults);
        }
        
        if (convergenceResult != null)
        {
            ConvergenceChartGenerator.GenerateHtmlReport(convergenceResult);
        }
        
        if (transientResult is not null)
        {
            ChartGenerator.GenerateTransientAnalysisReport(transientResult, result);
        }

        // 1.5
        var continuousRunResult = ContinuousRunAnalysis.RunAnalysis(cfg);

        // --- ГЕНЕРАЦИЯ ОТЧЕТА ПО НЕПРЕРЫВНОМУ ПРОГОНУ ---
        if (continuousRunResult is not null)
        {
            ContinuousRunChartGenerator.GenerateHtmlReport(continuousRunResult);
        }
        
        // 1.6
        var sensitivityResults = SensitivityAnalysis.PerformComprehensiveAnalysis(cfg);
        SensitivityChartGenerator.GenerateHtmlReport(sensitivityResults);
        
        Console.WriteLine("\nГотово.");
    }
    
    public static List<ChiSquaredMetricTestResult> RunChiSquaredTests()
    {
        Console.WriteLine("\n" + new string('-', 60));
        Console.WriteLine("ПРОВЕРКА ГИПОТЕЗЫ О НОРМАЛЬНОСТИ ОТКЛИКОВ (ХИ-КВАДРАТ)");
        Console.WriteLine(new string('-', 60));

        int replications = 1000;
        Console.WriteLine($"\nШаг 1: Выполнение {replications} прогонов для сбора данных по всем откликам...");
        var cfg = new SimulatorConfig();
        var aggResults = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications);

        var metricSelectors = new List<(string Name, Func<SimulationResult, double> Selector)>
        {
            ("Среднее время в системе", r => r.AverageSystemTime),
            ("Среднее время ожидания", r => r.AverageWaitingTime),
            ("Средняя длина очереди", r => r.AverageQueueLength),
            ("Средняя загрузка клерков", r => r.ClerkUtilizations.Average()),
            ("Количество обслуженных покупателей", r => r.CustomersServed)
        };

        var metricResults = new List<ChiSquaredMetricTestResult>();

        foreach (var metric in metricSelectors)
        {
            var data = aggResults.ReplicationResults.Select(metric.Selector).ToList();
            Console.WriteLine($"Шаг 2: Данные собраны. Тестируется отклик: '{metric.Name}'. Размер выборки: {data.Count}.");

            if (data.Count < 20)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  ⚠️ Недостаточно данных для надежного теста. Пропускаем этот отклик.");
                Console.ResetColor();
                continue;
            }

            try
            {
                var chiSquaredTest = new ChiSquaredTest(data);
                var testResult = chiSquaredTest.PerformTest();
                var metricResult = new ChiSquaredMetricTestResult(metric.Name, testResult);
                metricResults.Add(metricResult);

                Console.WriteLine("\nШаг 3: Результаты теста хи-квадрат.");
                Console.WriteLine($"Выборочное среднее: {testResult.Mean:F3}, Стандартное отклонение: {testResult.StdDev:F3}");

                var sb = new StringBuilder();
                sb.AppendLine("+----------------+--------------------+-------------------+----------------+");
                sb.AppendLine("|    Интервал    | Наблюдаемая частота| Ожидаемая частота |    (O-E)²/E    |");
                sb.AppendLine("+----------------+--------------------+-------------------+----------------+");

                foreach (var interval in testResult.Intervals)
                {
                    string intervalStr = $"[{interval.LowerBound:F2}, {interval.UpperBound:F2})";
                    double oMinusESquaredOverE = Math.Pow(interval.ObservedFrequency - interval.ExpectedFrequency, 2) / interval.ExpectedFrequency;
                    sb.AppendLine($"| {intervalStr,-14} | {interval.ObservedFrequency,-18} | {interval.ExpectedFrequency,-17:F3} | {oMinusESquaredOverE,-14:F4} |");
                }
                sb.AppendLine("+----------------+--------------------+-------------------+----------------+");
                Console.WriteLine(sb.ToString());

                Console.WriteLine("Итоговые результаты:");
                Console.WriteLine($"  - Отклик: {metric.Name}");
                Console.WriteLine($"  - Рассчитанное значение χ²: {testResult.ChiSquaredStatistic:F4}");
                Console.WriteLine($"  - Степени свободы: {testResult.DegreesOfFreedom}");
                Console.WriteLine($"  - Критическое значение (α=0.05): {testResult.CriticalValue:F4}");

                Console.WriteLine("Вывод:");
                if (testResult.IsNormal)
                {
                    Console.WriteLine("  => Гипотеза о нормальности распределения отклика ПРИНИМАЕТСЯ.");
                }
                else
                {
                    Console.WriteLine("  => Гипотеза о нормальности распределения отклика ОТВЕРГАЕТСЯ.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nОШИБКА при выполнении теста для отклика '{metric.Name}': {ex.Message}");
                Console.ResetColor();
            }
        }

        return metricResults;
    }
    
    public static TransientAnalysisResult RunTransientAnalysis(SimulatorConfig baseConfig)
    {
        Console.WriteLine("\n" + new string('-', 60));
        Console.WriteLine("АНАЛИЗ ПЕРЕХОДНОГО ПЕРИОДА");
        Console.WriteLine(new string('-', 60));

        double t0 = 480.0;
        double t1 = 360.0;
        // Уменьшил до 30, т.к. t-тесту не нужны тысячи прогонов, это экономит время. 30 - стандартное "минимально-большое" число в статистике.
        int replications = 30; 
        
        Console.WriteLine($"Проверяется гипотеза о возможности сокращения времени прогона с T₀={t0} до T₁={t1} на выборке из {replications} репликаций.");
        
        try
        {
            var runner = new TransientAnalysisRunner(baseConfig, t0, t1, replications);
            var result = runner.Run();
            Console.WriteLine($"\nИтоговый вывод: {result.Conclusion}");
            return result;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nОШИБКА при выполнении анализа переходного периода: {ex.Message}");
            Console.ResetColor();
            return null;
        }
    }
}
}