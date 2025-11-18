using Xunit.Abstractions;

namespace WholesaleStoreSimulation.Tests
{
    public class SimulationComparisonTests
    {
        private readonly ITestOutputHelper _output;

        public SimulationComparisonTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Сравнительный тест: Увеличение числа клерков с 1 до 3.
        /// Гипотеза: Добавление клерков должно значительно улучшить показатели обслуживания.
        /// </summary>
        [Fact(DisplayName = "1 -> 3 клерка: уменьшение ожидания и очереди")]
        public void AddingClerks_ShouldReduceWaitTimeAndQueueLength()
        {
            // ARRANGE

            // 1. Создаем базовую конфигурацию с одним клерком (создаем "бутылочное горлышко")
            var cfgBase = new SimulatorConfig
            {
                MeanInterArrivalTime = 0.5, // Интенсивный поток клиентов, чтобы 1 клерк не справлялся
                ClerkCount = 1, // <--- Ключевой параметр для этого теста
                ClerkBatchSize = 3
            };

            // 2. Создаем улучшенную конфигурацию с тремя клерками
            // Используем тот же seed, чтобы поток клиентов был идентичным!
            var cfgImproved = new SimulatorConfig
            {
                MeanInterArrivalTime = 0.5,
                ClerkCount = 3, // <--- Увеличиваем число клерков
                ClerkBatchSize = 3
            };

            // ACT

            // Запускаем симуляцию для базового сценария
            var resultBase = RunSimulation(cfgBase);

            // Запускаем симуляцию для улучшенного сценария
            var resultImproved = RunSimulation(cfgImproved);

            _output.WriteLine($"1 Клерк:  Ожидание={resultBase.AverageWaitingTime:F2}, Очередь={resultBase.AverageQueueLength:F2}, Обслужено={resultBase.CustomersServed}");
            _output.WriteLine($"3 Клерка: Ожидание={resultImproved.AverageWaitingTime:F2}, Очередь={resultImproved.AverageQueueLength:F2}, Обслужено={resultImproved.CustomersServed}");

            // ASSERT

            Assert.True(resultImproved.AverageWaitingTime < resultBase.AverageWaitingTime, "Среднее время ожидания должно было уменьшиться.");
            Assert.True(resultImproved.AverageSystemTime < resultBase.AverageSystemTime, "Среднее время в системе должно было уменьшиться.");
            Assert.True(resultImproved.AverageQueueLength < resultBase.AverageQueueLength, "Средняя длина очереди должна была уменьшиться.");
            Assert.True(resultImproved.CustomersServed >= resultBase.CustomersServed, "Количество обслуженных клиентов должно было вырасти или остаться прежним.");
        }

        /// <summary>
        /// Сравнительный тест: Увеличение размера партии с 1 до 6.
        /// Гипотеза: Пакетная обработка повышает эффективность клерков за счет сокращения числа походов на склад.
        /// </summary>
        [Fact(DisplayName = "Размер партии 1 vs 6: влияние на загрузку клерков")]
        public void IncreasingBatchSize_ShouldImproveClerkUtilization()
        {
            // ARRANGE

            // 1. Базовая конфигурация: клерки ходят за каждым клиентом индивидуально
            var cfgBase = new SimulatorConfig
            {
                MeanInterArrivalTime = 3.0, // Менее интенсивный поток, чтобы партии успевали формироваться
                ClerkCount = 2,
                ClerkBatchSize = 1 // <--- Ключевой параметр: нет пакетной обработки
            };

            // 2. Улучшенная конфигурация: клерки используют пакетную обработку
            var cfgImproved = new SimulatorConfig
            {
                MeanInterArrivalTime = 3.0,
                ClerkCount = 2,
                ClerkBatchSize = 6 // <--- Ключевой параметр: есть пакетная обработка
            };

            // ACT
            var resultBase = RunSimulation(cfgBase);
            var resultImproved = RunSimulation(cfgImproved);

            // Считаем среднюю загрузку по всем клеркам
            double avgUtilizationBase = resultBase.ClerkUtilizations.Average();
            double avgUtilizationImproved = resultImproved.ClerkUtilizations.Average();

            _output.WriteLine("--- Сравнение: Размер партии 1 vs 6 ---");
            _output.WriteLine($"Партия=1: Средняя загрузка={avgUtilizationBase:P2}, Ожидание={resultBase.AverageWaitingTime:F2}");
            _output.WriteLine($"Партия=6: Средняя загрузка={avgUtilizationImproved:P2}, Ожидание={resultImproved.AverageWaitingTime:F2}");
            _output.WriteLine("(Примечание: время ожидания может вырасти из-за необходимости ждать формирования полной партии)");

            // ASSERT

            // Проверяем главный эффект от пакетной обработки - повышение эффективности (снижение загрузки)
            Assert.True(avgUtilizationImproved < avgUtilizationBase, "Средняя загрузка клерков должна была уменьшиться с введением пакетной обработки.");
        }

        /// <summary>
        /// Сравнительный тест: Увеличение интенсивности потока клиентов.
        /// Гипотеза: Более частые прибытия клиентов создают нагрузку на систему, ухудшая все показатели.
        /// </summary>
        [Fact(DisplayName = "Низкая vs Высокая интенсивность: ухудшение метрик")]
        public void IncreasingArrivalRate_ShouldWorsenAllMetrics()
        {
            // ARRANGE
            const int sharedSeed = 789;
            var cfgBase = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                MeanInterArrivalTime = 2.0, // <--- Низкая интенсивность
            };
            var cfgStressed = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                MeanInterArrivalTime = 0.5, // <--- Высокая интенсивность
            };

            // ACT
            var resultBase = RunSimulation(cfgBase);
            var resultStressed = RunSimulation(cfgStressed);

            _output.WriteLine("--- Сравнение: Низкая vs Высокая интенсивность потока ---");
            PrintComparison("Низкая ", resultBase);
            PrintComparison("Высокая", resultStressed);

            // ASSERT
            Assert.True(resultStressed.AverageWaitingTime > resultBase.AverageWaitingTime, "Время ожидания должно было увеличиться.");
            Assert.True(resultStressed.AverageQueueLength > resultBase.AverageQueueLength, "Длина очереди должна была увеличиться.");
            Assert.True(resultStressed.ClerkUtilizations.Average() > resultBase.ClerkUtilizations.Average(), "Загрузка клерков должна была увеличиться.");
        }

        /// <summary>
        /// Сравнительный тест: Увеличение времени выбора товаров клиентами.
        /// Гипотеза: Клиенты проводят больше времени в магазине в целом, но это может не ухудшить (и даже улучшить) ситуацию в очереди,
        /// так как они подходят к ней более размеренно.
        /// </summary>
        [Fact(DisplayName = "Долгое выбор товара: влияние на время в системе")]
        public void LongerCatalogSelection_ShouldIncreaseSystemTime()
        {
            // ARRANGE
            const int sharedSeed = 101;
            var cfgBase = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                CatalogSelectionMin = 2.0,
                CatalogSelectionMax = 5.0, // <--- "Быстрые" клиенты
            };
            var cfgSlowCustomers = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                CatalogSelectionMin = 15.0,
                CatalogSelectionMax = 20.0, // <--- "Медленные" клиенты
            };

            // ACT
            var resultBase = RunSimulation(cfgBase);
            var resultSlow = RunSimulation(cfgSlowCustomers);

            _output.WriteLine("--- Сравнение: Быстрый vs Медленный выбор товаров ---");
            PrintComparison("Быстрый выбор", resultBase);
            PrintComparison("Медленный выбор", resultSlow);

            // ASSERT
            Assert.True(resultSlow.AverageSystemTime > resultBase.AverageSystemTime, "Общее время в системе должно было вырасти.");
        }

        /// <summary>
        /// Сравнительный тест: Увеличение времени расчета одного клиента.
        /// Гипотеза: Это "узкое место" в конце процесса обслуживания. Должно резко ухудшить все показатели очереди.
        /// </summary>
        [Fact(DisplayName = "Медленный расчёт: ухудшение метрик очереди")]
        public void SlowerCheckout_ShouldWorsenQueueMetrics()
        {
            // ARRANGE
            const int sharedSeed = 202;
            var cfgBase = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                CheckoutTimeMin = 1.0,
                CheckoutTimeMax = 2.0,
            };
            var cfgSlowCheckout = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                CheckoutTimeMin = 5.0,
                CheckoutTimeMax = 6.0,
            };

            // ACT
            var resultBase = RunSimulation(cfgBase);
            var resultSlow = RunSimulation(cfgSlowCheckout);

            _output.WriteLine("--- Сравнение: Быстрый vs Медленный расчет ---");
            PrintComparison("Быстрый расчет", resultBase);
            PrintComparison("Медленный расчет", resultSlow);

            // ASSERT
            Assert.True(resultSlow.AverageWaitingTime > resultBase.AverageWaitingTime, "Время ожидания должно было резко увеличиться.");
            Assert.True(resultSlow.AverageQueueLength > resultBase.AverageQueueLength, "Длина очереди должна была резко увеличиться.");
            Assert.True(resultSlow.CustomersServed < resultBase.CustomersServed, "Пропускная способность (обслужено клиентов) должна была упасть.");
        }
        
        /// <summary>
        /// Сравнительный тест: Увеличение времени похода на склад.
        /// Гипотеза: Замедление логистики (поход на склад) должно увеличить время обслуживания и снизить пропускную способность.
        /// </summary>
        [Fact(DisplayName = "Быстрый vs Медленный склад: очередь и пропускная способность")]
        public void SlowerWarehouseTrip_ShouldWorsenQueueMetricsAndThroughput()
        {
            const int sharedSeed = 303;
            var cfgBase = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                WarehouseTripMin = 5.0, WarehouseTripMode = 6.0, WarehouseTripMax = 7.0 // Быстрый склад
            };
            var cfgSlowWarehouse = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                WarehouseTripMin = 20.0, WarehouseTripMode = 25.0, WarehouseTripMax = 30.0 // Медленный склад
            };
            
            var resultBase = RunSimulation(cfgBase);
            var resultSlow = RunSimulation(cfgSlowWarehouse);

            _output.WriteLine("--- Сравнение: Быстрый vs Медленный склад ---");
            PrintComparison("Быстрый склад", resultBase);
            PrintComparison("Медленный склад", resultSlow);

            Assert.True(resultSlow.AverageWaitingTime > resultBase.AverageWaitingTime, "Время ожидания должно было увеличиться.");
            Assert.True(resultSlow.AverageSystemTime > resultBase.AverageSystemTime, "Время в системе должно было увеличиться.");
            Assert.True(resultSlow.CustomersServed < resultBase.CustomersServed, "Пропускная способность (обслужено клиентов) должна была упасть.");
        }
        
        /// <summary>
        /// Сравнительный тест: Увеличение длительности симуляции.
        /// Гипотеза: При увеличении времени работы общее число обслуженных клиентов должно вырасти,
        /// а средние показатели (время ожидания, загрузка) должны оставаться относительно стабильными.
        /// </summary>
        [Fact(DisplayName = "8 ч vs 12 ч: общее число обслуженных клиентов")]
        public void LongerSimulationDuration_ShouldIncreaseTotalServedCustomers()
        {
            // ARRANGE
            const int sharedSeed = 42;
            
            // Базовый сценарий: стандартная смена
            var cfgBase = new SimulatorConfig
            {
                RandomSeed = sharedSeed,
                SimulationDuration = 480.0 // 8 часов
            };

            // Сценарий сравнения: работа в полторы смены
            var cfgLonger = new SimulatorConfig
            {
                RandomSeed = sharedSeed, // Тот же seed
                SimulationDuration = 720.0 // 12 часов
            };

            // ACT
            var resultBase = RunSimulation(cfgBase);
            var resultLonger = RunSimulation(cfgLonger);

            _output.WriteLine("--- Сравнение: 8 часов vs 12 часов работы ---");
            PrintComparison(" 8 часов", resultBase);
            PrintComparison("12 часов", resultLonger);

            // ASSERT
            Assert.True(resultLonger.CustomersServed > resultBase.CustomersServed, "За большее время должно быть обслужено больше клиентов.");
            // Проверяем, что среднее время ожидания не изменилось кардинально, что говорит о стабильности системы
            // (погрешность 50% на случай стохастических выбросов)
            Assert.InRange(resultLonger.AverageWaitingTime, resultBase.AverageWaitingTime * 0.5, resultBase.AverageWaitingTime * 1.5);
        }
        
        /// <summary>
        /// Инвариант: среднее время в системе >= среднее время ожидания (system = waiting + service).
        /// </summary>
        [Fact(DisplayName = "Инвариант: Время в системе >= Время ожидания")]
        public void AverageSystemTime_ShouldBeAtLeastAverageWaitingTime()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 123,
                SimulationDuration = 480.0,
                MeanInterArrivalTime = 0.8,
                ClerkCount = 2
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"AvgSystem={res.AverageSystemTime:F3}, AvgWait={res.AverageWaitingTime:F3}");
            Assert.True(res.AverageSystemTime + 1e-9 >= res.AverageWaitingTime, "Среднее время в системе должно быть не меньше среднего времени ожидания.");
        }
        
        /// <summary>
        /// Инвариант: количество обслуженных не может превышать количество прибывших.
        /// </summary>
        [Fact(DisplayName = "Обслужено <= Пришло")]
        public void CustomersServed_CannotExceed_CustomersArrived()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 222,
                SimulationDuration = 240.0,
                MeanInterArrivalTime = 0.1 // много клиентов
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"Arrived={res.CustomersArrived}, Served={res.CustomersServed}");
            Assert.True(res.CustomersServed <= res.CustomersArrived, "Обслужено не может быть больше, чем прибыло.");
        }
        
        /// <summary>
        /// Инвариант: max queue length не может быть отрицательным и не превосходит общего количества пришедших клиентов.
        /// </summary>
        [Fact(DisplayName = "MaxQueueLength: неотрицательная и не больше прибывших")]
        public void MaxQueueLength_ShouldBeReasonable()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 333,
                SimulationDuration = 300.0,
                MeanInterArrivalTime = 0.2
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"MaxQueue={res.MaxQueueLength}, Arrived={res.CustomersArrived}");
            Assert.True(res.MaxQueueLength >= 0, "MaxQueueLength не может быть отрицательным.");
            Assert.True(res.MaxQueueLength <= res.CustomersArrived, "MaxQueueLength не может превышать количество прибывших клиентов.");
        }
        
        /// <summary>
        /// Граничный случай: нулевой шаг симуляции — всё должно быть нулями / пустым.
        /// </summary>
        [Fact(DisplayName = "Нулевая длительность симуляции -> нулевые метрики")]
        public void ZeroSimulationDuration_ShouldProduceZeroMetrics()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 555,
                SimulationDuration = 0.0001, // ничего не должно происходить
                MeanInterArrivalTime = 1.0,
                ClerkCount = 2
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"Arrived={res.CustomersArrived}, Served={res.CustomersServed}, AvgQueue={res.AverageQueueLength:F3}");
            Assert.Equal(0, res.CustomersArrived);
            Assert.Equal(0, res.CustomersServed);
            Assert.Equal(0.0, res.AverageQueueLength, 6);
            Assert.Equal(0.0, res.AverageWaitingTime);
            Assert.All(res.ClerkUtilizations, u => Assert.Equal(0.0, u));
        }
        
        /// <summary>
        /// Сценарий: почти никаких прибытий (mean inter-arrival > duration) — система простаивает.
        /// Ожидаем: обслужено 0, загрузки 0, средняя очередь 0.
        /// </summary>
        [Fact(DisplayName = "Нет прибытий -> система простаивает")]
        public void NoArrivals_DuringSimulation_ShouldBeIdle()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 666,
                SimulationDuration = 60.0,
                MeanInterArrivalTime = 1000.0, // очень редкие клиенты: ни одного за время симуляции
                ClerkCount = 3
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"Arrived={res.CustomersArrived}, Served={res.CustomersServed}");
            Assert.Equal(0, res.CustomersServed);
            Assert.Equal(0, res.CustomersArrived);
            Assert.Equal(0.0, res.AverageQueueLength);
            Assert.All(res.ClerkUtilizations, u => Assert.Equal(0.0, u));
        }
        
        /// <summary>
        /// Регрессионный тест: при единичном размере партии (ClerkBatchSize = 6) поведение должно быть рабочим:
        /// </summary>
        [Fact(DisplayName = "ClerkBatchSize = 6: поведение при низкой нагрузке")]
        public void BatchSizeOne_BehaviorUnderLowLoad()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 777,
                SimulationDuration = 240.0,
                MeanInterArrivalTime = 10.0, // низкая интенсивность — партия успевает не скапливаться
                ClerkCount = 2,
                ClerkBatchSize = 6
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"Arrived={res.CustomersArrived}, Served={res.CustomersServed}, AvgWait={res.AverageWaitingTime:F3}");
            // При низкой нагрузке ожидание должно быть небольшим (на уровне 0..checkout time)
            Assert.True(res.AverageWaitingTime < 10.0, "При низкой интенсивности среднее время ожидания должно быть небольшим.");
            Assert.True(res.AverageSystemTime >= res.AverageWaitingTime);
        }
        
        /// <summary>
        /// Инвариант: AverageQueueLength в разумных пределах [0, MaxQueueLength]
        /// (проверяем, что агрегатная метрика соответствует ожидаемым границам).
        /// </summary>
        [Fact(DisplayName = "AverageQueueLength в пределах [0, MaxQueueLength]")]
        public void AverageQueueLength_IsBetweenZeroAndMaxQueueLengthPlusEps()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 888,
                SimulationDuration = 480.0,
                MeanInterArrivalTime = 0.3,
                ClerkCount = 3
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"AvgQueue={res.AverageQueueLength:F3}, MaxQueue={res.MaxQueueLength}");
            Assert.True(res.AverageQueueLength >= -1e-9, "AverageQueueLength не может быть отрицательной.");
            Assert.True(res.AverageQueueLength <= res.MaxQueueLength + 1e-6, "AverageQueueLength не должна быть существенно больше MaxQueueLength.");
        }
        
        [Fact(DisplayName = "Детерминизм: одинаковые seed'ы и репликации -> одинаковые агрегаты")]
        public void Determinism_SameSeed_SameResults()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 999,
                SimulationDuration = 300.0,
                MeanInterArrivalTime = 1.0,
                ClerkCount = 2
            };

            var agg1 = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications: 5, trace: false);
            var agg2 = WholesaleStoreSimulator.VerifyWithReplications(cfg, replications: 5, trace: false);

            _output.WriteLine($"agg1.MeanCustomersServed={agg1.MeanCustomersServed}, agg2.MeanCustomersServed={agg2.MeanCustomersServed}");
            // Ожидаем точное совпадение, т.к. Используются те же seed'ы и алгоритм детерминирован
            Assert.Equal(agg1.MeanCustomersServed, agg2.MeanCustomersServed);
            Assert.Equal(agg1.MeanAvgWait, agg2.MeanAvgWait);
            Assert.Equal(agg1.MeanAvgQueueLength, agg2.MeanAvgQueueLength);
        }
        
        [Fact(DisplayName = "Учет частичного времени занятости клерка в конце симуляции")]
        public void ClerkBusyAtSimulationEnd_IsCountedPartially()
        {
            // Настраиваем очень долгий поход на склад, чтобы клерк ушёл до конца и остался занятым
            var cfg = new SimulatorConfig
            {
                RandomSeed = 42,
                SimulationDuration = 10.0,
                MeanInterArrivalTime = 0.2, // достаточно входящих, чтобы сформировать партию
                ClerkCount = 1,
                ClerkBatchSize = 2,
                WarehouseTripMin = 50.0,
                WarehouseTripMode = 60.0,
                WarehouseTripMax = 70.0
            };

            var res = RunSimulation(cfg);

            _output.WriteLine($"Clerk utilization[0]={res.ClerkUtilizations.First():F6}, CustomersServed={res.CustomersServed}");
            Assert.True(res.ClerkUtilizations.Length == cfg.ClerkCount);
            // Ожидаем, что время занятости > 0, т.к. клерк ушёл на длительный склад и был занят в конце прогонки
            Assert.True(res.ClerkUtilizations[0] > 0.0, "Частичное время занятости клерка должно учитываться.");
        }
        
        [Fact(DisplayName = "Метрики неотрицательны")]
        public void Metrics_AreNonNegative()
        {
            var cfg = new SimulatorConfig
            {
                RandomSeed = 1357,
                SimulationDuration = 200.0,
                MeanInterArrivalTime = 0.5,
                ClerkCount = 3
            };

            var res = RunSimulation(cfg);

            Assert.True(res.AverageWaitingTime >= -1e-9);
            Assert.True(res.AverageSystemTime >= -1e-9);
            Assert.True(res.AverageQueueLength >= -1e-9);
            Assert.True(res.CustomersServed >= 0);
            Assert.True(res.CustomersArrived >= 0);
        }

        /// <summary>
        /// Вспомогательный метод для запуска симуляции.
        /// </summary>
        private SimulationResult RunSimulation(SimulatorConfig cfg)
        {
            var simulator = new WholesaleStoreSimulator(cfg);
            simulator.Run();
            return simulator.GetResult();
        }
        
        /// <summary>
        /// Вспомогательный метод для красивого вывода результатов.
        /// </summary>
        private void PrintComparison(string scenarioName, SimulationResult result)
        {
            _output.WriteLine(
                $"{scenarioName}: " +
                $"Ожидание={result.AverageWaitingTime:F2}, " +
                $"Очередь={result.AverageQueueLength:F2}, " +
                $"В системе={result.AverageSystemTime:F2}, " +
                $"Загрузка={result.ClerkUtilizations.Average():P2}, " +
                $"Обслужено={result.CustomersServed}");
        }
    }
}