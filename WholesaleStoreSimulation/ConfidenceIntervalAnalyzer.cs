using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WholesaleStoreSimulation
{
    public class ConfidenceInterval
    {
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public int SampleSize { get; set; }
        public double LowerBound { get; set; }
        public double UpperBound { get; set; }
        public double WillinkLowerBound { get; set; }
        public double WillinkUpperBound { get; set; }
        public double ConfidenceLevel { get; set; }
        public double Skewness { get; set; }
        public List<double> AllValues { get; set; } = new List<double>();
    }

    public class ConfidenceIntervalResult
    {
        public string MetricName { get; set; } = "";
        public ConfidenceInterval Interval { get; set; } = new ConfidenceInterval();
    }

    public static class ConfidenceIntervalAnalyzer
    {
        public static SimulatorConfig CloneConfig(SimulatorConfig original)
        {
            return JsonSerializer.Deserialize<SimulatorConfig>(JsonSerializer.Serialize(original));
        }

        public static List<ConfidenceIntervalResult> PerformConfidenceIntervalAnalysis(SimulatorConfig config)
        {
            var results = new List<ConfidenceIntervalResult>();
            const int replications = 20;

            var systemTimeData = new List<double>();
            var waitTimeData = new List<double>();
            var customersServedData = new List<double>();
            var queueLengthData = new List<double>();
            var utilizationData = new List<double>();

            Console.WriteLine("\n" + new string('-', 60));
            Console.WriteLine("1.2 ВЫЧИСЛЕНИЕ ТОЧЕЧНЫХ И ИНТЕРВАЛЬНЫХ ОЦЕНОК ОТКЛИКОВ");
            Console.WriteLine(new string('-', 60));
            Console.WriteLine("│  Выполнение 20 прогонов для построения доверительных интервалов...");

            for (int i = 0; i < replications; i++)
            {
                var cfg = CloneConfig(config);
                cfg.RandomSeed = config.RandomSeed + 1000 + i * 37;

                var sim = new WholesaleStoreSimulator(cfg, trace: false);
                sim.Run();
                var result = sim.GetResult();

                systemTimeData.Add(result.AverageSystemTime);
                waitTimeData.Add(result.AverageWaitingTime);
                customersServedData.Add(result.CustomersServed);
                queueLengthData.Add(result.AverageQueueLength);
                utilizationData.Add(result.ClerkUtilizations.Average());
            }

            // Вычисляем доверительные интервалы для всех метрик
            var ci1 = CalculateConfidenceInterval(systemTimeData);
            results.Add(new ConfidenceIntervalResult { 
                MetricName = "Среднее время в системе", 
                Interval = ci1 
            });

            /*var ci2 = CalculateConfidenceInterval(waitTimeData);
            results.Add(new ConfidenceIntervalResult { 
                MetricName = "Среднее время ожидания", 
                Interval = ci2 
            });

            var ci3 = CalculateConfidenceInterval(customersServedData);
            results.Add(new ConfidenceIntervalResult { 
                MetricName = "Количество обслуженных покупателей", 
                Interval = ci3 
            });

            var ci4 = CalculateConfidenceInterval(queueLengthData);
            results.Add(new ConfidenceIntervalResult { 
                MetricName = "Средняя длина очереди", 
                Interval = ci4 
            });

            var ci5 = CalculateConfidenceInterval(utilizationData);
            results.Add(new ConfidenceIntervalResult { 
                MetricName = "Средняя загрузка клерков", 
                Interval = ci5 
            });*/

            // Вывод результатов в консоль
            Console.WriteLine($"│");
            foreach (var result in results)
            {
                var ci = result.Interval;
                Console.WriteLine($"│  Отклик: '{result.MetricName}'");
                Console.WriteLine($"│    Среднее: {ci.Mean:F3}, σ = {ci.StdDev:F3}");
                Console.WriteLine($"│    95% ДИ (стандартный): [{ci.LowerBound:F3}, {ci.UpperBound:F3}]");
                Console.WriteLine($"│    95% ДИ (Уиллинка): [{ci.WillinkLowerBound:F3}, {ci.WillinkUpperBound:F3}]");
                Console.WriteLine($"│    Коэффициент асимметрии: {ci.Skewness:F4}");
                Console.WriteLine($"│");
            }
            Console.WriteLine($"└─ Анализ завершен");

            return results;
        }

        public static ConfidenceInterval CalculateConfidenceInterval(List<double> data, double alpha = 0.05)
        {
            int n = data.Count;
            double mean = data.Average();
            
            // Несмещенная оценка дисперсии
            double variance = data.Sum(x => Math.Pow(x - mean, 2)) / (n - 1);
            double stdDev = Math.Sqrt(variance);
            double sem = stdDev / Math.Sqrt(n);

            // t-критерий Стьюдента для (n-1) степеней свободы
            double tValue = GetTCritical(n - 1, alpha / 2);

            // Стандартный доверительный интервал (симметричный)
            double marginOfError = tValue * sem;

            // Метод Уиллинка для асимметричных распределений
            double mu3 = data.Sum(x => Math.Pow(x - mean, 3)) * n / ((n - 1) * (n - 2));
            double skewness = mu3 / Math.Pow(stdDev, 3);
            double a = mu3 / (6 * Math.Sqrt(n) * Math.Pow(stdDev, 3));

            double gLeft = tValue, gRight = -tValue;
            if (Math.Abs(a) > 1e-10)
            {
                double term1 = 1 + 6 * a * (tValue - a);
                double term2 = 1 + 6 * a * (-tValue - a);
                
                if (term1 > 0) gLeft = (Math.Pow(term1, 1.0 / 3) - 1) * 0.5 / a;
                if (term2 > 0) gRight = (Math.Pow(term2, 1.0 / 3) - 1) * 0.5 / a;
            }

            return new ConfidenceInterval
            {
                Mean = mean,
                StdDev = stdDev,
                SampleSize = n,
                LowerBound = mean - marginOfError,
                UpperBound = mean + marginOfError,
                WillinkLowerBound = mean - gLeft * stdDev / Math.Sqrt(n),
                WillinkUpperBound = mean - gRight * stdDev / Math.Sqrt(n),
                ConfidenceLevel = 1 - alpha,
                Skewness = skewness,
                AllValues = data
            };
        }

        private static double GetTCritical(int degreesOfFreedom, double alpha)
        {
            // Таблица t-критических значений для двустороннего теста
            var tTable = new Dictionary<int, double>
            {
                { 1, 12.706 }, { 2, 4.303 }, { 3, 3.182 }, { 4, 2.776 }, { 5, 2.571 },
                { 6, 2.447 }, { 7, 2.365 }, { 8, 2.306 }, { 9, 2.262 }, { 10, 2.228 },
                { 11, 2.201 }, { 12, 2.179 }, { 13, 2.160 }, { 14, 2.145 }, { 15, 2.131 },
                { 16, 2.120 }, { 17, 2.110 }, { 18, 2.101 }, { 19, 2.093 }, { 20, 2.086 },
                { 21, 2.080 }, { 22, 2.074 }, { 23, 2.069 }, { 24, 2.064 }, { 25, 2.060 },
                { 26, 2.056 }, { 27, 2.052 }, { 28, 2.048 }, { 29, 2.045 }, { 30, 2.042 }
            };

            if (tTable.ContainsKey(degreesOfFreedom))
                return tTable[degreesOfFreedom];
            else
                return 1.96; // Приближение для больших степеней свободы
        }
    }

    public class ConfidenceIntervalChartGenerator
    {
        public static void GenerateHtmlReport(List<ConfidenceIntervalResult> results, string fileName = "confidence_intervals.html")
        {
            var html = GenerateHtmlContent(results);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            File.WriteAllText(fullPath, html, Encoding.UTF8);
            Console.WriteLine($"\nОтчет по доверительным интервалам успешно сгенерирован: {fullPath}");
        }

        private static string GenerateHtmlContent(List<ConfidenceIntervalResult> results)
        {
            var culture = CultureInfo.InvariantCulture;
            var htmlBuilder = new StringBuilder();

            htmlBuilder.AppendLine("<!DOCTYPE html>");
            htmlBuilder.AppendLine("<html lang=\"ru\"><head>");
            htmlBuilder.AppendLine("<meta charset=\"UTF-8\"><title>Доверительные интервалы откликов</title>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            GenerateStyles(htmlBuilder);
            htmlBuilder.AppendLine("</head><body><div class=\"container\">");
            htmlBuilder.AppendLine("<h1>📊 1.2 Точечные и интервальные оценки откликов ИМ</h1>");
            
            // Информация о параметрах
            htmlBuilder.AppendLine("<div class='section'>");
            htmlBuilder.AppendLine("<div class='info-box'><strong>Параметры:</strong> n = 20 повторных прогонов, доверительная вероятность 95% (α = 0.05)<br>");
            htmlBuilder.AppendLine("<strong>Методы:</strong> Стандартный t-критерий Стьюдента + Метод Уиллинка для асимметричных распределений</div>");

            // Таблица с результатами
            htmlBuilder.AppendLine("        <h2>Результаты вычислений</h2>");
            htmlBuilder.AppendLine("        <div class=\"table-container\">");
            htmlBuilder.AppendLine("            <table>");
            htmlBuilder.AppendLine("                <thead><tr><th>Отклик</th><th>Среднее</th><th>σ (ст.откл.)</th><th>ДИ (стандартный)</th><th>ДИ (Уиллинка)</th><th>Асимметрия</th></tr></thead>");
            htmlBuilder.AppendLine("                <tbody>");
            
            foreach (var result in results)
            {
                var ci = result.Interval;
                htmlBuilder.AppendLine($"                <tr>");
                htmlBuilder.AppendLine($"                    <td>{result.MetricName}</td>");
                htmlBuilder.AppendLine($"                    <td>{ci.Mean:F3}</td>");
                htmlBuilder.AppendLine($"                    <td>{ci.StdDev:F3}</td>");
                htmlBuilder.AppendLine($"                    <td>[{ci.LowerBound:F3}, {ci.UpperBound:F3}]</td>");
                htmlBuilder.AppendLine($"                    <td>[{ci.WillinkLowerBound:F3}, {ci.WillinkUpperBound:F3}]</td>");
                htmlBuilder.AppendLine($"                    <td>{ci.Skewness:F4}</td>");
                htmlBuilder.AppendLine($"                </tr>");
            }
            
            htmlBuilder.AppendLine("                </tbody>");
            htmlBuilder.AppendLine("            </table>");
            htmlBuilder.AppendLine("        </div>");

            // График доверительных интервалов - разделенные
            htmlBuilder.AppendLine("        <h2>Сравнение доверительных интервалов</h2>");
            htmlBuilder.AppendLine("        <div class=\"grid-container\">");
            htmlBuilder.AppendLine("            <div class=\"chart-container\">");
            htmlBuilder.AppendLine("                <h3>Стандартные доверительные интервалы (95%)</h3>");
            htmlBuilder.AppendLine("                <canvas id=\"standardCIChart\"></canvas>");
            htmlBuilder.AppendLine("            </div>");
            htmlBuilder.AppendLine("            <div class=\"chart-container\">");
            htmlBuilder.AppendLine("                <h3>Доверительные интервалы Уиллинка (95%)</h3>");
            htmlBuilder.AppendLine("                <canvas id=\"willinkCIChart\"></canvas>");
            htmlBuilder.AppendLine("            </div>");
            htmlBuilder.AppendLine("        </div>");

            // Графики распределения значений для каждой метрики - разделенные
            htmlBuilder.AppendLine("        <h2>Распределение значений по репликациям</h2>");
            
            // Стандартные доверительные интервалы
            htmlBuilder.AppendLine("        <h3>Стандартные доверительные интервалы</h3>");
            htmlBuilder.AppendLine("        <div class=\"grid-container\">");
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                htmlBuilder.AppendLine($"        <div class=\"chart-container\">");
                htmlBuilder.AppendLine($"            <h4>{result.MetricName}</h4>");
                htmlBuilder.AppendLine($"            <canvas id=\"standardDistributionChart{i}\"></canvas>");
                htmlBuilder.AppendLine($"        </div>");
            }
            htmlBuilder.AppendLine("        </div>");
            
            // Доверительные интервалы Уиллинка
            htmlBuilder.AppendLine("        <h3>Доверительные интервалы Уиллинка</h3>");
            htmlBuilder.AppendLine("        <div class=\"grid-container\">");
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                htmlBuilder.AppendLine($"        <div class=\"chart-container\">");
                htmlBuilder.AppendLine($"            <h4>{result.MetricName}</h4>");
                htmlBuilder.AppendLine($"            <canvas id=\"willinkDistributionChart{i}\"></canvas>");
                htmlBuilder.AppendLine($"        </div>");
            }
            htmlBuilder.AppendLine("        </div>");
            
            htmlBuilder.AppendLine("    </div>");

            // JavaScript для графиков
            htmlBuilder.AppendLine("    <script>");
            
            // Данные для графиков
            htmlBuilder.AppendLine("        const confidenceData = {");
            htmlBuilder.AppendLine("            labels: " + JsonSerializer.Serialize(results.Select(r => r.MetricName).ToArray()) + ",");
            htmlBuilder.AppendLine("            means: " + JsonSerializer.Serialize(results.Select(r => r.Interval.Mean).ToArray()) + ",");
            htmlBuilder.AppendLine("            lowerBounds: " + JsonSerializer.Serialize(results.Select(r => r.Interval.LowerBound).ToArray()) + ",");
            htmlBuilder.AppendLine("            upperBounds: " + JsonSerializer.Serialize(results.Select(r => r.Interval.UpperBound).ToArray()) + ",");
            htmlBuilder.AppendLine("            willinkLowerBounds: " + JsonSerializer.Serialize(results.Select(r => r.Interval.WillinkLowerBound).ToArray()) + ",");
            htmlBuilder.AppendLine("            willinkUpperBounds: " + JsonSerializer.Serialize(results.Select(r => r.Interval.WillinkUpperBound).ToArray()) + ",");
            htmlBuilder.AppendLine("            allValues: " + JsonSerializer.Serialize(results.Select(r => r.Interval.AllValues).ToArray()));
            htmlBuilder.AppendLine("        };");

            // Создание основного графика стандартных доверительных интервалов
            htmlBuilder.AppendLine(@"
        // Стандартные доверительные интервалы
        const standardCtx = document.getElementById('standardCIChart').getContext('2d');
        new Chart(standardCtx, {
            type: 'bar',
            data: {
                labels: confidenceData.labels,
                datasets: [
                    {
                        label: 'Точечная оценка (среднее)',
                        data: confidenceData.means,
                        backgroundColor: '#3498db',
                        borderColor: '#2980b9',
                        borderWidth: 2
                    },
                    {
                        label: 'Стандартный ДИ (95%)',
                        data: confidenceData.upperBounds.map((upper, i) => upper - confidenceData.lowerBounds[i]),
                        backgroundColor: 'rgba(52, 152, 219, 0.3)',
                        borderColor: 'rgba(52, 152, 219, 0.8)',
                        borderWidth: 1,
                        barPercentage: 0.6
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Значения'
                        }
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: function(context) {
                                const datasetLabel = context.dataset.label || '';
                                const index = context.dataIndex;
                                if (datasetLabel.includes('Стандартный')) {
                                    return `Стандартный ДИ: [${confidenceData.lowerBounds[index].toFixed(3)}, ${confidenceData.upperBounds[index].toFixed(3)}]`;
                                }
                                return `${datasetLabel}: ${context.parsed.y.toFixed(3)}`;
                            }
                        }
                    }
                }
            }
        });");

            // Создание основного графика доверительных интервалов Уиллинка
            htmlBuilder.AppendLine(@"
        // Доверительные интервалы Уиллинка
        const willinkCtx = document.getElementById('willinkCIChart').getContext('2d');
        new Chart(willinkCtx, {
            type: 'bar',
            data: {
                labels: confidenceData.labels,
                datasets: [
                    {
                        label: 'Точечная оценка (среднее)',
                        data: confidenceData.means,
                        backgroundColor: '#e74c3c',
                        borderColor: '#c0392b',
                        borderWidth: 2
                    },
                    {
                        label: 'ДИ Уиллинка (95%)',
                        data: confidenceData.willinkUpperBounds.map((upper, i) => upper - confidenceData.willinkLowerBounds[i]),
                        backgroundColor: 'rgba(231, 76, 60, 0.3)',
                        borderColor: 'rgba(231, 76, 60, 0.8)',
                        borderWidth: 1,
                        barPercentage: 0.6
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Значения'
                        }
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: function(context) {
                                const datasetLabel = context.dataset.label || '';
                                const index = context.dataIndex;
                                if (datasetLabel.includes('Уиллинка')) {
                                    return `ДИ Уиллинка: [${confidenceData.willinkLowerBounds[index].toFixed(3)}, ${confidenceData.willinkUpperBounds[index].toFixed(3)}]`;
                                }
                                return `${datasetLabel}: ${context.parsed.y.toFixed(3)}`;
                            }
                        }
                    }
                }
            }
        });");

            // Создание графиков распределения для стандартных доверительных интервалов
            for (int i = 0; i < results.Count; i++)
            {
                htmlBuilder.AppendLine($@"
        // Стандартный ДИ для {results[i].MetricName}
        const standardCtx{i} = document.getElementById('standardDistributionChart{i}').getContext('2d');
        new Chart(standardCtx{i}, {{
            type: 'scatter',
            data: {{
                datasets: [
                    {{
                        label: 'Значения по репликациям',
                        data: confidenceData.allValues[{i}].map((value, index) => ({{x: index + 1, y: value}})),
                        backgroundColor: '#3498db',
                        pointRadius: 4
                    }},
                    {{
                        label: 'Точечная оценка',
                        data: Array(confidenceData.allValues[{i}].length).fill().map((_, index) => ({{x: index + 1, y: confidenceData.means[{i}]}})),
                        borderColor: '#2c3e50',
                        borderWidth: 2,
                        pointRadius: 0,
                        type: 'line',
                        fill: false
                    }},
                    {{
                        label: 'Верхняя граница ДИ',
                        data: Array(confidenceData.allValues[{i}].length).fill().map((_, index) => ({{x: index + 1, y: confidenceData.upperBounds[{i}]}})),
                        borderColor: '#27ae60',
                        borderWidth: 1,
                        borderDash: [5, 5],
                        pointRadius: 0,
                        type: 'line',
                        fill: false
                    }},
                    {{
                        label: 'Нижняя граница ДИ',
                        data: Array(confidenceData.allValues[{i}].length).fill().map((_, index) => ({{x: index + 1, y: confidenceData.lowerBounds[{i}]}})),
                        borderColor: '#e67e22',
                        borderWidth: 1,
                        borderDash: [5, 5],
                        pointRadius: 0,
                        type: 'line',
                        fill: false
                    }}
                ]
            }},
            options: {{
                responsive: true,
                scales: {{
                    x: {{
                        title: {{
                            display: true,
                            text: 'Номер репликации'
                        }}
                    }},
                    y: {{
                        title: {{
                            display: true,
                            text: 'Значение'
                        }}
                    }}
                }}
            }}
        }});");
            }

            // Создание графиков распределения для доверительных интервалов Уиллинка
            for (int i = 0; i < results.Count; i++)
            {
                htmlBuilder.AppendLine($@"
        // ДИ Уиллинка для {results[i].MetricName}
        const willinkCtx{i} = document.getElementById('willinkDistributionChart{i}').getContext('2d');
        new Chart(willinkCtx{i}, {{
            type: 'scatter',
            data: {{
                datasets: [
                    {{
                        label: 'Значения по репликациям',
                        data: confidenceData.allValues[{i}].map((value, index) => ({{x: index + 1, y: value}})),
                        backgroundColor: '#e74c3c',
                        pointRadius: 4
                    }},
                    {{
                        label: 'Точечная оценка',
                        data: Array(confidenceData.allValues[{i}].length).fill().map((_, index) => ({{x: index + 1, y: confidenceData.means[{i}]}})),
                        borderColor: '#2c3e50',
                        borderWidth: 2,
                        pointRadius: 0,
                        type: 'line',
                        fill: false
                    }},
                    {{
                        label: 'Верхняя граница ДИ Уиллинка',
                        data: Array(confidenceData.allValues[{i}].length).fill().map((_, index) => ({{x: index + 1, y: confidenceData.willinkUpperBounds[{i}]}})),
                        borderColor: '#8e44ad',
                        borderWidth: 1,
                        borderDash: [5, 5],
                        pointRadius: 0,
                        type: 'line',
                        fill: false
                    }},
                    {{
                        label: 'Нижняя граница ДИ Уиллинка',
                        data: Array(confidenceData.allValues[{i}].length).fill().map((_, index) => ({{x: index + 1, y: confidenceData.willinkLowerBounds[{i}]}})),
                        borderColor: '#f39c12',
                        borderWidth: 1,
                        borderDash: [5, 5],
                        pointRadius: 0,
                        type: 'line',
                        fill: false
                    }}
                ]
            }},
            options: {{
                responsive: true,
                scales: {{
                    x: {{
                        title: {{
                            display: true,
                            text: 'Номер репликации'
                        }}
                    }},
                    y: {{
                        title: {{
                            display: true,
                            text: 'Значение'
                        }}
                    }}
                }}
            }}
        }});");
            }

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
            sb.AppendLine("        h3 { color: #34495e; margin-top: 30px; }");
            sb.AppendLine("        h4 { color: #7f8c8d; margin: 15px 0; }");
            sb.AppendLine("        .section { margin: 30px 0; }");
            sb.AppendLine("        .info-box { background: #e8f4fd; border-left: 4px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px; }");
            sb.AppendLine("        .table-container { margin: 20px 0; overflow-x: auto; }");
            sb.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
            sb.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: center; }");
            sb.AppendLine("        th { background-color: #3498db; color: white; font-weight: bold; }");
            sb.AppendLine("        tr:nth-child(even) { background-color: #f8f9fa; }");
            sb.AppendLine("        .chart-container { background: #fff; padding: 25px; border-radius: 15px; box-shadow: 0 5px 15px rgba(0,0,0,0.05); margin: 20px 0; }");
            sb.AppendLine("        .grid-container { display: grid; grid-template-columns: repeat(auto-fit, minmax(500px, 1fr)); gap: 25px; margin-bottom: 40px; }");
            sb.AppendLine("    </style>");
        }
    }
}