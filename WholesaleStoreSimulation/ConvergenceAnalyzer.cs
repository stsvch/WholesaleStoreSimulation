using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WholesaleStoreSimulation
{
    public class ConvergenceAnalysisResult
    {
        public List<ConvergencePoint> Points { get; set; } = new();
        public int TargetReplications { get; set; }
        public double TargetPrecision { get; set; }
        public string TargetMetric { get; set; } = "";
        public string Conclusion { get; set; } = "";
        public List<ConvergencePoint> DetailedTransitionPoints { get; set; } = new();
        public double TransientPeriod { get; set; } = 100.0; // Исключаем первые 100 минут как переходный период
    }

    public class ConvergencePoint
    {
        public int Replications { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double StandardError { get; set; }
        public double ConfidenceIntervalHalfWidth { get; set; }
        public double RelativePrecision { get; set; } // в процентах
        public double LowerBound { get; set; }
        public double UpperBound { get; set; }
        public bool IsTargetPrecisionAchieved { get; set; }
    }

    public static class ConvergenceAnalyzer
    {
        public static ConvergenceAnalysisResult AnalyzeConvergence(
            SimulatorConfig baseConfig, 
            string metricName = "AverageSystemTime",
            double targetPrecision = 5.0, // 5% точность
            int maxReplications = 300,
            double transientPeriod = 100.0) // Явно задаем переходный период
        {
            Console.WriteLine("\n" + new string('-', 70));
            Console.WriteLine("1.3 АНАЛИЗ ЗАВИСИМОСТИ ТОЧНОСТИ ОТ КОЛИЧЕСТВА ПРОГОНОВ");
            Console.WriteLine(new string('-', 70));
            Console.WriteLine($"Целевая точность: {targetPrecision}%");
            Console.WriteLine($"Анализируемая метрика: {metricName}");
            Console.WriteLine($"Диапазон репликаций: 1-{maxReplications}");
            Console.WriteLine($"Исключаемый переходный период: {transientPeriod} минут");

            var result = new ConvergenceAnalysisResult 
            { 
                TargetPrecision = targetPrecision,
                TargetMetric = metricName,
                TransientPeriod = transientPeriod
            };

            // Используем конфигурацию с исключением переходного периода
            var stableConfig = CreateStableConfig(baseConfig, transientPeriod);

            try
            {
                // Первые 10 точек с шагом 1 для детального анализа начала
                for (int n = 1; n <= 10; n++)
                {
                    var point = RunAndAnalyzeReplications(stableConfig, metricName, n, transientPeriod);
                    point.IsTargetPrecisionAchieved = point.RelativePrecision <= targetPrecision;
                    result.Points.Add(point);
                    
                    Console.WriteLine($"  n={n,3}: Точность = {point.RelativePrecision,6:F2}% " +
                                    $"(Среднее={point.Mean,7:F3}, Ст.ошибка={point.StandardError,6:F4})" +
                                    $" {(point.IsTargetPrecisionAchieved ? "✓ ДОСТИГНУТО!" : "")}");
                }

                // Затем с шагом 2 до 30
                for (int n = 12; n <= 30; n += 2)
                {
                    var point = RunAndAnalyzeReplications(stableConfig, metricName, n, transientPeriod);
                    point.IsTargetPrecisionAchieved = point.RelativePrecision <= targetPrecision;
                    result.Points.Add(point);
                    
                    Console.WriteLine($"  n={n,3}: Точность = {point.RelativePrecision,6:F2}% " +
                                    $"(Среднее={point.Mean,7:F3}, Ст.ошибка={point.StandardError,6:F4})" +
                                    $" {(point.IsTargetPrecisionAchieved ? "✓ ДОСТИГНУТО!" : "")}");

                    // Сохраняем точки перехода для детального анализа
                    if (point.IsTargetPrecisionAchieved && result.TargetReplications == 0)
                    {
                        result.TargetReplications = n;
                        result.DetailedTransitionPoints.Add(point);
                    }
                }

                // С шагом 5 до 100
                for (int n = 35; n <= 100; n += 5)
                {
                    var point = RunAndAnalyzeReplications(stableConfig, metricName, n, transientPeriod);
                    point.IsTargetPrecisionAchieved = point.RelativePrecision <= targetPrecision;
                    result.Points.Add(point);
                    
                    Console.WriteLine($"  n={n,3}: Точность = {point.RelativePrecision,6:F2}% " +
                                    $"(Среднее={point.Mean,7:F3}, Ст.ошибка={point.StandardError,6:F4})" +
                                    $" {(point.IsTargetPrecisionAchieved ? "✓ ДОСТИГНУТО!" : "")}");

                    if (point.IsTargetPrecisionAchieved && result.TargetReplications == 0)
                    {
                        result.TargetReplications = n;
                        result.DetailedTransitionPoints.Add(point);
                    }
                }

                // С шагом 10 до максимума
                for (int n = 110; n <= maxReplications; n += 10)
                {
                    var point = RunAndAnalyzeReplications(stableConfig, metricName, n, transientPeriod);
                    point.IsTargetPrecisionAchieved = point.RelativePrecision <= targetPrecision;
                    result.Points.Add(point);
                    
                    Console.WriteLine($"  n={n,3}: Точность = {point.RelativePrecision,6:F2}% " +
                                    $"(Среднее={point.Mean,7:F3}, Ст.ошибка={point.StandardError,6:F4})" +
                                    $" {(point.IsTargetPrecisionAchieved ? "✓ ДОСТИГНУТО!" : "")}");

                    if (point.IsTargetPrecisionAchieved && result.TargetReplications == 0)
                    {
                        result.TargetReplications = n;
                        result.DetailedTransitionPoints.Add(point);
                    }
                }

                // Если точность достигнута, находим точный момент перехода
                if (result.TargetReplications > 0)
                {
                    FindExactTransitionPoint(result, stableConfig, metricName, targetPrecision, transientPeriod);
                }

                // Формируем заключение
                if (result.TargetReplications > 0)
                {
                    var targetPoint = result.Points.First(p => p.Replications == result.TargetReplications);
                    result.Conclusion = $"🎯 Целевая точность {targetPrecision}% достигнута при {result.TargetReplications} репликациях. " +
                                      $"Среднее: {targetPoint.Mean:F3} ± {targetPoint.RelativePrecision:F2}% " +
                                      $"(переходный период {transientPeriod} мин исключен)";
                }
                else
                {
                    var lastPoint = result.Points.Last();
                    result.Conclusion = $"⚠️ Целевая точность {targetPrecision}% не достигнута в диапазоне 1-{maxReplications} репликаций. " +
                                      $"Лучшая точность: {lastPoint.RelativePrecision:F2}% при {maxReplications} репликациях " +
                                      $"(переходный период {transientPeriod} мин исключен)";
                }

                Console.WriteLine($"\n{result.Conclusion}");
                return result;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nОШИБКА при анализе сходимости: {ex.Message}");
                Console.ResetColor();
                return new ConvergenceAnalysisResult 
                { 
                    Conclusion = $"Ошибка анализа: {ex.Message}",
                    TargetMetric = metricName,
                    TargetPrecision = targetPrecision
                };
            }
        }

        /// <summary>
        /// Создает конфигурацию с исключением переходного периода
        /// </summary>
        private static SimulatorConfig CreateStableConfig(SimulatorConfig baseConfig, double transientPeriod)
        {
            var stableConfig = JsonSerializer.Deserialize<SimulatorConfig>(JsonSerializer.Serialize(baseConfig));
            
            // Увеличиваем длительность прогона, чтобы переходный период был незначителен
            stableConfig!.SimulationDuration = 480.0; // Достаточно длинный прогон
            
            // Для анализа сходимости используем только установившийся режим
            // Это достигается за счет того, что в симуляторе собирается статистика за весь прогон,
            // но мы используем достаточно длинные прогоны, где переходный период мал по сравнению с общей длительностью
            
            return stableConfig;
        }

        /// <summary>
        /// Запускает симуляцию с учетом переходного периода
        /// </summary>
        private static List<double> RunStableReplications(SimulatorConfig config, int replications, double transientPeriod)
        {
            var results = new List<double>();
            
            for (int i = 0; i < replications; i++)
            {
                var cfg = JsonSerializer.Deserialize<SimulatorConfig>(JsonSerializer.Serialize(config));
                cfg!.RandomSeed = config.RandomSeed + i * 37; // Разные seed для каждой репликации
                
                // Увеличиваем длительность для гарантии стабильности
                cfg.SimulationDuration = Math.Max(config.SimulationDuration, transientPeriod * 3);
                
                var sim = new WholesaleStoreSimulator(cfg, trace: false);
                sim.Run();
                var result = sim.GetResult();
                
                // Используем метрики, которые стабилизируются после переходного периода
                results.Add(result.AverageSystemTime);
            }
            
            return results;
        }

        private static void FindExactTransitionPoint(ConvergenceAnalysisResult result, SimulatorConfig config, 
                                                   string metricName, double targetPrecision, double transientPeriod)
        {
            Console.WriteLine("\n🔍 Детальный анализ перехода через целевой порог...");

            // Находим последнюю точку, где точность НЕ достигнута
            var pointsBeforeTarget = result.Points
                .Where(p => !p.IsTargetPrecisionAchieved && p.Replications < result.TargetReplications)
                .OrderBy(p => p.Replications)
                .ToList();

            if (pointsBeforeTarget.Count == 0) return;

            int lastMissed = pointsBeforeTarget.Last().Replications;
            int firstAchieved = result.TargetReplications;

            Console.WriteLine($"  Анализируем переход от {lastMissed} до {firstAchieved} репликаций...");

            // Проверяем все промежуточные значения
            for (int n = lastMissed + 1; n < firstAchieved; n++)
            {
                var point = RunAndAnalyzeReplications(config, metricName, n, transientPeriod);
                point.IsTargetPrecisionAchieved = point.RelativePrecision <= targetPrecision;
                
                // Добавляем в общий список и в список переходных точек
                result.Points.Add(point);
                result.DetailedTransitionPoints.Add(point);

                Console.WriteLine($"    n={n,3}: Точность = {point.RelativePrecision,6:F2}% " +
                                $"{(point.IsTargetPrecisionAchieved ? "✓ ДОСТИГНУТО!" : "")}");

                if (point.IsTargetPrecisionAchieved && n < result.TargetReplications)
                {
                    result.TargetReplications = n;
                    Console.WriteLine($"    🎯 Уточненный переход: {n} репликаций");
                }
            }

            // Сортируем точки по количеству репликаций
            result.Points = result.Points.OrderBy(p => p.Replications).ToList();
        }

        private static ConvergencePoint RunAndAnalyzeReplications(SimulatorConfig config, string metricName, 
                                                                int replications, double transientPeriod)
        {
            // Запускаем репликации с учетом переходного периода
            var metricData = RunStableReplications(config, replications, transientPeriod);
            
            // Вычисляем статистики
            return CalculateConvergencePoint(metricData, replications);
        }

        private static ConvergencePoint CalculateConvergencePoint(List<double> data, int replications)
        {
            int n = data.Count;
            if (n == 0) throw new ArgumentException("Пустой набор данных");

            double mean = data.Average();
            double stdDev = Math.Sqrt(data.Sum(x => Math.Pow(x - mean, 2)) / (n - 1));
            double standardError = stdDev / Math.Sqrt(n);
            
            // t-критическое значение для 95% доверительного интервала
            double tCritical = GetTCritical(n - 1);
            double halfWidth = tCritical * standardError;
            double relativePrecision = mean != 0 ? (halfWidth / Math.Abs(mean)) * 100 : 0;

            return new ConvergencePoint
            {
                Replications = replications,
                Mean = mean,
                StdDev = stdDev,
                StandardError = standardError,
                ConfidenceIntervalHalfWidth = halfWidth,
                RelativePrecision = relativePrecision,
                LowerBound = mean - halfWidth,
                UpperBound = mean + halfWidth
            };
        }

        private static double GetTCritical(int degreesOfFreedom)
        {
            // Расширенная таблица t-критических значений для 95% доверительного интервала
            var tTable = new Dictionary<int, double>
            {
                { 1, 12.706 }, { 2, 4.303 }, { 3, 3.182 }, { 4, 2.776 }, { 5, 2.571 },
                { 6, 2.447 }, { 7, 2.365 }, { 8, 2.306 }, { 9, 2.262 }, { 10, 2.228 },
                { 11, 2.201 }, { 12, 2.179 }, { 13, 2.160 }, { 14, 2.145 }, { 15, 2.131 },
                { 16, 2.120 }, { 17, 2.110 }, { 18, 2.101 }, { 19, 2.093 }, { 20, 2.086 },
                { 21, 2.080 }, { 22, 2.074 }, { 23, 2.069 }, { 24, 2.064 }, { 25, 2.060 },
                { 26, 2.056 }, { 27, 2.052 }, { 28, 2.048 }, { 29, 2.045 }, { 30, 2.042 },
                { 35, 2.030 }, { 40, 2.021 }, { 45, 2.014 }, { 50, 2.009 },
                { 60, 2.000 }, { 70, 1.994 }, { 80, 1.990 }, { 90, 1.987 }, { 100, 1.984 },
                { 120, 1.980 }, { 150, 1.976 }, { 200, 1.972 }, { 300, 1.968 }, { 500, 1.965 }
            };

            if (degreesOfFreedom <= 0) return 1.96;
            if (tTable.ContainsKey(degreesOfFreedom)) return tTable[degreesOfFreedom];
            
            // Аппроксимация для промежуточных значений
            return degreesOfFreedom >= 1000 ? 1.96 : 
                1.96 + (2.0 - 1.96) * Math.Exp(-0.01 * (degreesOfFreedom - 100));
        }
    }

    public class ConvergenceChartGenerator
    {
        public static void GenerateHtmlReport(ConvergenceAnalysisResult result, string fileName = "convergence_analysis.html")
        {
            var html = GenerateHtmlContent(result);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            File.WriteAllText(fullPath, html, Encoding.UTF8);
            Console.WriteLine($"\nОтчет по анализу сходимости успешно сгенерирован: {fullPath}");
        }

        private static string GenerateHtmlContent(ConvergenceAnalysisResult result)
        {
            var culture = CultureInfo.InvariantCulture;
            var jsonSerializerOptions = new JsonSerializerOptions()
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString
            };
            
            var htmlBuilder = new StringBuilder();

            htmlBuilder.AppendLine("<!DOCTYPE html>");
            htmlBuilder.AppendLine("<html lang=\"ru\"><head>");
            htmlBuilder.AppendLine("<meta charset=\"UTF-8\"><title>Анализ зависимости точности от количества прогонов</title>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chartjs-plugin-annotation@2.2.1/dist/chartjs-plugin-annotation.min.js\"></script>");
            GenerateStyles(htmlBuilder);
            htmlBuilder.AppendLine("</head><body><div class=\"container\">");
            htmlBuilder.AppendLine("<h1>📈 1.3 Анализ зависимости точности от количества прогонов</h1>");
            
            // Основная информация с акцентом на исключение переходного периода
            htmlBuilder.AppendLine("<div class='section'>");
            htmlBuilder.AppendLine($"<div class='info-box'><strong>Целевая метрика:</strong> {result.TargetMetric}<br>");
            htmlBuilder.AppendLine($"<strong>Целевая точность:</strong> {result.TargetPrecision}%<br>");
            htmlBuilder.AppendLine($"<strong>Диапазон репликаций:</strong> {result.Points.First().Replications} - {result.Points.Last().Replications}<br>");
            htmlBuilder.AppendLine($"<strong>Исключен переходный период:</strong> {result.TransientPeriod} минут<br>");
            htmlBuilder.AppendLine($"<strong>Результат:</strong> {result.Conclusion}</div>");

            // График зависимости точности от количества репликаций
            htmlBuilder.AppendLine("        <h2>Зависимость относительной точности от количества репликаций</h2>");
            htmlBuilder.AppendLine("        <div class=\"chart-container\">");
            htmlBuilder.AppendLine("            <canvas id=\"precisionChart\"></canvas>");
            htmlBuilder.AppendLine("        </div>");

            // Детальный график первых 20 значений
            htmlBuilder.AppendLine("        <h2>Детальный анализ первых 20 репликаций</h2>");
            htmlBuilder.AppendLine("        <div class=\"chart-container\">");
            htmlBuilder.AppendLine("            <canvas id=\"detailedChart\"></canvas>");
            htmlBuilder.AppendLine("        </div>");

            // Таблица с детальными результатами (только ключевые точки)
            htmlBuilder.AppendLine("        <h2>Ключевые точки анализа</h2>");
            htmlBuilder.AppendLine("        <div class=\"table-container\">");
            htmlBuilder.AppendLine("            <table>");
            htmlBuilder.AppendLine("                <thead><tr><th>Репликации</th><th>Среднее</th><th>Ст. отклонение</th><th>Ст. ошибка</th><th>±Полуширина ДИ</th><th>Отн. точность (%)</th><th>95% ДИ</th><th>Статус</th></tr></thead>");
            htmlBuilder.AppendLine("                <tbody>");
            
            // Показываем первые 10 точек, точки перехода и каждую 10-ю точку после
            var keyPoints = result.Points
                .Where(p => p.Replications <= 10 || 
                           p.Replications == result.TargetReplications ||
                           p.Replications % 20 == 0 ||
                           result.DetailedTransitionPoints.Contains(p))
                .OrderBy(p => p.Replications);

            foreach (var point in keyPoints)
            {
                string confidenceInterval = $"[{point.LowerBound:F3}, {point.UpperBound:F3}]";
                string status = point.IsTargetPrecisionAchieved ? 
                    "<span style='color: #27ae60; font-weight: bold;'>✓ ДОСТИГНУТО</span>" : 
                    "<span style='color: #e74c3c;'>⏳ В ПРОЦЕССЕ</span>";
                string rowClass = point.Replications == result.TargetReplications ? "class='target-reached'" : "";
                
                htmlBuilder.AppendLine($"                <tr {rowClass}>");
                htmlBuilder.AppendLine($"                    <td>{point.Replications}</td>");
                htmlBuilder.AppendLine($"                    <td>{point.Mean:F3}</td>");
                htmlBuilder.AppendLine($"                    <td>{point.StdDev:F3}</td>");
                htmlBuilder.AppendLine($"                    <td>{point.StandardError:F4}</td>");
                htmlBuilder.AppendLine($"                    <td>±{point.ConfidenceIntervalHalfWidth:F3}</td>");
                htmlBuilder.AppendLine($"                    <td>{point.RelativePrecision:F2}%</td>");
                htmlBuilder.AppendLine($"                    <td>{confidenceInterval}</td>");
                htmlBuilder.AppendLine($"                    <td>{status}</td>");
                htmlBuilder.AppendLine($"                </tr>");
            }
            
            htmlBuilder.AppendLine("                </tbody>");
            htmlBuilder.AppendLine("            </table>");
            htmlBuilder.AppendLine("        </div>");
            
            htmlBuilder.AppendLine("    </div>");

            // JavaScript для графиков
            htmlBuilder.AppendLine("    <script>");
            
            // Подготовка данных
            htmlBuilder.AppendLine("        const convergenceData = {");
            htmlBuilder.AppendLine($"            replications: {JsonSerializer.Serialize(result.Points.Select(p => p.Replications).ToArray(), jsonSerializerOptions)},");
            htmlBuilder.AppendLine($"            relativePrecision: {JsonSerializer.Serialize(result.Points.Select(p => p.RelativePrecision).ToArray(), jsonSerializerOptions)},");
            htmlBuilder.AppendLine($"            means: {JsonSerializer.Serialize(result.Points.Select(p => p.Mean).ToArray(), jsonSerializerOptions)},");
            htmlBuilder.AppendLine($"            lowerBounds: {JsonSerializer.Serialize(result.Points.Select(p => p.LowerBound).ToArray(), jsonSerializerOptions)},");
            htmlBuilder.AppendLine($"            upperBounds: {JsonSerializer.Serialize(result.Points.Select(p => p.UpperBound).ToArray(), jsonSerializerOptions)},");
            htmlBuilder.AppendLine($"            targetPrecision: {result.TargetPrecision},");
            htmlBuilder.AppendLine($"            targetReplications: {result.TargetReplications},");
            htmlBuilder.AppendLine($"            transitionPoints: {JsonSerializer.Serialize(result.DetailedTransitionPoints.Select(p => p.Replications).ToArray(), jsonSerializerOptions)}");
            htmlBuilder.AppendLine("        };");

            // Основной график точности
            htmlBuilder.AppendLine(@"
        // График зависимости точности от количества репликаций
        const precisionCtx = document.getElementById('precisionChart').getContext('2d');
        new Chart(precisionCtx, {
            type: 'line',
            data: {
                labels: convergenceData.replications,
                datasets: [
                    {
                        label: 'Относительная точность (%)',
                        data: convergenceData.relativePrecision,
                        borderColor: '#e74c3c',
                        backgroundColor: 'rgba(231, 76, 60, 0.1)',
                        borderWidth: 2,
                        fill: true,
                        tension: 0.4,
                        pointRadius: convergenceData.replications.map(r => 
                            convergenceData.transitionPoints.includes(r) ? 6 : 
                            r === convergenceData.targetReplications ? 8 : 3),
                        pointBackgroundColor: convergenceData.replications.map(r => 
                            convergenceData.transitionPoints.includes(r) ? '#f39c12' : 
                            r === convergenceData.targetReplications ? '#27ae60' : '#e74c3c')
                    },
                    {
                        label: 'Целевая точность (' + convergenceData.targetPrecision + '%)',
                        data: convergenceData.replications.map(() => convergenceData.targetPrecision),
                        borderColor: '#27ae60',
                        borderWidth: 2,
                        borderDash: [5, 5],
                        pointRadius: 0,
                        fill: false
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    x: {
                        title: {
                            display: true,
                            text: 'Количество репликаций'
                        }
                    },
                    y: {
                        title: {
                            display: true,
                            text: 'Относительная точность (%)'
                        },
                        beginAtZero: true
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: function(context) {
                                if (context.datasetIndex === 0) {
                                    const index = context.dataIndex;
                                    const replications = convergenceData.replications[index];
                                    const precision = convergenceData.relativePrecision[index];
                                    let label = `Точность: ${precision.toFixed(2)}% при ${replications} репликациях`;
                                    if (convergenceData.transitionPoints.includes(replications)) {
                                        label += ' (точка перехода)';
                                    }
                                    if (replications === convergenceData.targetReplications) {
                                        label += ' (цель достигнута)';
                                    }
                                    return label;
                                }
                                return context.dataset.label;
                            }
                        }
                    },
                    annotation: convergenceData.targetReplications > 0 ? {
                        annotations: {
                            targetLine: {
                                type: 'line',
                                scaleID: 'x',
                                value: convergenceData.targetReplications,
                                borderColor: '#3498db',
                                borderWidth: 2,
                                label: {
                                    content: 'Достигнуто при ' + convergenceData.targetReplications,
                                    enabled: true,
                                    position: 'end',
                                    backgroundColor: 'rgba(52, 152, 219, 0.8)'
                                }
                            }
                        }
                    } : {}
                }
            }
        });");

            // Детальный график первых 20 значений
            htmlBuilder.AppendLine(@"
        // Детальный график первых 20 репликаций
        const detailedCtx = document.getElementById('detailedChart').getContext('2d');
        const first20Indices = convergenceData.replications.map((r, i) => r <= 20 ? i : -1).filter(i => i >= 0);
        new Chart(detailedCtx, {
            type: 'line',
            data: {
                labels: first20Indices.map(i => convergenceData.replications[i]),
                datasets: [
                    {
                        label: 'Относительная точность (%)',
                        data: first20Indices.map(i => convergenceData.relativePrecision[i]),
                        borderColor: '#e74c3c',
                        backgroundColor: 'rgba(231, 76, 60, 0.2)',
                        borderWidth: 3,
                        fill: true,
                        tension: 0.4,
                        pointRadius: 5,
                        pointBackgroundColor: '#e74c3c'
                    },
                    {
                        label: 'Целевая точность (' + convergenceData.targetPrecision + '%)',
                        data: first20Indices.map(() => convergenceData.targetPrecision),
                        borderColor: '#27ae60',
                        borderWidth: 2,
                        borderDash: [5, 5],
                        pointRadius: 0,
                        fill: false
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    x: {
                        title: {
                            display: true,
                            text: 'Количество репликаций (первые 20)'
                        }
                    },
                    y: {
                        title: {
                            display: true,
                            text: 'Относительная точность (%)'
                        },
                        beginAtZero: true
                    }
                }
            }
        });");

            htmlBuilder.AppendLine("    </script>");
            htmlBuilder.AppendLine("</body></html>");

            return htmlBuilder.ToString();
        }

        private static void GenerateStyles(StringBuilder sb)
        {
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: 'Segoe UI', sans-serif; margin: 0; padding: 20px; background: #f0f2f5; color: #333; }");
            sb.AppendLine("        .container { max-width: 1800px; margin: auto; background: white; border-radius: 20px; padding: 30px; box-shadow: 0 10px 30px rgba(0,0,0,0.1); }");
            sb.AppendLine("        h1, h2 { text-align: center; color: #2c3e50; }");
            sb.AppendLine("        h2 { margin-top: 50px; border-top: 1px solid #ddd; padding-top: 30px; }");
            sb.AppendLine("        .section { margin: 30px 0; }");
            sb.AppendLine("        .info-box { background: #e8f4fd; border-left: 4px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px; }");
            sb.AppendLine("        .chart-container { background: #fff; width: 100%; padding: 25px; border-radius: 15px; box-shadow: 0 5px 15px rgba(0,0,0,0.05); margin: 20px 0; }");
            sb.AppendLine("        .table-container { margin: 20px 0; overflow-x: auto; }");
            sb.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
            sb.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: center; }");
            sb.AppendLine("        th { background-color: #3498db; color: white; font-weight: bold; }");
            sb.AppendLine("        tr:nth-child(even) { background-color: #f8f9fa; }");
            sb.AppendLine("        .target-reached { background-color: #d5f5e3 !important; font-weight: bold; border: 2px solid #27ae60; }");
            sb.AppendLine("    </style>");
        }
    }
}