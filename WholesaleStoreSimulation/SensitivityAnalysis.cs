using System.Text;
using System.Text.Json;

namespace WholesaleStoreSimulation
{
    /// <summary>
    /// Результат анализа чувствительности для одного параметра
    /// </summary>
    public class SensitivityAnalysisResult
    {
        public string ParameterName { get; set; } = "";
        public List<(double paramValue, double response)> Results { get; set; } = new();
        public double Slope { get; set; }
        public double Intercept { get; set; }
        public double CorrelationCoefficient { get; set; }
        public bool IsSensitive { get; set; }
        public double BaseValue { get; set; }
        public (double min, double max) VariationRange { get; set; }
        public string ResponseMetric { get; set; } = "AverageSystemTime";
    }

    /// <summary>
    /// Класс для анализа чувствительности с использованием независимых прогонов
    /// </summary>
    public static class SensitivityAnalysis
    {
        /// <summary>
        /// Анализ чувствительности одного параметра с использованием независимых прогонов
        /// </summary>
        public static SensitivityAnalysisResult AnalyzeSensitivity(
            SimulatorConfig baseConfig,
            string parameterName,
            Func<SimulatorConfig, double> getParameter,
            Action<SimulatorConfig, double> setParameter,
            string responseMetric = "AverageSystemTime",
            double variationPercent = 20,
            int steps = 11)
        {
            var results = new List<(double paramValue, double response)>();
            double baseValue = getParameter(baseConfig);

            double minValue = baseValue * (1 - variationPercent / 100);
            double maxValue = baseValue * (1 + variationPercent / 100);
            double stepSize = (maxValue - minValue) / (steps - 1);

            Console.WriteLine($"\n--- Анализ чувствительности: {parameterName} ---");
            Console.WriteLine($"Базовое значение: {baseValue:F2}");
            Console.WriteLine($"Диапазон варьирования: [{minValue:F2}, {maxValue:F2}]");
            Console.WriteLine($"Отклик: {responseMetric}");
            Console.WriteLine($"Шагов: {steps}");

            // Однофакторный эксперимент: варьируем один параметр
            for (int i = 0; i < steps; i++)
            {
                double paramValue = minValue + i * stepSize;
                
                // Создаем конфиг с новым значением параметра
                var cfg = CloneConfig(baseConfig);
                setParameter(cfg, paramValue);

                // Запускаем независимый прогон
                var sim = new WholesaleStoreSimulator(cfg, trace: false);
                sim.Run();
                var result = sim.GetResult();

                // Выбираем нужный отклик
                double responseValue = GetResponseValue(result, responseMetric);
                
                results.Add((paramValue, responseValue));
                
                Console.WriteLine($"  Шаг {i+1}: {parameterName} = {paramValue:F2} -> {responseMetric} = {responseValue:F2}");
            }

            // Линейная регрессия: response = b0 + b1 * parameter
            var regression = CalculateLinearRegression(
                results.Select(r => r.paramValue).ToList(),
                results.Select(r => r.response).ToList()
            );

            // Получаем отклик при базовом значении параметра
            double baseResponse = results[steps / 2].response;
            
            // Порог чувствительности = 5% от базового значения отклика при изменении параметра на 1%
            double threshold = (0.05 * baseResponse) / baseValue;

            // Условия чувствительности согласно теории:
            // 1. |R| > 0.5 (сильная корреляция)
            // 2. |b1| < порогового значения (малый наклон) -> низкая чувствительность
            bool isLowSensitivity = Math.Abs(regression.r) > 0.5 && Math.Abs(regression.slope) <= threshold;
            bool isSensitive = !isLowSensitivity;

            Console.WriteLine($"Коэффициент корреляции R = {regression.r:F4}");
            Console.WriteLine($"Наклон b1 = {regression.slope:F4}");
            Console.WriteLine($"Порог чувствительности = {threshold:F4}");
            Console.WriteLine($"Чувствительность: {(isSensitive ? "ВЫСОКАЯ" : "низкая")}");
            Console.WriteLine($"Условия: |R| > 0.5 ({Math.Abs(regression.r):F4} {(Math.Abs(regression.r) > 0.5 ? "> 0.5" : "<= 0.5")}) И |b1| {(Math.Abs(regression.slope) <= threshold ? "<=" : ">")} порог ({Math.Abs(regression.slope):F4} {(Math.Abs(regression.slope) <= threshold ? "<=" : ">")} {threshold:F4})");

            return new SensitivityAnalysisResult
            {
                ParameterName = parameterName,
                Results = results,
                Slope = regression.slope,
                Intercept = regression.intercept,
                CorrelationCoefficient = regression.r,
                IsSensitive = isSensitive,
                BaseValue = baseValue,
                VariationRange = (minValue, maxValue),
                ResponseMetric = responseMetric
            };
        }

        /// <summary>
        /// Полный анализ чувствительности для ключевых параметров системы
        /// </summary>
        public static List<SensitivityAnalysisResult> PerformComprehensiveAnalysis(SimulatorConfig config)
        {
            var results = new List<SensitivityAnalysisResult>();

            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("КОМПЛЕКСНЫЙ АНАЛИЗ ЧУВСТВИТЕЛЬНОСТИ СИСТЕМЫ");
            Console.WriteLine(new string('=', 70));

            // 1. Анализ чувствительности к интенсивности входного потока
            Console.WriteLine("\n1. Анализ параметра: MeanInterArrivalTime (±30%)");
            var sens1 = AnalyzeSensitivity(
                config,
                "Средний интервал между прибытиями (λ)",
                c => c.MeanInterArrivalTime,
                (c, v) => c.MeanInterArrivalTime = Math.Max(0.1, v), // Минимальное значение 0.1
                "AverageSystemTime",
                15, 10
            );
            results.Add(sens1);

            // 2. Анализ чувствительности к количеству клерков
            /*Console.WriteLine("\n2. Анализ параметра: ClerkCount (±50%, дискретные значения)");
            var sens2 = AnalyzeSensitivity(
                config,
                "Количество клерков",
                c => c.ClerkCount,
                (c, v) => c.ClerkCount = Math.Max(1, (int)Math.Round(v)), // Целое число, минимум 1
                "AverageSystemTime",
                15, 10
            );
            results.Add(sens2);*/

            // 3. Анализ чувствительности к размеру партии
            /*Console.WriteLine("\n3. Анализ параметра: ClerkBatchSize (±40%)");
            var sens3 = AnalyzeSensitivity(
                config,
                "Максимальный размер партии",
                c => c.ClerkBatchSize,
                (c, v) => c.ClerkBatchSize = Math.Max(1, (int)Math.Round(v)), // Целое число, минимум 1
                "AverageSystemTime",
                20, 10
            );
            results.Add(sens3);*/

            // 4. Анализ чувствительности к времени выбора товаров
            Console.WriteLine("\n4. Анализ параметра: CatalogSelectionMax (±25%)");
            var sens4 = AnalyzeSensitivity(
                config,
                "Макс. время выбора товаров",
                c => c.CatalogSelectionMax,
                (c, v) => c.CatalogSelectionMax = Math.Max(c.CatalogSelectionMin + 1, v),
                "AverageSystemTime",
                15, 10
            );
            results.Add(sens4);

            // 5. Анализ для разных откликов (опционально)
            Console.WriteLine("\n5. Анализ для отклика 'Средняя длина очереди'");
            var sens5 = AnalyzeSensitivity(
                config,
                "Средний интервал между прибытиями (λ)",
                c => c.MeanInterArrivalTime,
                (c, v) => c.MeanInterArrivalTime = Math.Max(0.1, v),
                "AverageQueueLength",
                15, 10
            );
            results.Add(sens5);

            // Вывод сводки
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("СВОДКА ПО АНАЛИЗУ ЧУВСТВИТЕЛЬНОСТИ");
            Console.WriteLine(new string('=', 70));
            
            foreach (var result in results)
            {
                string status = result.IsSensitive ? "✗ ВЫСОКАЯ" : "✓ низкая";
                Console.WriteLine($"│ {result.ParameterName,-35} │ R={result.CorrelationCoefficient,6:F3} │ b₁={result.Slope,7:F3} │ {status,-12} │");
            }
            Console.WriteLine(new string('=', 70));

            return results;
        }

        /// <summary>
        /// Получение значения отклика из результатов симуляции
        /// </summary>
        private static double GetResponseValue(SimulationResult result, string responseMetric)
        {
            return responseMetric switch
            {
                "AverageQueueLength" => result.AverageQueueLength,
                "AverageWaitingTime" => result.AverageWaitingTime,
                "AverageSystemTime" => result.AverageSystemTime,
                "CustomersServed" => result.CustomersServed,
                "ClerkUtilization" => result.ClerkUtilizations.Average(),
                _ => result.AverageSystemTime // по умолчанию
            };
        }

        /// <summary>
        /// Расчет линейной регрессии
        /// </summary>
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

            // Коэффициент корреляции Пирсона
            double r = (n * sumXY - sumX * sumY) / 
                      Math.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));

            return (slope, intercept, r);
        }

        /// <summary>
        /// Клонирование конфигурации через сериализацию
        /// </summary>
        private static SimulatorConfig CloneConfig(SimulatorConfig original)
        {
            return JsonSerializer.Deserialize<SimulatorConfig>(
                JsonSerializer.Serialize(original)) ?? new SimulatorConfig();
        }
    }

    /// <summary>
    /// Генератор отчетов для анализа чувствительности
    /// </summary>
    public static class SensitivityChartGenerator
    {
        public static void GenerateHtmlReport(List<SensitivityAnalysisResult> results, string fileName = "sensitivity_analysis.html")
        {
            var html = GenerateHtmlContent(results);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            File.WriteAllText(fullPath, html, Encoding.UTF8);
            Console.WriteLine($"\nОтчет по анализу чувствительности успешно сгенерирован: {fullPath}");
        }

        private static string GenerateHtmlContent(List<SensitivityAnalysisResult> results)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var htmlBuilder = new StringBuilder();

            htmlBuilder.AppendLine("<!DOCTYPE html>");
            htmlBuilder.AppendLine("<html lang=\"ru\"><head>");
            htmlBuilder.AppendLine("<meta charset=\"UTF-8\">");
            htmlBuilder.AppendLine("<title>Анализ чувствительности имитационной модели</title>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            GenerateStyles(htmlBuilder);
            htmlBuilder.AppendLine("</head><body>");
            htmlBuilder.AppendLine("<div class=\"container\">");
            
            // Заголовок
            htmlBuilder.AppendLine("<h1>🎯 1.6 Анализ чувствительности откликов к вариациям переменных ИМ</h1>");
            htmlBuilder.AppendLine("<div class=\"info-box\">");
            htmlBuilder.AppendLine("<strong>Цель:</strong> Оценить степень влияния изменений входных параметров на выходные отклики системы");
            htmlBuilder.AppendLine("</div>");

            // Методология
            htmlBuilder.AppendLine("<div class=\"methodology\">");
            htmlBuilder.AppendLine("<h2>Методология анализа</h2>");
            htmlBuilder.AppendLine("<ul>");
            htmlBuilder.AppendLine("<li><strong>Метод:</strong> Однофакторный эксперимент с линейной регрессией y = b₀ + b₁·w</li>");
            htmlBuilder.AppendLine("<li><strong>Критерии чувствительности:</strong> Если |R| > 0.5 И |b₁| ≤ порога (5% от отклика), то чувствительность низкая</li>");
            htmlBuilder.AppendLine("<li><strong>Тип прогона:</strong> Независимые прогоны для каждого значения параметра</li>");
            htmlBuilder.AppendLine("<li><strong>Интерпретация:</strong> Высокая чувствительность → требуется высокая точность задания параметра</li>");
            htmlBuilder.AppendLine("</ul>");
            htmlBuilder.AppendLine("</div>");

            // Сводная таблица
            htmlBuilder.AppendLine("<h2>Сводные результаты анализа чувствительности</h2>");
            htmlBuilder.AppendLine("<table class=\"summary-table\">");
            htmlBuilder.AppendLine("<thead><tr>");
            htmlBuilder.AppendLine("<th>Параметр</th><th>Базовое значение</th><th>Диапазон варьирования</th>");
            htmlBuilder.AppendLine("<th>Коэффициент корреляции (R)</th><th>Наклон (b₁)</th><th>Чувствительность</th>");
            htmlBuilder.AppendLine("</tr></thead><tbody>");

            foreach (var sens in results)
            {
                string sensClass = sens.IsSensitive ? "sensitive" : "not-sensitive";
                string sensText = sens.IsSensitive ? 
                    "✗ ВЫСОКАЯ (требует точного задания)" : 
                    "✓ Низкая (допускает приближенное задание)";

                htmlBuilder.AppendLine($"<tr class=\"{sensClass}\">");
                htmlBuilder.AppendLine($"<td>{sens.ParameterName}</td>");
                htmlBuilder.AppendLine($"<td>{sens.BaseValue:F2}</td>");
                htmlBuilder.AppendLine($"<td>[{sens.VariationRange.min:F2}, {sens.VariationRange.max:F2}]</td>");
                htmlBuilder.AppendLine($"<td>{sens.CorrelationCoefficient:F4}</td>");
                htmlBuilder.AppendLine($"<td>{sens.Slope:F4}</td>");
                htmlBuilder.AppendLine($"<td><strong>{sensText}</strong></td>");
                htmlBuilder.AppendLine("</tr>");
            }

            htmlBuilder.AppendLine("</tbody></table>");

            // Графики чувствительности
            htmlBuilder.AppendLine("<h2>Диаграммы чувствительности с линиями регрессии</h2>");
            htmlBuilder.AppendLine("<div class=\"charts-grid\">");

            for (int i = 0; i < results.Count; i++)
            {
                var sens = results[i];
                htmlBuilder.AppendLine($"<div class=\"chart-container\">");
                htmlBuilder.AppendLine($"<h3>{sens.ParameterName}</h3>");
                htmlBuilder.AppendLine($"<canvas id=\"sensChart{i}\"></canvas>");
                htmlBuilder.AppendLine($"<div class=\"chart-info\">");
                htmlBuilder.AppendLine($"<p>R = {sens.CorrelationCoefficient:F4}, b₁ = {sens.Slope:F4}</p>");
                htmlBuilder.AppendLine($"<p class=\"{(sens.IsSensitive ? "sensitive-text" : "not-sensitive-text")}\">");
                htmlBuilder.AppendLine(sens.IsSensitive ? "Высокая чувствительность" : "Низкая чувствительность");
                htmlBuilder.AppendLine("</p></div></div>");
            }

            htmlBuilder.AppendLine("</div>");
            htmlBuilder.AppendLine("</div>"); // закрываем container

            // JavaScript для графиков
            htmlBuilder.AppendLine("<script>");
            htmlBuilder.AppendLine("const createChart = (id, config) => new Chart(document.getElementById(id), config);");
            
            for (int i = 0; i < results.Count; i++)
            {
                var sens = results[i];
                
                // Данные для scatter plot
                var scatterData = string.Join(", ", sens.Results.Select(r => 
                    $"{{x: {r.paramValue.ToString("F3", culture)}, y: {r.response.ToString("F3", culture)}}}"));
                
                // Данные для линии регрессии
                var regressionData = string.Join(", ", sens.Results.Select(r => 
                {
                    double y = sens.Intercept + sens.Slope * r.paramValue;
                    return $"{{x: {r.paramValue.ToString("F3", culture)}, y: {y.ToString("F3", culture)}}}";
                }));

                htmlBuilder.AppendLine($@"createChart('sensChart{i}', {{
                    type: 'scatter',
                    data: {{
                        datasets: [
                            {{
                                label: 'Экспериментальные точки',
                                data: [{scatterData}],
                                backgroundColor: 'rgba(52, 152, 219, 0.7)',
                                borderColor: 'rgba(52, 152, 219, 1)',
                                pointRadius: 6,
                                pointBorderWidth: 2,
                                pointBorderColor: '#fff'
                            }},
                            {{
                                label: 'Линия регрессии (R={sens.CorrelationCoefficient:F4})',
                                data: [{regressionData}],
                                type: 'line',
                                borderColor: 'rgba(231, 76, 60, 1)',
                                backgroundColor: 'transparent',
                                borderWidth: 3,
                                pointRadius: 0,
                                fill: false,
                                tension: 0
                            }}
                        ]
                    }},
                    options: {{
                        responsive: true,
                        plugins: {{ 
                            title: {{ 
                                display: true, 
                                text: '{sens.ParameterName}',
                                font: {{ size: 14, weight: 'bold' }}
                            }},
                            legend: {{ position: 'top' }}
                        }},
                        scales: {{
                            x: {{ 
                                type: 'linear',
                                title: {{ 
                                    display: true, 
                                    text: '{sens.ParameterName}',
                                    font: {{ weight: 'bold' }}
                                }}
                            }},
                            y: {{ 
                                beginAtZero: false, 
                                title: {{ 
                                    display: true, 
                                    text: '{sens.ResponseMetric}',
                                    font: {{ weight: 'bold' }}
                                }}
                            }}
                        }}
                    }}
                }});");
            }

            htmlBuilder.AppendLine("</script>");
            htmlBuilder.AppendLine("</body></html>");

            return htmlBuilder.ToString();
        }

        private static void GenerateStyles(StringBuilder sb)
        {
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 20px; background: #f5f7fa; color: #333; }");
            sb.AppendLine(".container { max-width: 1400px; margin: 0 auto; background: white; border-radius: 15px; padding: 30px; box-shadow: 0 5px 25px rgba(0,0,0,0.1); }");
            sb.AppendLine("h1 { color: #2c3e50; text-align: center; margin-bottom: 30px; border-bottom: 2px solid #3498db; padding-bottom: 15px; }");
            sb.AppendLine("h2 { color: #34495e; margin-top: 40px; border-left: 4px solid #3498db; padding-left: 15px; }");
            sb.AppendLine("h3 { color: #2c3e50; margin: 20px 0 15px 0; }");
            sb.AppendLine(".info-box { background: #e8f4fc; border: 1px solid #3498db; border-radius: 8px; padding: 15px; margin: 20px 0; }");
            sb.AppendLine(".methodology { background: #f8f9fa; border-radius: 8px; padding: 20px; margin: 20px 0; }");
            sb.AppendLine(".methodology ul { margin: 10px 0; }");
            sb.AppendLine(".methodology li { margin: 8px 0; }");
            sb.AppendLine(".summary-table { width: 100%; border-collapse: collapse; margin: 20px 0; font-size: 14px; }");
            sb.AppendLine(".summary-table th { background: #34495e; color: white; padding: 12px; text-align: left; }");
            sb.AppendLine(".summary-table td { padding: 10px; border-bottom: 1px solid #ddd; }");
            sb.AppendLine(".summary-table tr.sensitive { background: #ffeaea; }");
            sb.AppendLine(".summary-table tr.not-sensitive { background: #f0fff0; }");
            sb.AppendLine(".charts-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(500px, 1fr)); gap: 25px; margin: 30px 0; }");
            sb.AppendLine(".chart-container { background: #fff; padding: 20px; border-radius: 10px; box-shadow: 0 3px 15px rgba(0,0,0,0.1); border: 1px solid #e0e0e0; }");
            sb.AppendLine(".chart-info { text-align: center; margin-top: 15px; padding: 10px; background: #f8f9fa; border-radius: 5px; }");
            sb.AppendLine(".sensitive-text { color: #e74c3c; font-weight: bold; }");
            sb.AppendLine(".not-sensitive-text { color: #27ae60; font-weight: bold; }");
            sb.AppendLine("</style>");
        }
    }
}