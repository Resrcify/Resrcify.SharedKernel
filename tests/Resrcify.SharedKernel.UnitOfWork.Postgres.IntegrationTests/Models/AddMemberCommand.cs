using System;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

/// <summary>Adds a member as given, so a test can break any of the table's constraints through the mediator.</summary>
internal sealed record AddMemberCommand(
    Guid ShardId,
    string? Nickname,
    int Rank)
    : ICommand;

internal sealed class AddMemberCommandHandler(TestDbContext context)
    : ICommandHandler<AddMemberCommand>
{
    public Task<Result> Handle(
        AddMemberCommand request,
        CancellationToken cancellationToken)
    {
        context.Members.Add(new Member(
            Guid.NewGuid(),
            request.ShardId,
            request.Nickname,
            request.Rank));
        return Task.FromResult(Result.Success());
    }
}
