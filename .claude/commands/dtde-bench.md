---
description: Run the DTDE BenchmarkDotNet harness in Release and surface the summary.
allowed-tools: Bash(dotnet run:*), Bash(dotnet build:*)
argument-hint: "[--filter <regex>]"
---

Run the DTDE benchmarks and print the summary table.

Steps:

1. Build benchmarks: `dotnet build benchmarks/Dtde.Benchmarks/Dtde.Benchmarks.csproj -c Release`.
2. Run the harness:
   `dotnet run --project benchmarks/Dtde.Benchmarks -c Release -- $ARGUMENTS`
   (default to `--job short` if no arguments).
3. After the run, locate the produced markdown report (typically under
   `benchmarks/Dtde.Benchmarks/BenchmarkDotNet.Artifacts/results/`), read it,
   and report:
   - The benchmark groups exercised.
   - For each group: the mean / allocated columns, sorted by mean ascending.
   - Any rows that look anomalous (regressions, very high allocation, etc.).
4. Note where the full report lives so the user can open it.
