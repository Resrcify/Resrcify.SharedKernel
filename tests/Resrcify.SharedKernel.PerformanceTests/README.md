# Resrcify.SharedKernel.PerformanceTests

`Resrcify.SharedKernel.PerformanceTests` contains BenchmarkDotNet suites for SharedKernel modules.

## Table of Contents

- [Resrcify.SharedKernel.PerformanceTests](#resrcifysharedkernelperformancetests)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Project layout](#project-layout)
  - [Run benchmarks](#run-benchmarks)
    - [Fast self-test](#fast-self-test)
    - [Messaging benchmarks](#messaging-benchmarks)
    - [Full benchmark run](#full-benchmark-run)
  - [Output](#output)

## What you get

- Benchmarks grouped by source module:
    - `Abstractions`
    - `Caching`
    - `DomainDrivenDesign`
    - `Messaging`
    - `Repository`
    - `Results`
    - `UnitOfWork`
    - `Web`
- Shared runner in `Program.cs`.
- Quick smoke path via `--self-test`.

## Prerequisites

- .NET 10 SDK.
- Release build configuration for reliable measurements.

## Project layout

- Benchmark project: `tests/Resrcify.SharedKernel.PerformanceTests/Resrcify.SharedKernel.PerformanceTests.csproj`
- Main benchmark entry point: `tests/Resrcify.SharedKernel.PerformanceTests/Program.cs`
- Artifacts output folder: `tests/Resrcify.SharedKernel.PerformanceTests/BenchmarkDotNet.Artifacts`

## Run benchmarks

### Fast self-test

```powershell
Set-Location "d:\Google Drive\Projects\Titan404\Resrcify.SharedKernel"
dotnet run -c Release --project ".\tests\Resrcify.SharedKernel.PerformanceTests\Resrcify.SharedKernel.PerformanceTests.csproj" -- --self-test
```

### Messaging benchmarks

```powershell
Set-Location "d:\Google Drive\Projects\Titan404\Resrcify.SharedKernel"
dotnet run -c Release --project ".\tests\Resrcify.SharedKernel.PerformanceTests\Resrcify.SharedKernel.PerformanceTests.csproj" -- --filter "*Messaging*"
```

Specific suites:

```powershell
Set-Location "d:\Google Drive\Projects\Titan404\Resrcify.SharedKernel"
dotnet run -c Release --project ".\tests\Resrcify.SharedKernel.PerformanceTests\Resrcify.SharedKernel.PerformanceTests.csproj" -- --filter "*MessagingStreamBenchmarks*"
dotnet run -c Release --project ".\tests\Resrcify.SharedKernel.PerformanceTests\Resrcify.SharedKernel.PerformanceTests.csproj" -- --filter "*MessagingProcessorMatrixBenchmarks*"
dotnet run -c Release --project ".\tests\Resrcify.SharedKernel.PerformanceTests\Resrcify.SharedKernel.PerformanceTests.csproj" -- --filter "*MessagingPolymorphicBenchmarks*"
```

### Full benchmark run

```powershell
Set-Location "d:\Google Drive\Projects\Titan404\Resrcify.SharedKernel"
dotnet run -c Release --project ".\tests\Resrcify.SharedKernel.PerformanceTests\Resrcify.SharedKernel.PerformanceTests.csproj"
```

## Output

BenchmarkDotNet writes detailed reports under:

- `BenchmarkDotNet.Artifacts\results`
