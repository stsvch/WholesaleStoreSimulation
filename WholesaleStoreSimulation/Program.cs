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
        var chiSquaredResult = RunChiSquaredTest();
        
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

        if (chiSquaredResult is not null)
        {
            ChartGenerator.GenerateHtmlReport(result, agg, chiSquaredResult);
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
    
    public static ChiSquaredResult RunChiSquaredTest()
    {
        Console.WriteLine("\n" + new string('-', 60));
        Console.WriteLine("ПРОВЕРКА ГИПОТЕЗЫ О НОРМАЛЬНОСТИ ОТКЛИКОВ (ХИ-КВАДРАТ)");
        Console.WriteLine(new string('-', 60));

        int replications = 1000; 
        Console.WriteLine($"\nШаг 1: Выполнение {replications} прогонов для сбора данных...");
        var cfg = new SimulatorConfig();
        var aggResults = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications);
        var data = aggResults.ReplicationResults.Select(r => r.AverageSystemTime).ToList();
        Console.WriteLine($"Шаг 2: Данные собраны. Тестируется отклик: 'Среднее время в системе'. Размер выборки: {data.Count}.");

        try
        {
            var chiSquaredTest = new ChiSquaredTest(data);
            var testResult = chiSquaredTest.PerformTest();

            Console.WriteLine("\nШаг 3: Результаты теста хи-квадрат.");
            Console.WriteLine($"\nВыборочное среднее: {testResult.Mean:F3}, Стандартное отклонение: {testResult.StdDev:F3}\n");

            // --- ВОССТАНОВЛЕННЫЙ БЛОК ВЫВОДА ТАБЛИЦЫ ---
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
            // --- КОНЕЦ ВОССТАНОВЛЕННОГО БЛОКА ---

            Console.WriteLine("Итоговые результаты:");
            Console.WriteLine($"  - Рассчитанное значение хи-квадрат (χ²): {testResult.ChiSquaredStatistic:F4}");
            Console.WriteLine($"  - Число степеней свободы (df): {testResult.DegreesOfFreedom}");
            Console.WriteLine($"  - Критическое значение χ² для alpha=0.05: {testResult.CriticalValue:F4}");

            Console.WriteLine("\nВывод:");
            if (testResult.IsNormal)
            {
                Console.WriteLine($"  Рассчитанное значение ({testResult.ChiSquaredStatistic:F4}) МЕНЬШЕ критического ({testResult.CriticalValue:F4}).");
                Console.WriteLine("  => Гипотеза о нормальности распределения отклика ПРИНИМАЕТСЯ.");
            }
            else
            {
                Console.WriteLine($"  Рассчитанное значение ({testResult.ChiSquaredStatistic:F4}) БОЛЬШЕ критического ({testResult.CriticalValue:F4}).");
                Console.WriteLine("  => Гипотеза о нормальности распределения отклика ОТВЕРГАЕТСЯ.");
            }

            return testResult;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nОШИБКА при выполнении теста: {ex.Message}");
            Console.ResetColor();
            return null;
        }
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