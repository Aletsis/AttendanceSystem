using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Behaviors;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Queries.GetTardinessAnalysis;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Services;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>F07-02 / A05: los rangos inválidos se rechazan antes de consultar o modificar datos.</summary>
public class DateRangeValidationTests
{
    private static readonly DateTime Day = new(2026, 8, 3);

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 365, null)]
    [InlineData(5, 0, "anterior a la inicial")]
    [InlineData(0, 366, "excede el máximo")]
    public void Policy_ValidatesOrderAndLength(int startOffset, int endOffset, string? expected)
    {
        var error = DateRangePolicy.Validate(Day.AddDays(startOffset), Day.AddDays(endOffset), DateRangePolicy.MaxRecalculationDays);
        if (expected == null) error.Should().BeNull();
        else error.Should().Contain(expected);
    }

    [Fact]
    public void Policy_RejectsDefaultAndExtremeDates()
    {
        DateRangePolicy.Validate(DateTime.MinValue, Day, 366).Should().Contain("posteriores al 01/01/2000");
        DateRangePolicy.Validate(Day, DateTime.MaxValue, 366).Should().Contain("no pueden ser posteriores");
    }

    [Fact]
    public void Slices_CoverTheRangeWithoutGapsOrOverlaps()
    {
        var slices = DateRangePolicy.Slices(Day, Day.AddDays(800), 366).ToList();
        slices.Should().HaveCount(3);
        slices[0].Should().Be((Day, Day.AddDays(365)));
        slices[1].Start.Should().Be(Day.AddDays(366));
        slices[2].End.Should().Be(Day.AddDays(800));
    }

    [Fact]
    public async Task Behavior_ThrowsForNonResultRequests_WithoutCallingTheHandler()
    {
        var behavior = new DateRangeValidationBehavior<ProcessDailyAttendanceCommand, int>();
        var called = false;

        var act = () => behavior.Handle(new ProcessDailyAttendanceCommand(Day, Day.AddDays(-1)), _ => { called = true; return Task.FromResult(0); }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDateRangeException>();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Behavior_ReturnsAFailureForResultRequests()
    {
        var behavior = new DateRangeValidationBehavior<GetTardinessAnalysisQuery, Result<TardinessAnalysisDto>>();

        var result = await behavior.Handle(new GetTardinessAnalysisQuery(Day, Day.AddDays(400)),
            _ => throw new InvalidOperationException("no debe llamarse"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("excede el máximo");
    }

    [Fact]
    public async Task Handler_ValidatesEvenWhenInvokedDirectly_AndTouchesNothing()
    {
        var h = new AttendanceRecalculationHarness();

        var act = () => h.RecalculateAsync(DateTime.MinValue, Day);

        await act.Should().ThrowAsync<InvalidDateRangeException>();
        h.Store.SaveChangesCount.Should().Be(0);
    }
}
