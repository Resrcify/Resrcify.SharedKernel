using System.Data.Common;
using NSubstitute;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>Database exceptions with a given <c>SQLSTATE</c>, as a provider raises them.</summary>
internal static class DbExceptions
{
    public static DbException WithSqlState(string? sqlState)
    {
        var exception = Substitute.For<DbException>();
        exception.SqlState.Returns(sqlState);
        return exception;
    }
}
