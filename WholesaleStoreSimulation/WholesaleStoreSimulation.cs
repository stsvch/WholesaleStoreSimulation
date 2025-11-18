using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WholesaleStoreSimulation
{
// Event types for discrete event simulation
public enum EventType
{
CustomerArrival,
CustomerCatalogSelectionComplete,
ClerkAssignment,
ClerkReturnFromWarehouse,
CustomerCheckoutComplete
}

// Simulation event
public class SimulationEvent
{
    public long Sequence { get; set; }
    public double Time { get; set; }
    public EventType Type { get; set; }
    public Customer Customer { get; set; }
    public Clerk Clerk { get; set; }

    public override string ToString() => $"[{Time:0.##}] {Type} (Seq={Sequence})";
}

// Customer model
public class Customer
{
    public int Id { get; set; }
    public double ArrivalTime { get; set; }
    public double CatalogSelectionDuration { get; set; }
    public double WaitStartTime { get; set; } = -1;
    public double ServiceStartTime { get; set; } = -1;
    public double DepartureTime { get; set; } = -1;
    public Clerk AssignedClerk { get; set; }
}

// Clerk model
public class Clerk
{
    public int Id { get; set; }
    public bool IsBusy { get; set; } = false; // true while on trip or servicing returned customers
    public List<Customer> AssignedCustomers { get; } = new List<Customer>();
    public double? BusySince { get; set; } = null; // when clerk became busy (trip start)

    public int AssignedCount => AssignedCustomers.Count;
}

// Configuration container (can be loaded from JSON)
public class SimulatorConfig
{
    // Общие параметры симуляции
    public double SimulationDuration { get; set; } = 480.0; // minutes
    public int RandomSeed { get; set; } = 42;
    
    // Неуправляемые параметры (внешняя среда и процессы)
    public double MeanInterArrivalTime { get; set; } = 2.0; // λ = 0.5 клиентов в минуту
    
    // 2. Выбор товаров (Равномерное распределение)
    public double CatalogSelectionMin { get; set; } = 2.0;
    public double CatalogSelectionMax { get; set; } = 10.0;
    
    // 3. Поход на склад (Треугольное несимметричное распределение)
    public double WarehouseTripMin { get; set; } = 8.0;
    public double WarehouseTripMode { get; set; } = 12.0;
    public double WarehouseTripMax { get; set; } = 20.0;
    
    // 4. Расчет клиента (Равномерное симметричное распределение)
    public double CheckoutTimeMin { get; set; } = 1.0;
    public double CheckoutTimeMax { get; set; } = 3.0;
    
    // Управляемые параметры системы
    public int ClerkCount { get; set; } = 3;
    public int ClerkBatchSize { get; set; } = 6; // Capacity per clerk
}

// Структура для хранения точки "Время-Значение"
public record TimeValuePoint(double Time, double Value);

// Main simulator
public class WholesaleStoreSimulator
{
    private readonly SimulatorConfig _cfg;
    private readonly PriorityQueue<SimulationEvent, double> _eventQueue = new();
    private readonly List<Clerk> _clerks = new();
    private readonly Queue<Customer> _waitingCustomers = new();
    private readonly Random _rng;

    private double _currentTime = 0.0;
    private int _nextCustomerId = 1;
    private long _nextEventSeq = 1;
    private bool _traceMode = false;

    // Метрики
    private int _customersArrived = 0;
    private int _customersServed = 0;
    private double _totalWaitingTime = 0.0;
    private double _totalSystemTime = 0.0;
    private double[] _clerkBusyTime; // accumulated busy time per clerk
    private int _maxQueueLength = 0;
    // Для расчета средней длины очереди (непрерывный отклик)
    private double _integratedQueueLength = 0.0;
    private double _lastEventTime = 0.0;

    // Списки для сбора данных для графиков (по одному прогону)
    private readonly List<TimeValuePoint> _queueLengthHistory = new();
    private readonly List<TimeValuePoint> _catalogSelectionHistory = new();
    private readonly List<TimeValuePoint> _warehouseTripHistory = new();
    private readonly List<TimeValuePoint> _checkoutTimeHistory = new();
    private readonly List<TimeValuePoint> _interArrivalTimeHistory = new();

    // Новые: истории для выходных метрик
    private readonly List<TimeValuePoint> _clerkUtilizationHistory = new(); // значение 0..1
    private readonly List<TimeValuePoint> _customersServedHistory = new(); // (time, cumulative served)
    private readonly List<TimeValuePoint> _perCustomerSystemTime = new();  // (departureTime, systemTime)
    private readonly List<TimeValuePoint> _perCustomerWaitingTime = new(); // (departureTime, waitingTime)

    public WholesaleStoreSimulator(SimulatorConfig? cfg = null, bool trace = false)
    {
        _cfg = cfg ?? new SimulatorConfig();
        _traceMode = trace;
        _rng = new Random(_cfg.RandomSeed);
        _clerkBusyTime = new double[_cfg.ClerkCount]; 

        for (int i = 1; i <= _cfg.ClerkCount; i++)
            _clerks.Add(new Clerk { Id = i });
    }
    
    private double Uniform(double min, double max) => min + _rng.NextDouble() * (max - min);

    private double Triangular(double min, double mode, double max)
    {
        var u = _rng.NextDouble();
        var c = (mode - min) / (max - min);
        return u <= c
            ? min + Math.Sqrt(u * (max - min) * (mode - min))
            : max - Math.Sqrt((1 - u) * (max - min) * (max - mode));
    }

    // Public: run single replication
    public void Run()
    {
        // Начальные точки для графиков
        _queueLengthHistory.Add(new TimeValuePoint(0, 0));
        _clerkUtilizationHistory.Add(new TimeValuePoint(0, 0));
        _customersServedHistory.Add(new TimeValuePoint(0, 0));

        // initial arrival
        ScheduleEvent(new SimulationEvent
        {
            Time = Exponential(_cfg.MeanInterArrivalTime),
            Type = EventType.CustomerArrival
        });

        while (_eventQueue.TryPeek(out var nextEvent, out _) && nextEvent.Time <= _cfg.SimulationDuration)
        {
            var evt = _eventQueue.Dequeue();
            
            // Обновляем интегральную метрику длины очереди
            double timeSinceLastEvent = evt.Time - _lastEventTime;
            _integratedQueueLength += _waitingCustomers.Count * timeSinceLastEvent;
            
            // Запись состояния очереди и загрузки клерков до события (для непрерывных графиков)
            if (_queueLengthHistory.Last().Time < evt.Time)
            {
                _queueLengthHistory.Add(new TimeValuePoint(evt.Time, _waitingCustomers.Count));
            }
            var busyCount = _clerks.Count(c => c.IsBusy);
            if (_clerkUtilizationHistory.Last().Time < evt.Time)
            {
                _clerkUtilizationHistory.Add(new TimeValuePoint(evt.Time, _cfg.ClerkCount > 0 ? busyCount / (double)_cfg.ClerkCount : 0.0));
            }

            _currentTime = evt.Time;
            _lastEventTime = _currentTime;

            if (_traceMode)
            {
                Console.WriteLine($"Time {_currentTime:0.##}: Processing {evt.Type} for Customer:{evt.Customer?.Id}, Clerk:{evt.Clerk?.Id}");
            }

            switch (evt.Type)
            {
                case EventType.CustomerArrival:
                    HandleCustomerArrival();
                    break;
                case EventType.CustomerCatalogSelectionComplete:
                    HandleCatalogSelectionComplete(evt.Customer);
                    break;
                case EventType.ClerkAssignment:
                    HandleClerkAssignment();
                    break;
                case EventType.ClerkReturnFromWarehouse:
                    HandleClerkReturnFromWarehouse(evt.Clerk);
                    break;
                case EventType.CustomerCheckoutComplete:
                    HandleCustomerCheckoutComplete(evt.Customer, evt.Clerk);
                    break;
            }
            
            // Запись состояния очереди после события
            _queueLengthHistory.Add(new TimeValuePoint(_currentTime, _waitingCustomers.Count));
            _maxQueueLength = Math.Max(_maxQueueLength, _waitingCustomers.Count);
        }

        // finalize: in case clerks still busy at simulation end, account partial busy times up to simulation end
        FinalizeBusyTimesAtSimulationEnd();
    }

    private void HandleCustomerArrival()
    {
        _customersArrived++;
        var customer = new Customer { Id = _nextCustomerId++, ArrivalTime = _currentTime };

        if (_traceMode) Console.WriteLine($"  Customer {customer.Id} arrived.");

        // schedule catalog selection completion
        double selection = Uniform(_cfg.CatalogSelectionMin, _cfg.CatalogSelectionMax);
        _catalogSelectionHistory.Add(new TimeValuePoint(_currentTime, selection)); // Сбор данных
        customer.CatalogSelectionDuration = selection;
        ScheduleEvent(new SimulationEvent { 
            Time = _currentTime + selection, 
            Type = EventType.CustomerCatalogSelectionComplete, 
            Customer = customer 
        });
        
        // Следующее прибытие
        double interArrival = Exponential(_cfg.MeanInterArrivalTime);
        _interArrivalTimeHistory.Add(new TimeValuePoint(_currentTime, interArrival)); // Сбор данных
        double nextArrival = _currentTime + interArrival;
        
        if (nextArrival <= _cfg.SimulationDuration)
        {
            ScheduleEvent(new SimulationEvent
            {
                Time = nextArrival,
                Type = EventType.CustomerArrival
            });
        }
    }

    private void HandleCatalogSelectionComplete(Customer customer)
    {
        if (_traceMode)
        {
            Console.WriteLine($"  Customer {customer.Id} finished selecting (took {customer.CatalogSelectionDuration:0.##}). Now in queue.");
        }
        
        customer.WaitStartTime = _currentTime;
        _waitingCustomers.Enqueue(customer);

        // immediate attempt to assign available clerks
        ScheduleEvent(new SimulationEvent
        {
            Time = _currentTime, 
            Type = EventType.ClerkAssignment
        });
    }

    private void HandleClerkAssignment()
    {
        // assign waiting customers to clerks with free capacity (not busy and not full)
        while (_waitingCustomers.Count > 0)
        {
            var clerk = _clerks
                .Where(c => !c.IsBusy && c.AssignedCount < _cfg.ClerkBatchSize)
                .OrderBy(c => c.AssignedCount) // prefer least-loaded
                .FirstOrDefault();

            if (clerk == null)
                break; // no available clerk now

            var customer = _waitingCustomers.Dequeue();
            customer.AssignedClerk = clerk;
            customer.ServiceStartTime = _currentTime;
            _totalWaitingTime += (_currentTime - customer.WaitStartTime);

            clerk.AssignedCustomers.Add(customer);
            
            if (_traceMode)
            {
                Console.WriteLine($"  Assigned Customer {customer.Id} to Clerk {clerk.Id} (now has {clerk.AssignedCount}).");
            }
            
            // if clerk reached batch size, send immediately
            if (clerk.AssignedCount >= _cfg.ClerkBatchSize)
            {
                DispatchClerkToWarehouse(clerk);
            }
        }

        // If there are no waiting customers now but some clerks hold customers and are not busy, dispatch them as partial batches
        if (_waitingCustomers.Count == 0)
        {
            foreach (var clerk in _clerks.Where(c => c is { IsBusy: false, AssignedCount: > 0 }))
                DispatchClerkToWarehouse(clerk);
        }
    }

    private void DispatchClerkToWarehouse(Clerk clerk)
    {
        if (clerk.IsBusy) return;
        clerk.IsBusy = true;
        clerk.BusySince = _currentTime; // start busy period (trip + checkout)

        if (_traceMode)
        {
            Console.WriteLine($"  Clerk {clerk.Id} going to warehouse with {clerk.AssignedCount} customers.");
        }
        
        var tripTime = Triangular(
            _cfg.WarehouseTripMin, 
            _cfg.WarehouseTripMode, 
            _cfg.WarehouseTripMax
        );
        _warehouseTripHistory.Add(new TimeValuePoint(_currentTime, tripTime)); // Сбор данных

        if (_traceMode)
        {
            Console.WriteLine($"    (Warehouse trip will take {tripTime:0.##} minutes)");
        }
        
        ScheduleEvent(new SimulationEvent { 
            Time = _currentTime + tripTime, 
            Type = EventType.ClerkReturnFromWarehouse, 
            Clerk = clerk 
        });
    }

    private void HandleClerkReturnFromWarehouse(Clerk clerk)
    {
        if (_traceMode)
        {
            Console.WriteLine($"  Clerk {clerk.Id} returned from warehouse. Starts checkout process.");
        }
        
        // start servicing assigned customers in FIFO order
        if (clerk.AssignedCount > 0)
        {
            var next = clerk.AssignedCustomers[0];
            double checkout = Uniform(_cfg.CheckoutTimeMin, _cfg.CheckoutTimeMax);
            _checkoutTimeHistory.Add(new TimeValuePoint(_currentTime, checkout)); // Сбор данных

            if (_traceMode)
            {
                Console.WriteLine($"    (Checkout for Customer {next.Id} will take {checkout:0.##} minutes)");
            }
            
            ScheduleEvent(new SimulationEvent { 
                Time = _currentTime + checkout, 
                Type = EventType.CustomerCheckoutComplete, 
                Customer = next, 
                Clerk = clerk 
            });
        }
    }

    private void HandleCustomerCheckoutComplete(Customer customer, Clerk clerk)
    {
        customer.DepartureTime = _currentTime;
        _customersServed++;
        _totalSystemTime += (customer.DepartureTime - customer.ArrivalTime);

        if (_traceMode)
        {
            Console.WriteLine($"  Customer {customer.Id} checked out by Clerk {clerk.Id} at {_currentTime:0.##}.");
        }

        // Recording per-customer metrics
        double systemTime = customer.DepartureTime - customer.ArrivalTime;
        double waitingTime = (customer.ServiceStartTime >= 0 && customer.WaitStartTime >= 0) ? (customer.ServiceStartTime - customer.WaitStartTime) : 0.0;
        _perCustomerSystemTime.Add(new TimeValuePoint(_currentTime, systemTime));
        _perCustomerWaitingTime.Add(new TimeValuePoint(_currentTime, waitingTime));
        _customersServedHistory.Add(new TimeValuePoint(_currentTime, _customersServed));

        // remove customer from clerk's list
        clerk.AssignedCustomers.Remove(customer);

        // Если в партии остались клиенты, обслуживаем следующего
        if (clerk.AssignedCount > 0)
        {
            var next = clerk.AssignedCustomers[0];
            double checkout = Uniform(_cfg.CheckoutTimeMin, _cfg.CheckoutTimeMax);
            _checkoutTimeHistory.Add(new TimeValuePoint(_currentTime, checkout)); // Сбор данных
            
            ScheduleEvent(new SimulationEvent { 
                Time = _currentTime + checkout, 
                Type = EventType.CustomerCheckoutComplete, 
                Customer = next, 
                Clerk = clerk 
            });
        }
        else // Партия закончилась
        {
            // clerk finished all assigned customers -> become free now
            if (clerk.BusySince.HasValue)
            {
                double busyDuration = _currentTime - clerk.BusySince.Value;
                _clerkBusyTime[clerk.Id - 1] += busyDuration;
                clerk.BusySince = null;
            }

            clerk.IsBusy = false;

            if (_traceMode)
            {
                Console.WriteLine($"  Clerk {clerk.Id} is now free at {_currentTime:0.##}.");
            }

            // update utilization history at this moment
            var busyCount = _clerks.Count(c => c.IsBusy);
            _clerkUtilizationHistory.Add(new TimeValuePoint(_currentTime, _cfg.ClerkCount > 0 ? busyCount / (double)_cfg.ClerkCount : 0.0));

            // try to assign waiting customers immediately
            if (_waitingCustomers.Count > 0)
            {
                ScheduleEvent(new SimulationEvent
                {
                    Time = _currentTime, 
                    Type = EventType.ClerkAssignment
                });
            }
        }
    }

    private void FinalizeBusyTimesAtSimulationEnd()
    {
        // Досчитываем интеграл очереди до конца симуляции
        if (_lastEventTime < _cfg.SimulationDuration)
        {
            _integratedQueueLength += _waitingCustomers.Count * (_cfg.SimulationDuration - _lastEventTime);
            _queueLengthHistory.Add(new TimeValuePoint(_cfg.SimulationDuration, _waitingCustomers.Count)); // Финальная точка
        }

        // Досчитываем время занятости клерков, которые были заняты в момент окончания симуляции
        foreach (var clerk in _clerks)
        {
            if (clerk.BusySince.HasValue)
            {
                _clerkBusyTime[clerk.Id - 1] += (_cfg.SimulationDuration - clerk.BusySince.Value);
                clerk.BusySince = null;
            }
        }

        // Финальные точки истории загрузки клерков и обслуженных клиентов
        var busyCount = _clerks.Count(c => c.IsBusy);
        _clerkUtilizationHistory.Add(new TimeValuePoint(_cfg.SimulationDuration, _cfg.ClerkCount > 0 ? busyCount / (double)_cfg.ClerkCount : 0.0));
        _customersServedHistory.Add(new TimeValuePoint(_cfg.SimulationDuration, _customersServed));
    }

    // Helpers for event queue
    private void ScheduleEvent(SimulationEvent evt)
    {
        evt.Sequence = _nextEventSeq++;
        // priority = time + tiny epsilon * sequence to preserve insertion order for equal times
        var priority = evt.Time + evt.Sequence * 1e-9;
        _eventQueue.Enqueue(evt, priority);
    }

    private double Exponential(double mean)
    {
        var u = _rng.NextDouble();
        return -mean * Math.Log(1.0 - u);
    }

    // Results & metrics
    public SimulationResult GetResult() 
        => new()
    {
        CustomersArrived = _customersArrived,
        CustomersServed = _customersServed,
        AverageQueueLength = _cfg.SimulationDuration > 0 ? _integratedQueueLength / _cfg.SimulationDuration : 0.0,
        MaxQueueLength = _maxQueueLength,
        AverageWaitingTime = _customersServed > 0 ? _totalWaitingTime / _customersServed : 0.0,
        AverageSystemTime = _customersServed > 0 ? _totalSystemTime / _customersServed : 0.0,
        ClerkUtilizations = _clerkBusyTime.Select(b => b / _cfg.SimulationDuration).ToArray(),
        // Передача собранных данных
        QueueLengthHistory = _queueLengthHistory,
        CatalogSelectionHistory = _catalogSelectionHistory,
        WarehouseTripHistory = _warehouseTripHistory,
        CheckoutTimeHistory = _checkoutTimeHistory,
        InterArrivalTimeHistory = _interArrivalTimeHistory,
        // Новые возвращаемые ряды
        ClerkUtilizationHistory = _clerkUtilizationHistory,
        CustomersServedHistory = _customersServedHistory,
        PerCustomerSystemTime = _perCustomerSystemTime,
        PerCustomerWaitingTime = _perCustomerWaitingTime
    };

    // Static method to run replications
    public static AggregatedResult VerifyWithReplications(SimulatorConfig cfg, int replications = 10, bool trace = false)
    {
        var results = new List<SimulationResult>();

        for (int i = 0; i < replications; i++)
        {
            var cfgCopy = JsonSerializer.Deserialize<SimulatorConfig>(JsonSerializer.Serialize(cfg));
            cfgCopy!.RandomSeed = cfg.RandomSeed + i; // distinct seed per replication
            
            var sim = new WholesaleStoreSimulator(cfgCopy, trace);
            sim.Run();
            var res = sim.GetResult();
            results.Add(res);

            if (!trace && (replications <= 20 || (i+1) % (replications/10) == 0)) // Avoid flooding console
            {
                Console.WriteLine($"Replication {i + 1}/{replications}: AvgWait={res.AverageWaitingTime:0.##}, AvgSys={res.AverageSystemTime:0.##}, AvgUtil={res.ClerkUtilizations.Average():0.##}, AvgQueue={res.AverageQueueLength:0.##}");
            }
        }

        return new AggregatedResult(results);
    }
    

}

// Results DTOs
public class SimulationResult
{
    public int CustomersArrived { get; set; }
    public int CustomersServed { get; set; }
    public double AverageQueueLength { get; set; }
    public int MaxQueueLength { get; set; }
    public double AverageWaitingTime { get; set; }
    public double AverageSystemTime { get; set; }
    public double[] ClerkUtilizations { get; set; }
    
    // Свойства для хранения данных для графиков
    public List<TimeValuePoint> QueueLengthHistory { get; set; } = new();
    public List<TimeValuePoint> CatalogSelectionHistory { get; set; } = new();
    public List<TimeValuePoint> WarehouseTripHistory { get; set; } = new();
    public List<TimeValuePoint> CheckoutTimeHistory { get; set; } = new();
    public List<TimeValuePoint> InterArrivalTimeHistory { get; set; } = new();

    // Новые: выходные метрики в виде рядов
    public List<TimeValuePoint> ClerkUtilizationHistory { get; set; } = new(); // 0..1
    public List<TimeValuePoint> CustomersServedHistory { get; set; } = new();
    public List<TimeValuePoint> PerCustomerSystemTime { get; set; } = new();
    public List<TimeValuePoint> PerCustomerWaitingTime { get; set; } = new();
}

public class AggregatedResult
{
    public double MeanCustomersServed { get; set; }
    public double StdDevCustomersServed { get; set; }
    public double MeanAvgWait { get; set; }
    public double StdDevAvgWait { get; set; }
    public double MeanAvgSystem { get; set; }
    public double StdDevAvgSystem { get; set; }
    public double MeanAvgUtil { get; set; }
    public double StdDevAvgUtil { get; set; }
    public double MeanAvgQueueLength { get; set; }
    public double StdDevAvgQueueLength { get; set; }
    public List<SimulationResult> ReplicationResults { get; set; } = new();
    
    public AggregatedResult(List<SimulationResult> results)
    {
        ReplicationResults = results;
        MeanCustomersServed = results.Average(r => r.CustomersServed);
        StdDevCustomersServed = StdDev(results.Select(r => (double)r.CustomersServed));
        MeanAvgQueueLength = results.Average(r => r.AverageQueueLength);
        StdDevAvgQueueLength = StdDev(results.Select(r => r.AverageQueueLength));
        MeanAvgSystem = results.Average(r => r.AverageSystemTime);
        StdDevAvgSystem = StdDev(results.Select(r => r.AverageSystemTime));
        MeanAvgWait = results.Average(r => r.AverageWaitingTime);
        StdDevAvgWait = StdDev(results.Select(r => r.AverageWaitingTime));
        MeanAvgUtil = results.Average(r => r.ClerkUtilizations.Average());
        StdDevAvgUtil = StdDev(results.Select(r => r.ClerkUtilizations.Average()));
    }

    public AggregatedResult() {}
    
    private static double StdDev(IEnumerable<double> values)
    {
        var valueList = values.ToList();
        if (valueList.Count < 2) return 0.0;
        var avg = valueList.Average();
        return Math.Sqrt(valueList.Sum(v => Math.Pow(v - avg, 2)) / (valueList.Count - 1));
    }
}
}