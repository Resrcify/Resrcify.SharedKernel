using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Makes the <typeparamref name="TCommand"/> a <see cref="FactoryCommandJob{TCommand, TResponse}"/> sends each run,
/// from the run's DI scope: the <c>createCommand</c> given to <c>AddIntervalCommandJob</c> / <c>AddCronCommandJob</c>,
/// registered as a singleton by <see cref="CommandFactory.Register{TCommand, TResponse}"/>. One per command type, as
/// the command's job key is.
/// </summary>
/// <typeparam name="TCommand">The command made.</typeparam>
internal sealed class CommandFactory<TCommand>
{
    private readonly Func<IServiceProvider, TCommand> _createCommand;

    public CommandFactory(Func<IServiceProvider, TCommand> createCommand)
        => _createCommand = createCommand;

    /// <summary>The command to send this run, made from the run's scope.</summary>
    /// <exception cref="InvalidOperationException">The factory returned <see langword="null"/>.</exception>
    public TCommand Create(IServiceProvider services)
        => _createCommand(services)
            ?? throw new InvalidOperationException(
                $"The createCommand given for {typeof(TCommand).Name} returned null; it must make a command every run.");
}

/// <summary>Registers the <see cref="CommandFactory{TCommand}"/> of a command job.</summary>
internal static class CommandFactory
{
    /// <summary>
    /// Checks <typeparamref name="TCommand"/> returns <typeparamref name="TResponse"/> of its own and registers
    /// <paramref name="createCommand"/> as its factory.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TCommand"/> returns <typeparamref name="TResponse"/> only through variance, or has a factory
    /// already.
    /// </exception>
    public static void Register<TCommand, TResponse>(
        IQuartzBuilder quartz,
        Func<IServiceProvider, TCommand> createCommand)
        where TCommand : IRequest<TResponse>
        where TResponse : Result
    {
        ArgumentNullException.ThrowIfNull(quartz);
        ArgumentNullException.ThrowIfNull(createCommand);
        CommandJobs.EnsureReturns<TCommand, TResponse>();

        if (quartz.Services.Any(service => service.ServiceType == typeof(CommandFactory<TCommand>)))
            throw new InvalidOperationException(
                $"A createCommand for {typeof(TCommand).Name} is registered already. One command job sends a command "
                + $"type (its key is SendCommandJob-{typeof(TCommand).Name}...): register it once, with one "
                + "AddIntervalCommandJob or AddCronCommandJob.");

        quartz.Services.AddSingleton(new CommandFactory<TCommand>(createCommand));
    }
}
