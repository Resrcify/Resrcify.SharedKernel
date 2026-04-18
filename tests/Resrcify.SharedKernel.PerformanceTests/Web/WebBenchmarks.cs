using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;

namespace Resrcify.SharedKernel.PerformanceTests.Web;

[MemoryDiagnoser]
public class WebBenchmarks
{
    private static readonly Result FailureResult = Result.Failure(
        Error.Validation("Bench.Validation", "Validation failed"));
    private readonly int _state = System.Environment.CurrentManagedThreadId;

    [Benchmark(Baseline = true)]
    public IResult ToProblemDetails()
    {
        _ = _state;
        return FailureResult.ToProblemDetails();
    }

    [Benchmark]
    public Task<IResult> Match_Result()
    {
        _ = _state;
        return Resrcify.SharedKernel.Web.Extensions.ResultExtensions.Match(
            Task.FromResult(FailureResult),
            onSuccess: () => Microsoft.AspNetCore.Http.Results.Ok(),
            onFailure: result => result.ToProblemDetails());
    }

    public static void SelfTest()
    {
        var instance = new WebBenchmarks();
        _ = instance.ToProblemDetails();
        _ = instance.Match_Result();
    }
}
