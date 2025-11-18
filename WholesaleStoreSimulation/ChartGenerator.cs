using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WholesaleStoreSimulation
{
    public class ChartGenerator
    {
        #region --- Генератор основного отчета ---
        
        public static void GenerateHtmlReport(
            SimulationResult singleResult,
            AggregatedResult aggregatedResult,
            IEnumerable<ChiSquaredMetricTestResult> chiSquaredResults = null,
            string fileName = "simulation_results.html")
        {
            var html = GenerateMainHtmlContent(singleResult, aggregatedResult, chiSquaredResults);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            File.WriteAllText(fullPath, html, Encoding.UTF8);
            Console.WriteLine($"\nОсновной HTML отчет успешно сгенерирован: {fullPath}");
        }

        private static string GenerateMainHtmlContent(
            SimulationResult single,
            AggregatedResult aggregated,
            IEnumerable<ChiSquaredMetricTestResult> chiSquaredResults = null)
        {
            var culture = CultureInfo.InvariantCulture;
            var htmlBuilder = new StringBuilder();
            var chiSquaredList = chiSquaredResults?.ToList() ?? new List<ChiSquaredMetricTestResult>();

            htmlBuilder.AppendLine("<!DOCTYPE html>");
            htmlBuilder.AppendLine("<html lang=\"ru\"><head>");
            htmlBuilder.AppendLine(
                "<meta charset=\"UTF-8\"><title>Тест хи-квадрат на нормальность распределения</title>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            GenerateCommonStyles(htmlBuilder);
            htmlBuilder.AppendLine("</head><body><div class=\"container\">");
            htmlBuilder.AppendLine("<h1>🏪 1.1 Тест хи-квадрат на нормальность распределения откликов</h1>");

            if (chiSquaredList.Any())
            {
                htmlBuilder.AppendLine("        <p>Для каждого ключевого отклика выполнен отдельный тест хи-квадрат.</p>");

                for (int index = 0; index < chiSquaredList.Count; index++)
                {
                    var metricResult = chiSquaredList[index];
                    string cssClass = metricResult.Result.IsNormal ? "chi-squared-accepted" : "chi-squared-rejected";
                    string resultText = metricResult.Result.IsNormal ? "ГИПОТЕЗА ПРИНЯТА" : "ГИПОТЕЗА ОТВЕРГНУТА";
                    string color = metricResult.Result.IsNormal ? "#27ae60" : "#e74c3c";

                    htmlBuilder.AppendLine($"        <div class=\"chi-squared-result {cssClass}\">");
                    htmlBuilder.AppendLine($"            <h3>{metricResult.MetricName}</h3>");
                    htmlBuilder.AppendLine(
                        $"            <p><strong>Выборочное среднее:</strong> {metricResult.Result.Mean:F3} | <strong>Стандартное отклонение:</strong> {metricResult.Result.StdDev:F3}</p>");
                    htmlBuilder.AppendLine(
                        $"            <p><strong>Статистика χ²:</strong> {metricResult.Result.ChiSquaredStatistic:F4} | <strong>Критическое значение:</strong> {metricResult.Result.CriticalValue:F4} | <strong>df:</strong> {metricResult.Result.DegreesOfFreedom}</p>");
                    htmlBuilder.AppendLine(
                        $"            <p><strong>Результат:</strong> <span style=\"color:{color}; font-weight:bold;\">{resultText}</span></p>");
                    htmlBuilder.AppendLine(
                        $"            <div class=\"chart-container\"><h4>Гистограмма наблюдаемых и ожидаемых частот</h4><canvas id=\"chiSquaredHistogramChart{index}\"></canvas></div>");
                    htmlBuilder.AppendLine("        </div>");
                }

                var serializedChiSquared = SerializeChiSquaredResults(chiSquaredList);
                htmlBuilder.AppendLine($"<script>const chiSquaredData = {serializedChiSquared};</script>");
            }

            htmlBuilder.AppendLine("    </div>");
            htmlBuilder.AppendLine("    <script>");
            htmlBuilder.AppendLine(
                "        const createChart = (id, config) => new Chart(document.getElementById(id), config);");

            // Создание гистограмм хи-квадрат для каждого отклика
            htmlBuilder.AppendLine("        if (typeof chiSquaredData !== 'undefined') {");
            htmlBuilder.AppendLine(
                "            chiSquaredData.forEach(item => { const labels = item.intervals.map(i => `[${i.lower.toFixed(1)}, ${i.upper.toFixed(1)})`); createChart(item.chartId, { type: 'bar', data: { labels, datasets: [{ label: 'Наблюдаемые', data: item.intervals.map(i => i.observed), backgroundColor: '#3498dbCC', borderWidth: 1 }, { label: 'Ожидаемые', data: item.intervals.map(i => i.expected), borderColor: '#e74c3c', borderWidth: 2, type: 'line', pointRadius: 4, pointBackgroundColor: '#e74c3c' }] }, options: { responsive: true, scales: { y: { beginAtZero: true } } } }); });");
            htmlBuilder.AppendLine("        }");
            htmlBuilder.AppendLine("    </script>");
            htmlBuilder.AppendLine("</body></html>");
            return htmlBuilder.ToString();
        }

        #endregion
        
        #region --- Генератор отчета по переходному периоду ---
        public static void GenerateTransientAnalysisReport(TransientAnalysisResult transientResult, SimulationResult singleResult, string fileName = "transient_analysis_report.html")
        {
            var html = GenerateTransientHtmlContent(transientResult, singleResult);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            File.WriteAllText(fullPath, html, Encoding.UTF8);
            Console.WriteLine($"\nОтчет по анализу переходного периода успешно сгенерирован: {fullPath}");
        }
        private static string GenerateTransientHtmlContent(TransientAnalysisResult transientResult, SimulationResult singleResult)
        {
            var culture = CultureInfo.InvariantCulture;
            var perCustSystemData = SerializeTimeValuePoints(singleResult.PerCustomerSystemTime, culture);

            // *** ИСПРАВЛЕНИЕ ЗДЕСЬ ***
            // Используем экспоненциальное среднее (EMA), чтобы линия начиналась с самого начала, как на вашем примере
            var perCustSystemMaJson = SerializeTimeValuePoints(CalculateExponentialMovingAverage(singleResult.PerCustomerSystemTime, 50), culture);

            var htmlBuilder = new StringBuilder();
            htmlBuilder.AppendLine("<!DOCTYPE html><html lang=\"ru\"><head>");
            htmlBuilder.AppendLine("<meta charset=\"UTF-8\"><title>Анализ переходного периода симуляции</title>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
            htmlBuilder.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.1.1/dist/chartjs-plugin-zoom.min.js\"></script>");
            GenerateCommonStyles(htmlBuilder);
            htmlBuilder.AppendLine("</head><body><div class=\"container\">");
            htmlBuilder.AppendLine("    <h1>📈 1.4 Анализ переходного периода и сокращения времени прогона</h1>");
            htmlBuilder.AppendLine("    <p style='text-align: center;'>Цель: Проверить гипотезу о том, что среднее значение отклика ('Среднее время в системе') не изменится, если сократить время симуляции, исключив начальный переходный период.</p>");
            htmlBuilder.AppendLine("    <h3>Шаг 1: Визуальное определение переходного периода</h3>");
            htmlBuilder.AppendLine("    <p style='text-align: center;'>На графике ниже показана динамика времени в системе для каждого клиента в течение одного длительного прогона. Визуально можно оценить, когда система стабилизируется (когда оранжевая линия скользящего среднего перестает показывать явный тренд).</p>");
            htmlBuilder.AppendLine("    <div class=\"grid-container\" style=\"grid-template-columns: 1fr;\"><div class=\"chart-container\"><h3>Динамика отклика 'Время в системе'</h3><canvas id=\"transientTimeSeriesChart\"></canvas></div></div>");
            htmlBuilder.AppendLine("    <h3>Шаг 2 и 3: Статистическая проверка гипотезы</h3>");
            htmlBuilder.AppendLine($"    <div class=\"result-block\"><h4>Параметры эксперимента</h4><ul><li><b>Эталонное время (T₀):</b> {transientResult.T0} мин</li><li><b>Сокращенное время (T₁):</b> {transientResult.T1} мин</li><li><b>Количество репликаций (n):</b> {transientResult.Replications}</li></ul></div>");
            htmlBuilder.AppendLine("    <div class=\"grid-container\">");
            htmlBuilder.AppendLine("        <div class=\"result-block\"><h4>Результаты F-теста (проверка равенства дисперсий)</h4><table><tr><th>Параметр</th><th>Значение</th></tr>" +
                                     $"<tr><td>F-статистика</td><td>{transientResult.FTest.FStatistic:F4}</td></tr>" +
                                     $"<tr><td>F-критическое (df1={transientResult.FTest.Df1}, df2={transientResult.FTest.Df2})</td><td>{transientResult.FTest.FCritical:F4}</td></tr>" +
                                     $"<tr><td><b>Вывод</b></td><td><b>Дисперсии {(transientResult.FTest.VariancesAreEqual ? "статистически РАВНЫ" : "статистически НЕ РАВНЫ")}</b></td></tr></table></div>");
            htmlBuilder.AppendLine("        <div class=\"result-block\"><h4>Результаты t-теста (проверка равенства средних)</h4>" +
                                     $"<p><i>Тип критерия: {transientResult.TTest.TestType}</i></p><table><tr><th>Параметр</th><th>Значение</th></tr>" +
                                     $"<tr><td>|t-статистика|</td><td>{transientResult.TTest.TStatistic:F4}</td></tr>" +
                                     $"<tr><td>t-критическое</td><td>{transientResult.TTest.TCritical:F4}</td></tr>" +
                                     $"<tr><td><b>Вывод</b></td><td><b>Средние {(transientResult.TTest.MeansAreEqual ? "статистически РАВНЫ" : "статистически НЕ РАВНЫ")}</b></td></tr></table></div>");
            htmlBuilder.AppendLine("    </div>");
            htmlBuilder.AppendLine($"    <div class=\"result-block {(transientResult.TTest.MeansAreEqual ? "result-accepted" : "result-rejected")}\"><h4>Итоговое заключение</h4><p>{transientResult.Conclusion}</p></div>");
            htmlBuilder.AppendLine("    <h3>Сравнение распределений выборок</h3>");
            htmlBuilder.AppendLine("    <div class=\"grid-container\">");
            htmlBuilder.AppendLine($"        <div class=\"chart-container\"><h3>Гистограмма откликов для T₀ = {transientResult.T0}</h3><canvas id=\"transientHistT0\"></canvas></div>");
            htmlBuilder.AppendLine($"        <div class=\"chart-container\"><h3>Гистограмма откликов для T₁ = {transientResult.T1}</h3><canvas id=\"transientHistT1\"></canvas></div>");
            htmlBuilder.AppendLine("    </div></div>");
            htmlBuilder.AppendLine("<script>");
            htmlBuilder.AppendLine("    const commonInteraction = { interaction: { mode: 'index', intersect: false }, plugins: { zoom: { pan: { enabled: true, mode: 'x' }, zoom: { wheel: { enabled: true }, pinch: { enabled: true }, mode: 'x' } } } };");
            htmlBuilder.AppendLine("    const createChart = (id, config) => new Chart(document.getElementById(id), config);");
            htmlBuilder.AppendLine("    const createScatterChart = (canvasId, scatterData, maData, scatterLabel, color, title) => { const datasets = [{ type: 'scatter', label: scatterLabel, data: scatterData, backgroundColor: color + '99', pointRadius: 3 }]; if (maData && maData.length > 0) { datasets.push({ type: 'line', label: 'Среднее скользящее', data: maData, borderColor: '#e67e22', fill: false, pointRadius: 0, borderWidth: 2, tension: 0.3 }); } createChart(canvasId, { type: 'scatter', data: { datasets: datasets }, options: { ...commonInteraction, responsive: true, plugins: { legend: { position: 'top' }, title: { display: true, text: title } }, scales: { x: { type: 'linear', position: 'bottom', title: { display: true, text: 'Время симуляции (мин)' } }, y: { beginAtZero: true } } } }); };");
            htmlBuilder.AppendLine($"    createScatterChart('transientTimeSeriesChart', [{perCustSystemData}], [{perCustSystemMaJson}], 'Время в системе (по клиентам)', '#34495e');");
            htmlBuilder.AppendLine("    const createHistogram = (canvasId, rawData, label, color) => { if (!rawData || rawData.length === 0) return; const values = rawData.sort((a, b) => a - b); const min = values[0], max = values[values.length - 1]; const binCount = Math.ceil(1 + 3.322 * Math.log10(values.length)); const binWidth = (max - min) / binCount; const bins = Array(binCount).fill(0); const labels = []; for (let i = 0; i < binCount; i++) { labels.push(`[${(min + i * binWidth).toFixed(1)}, ${(min + (i + 1) * binWidth).toFixed(1)})`); } values.forEach(v => { let binIndex = Math.floor((v - min) / binWidth); if (binIndex >= binCount) binIndex = binCount - 1; bins[binIndex]++; }); createChart(canvasId, { type: 'bar', data: { labels, datasets: [{ label, data: bins, backgroundColor: color + 'CC' }] }, options: { responsive: true, scales: { x: { title: { display: true, text: 'Значение отклика (мин)' }}, y: { beginAtZero: true, title: { display: true, text: 'Частота' }} } } }); }");
            htmlBuilder.AppendLine($"    const dataT0 = {SerializeToJson(transientResult.DataSample0)}; const dataT1 = {SerializeToJson(transientResult.DataSample1)};");
            htmlBuilder.AppendLine($"    createHistogram('transientHistT0', dataT0, 'Распределение для T₀={transientResult.T0}', '#3498db');");
            htmlBuilder.AppendLine($"    createHistogram('transientHistT1', dataT1, 'Распределение для T₁={transientResult.T1}', '#e74c3c');");
            htmlBuilder.AppendLine("</script></body></html>");
            return htmlBuilder.ToString();
        }
        #endregion

        #region --- Общие вспомогательные методы ---
        private static void GenerateCommonStyles(StringBuilder sb)
        {
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: 'Segoe UI', sans-serif; margin: 0; padding: 20px; background: #f0f2f5; color: #333; }");
            sb.AppendLine("        .container { max-width: 1800px; margin: auto; background: white; border-radius: 20px; padding: 30px; box-shadow: 0 10px 30px rgba(0,0,0,0.1); }");
            sb.AppendLine("        h1, h2 { text-align: center; color: #2c3e50; }");
            sb.AppendLine("        h2 { margin-top: 50px; border-top: 1px solid #ddd; padding-top: 30px; }");
            sb.AppendLine("        h3, h4 { color: #34495e; }");
            sb.AppendLine("        .grid-container { display: grid; grid-template-columns: repeat(auto-fit, minmax(400px, 1fr)); gap: 25px; margin-bottom: 40px; }");
            sb.AppendLine("        .stat-item, .chart-container { background: #fff; padding: 25px; border-radius: 15px; box-shadow: 0 5px 15px rgba(0,0,0,0.05); text-align: center; }");
            sb.AppendLine("        .stat-item h4, .chart-container h3 { margin: 0 0 15px; }");
            sb.AppendLine("        .stat-value { font-size: 2.2em; font-weight: bold; color: #e74c3c; }");
            sb.AppendLine("        .stat-deviation { font-size: 0.9em; color: #7f8c8d; margin-top: 5px; }");
            sb.AppendLine("        .result-block { background: #f8f9fa; border-left: 4px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px;}");
            sb.AppendLine("        .result-accepted { border-left-color: #27ae60; } .result-rejected { border-left-color: #e74c3c; }");
            sb.AppendLine("        table { width: 100%; border-collapse: collapse; margin-top: 15px; } th, td { border: 1px solid #ddd; padding: 8px; text-align: left; } th { background-color: #f2f2f2; }");
            sb.AppendLine("        .chi-squared-result { background: #f8f9fa; border-left: 4px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px;}");
            sb.AppendLine("        .chi-squared-accepted { border-left-color: #27ae60; } .chi-squared-rejected { border-left-color: #e74c3c; }");
            sb.AppendLine("    </style>");
        }
        private static string SerializeToJson<T>(T data) => JsonSerializer.Serialize(data);
        private static string SerializeTimeValuePoints(List<TimeValuePoint> data, CultureInfo culture)
        {
            if (data == null || !data.Any()) return "";
            var points = data.Select(d => $"{{x:{d.Time.ToString("F2", culture)},y:{d.Value.ToString("F2", culture)}}}");
            return string.Join(",", points);
        }
        private static List<TimeValuePoint> CalculateExponentialMovingAverage(List<TimeValuePoint> data, int period)
        {
            if (data == null || !data.Any()) return new List<TimeValuePoint>();
            var result = new List<TimeValuePoint>();
            double alpha = 2.0 / (period + 1);
            double previousEma = data.First().Value;
            result.Add(new TimeValuePoint(data.First().Time, previousEma));
            foreach (var point in data.Skip(1))
            {
                double currentEma = alpha * point.Value + (1 - alpha) * previousEma;
                result.Add(new TimeValuePoint(point.Time, currentEma));
                previousEma = currentEma;
            }
            return result;
        }
        private static List<TimeValuePoint> CalculateMovingAverage(List<TimeValuePoint> data, int windowSize)
        {
            if (data == null || data.Count < windowSize || windowSize <= 0) return new List<TimeValuePoint>();
            var result = new List<TimeValuePoint>();
            var windowValues = new Queue<double>();
            double sum = 0;
            foreach (var point in data)
            {
                windowValues.Enqueue(point.Value);
                sum += point.Value;
                if (windowValues.Count > windowSize) sum -= windowValues.Dequeue();
                if (windowValues.Count == windowSize) {
                    result.Add(new TimeValuePoint(point.Time, sum / windowSize));
                }
            }
            return result;
        }
        private static string SerializeChiSquaredResults(List<ChiSquaredMetricTestResult> results)
        {
            var payload = results.Select((res, index) => new
            {
                chartId = $"chiSquaredHistogramChart{index}",
                metric = res.MetricName,
                mean = res.Result.Mean,
                stdDev = res.Result.StdDev,
                chiSquared = res.Result.ChiSquaredStatistic,
                critical = res.Result.CriticalValue,
                df = res.Result.DegreesOfFreedom,
                isNormal = res.Result.IsNormal,
                intervals = res.Result.Intervals.Select(i => new
                {
                    lower = i.LowerBound,
                    upper = i.UpperBound,
                    observed = i.ObservedFrequency,
                    expected = i.ExpectedFrequency
                })
            });

            return JsonSerializer.Serialize(payload);
        }
        private static string SerializeChiSquaredComparison(ChiSquaredTestResult res, CultureInfo c) => $"{{'calculated':{res.ChiSquaredStatistic.ToString("F4", c)},'critical':{res.CriticalValue.ToString("F4", c)},'isNormal':{res.IsNormal.ToString().ToLower()},'degreesOfFreedom':{res.DegreesOfFreedom}}}";
        private static string GenerateChiSquaredDistributionData(int df, double calc, double crit)
        {
            var data = new List<string>();
            double maxX = Math.Max(crit * 1.5, calc * 1.2);
            maxX = Math.Max(maxX, df * 2.0);
            maxX = Math.Max(maxX, 15);
            for (double x = 0; x <= maxX; x += maxX / 200.0)
            {
                data.Add($"{{x:{x.ToString("F2", CultureInfo.InvariantCulture)},y:{ChiSquaredPDF(x, df).ToString("F5", CultureInfo.InvariantCulture)}}}");
            }
            return string.Join(",", data);
        }
        private static double ChiSquaredPDF(double x, int k)
        {
            if (x < 0 || k <= 0) return 0;
            double gamma = GammaFunction(k / 2.0);
            if (double.IsInfinity(gamma) || gamma == 0) return 0;
            return (1.0 / (Math.Pow(2, k / 2.0) * gamma)) * Math.Pow(x, k / 2.0 - 1) * Math.Exp(-x / 2.0);
        }
        private static double GammaFunction(double z)
        {
            if (z < 0.5) return Math.PI / (Math.Sin(Math.PI * z) * GammaFunction(1 - z));
            z -= 1;
            double x = 0.99999999999980993;
            double[] p = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            for (int i = 0; i < 8; i++) x += p[i] / (z + i + 1);
            double t = z + 8 - 0.5;
            return Math.Sqrt(2 * Math.PI) * Math.Pow(t, z + 0.5) * Math.Exp(-t) * x;
        }
        #endregion
    }
}