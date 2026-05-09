---
description: Build the full DTDE solution and run all tests across Core, EntityFramework, and Integration projects.
allowed-tools: Bash(dotnet build:*), Bash(dotnet test:*)
argument-hint: "[--no-test]"
---

Build the DTDE solution in `Release` configuration and run the test suite.

Steps:

1. Run `dotnet build -c Release`. Report errors/warnings count and any
   `error` lines.
2. If `$ARGUMENTS` does not contain `--no-test`, run each test project
   sequentially and report passed/failed counts:
   - `dotnet test tests/Dtde.Core.Tests/Dtde.Core.Tests.csproj -c Release --no-build`
   - `dotnet test tests/Dtde.EntityFramework.Tests/Dtde.EntityFramework.Tests.csproj -c Release --no-build`
   - `dotnet test tests/Dtde.Integration.Tests/Dtde.Integration.Tests.csproj -c Release --no-build`
3. Summarize: total passed, total failed, total skipped, total duration.
4. If anything failed, surface the first 10 failure lines verbatim.
