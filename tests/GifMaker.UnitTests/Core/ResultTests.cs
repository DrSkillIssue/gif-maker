using GifMaker.Core;

namespace GifMaker.UnitTests.Core;

public sealed class ResultTests
{
    [Fact]
    public void GenericResultMatchUsesOnlySuccessBranch()
    {
        var result = Result<int>.Ok(42);
        var successCalls = 0;
        var errorCalls = 0;

        var value = result.Match(
            success =>
            {
                successCalls++;
                return success;
            },
            _ =>
            {
                errorCalls++;
                return -1;
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(42, value);
        Assert.Equal(1, successCalls);
        Assert.Equal(0, errorCalls);
    }

    [Fact]
    public void GenericResultMatchUsesOnlyErrorBranch()
    {
        var result = Result<int>.Fail("nope");
        var successCalls = 0;
        var errorCalls = 0;

        var value = result.Match(
            _ =>
            {
                successCalls++;
                return -1;
            },
            error =>
            {
                errorCalls++;
                return error.Length;
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(4, value);
        Assert.Equal(0, successCalls);
        Assert.Equal(1, errorCalls);
    }

    [Fact]
    public void GenericGetValueOrThrowThrowsArgumentExceptionOnFailure()
    {
        var result = Result<int>.Fail("broken");

        var exception = Assert.Throws<ArgumentException>(() => result.GetValueOrThrow());

        Assert.Equal("broken", exception.Message);
    }

    [Fact]
    public void GenericGetValueOrThrowUsesCustomExceptionFactory()
    {
        var result = Result<int>.Fail("custom");

        var exception = Assert.Throws<InvalidOperationException>(
            () => result.GetValueOrThrow(error => new InvalidOperationException(error)));

        Assert.Equal("custom", exception.Message);
    }

    [Fact]
    public void NonGenericResultMatchUsesOnlySuccessBranch()
    {
        var result = Result.Ok();
        var successCalls = 0;
        var errorCalls = 0;

        var value = result.Match(
            () =>
            {
                successCalls++;
                return "ok";
            },
            _ =>
            {
                errorCalls++;
                return "bad";
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", value);
        Assert.Equal(1, successCalls);
        Assert.Equal(0, errorCalls);
    }

    [Fact]
    public void NonGenericResultMatchUsesOnlyErrorBranch()
    {
        var result = Result.Fail("bad");
        var successCalls = 0;
        var errorCalls = 0;

        var value = result.Match(
            () =>
            {
                successCalls++;
                return "";
            },
            error =>
            {
                errorCalls++;
                return error;
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("bad", value);
        Assert.Equal(0, successCalls);
        Assert.Equal(1, errorCalls);
    }
}
