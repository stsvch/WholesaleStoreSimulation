using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WholesaleStoreSimulation
{
    /// <summary>
    /// Реализует тест согласия хи-квадрат Пирсона для проверки гипотезы о нормальном распределении.
    /// </summary>
    public class ChiSquaredTest
    {
        private readonly List<double> _data;
        private readonly double _alpha;

        public ChiSquaredTest(IEnumerable<double> data, double alpha = 0.05)
        {
            _data = data.ToList();
            _alpha = alpha;
        }

        /// <summary>
        /// Выполняет тест хи-квадрат.
        /// </summary>
        public ChiSquaredResult PerformTest()
        {
            int n = _data.Count;
            if (n < 20)
            {
                throw new InvalidOperationException("Для теста хи-квадрат требуется не менее 20 точек данных для надежных результатов.");
            }

            // 1. Вычисление выборочного среднего и стандартного отклонения
            double mean = _data.Average();
            double stdDev = Math.Sqrt(_data.Sum(d => Math.Pow(d - mean, 2)) / (n - 1));

            // 2. Определение количества интервалов по формуле Стерджеса
            int k = (int)Math.Ceiling(1 + 3.322 * Math.Log10(n));

            double minVal = _data.Min();
            double maxVal = _data.Max();
            double intervalWidth = (maxVal - minVal) / k;

            var intervals = new List<IntervalInfo>();
            for (int i = 0; i < k; i++)
            {
                var interval = new IntervalInfo
                {
                    LowerBound = minVal + i * intervalWidth,
                    UpperBound = minVal + (i + 1) * intervalWidth
                };
                intervals.Add(interval);
            }

            // 3. Расчет наблюдаемых частот
            foreach (var point in _data)
            {
                for (int i = 0; i < intervals.Count; i++)
                {
                    if (point >= intervals[i].LowerBound && point < intervals[i].UpperBound || 
                       (i == intervals.Count - 1 && point.Equals(intervals[i].UpperBound)))
                    {
                        intervals[i].ObservedFrequency++;
                        break;
                    }
                }
            }

            // 4. Расчет ожидаемых частот
            for (int i = 0; i < k; i++)
            {
                double pUpper = NormalCdf(intervals[i].UpperBound, mean, stdDev);
                double pLower = (i == 0) ? 0 : NormalCdf(intervals[i].LowerBound, mean, stdDev);
                intervals[i].ExpectedFrequency = n * (pUpper - pLower);
            }
            
            // 5. Объединение интервалов, если ожидаемая частота < 5
            var mergedIntervals = MergeIntervals(intervals);

            // 6. Вычисление статистики хи-квадрат
            double chiSquared = mergedIntervals.Sum(i => Math.Pow(i.ObservedFrequency - i.ExpectedFrequency, 2) / i.ExpectedFrequency);

            // 7. Определение степеней свободы (k' - 1 - 2, т.к. оценивались 2 параметра: среднее и ст.откл.)
            int df = mergedIntervals.Count - 1 - 2;
            if (df <= 0)
            {
                throw new InvalidOperationException("Недостаточное количество интервалов после слияния для вычисления степеней свободы.");
            }

            // 8. Поиск критического значения и принятие решения
            double criticalValue = ChiSquaredProvider.GetCriticalValue(df, _alpha);
            bool isNormal = chiSquared < criticalValue;

            return new ChiSquaredResult
            {
                ChiSquaredStatistic = chiSquared,
                CriticalValue = criticalValue,
                DegreesOfFreedom = df,
                IsNormal = isNormal,
                Intervals = mergedIntervals,
                Mean = mean,
                StdDev = stdDev
            };
        }

        private List<IntervalInfo> MergeIntervals(List<IntervalInfo> intervals)
        {
            var merged = new List<IntervalInfo>(intervals);
            const double minExpectedFrequency = 5.0; // Минимальная ожидаемая частота

            // TODO change to Observed if we need to exclude intervals with < 5 values
            // Объединяем "хвосты" с конца
            while (merged.Count > 1 && merged.Last().ObservedFrequency < minExpectedFrequency)
            {
                var last = merged.Last();
                merged.RemoveAt(merged.Count - 1);
                var previous = merged.Last();
                previous.UpperBound = last.UpperBound;
                previous.ObservedFrequency += last.ObservedFrequency;
                previous.ExpectedFrequency += last.ExpectedFrequency;
            }

            // Объединяем "хвосты" с начала
            while (merged.Count > 1 && merged.First().ObservedFrequency < minExpectedFrequency)
            {
                var first = merged.First();
                merged.RemoveAt(0);
                var next = merged.First();
                next.LowerBound = first.LowerBound;
                next.ObservedFrequency += first.ObservedFrequency;
                next.ExpectedFrequency += first.ExpectedFrequency;
            }

            // Проходим по оставшимся центральным интервалам (если нужно)
            for (int i = 0; i < merged.Count - 1; i++)
            {
                if (merged[i].ObservedFrequency < minExpectedFrequency)
                {
                    merged[i+1].LowerBound = merged[i].LowerBound;
                    merged[i+1].ObservedFrequency += merged[i].ObservedFrequency;
                    merged[i+1].ExpectedFrequency += merged[i].ExpectedFrequency;
                    merged.RemoveAt(i);
                    i--; // Возвращаемся на шаг назад, чтобы перепроверить объединенный интервал
                }
            }

            return merged;
        }

        // <--- ЗАМЕНЕННЫЙ МЕТОД
        /// <summary>
        /// Аппроксимация интегральной функции нормального распределения (CDF).
        /// Не использует Math.Erf и работает в старых версиях .NET Framework.
        /// </summary>
        private static double NormalCdf(double x, double mean, double stdDev)
        {
            // Константы для аппроксимации (Abramowitz and Stegun formula 26.2.17)
            const double p = 0.2316419;
            const double b1 = 0.319381530;
            const double b2 = -0.356563782;
            const double b3 = 1.781477937;
            const double b4 = -1.821255978;
            const double b5 = 1.330274429;
            
            double z = (x - mean) / stdDev;
            double t = 1.0 / (1.0 + p * Math.Abs(z));
            double phi = (1.0 / Math.Sqrt(2 * Math.PI)) * Math.Exp(-0.5 * z * z);
            double cdf = 1.0 - phi * (b1 * t + b2 * t * t + b3 * Math.Pow(t, 3) + b4 * Math.Pow(t, 4) + b5 * Math.Pow(t, 5));

            return z > 0 ? cdf : 1.0 - cdf;
        }
    }

    /// <summary>
    /// Предоставляет критические значения для распределения хи-квадрат.
    /// </summary>
    public static class ChiSquaredProvider
    {
        // Таблица критических значений для alpha = 0.05
        private static readonly Dictionary<int, double> CriticalValues = new()
        {
            { 1, 3.841 }, { 2, 5.991 }, { 3, 7.815 }, { 4, 9.488 },
            { 5, 11.070 }, { 6, 12.592 }, { 7, 14.067 }, { 8, 15.507 },
            { 9, 16.919 }, { 10, 18.307 }, { 11, 19.675 }, { 12, 21.026 },
            { 13, 22.362 }, { 14, 23.685 }, { 15, 24.996 }, { 16, 26.296 },
            { 17, 27.587 }, { 18, 28.869 }, { 19, 30.144 }, { 20, 31.410 },
            { 21, 32.671 }, { 22, 33.924 }, { 23, 35.172 }, { 24, 36.415 },
            { 25, 37.652 }, { 26, 38.885 }, { 27, 40.113 }, { 28, 41.337 },
            { 29, 42.557 }, { 30, 43.773 }
        };

        public static double GetCriticalValue(int degreesOfFreedom, double alpha)
        {
            if (alpha != 0.05) throw new NotSupportedException("Поддерживается только уровень значимости alpha=0.05.");
            if (CriticalValues.TryGetValue(degreesOfFreedom, out double value))
            {
                return value;
            }
            throw new ArgumentException($"Критическое значение для {degreesOfFreedom} степеней свободы не найдено в таблице. Расширьте таблицу, если необходимо.");
        }
    }
}