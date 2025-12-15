using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace Dtde.Benchmarks;

/// <summary>
/// Centralized benchmark configuration with support for quick and full benchmark modes.
/// </summary>
public static class BenchmarkConfig
{
    /// <summary>
    /// Environment variable to enable quick benchmark mode.
    /// Set DTDE_QUICK_BENCHMARK=1 or pass --quick argument.
    /// </summary>
    public const string QuickModeEnvVar = "DTDE_QUICK_BENCHMARK";

    /// <summary>
    /// Gets whether quick mode is enabled via environment variable or command line.
    /// </summary>
    public static bool IsQuickMode { get; private set; }

    /// <summary>
    /// Initializes quick mode based on command line arguments.
    /// </summary>
    public static void Initialize(string[] args)
    {
        IsQuickMode = args.Contains("--quick", StringComparer.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable(QuickModeEnvVar) == "1";
    }

    /// <summary>
    /// Gets the standard configuration with exporters and columns.
    /// </summary>
    public static IConfig GetConfig()
    {
        var config = DefaultConfig.Instance
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(CsvExporter.Default)
            .AddColumn(StatisticColumn.Mean)
            .AddColumn(StatisticColumn.StdDev)
            .AddColumn(StatisticColumn.Median)
            .AddColumn(StatisticColumn.P95)
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        if (IsQuickMode)
        {
            config = config.WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(50));
        }

        return config;
    }

    /// <summary>
    /// Gets the BenchmarkDotNet job for the current mode.
    /// Quick mode: 1 warmup, 3 iterations (much faster).
    /// Full mode: 2 warmups, 5 iterations (more accurate).
    /// </summary>
    public static Job GetJob()
    {
        return IsQuickMode
            ? Job.Default
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithId("Quick")
            : Job.Default
                .WithWarmupCount(2)
                .WithIterationCount(5)
                .WithId("Full");
    }

    /// <summary>
    /// Prints mode information to console.
    /// </summary>
    public static void PrintModeInfo()
    {
        if (IsQuickMode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚡ QUICK MODE ENABLED - Reduced iterations for faster results");
            Console.WriteLine("   (Use full mode for accurate benchmarks: remove --quick flag)");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    #region Parameter Sets for Different Modes

    /// <summary>
    /// Record counts for SingleVsShardedBenchmarks.
    /// Quick: 10K only. Full: 10K, 50K, 100K.
    /// </summary>
    public static int[] SingleVsShardedRecordCounts =>
        IsQuickMode ? [10_000] : [10_000, 50_000, 100_000];

    /// <summary>
    /// Record counts for IndexedFieldsBenchmarks.
    /// Quick: 50K only. Full: 50K, 100K.
    /// </summary>
    public static int[] IndexedFieldsRecordCounts =>
        IsQuickMode ? [50_000] : [50_000, 100_000];

    /// <summary>
    /// Record counts for DateShardingBenchmarks.
    /// Quick: 50K only. Full: 50K, 100K.
    /// </summary>
    public static int[] DateShardingRecordCounts =>
        IsQuickMode ? [50_000] : [50_000, 100_000];

    /// <summary>
    /// Record counts for NestedPropertiesBenchmarks.
    /// Quick: 5K only. Full: 5K, 10K.
    /// </summary>
    public static int[] NestedPropertiesRecordCounts =>
        IsQuickMode ? [5_000] : [5_000, 10_000];

    /// <summary>
    /// Record counts for WriteOperationsBenchmarks.
    /// Quick: 100 only. Full: 100, 1000, 5000.
    /// </summary>
    public static int[] WriteOperationsRecordCounts =>
        IsQuickMode ? [100] : [100, 1_000, 5_000];

    /// <summary>
    /// Record counts for JoinIncludeBenchmarks.
    /// Quick: 5K only. Full: 5K, 20K.
    /// </summary>
    public static int[] JoinIncludeRecordCounts =>
        IsQuickMode ? [5_000] : [5_000, 20_000];

    /// <summary>
    /// Concurrency levels for ConcurrentAccessBenchmarks.
    /// Quick: 4 only. Full: 4, 8.
    /// </summary>
    public static int[] ConcurrencyLevels =>
        IsQuickMode ? [4] : [4, 8];

    /// <summary>
    /// Record counts for ConcurrentAccessBenchmarks.
    /// Quick: 5K only. Full: 10K.
    /// </summary>
    public static int[] ConcurrentAccessRecordCounts =>
        IsQuickMode ? [5_000] : [10_000];

    /// <summary>
    /// Record counts for CrossShardTransactionBenchmarks.
    /// Quick: 500 only. Full: 1000.
    /// </summary>
    public static int[] CrossShardRecordCounts =>
        IsQuickMode ? [500] : [1_000];

    /// <summary>
    /// Batch sizes for CrossShardTransactionBenchmarks.
    /// Quick: 10 only. Full: 10, 50.
    /// </summary>
    public static int[] CrossShardBatchSizes =>
        IsQuickMode ? [10] : [10, 50];

    #endregion
}
