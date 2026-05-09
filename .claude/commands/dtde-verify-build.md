---
description: Quick sanity check — restore, build with TreatWarningsAsErrors, and run the full test suite. Fails loudly on any deviation.
allowed-tools: Bash(dotnet restore:*), Bash(dotnet build:*), Bash(dotnet test:*)
---

Verify the working copy builds clean and all tests pass. Use as a
pre-commit / pre-PR sanity gate.

1. `dotnet restore`. Surface any package resolution warnings.
2. `dotnet build -c Release`. Must complete with `0 Error(s)` and `0 Warning(s)`.
   If either is non-zero, stop and report the first 20 lines of build output.
3. `dotnet test -c Release --no-build`. Sum passed/failed across all test
   projects. Report a one-line summary, e.g.
   `403 passed, 0 failed, 0 skipped — 2.4s`.
4. Final verdict: `OK to push` or `Do not push: <reason>`.
