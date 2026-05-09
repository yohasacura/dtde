---
description: Pack the three DTDE NuGet packages and validate metadata. Runs the nuget-packager agent.
allowed-tools: Bash(dotnet pack:*), Bash(dotnet build:*)
argument-hint: "[version] [--baseline <previous-version>]"
---

Produce production-ready NuGet packages for **`Dtde.Abstractions`**,
**`Dtde.Core`**, and **`Dtde.EntityFramework`**.

If `$ARGUMENTS` contains a version (e.g. `1.0.0` or `1.1.0-rc.1`), pass it
via `-p:Version=<version>`. Otherwise use the `VersionPrefix` from
`Directory.Build.props`.

If `$ARGUMENTS` contains `--baseline <ver>`, also enable package validation
against that baseline:
`-p:PackageValidationBaselineVersion=<ver>`.

Steps:

1. Run `dotnet pack -c Release` with the chosen properties; output goes to
   `./nupkg/`.
2. Delegate to the **nuget-packager** subagent with a one-paragraph prompt
   asking it to inspect every produced `.nupkg` and `.snupkg`, validate
   metadata, and report whether the packages are publish-ready.
3. Summarize the agent's verdict.

Do **not** call `dotnet nuget push` under any circumstances.
