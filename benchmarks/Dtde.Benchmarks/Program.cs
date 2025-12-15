using BenchmarkDotNet.Running;
using Dtde.Benchmarks;
using Dtde.Benchmarks.Comparisons;

// Initialize benchmark configuration (handles --quick flag)
BenchmarkConfig.Initialize(args);
var config = BenchmarkConfig.GetConfig();

Console.WriteLine("DTDE Comprehensive Benchmarks");
Console.WriteLine("==============================");
Console.WriteLine();
BenchmarkConfig.PrintModeInfo();
Console.WriteLine("Available benchmark suites:");
Console.WriteLine("1. Single Table vs Sharded Table Comparison");
Console.WriteLine("2. Indexed vs Non-Indexed Fields");
Console.WriteLine("3. Join and Include Operations");
Console.WriteLine("4. Nested Properties and Navigation");
Console.WriteLine("5. Write Operations (Insert/Update/Delete)");
Console.WriteLine("6. Concurrent Access Patterns");
Console.WriteLine("7. Cross-Shard Transactions");
Console.WriteLine("8. Date-Based Sharding");
Console.WriteLine("9. Run All Benchmarks");
Console.WriteLine();
Console.WriteLine("Tip: Add --quick flag for faster runs (reduced iterations/parameters)");
Console.WriteLine();
Console.Write("Select benchmark suite (1-9): ");

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
            BenchmarkRunner.Run<CrossShardTransactionBenchmarks>(config);
            break;
        case 8:
            BenchmarkRunner.Run<DateShardingBenchmarks>(config);
            break;
        case 9:
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
