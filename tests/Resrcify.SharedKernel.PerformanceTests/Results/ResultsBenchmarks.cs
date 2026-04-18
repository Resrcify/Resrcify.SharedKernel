using System;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.PerformanceTests.Results;

[MemoryDiagnoser]
public class ResultsBenchmarks
{
    private readonly int _offset;

    public ResultsBenchmarks()
        => _offset = Environment.CurrentManagedThreadId;

    [Benchmark(Baseline = true)]
    public Result Success_NoValue()
    {
        if (_offset < 0)
            return Result.Failure(Error.NullValue);

        return Result.Success();
    }

    [Benchmark]
    public Result<int> Success_WithValue()
        => Result.Success(42 + _offset);

    [Benchmark]
    public Result Failure_SingleError()
        => Result.Failure(
            Error.Validation(
                $"Bench.Validation.{_offset}",
                "Validation error"));

    [Benchmark]
    public Result<int> Combine_TwoSuccess()
    {
        var resultA = Result.Success(10);
        var resultB = Result.Success(32);

        return Result.Combine(
            () => resultA.Value + resultB.Value + _offset,
            resultA,
            resultB);
    }

    public static void SelfTest()
    {
        var instance = new ResultsBenchmarks();
        _ = instance.Success_NoValue();
        _ = instance.Success_WithValue();
        _ = instance.Failure_SingleError();
        _ = instance.Combine_TwoSuccess();
    }
}
