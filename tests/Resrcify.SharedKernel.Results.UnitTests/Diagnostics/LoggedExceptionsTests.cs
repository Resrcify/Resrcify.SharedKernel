using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Resrcify.SharedKernel.Results.Diagnostics;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Diagnostics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class LoggedExceptionsTests
{
    [Fact]
    public void Claim_ShouldBeTrueOnlyTheFirstTime()
    {
        var exception = new InvalidOperationException("once");

        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.Claim(exception).ShouldBeFalse();
        LoggedExceptions.Claim(exception).ShouldBeFalse();
    }

    [Fact]
    public void Claim_ShouldMarkTheExceptionUnderTheDocumentedKey()
    {
        var exception = new InvalidOperationException("marked");

        LoggedExceptions.Claim(exception);

        exception.Data[LoggedExceptions.DataKey].ShouldBe(true);
        LoggedExceptions.DataKey.ShouldBe("Resrcify.SharedKernel.Logged");
    }

    [Fact]
    public void Claim_ShouldBeFalse_WhenSomeoneMarkedItByTheKeyAlone()
    {
        // A package that doesn't reference this one follows the convention through the key.
        var exception = new InvalidOperationException("marked elsewhere");
        exception.Data["Resrcify.SharedKernel.Logged"] = true;

        LoggedExceptions.Claim(exception).ShouldBeFalse();
    }

    [Fact]
    public void Claim_ShouldAlwaysBeTrue_WhenTheExceptionsDataIsReadOnly()
    {
        var exception = Substitute.For<Exception>();
        exception.Data.Returns(new ReadOnlyDictionary<object, object>(new Dictionary<object, object>()));

        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
    }

    [Fact]
    public void IsLogged_ShouldSayWhetherTheExceptionWasClaimed()
    {
        var exception = new InvalidOperationException("seen");

        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
        LoggedExceptions.Claim(exception);
        LoggedExceptions.IsLogged(exception).ShouldBeTrue();
    }

    [Fact]
    public async Task Claim_ShouldBeTrueOnce_WhenManyClaimTheSameExceptionAtOnce()
    {
        var exception = new InvalidOperationException("contended");

        var claims = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => LoggedExceptions.Claim(exception))));

        claims.Count(claimed => claimed).ShouldBe(1);
    }

    [Fact]
    public void Claim_ShouldThrow_WhenTheExceptionIsNull()
        => Should.Throw<ArgumentNullException>(() => LoggedExceptions.Claim(null!));
}
