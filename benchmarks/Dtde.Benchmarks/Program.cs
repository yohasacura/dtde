using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using Dtde.Benchmarks;
using Dtde.Benchmarks.Comparisons;

// Configure and run benchmarks
var config = DefaultConfig.Instance
    .AddExporter(MarkdownExporter.GitHub)
    .AddExporter(CsvExporter.Default)
    .AddColumn(StatisticColumn.Mean)
    .AddColumn(StatisticColumn.StdDev)
    .AddColumn(StatisticColumn.Median)
    .AddColumn(StatisticColumn.P95)
    .AddColumn(StatisticColumn.OperationsPerSecond)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

Console.WriteLine("DTDE Comprehensive Benchmarks");
Console.WriteLine("==============================");
Console.WriteLine();
Console.WriteLine("Available benchmark suites:");
Console.WriteLine("1. Single Table vs Sharded Table Comparison");
Console.WriteLine("2. Indexed vs Non-Indexed Fields");
Console.WriteLine("3. Join and Include Operations");
Console.WriteLine("4. Nested Properties and Navigation");
Console.WriteLine("5. Write Operations (Insert/Update/Delete)");
Console.WriteLine("6. Concurrent Access Patterns");
Console.WriteLine("7. Run All Benchmarks");
Console.WriteLine();
Console.Write("Select benchmark suite (1-7): ");

var input = Console.ReadLine();
if (int.TryParse(input, out var choice))
{
    switch (choice)
    {
        case 1:
            BenchmarkRunner.Run<SingleVsShardedBenchmarks>(config);
            break;
        case 2:
            BenchmarkRunner.Run<IndexedFieldsBenchmarks>(config);
            break;
        case 3:
            BenchmarkRunner.Run<JoinIncludeBenchmarks>(config);
            break;
        case 4:
            BenchmarkRunner.Run<NestedPropertiesBenchmarks>(config);
            break;
        case 5:
            BenchmarkRunner.Run<WriteOperationsBenchmarks>(config);
            break;
        case 6:
            BenchmarkRunner.Run<ConcurrentAccessBenchmarks>(config);
            break;
        case 7:
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).RunAll(config);
            break;
        default:
            Console.WriteLine("Invalid choice");
            break;
    }
}
else
{
    // Run all benchmarks in release mode
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).RunAll(config);
}
