using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.ValueObjects;

public class ValueObjectsTests
{
    [Fact]
    public void BranchId_ShouldSupportCreationAndEquality()
    {
        var id1 = BranchId.CreateNew();
        var id2 = BranchId.From(id1.Value);
        var id3 = BranchId.CreateNew();

        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
        id1.ToString().Should().Be(id1.Value.ToString());
    }

    [Fact]
    public void DepartmentId_ShouldSupportCreationAndEquality()
    {
        var id1 = DepartmentId.CreateNew();
        var id2 = DepartmentId.From(id1.Value);

        id1.Should().Be(id2);
        id1.ToString().Should().Be(id1.Value.ToString());
    }

    [Fact]
    public void PositionId_ShouldSupportCreationAndEquality()
    {
        var id1 = PositionId.CreateNew();
        var id2 = PositionId.From(id1.Value);

        id1.Should().Be(id2);
        id1.ToString().Should().Be(id1.Value.ToString());
    }

    [Fact]
    public void ShiftId_ShouldSupportCreationAndEquality()
    {
        var id1 = ShiftId.CreateNew();
        var id2 = ShiftId.From(id1.Value);

        id1.Should().Be(id2);
        id1.ToString().Should().Be(id1.Value.ToString());
    }

    [Fact]
    public void DeviceId_WhenEmptyOrWhitespace_ShouldThrowDomainException()
    {
        var act1 = () => DeviceId.From("");
        var act2 = () => DeviceId.From("   ");

        act1.Should().Throw<DomainException>();
        act2.Should().Throw<DomainException>();
    }

    [Fact]
    public void DeviceId_WhenValid_ShouldInstantiate()
    {
        var dev = DeviceId.From("DEV-001");
        dev.Value.Should().Be("DEV-001");
        dev.ToString().Should().Be("DEV-001");
    }
}
