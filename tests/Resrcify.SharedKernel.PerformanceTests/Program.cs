using BenchmarkDotNet.Running;
using System;
using System.Linq;

namespace Resrcify.SharedKernel.PerformanceTests;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            SelfTestRunner.Run();
            return;
        }

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);
    }
}
