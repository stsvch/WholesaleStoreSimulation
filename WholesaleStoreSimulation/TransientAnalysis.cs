namespace WholesaleStoreSimulation
{
    // --- DTOs для хранения результатов анализа ---

    public class FTestResult
    {
        public double FStatistic { get; set; }
        public double FCritical { get; set; }
        public int Df1 { get; set; } // Степени свободы 1 (числитель)
        public int Df2 { get; set; } // Степени свободы 2 (знаменатель)
        public bool VariancesAreEqual { get; set; } // Гипотеза о равенстве дисперсий принята
    }

    public class TTestResult
    {
        public string TestType { get; set; } // "Стьюдента" или "Взвешенный (Уэлча)"
        public double TStatistic { get; set; }
        public double TCritical { get; set; }
        public int Df { get; set; } // Актуальные степени свободы
        public bool MeansAreEqual { get; set; } // Гипотеза о равенстве средних (однородности) принята
    }

    public class TransientAnalysisResult
    {
        // Входные параметры
        public double T0 { get; set; }
        public double T1 { get; set; }
        public int Replications { get; set; }

        // Выборки данных
        public List<double> DataSample0 { get; set; } = new();
        public List<double> DataSample1 { get; set; } = new();

        // Статистики выборок
        public double Mean0 { get; set; }
        public double Variance0 { get; set; }
        public double Mean1 { get; set; }
        public double Variance1 { get; set; }

        // Результаты тестов
        public FTestResult FTest { get; set; }
        public TTestResult TTest { get; set; }
        
        public string Conclusion { get; set; }
    }


    // --- Основной класс для выполнения анализа ---

    public class TransientAnalysisRunner
    {
        private readonly SimulatorConfig _baseConfig;
        private readonly double _t0;
        private readonly double _t1;
        private readonly int _replications;
        private const double Alpha = 0.05;

        public TransientAnalysisRunner(SimulatorConfig baseConfig, double t0, double t1, int replications)
        {
            _baseConfig = baseConfig;
            _t0 = t0;
            _t1 = t1;
            _replications = replications;
        }

        public TransientAnalysisResult Run()
        {
            // 1. Формирование выборок
            Console.WriteLine($"\nШаг 1.1: Выполнение {_replications} прогонов для T₀ = {_t0} (эталон)...");
            var data0 = RunReplicationsForDuration(_t0);

            Console.WriteLine($"\nШаг 1.2: Выполнение {_replications} прогонов для T₁ = {_t1} (проверяемый)...");
            var data1 = RunReplicationsForDuration(_t1);

            // 2. Расчет статистик
            double mean0 = data0.Average();
            double var0 = CalculateVariance(data0);
            double mean1 = data1.Average();
            double var1 = CalculateVariance(data1);

            // 3. Проверка гипотезы о равенстве дисперсий (F-тест)
            var fTestResult = PerformFTest(var0, data0.Count, var1, data1.Count);

            // 4. Проверка гипотезы о равенстве средних (t-тест)
            var tTestResult = fTestResult.VariancesAreEqual
                ? PerformStudentTTest(mean0, var0, data0.Count, mean1, var1, data1.Count)
                : PerformWelchTTest(mean0, var0, data0.Count, mean1, var1, data1.Count);
            
            // 5. Формирование вывода
            string conclusion;
            if (tTestResult.MeansAreEqual)
            {
                conclusion = $"Так как |t-статистика| ({tTestResult.TStatistic:F3}) < t-критического ({tTestResult.TCritical:F3}), гипотеза об однородности выборок ПРИНИМАЕТСЯ. " +
                             $"Уменьшение времени прогона с {_t0} до {_t1} является допустимым.";
            }
            else
            {
                conclusion = $"Так как |t-статистика| ({tTestResult.TStatistic:F3}) >= t-критического ({tTestResult.TCritical:F3}), гипотеза об однородности выборок ОТВЕРГАЕТСЯ. " +
                             $"Уменьшение времени прогона с {_t0} до {_t1} статистически не оправдано.";
            }

            return new TransientAnalysisResult
            {
                T0 = _t0,
                T1 = _t1,
                Replications = _replications,
                DataSample0 = data0,
                DataSample1 = data1,
                Mean0 = mean0,
                Variance0 = var0,
                Mean1 = mean1,
                Variance1 = var1,
                FTest = fTestResult,
                TTest = tTestResult,
                Conclusion = conclusion
            };
        }

        private List<double> RunReplicationsForDuration(double duration)
        {
            var config = new SimulatorConfig
            {
                SimulationDuration = duration,
                RandomSeed = _baseConfig.RandomSeed,
                ClerkCount = _baseConfig.ClerkCount,
                ClerkBatchSize = _baseConfig.ClerkBatchSize,
                // ... скопировать другие параметры при необходимости ...
            };
            var aggregatedResult = WholesaleStoreSimulator.VerifyWithReplications(config, _replications);
            // Используем 'Среднее время в системе' как отклик
            return aggregatedResult.ReplicationResults.Select(r => r.AverageSystemTime).ToList();
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
            // Объединенная дисперсия
            double sp2 = ((n0 - 1) * var0 + (n1 - 1) * var1) / df;
            // Стандартная ошибка
            double se = Math.Sqrt(sp2 * (1.0 / n0 + 1.0 / n1));
            // t-статистика
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
            // t-статистика (взвешенная)
            double tStat = Math.Abs(mean0 - mean1) / Math.Sqrt(var0 / n0 + var1 / n1);
            
            // Расчет взвешенного критического значения по вашей формуле
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
                Df = n0 + n1, // Условное значение, т.к. df здесь сложное
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

    // --- Провайдеры критических значений ---

    public static class TDistributionProvider
    {
        // Двусторонний t-тест, alpha=0.05 (т.е. ищем значение для 0.975)
        private static readonly Dictionary<int, double> CriticalValues = new()
        {
            { 10, 2.228 }, { 15, 2.131 }, { 20, 2.086 }, { 25, 2.060 }, { 29, 2.045 },
            { 30, 2.042 }, { 40, 2.021 }, { 50, 2.009 }, { 60, 2.000 }, { 80, 1.990 },
            { 99, 1.984 }, { 100, 1.984 }, { 120, 1.980 }, { 198, 1.972 }, { 1998, 1.961 }
        };

        public static double GetCriticalValue(int df, double alpha)
        {
            if (alpha != 0.05) throw new NotSupportedException("Поддерживается только уровень значимости alpha=0.05.");
            
            // Находим ближайшее значение в таблице
            var closestDf = CriticalValues.Keys.OrderBy(k => Math.Abs(k - df)).First();
            return CriticalValues[closestDf];
        }
    }

    public static class FDistributionProvider
    {
        // F-распределение, alpha=0.05. Ключ - (df1, df2)
        private static readonly Dictionary<(int, int), double> CriticalValues = new()
        {
            { (29, 29), 1.85 }, { (30, 30), 1.84 }, { (40, 40), 1.69 },
            { (50, 50), 1.60 }, { (60, 60), 1.53 }, { (100, 100), 1.39 },
            { (30, 40), 1.74 }, { (40, 30), 1.87 }, { (50, 40), 1.66 }, { (40, 50), 1.56 }
        };
        
        public static double GetCriticalValue(int df1, int df2, double alpha)
        {
            if (alpha != 0.05) throw new NotSupportedException("Поддерживается только уровень значимости alpha=0.05.");

            // Ищем точное совпадение
            if (CriticalValues.TryGetValue((df1, df2), out var val)) return val;
            
            // Ищем ближайшее значение
            var closestKey = CriticalValues.Keys.OrderBy(k => Math.Abs(k.Item1 - df1) + Math.Abs(k.Item2 - df2)).First();
            return CriticalValues[closestKey];
        }
    }
}