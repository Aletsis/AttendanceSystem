using AttendanceSystem.Application.Common;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        // Act
        var result = Result.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public void Failure_ShouldCreateFailedResultWithError()
    {
        // Act
        var result = Result.Failure("Ocurrió un error inesperado");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Ocurrió un error inesperado");
    }

    [Fact]
    public void GenericResult_Success_ShouldContainValue()
    {
        // Act
        var result = Result<int>.Success(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public void GenericResult_Failure_ShouldContainErrorAndDefaultValue()
    {
        // Act
        var result = Result<string>.Failure("No encontrado");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("No encontrado");
    }
}
