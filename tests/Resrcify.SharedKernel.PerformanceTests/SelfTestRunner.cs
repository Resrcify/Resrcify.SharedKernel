using System;
using Resrcify.SharedKernel.PerformanceTests.Abstractions;
using Resrcify.SharedKernel.PerformanceTests.Caching;
using Resrcify.SharedKernel.PerformanceTests.DomainDrivenDesign;
using Resrcify.SharedKernel.PerformanceTests.Messaging;
using Resrcify.SharedKernel.PerformanceTests.Repository;
using Resrcify.SharedKernel.PerformanceTests.Results;
using Resrcify.SharedKernel.PerformanceTests.UnitOfWork;
using Resrcify.SharedKernel.PerformanceTests.Web;

namespace Resrcify.SharedKernel.PerformanceTests;

internal static class SelfTestRunner
{
    public static void Run()
    {
        AbstractionsBenchmarks.SelfTest();
        CachingBenchmarks.SelfTest();
        DomainDrivenDesignBenchmarks.SelfTest();
        ResultsBenchmarks.SelfTest();
        RepositoryBenchmarks.SelfTest();
        UnitOfWorkBenchmarks.SelfTest();
        WebBenchmarks.SelfTest();

        using var messagingBenchmarks = new MediatorComparisonBenchmarks();
        messagingBenchmarks.GlobalSetup().GetAwaiter().GetResult();
        _ = messagingBenchmarks.Custom_Send_Typed_Task().GetAwaiter().GetResult();
        _ = messagingBenchmarks.MediatR_Send_Typed().GetAwaiter().GetResult();

        using var matrixBenchmarks = new MessagingPipelineMatrixBenchmarks
        {
            BehaviorCount = 1,
            PublishStrategy = Resrcify.SharedKernel.Messaging.Publishing.NotificationPublishStrategy.Sequential
        };

        matrixBenchmarks.GlobalSetup().GetAwaiter().GetResult();
        _ = matrixBenchmarks.Custom_Send_Typed_Matrix().GetAwaiter().GetResult();
        matrixBenchmarks.Custom_Publish_Matrix().GetAwaiter().GetResult();

        using var processorBenchmarks = new MessagingProcessorMatrixBenchmarks
        {
            PreProcessorCount = 1,
            PostProcessorCount = 1
        };

        processorBenchmarks.GlobalSetup().GetAwaiter().GetResult();
        _ = processorBenchmarks.Custom_Send_With_PrePost_Matrix().GetAwaiter().GetResult();

        using var streamBenchmarks = new MessagingStreamBenchmarks
        {
            BehaviorCount = 1,
            ItemCount = 16
        };

        streamBenchmarks.GlobalSetup().GetAwaiter().GetResult();
        _ = streamBenchmarks.Custom_CreateStream_ConsumeAll().GetAwaiter().GetResult();

        using var polymorphicBenchmarks = new MessagingPolymorphicBenchmarks
        {
            RequestTypeCount = 10,
            Distribution = MessagingPolymorphicBenchmarks.WorkloadDistribution.Uniform
        };

        polymorphicBenchmarks.GlobalSetup().GetAwaiter().GetResult();
        _ = polymorphicBenchmarks.Custom_Send_Object_Polymorphic().GetAwaiter().GetResult();
        _ = polymorphicBenchmarks.MediatR_Send_Object_Polymorphic().GetAwaiter().GetResult();

#pragma warning disable CA1303
        var completionMessage = "Performance self-test completed.";
        Console.WriteLine(completionMessage);
#pragma warning restore CA1303
    }
}
