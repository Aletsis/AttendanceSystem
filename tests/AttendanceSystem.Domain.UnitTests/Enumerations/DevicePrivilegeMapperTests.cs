using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Enumerations;

public class DevicePrivilegeMapperTests
{
    [Fact]
    public void GetSupportedPrivileges_ForHikvision_ShouldReturnUserAndAdminOnly()
    {
        var privileges = DevicePrivilegeMapper.GetSupportedPrivileges(DeviceBrand.Hikvision);

        privileges.Should().HaveCount(2);
        privileges.Select(p => p.Value).Should().Contain(new[] { DevicePrivilege.User, DevicePrivilege.Admin });
        privileges.Select(p => p.Value).Should().NotContain(DevicePrivilege.Registrar);
        privileges.Select(p => p.Value).Should().NotContain(DevicePrivilege.SuperAdmin);
    }

    [Fact]
    public void GetSupportedPrivileges_ForZKTecoSdk_ShouldReturnAllFourRolesWithSdkSuperAdmin()
    {
        var privileges = DevicePrivilegeMapper.GetSupportedPrivileges(DeviceBrand.ZKTeco, DeviceDownloadMethod.Sdk);

        privileges.Should().HaveCount(4);
        privileges.Select(p => p.Value).Should().Contain(new[]
        {
            DevicePrivilege.User,
            DevicePrivilege.Registrar,
            DevicePrivilege.Admin,
            DevicePrivilege.SuperAdmin
        });

        var superAdmin = privileges.First(p => p.Value == DevicePrivilege.SuperAdmin);
        superAdmin.ProtocolValue.Should().Be(3);
    }

    [Fact]
    public void GetSupportedPrivileges_ForZKTecoAdms_ShouldReturnAllFourRolesWithAdmsSuperAdmin()
    {
        var privileges = DevicePrivilegeMapper.GetSupportedPrivileges(DeviceBrand.ZKTeco, DeviceDownloadMethod.Adms);

        privileges.Should().HaveCount(4);
        var superAdmin = privileges.First(p => p.Value == DevicePrivilege.SuperAdmin);
        superAdmin.ProtocolValue.Should().Be(14);
    }

    [Theory]
    [InlineData(DevicePrivilege.SuperAdmin, DevicePrivilege.Admin)]
    [InlineData(DevicePrivilege.Registrar, DevicePrivilege.User)]
    [InlineData(DevicePrivilege.Admin, DevicePrivilege.Admin)]
    [InlineData(DevicePrivilege.User, DevicePrivilege.User)]
    public void NormalizeForDevice_Hikvision_ShouldNormalizeUnsupportedRoles(DevicePrivilege input, DevicePrivilege expected)
    {
        var result = DevicePrivilegeMapper.NormalizeForDevice(DeviceBrand.Hikvision, input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(DevicePrivilege.SuperAdmin, DevicePrivilege.SuperAdmin)]
    [InlineData(DevicePrivilege.Registrar, DevicePrivilege.Registrar)]
    [InlineData(DevicePrivilege.Admin, DevicePrivilege.Admin)]
    [InlineData(DevicePrivilege.User, DevicePrivilege.User)]
    public void NormalizeForDevice_ZKTeco_ShouldPreserveAllRoles(DevicePrivilege input, DevicePrivilege expected)
    {
        var result = DevicePrivilegeMapper.NormalizeForDevice(DeviceBrand.ZKTeco, input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(DevicePrivilege.User, 0)]
    [InlineData(DevicePrivilege.Registrar, 1)]
    [InlineData(DevicePrivilege.Admin, 2)]
    [InlineData(DevicePrivilege.SuperAdmin, 14)]
    public void MapToProtocolValue_ZKTecoAdms_ShouldMapCorrectly(DevicePrivilege privilege, int expectedProtocol)
    {
        var protocolVal = DevicePrivilegeMapper.MapToProtocolValue(DeviceBrand.ZKTeco, DeviceDownloadMethod.Adms, privilege);
        protocolVal.Should().Be(expectedProtocol);
    }

    [Theory]
    [InlineData(0, DevicePrivilege.User)]
    [InlineData(1, DevicePrivilege.Registrar)]
    [InlineData(2, DevicePrivilege.Admin)]
    [InlineData(14, DevicePrivilege.SuperAdmin)]
    [InlineData(3, DevicePrivilege.SuperAdmin)]
    public void MapFromProtocolValue_ZKTecoAdms_ShouldMapBackCorrectly(int protocolValue, DevicePrivilege expectedPrivilege)
    {
        var privilege = DevicePrivilegeMapper.MapFromProtocolValue(DeviceBrand.ZKTeco, DeviceDownloadMethod.Adms, protocolValue);
        privilege.Should().Be(expectedPrivilege);
    }

    [Theory]
    [InlineData(DevicePrivilege.User, "normal")]
    [InlineData(DevicePrivilege.Registrar, "normal")]
    [InlineData(DevicePrivilege.Admin, "admin")]
    [InlineData(DevicePrivilege.SuperAdmin, "admin")]
    public void MapToHikvisionUserType_ShouldMapCorrectly(DevicePrivilege privilege, string expectedType)
    {
        var userType = DevicePrivilegeMapper.MapToHikvisionUserType(privilege);
        userType.Should().Be(expectedType);
    }

    [Theory]
    [InlineData("admin", DevicePrivilege.Admin)]
    [InlineData("Admin", DevicePrivilege.Admin)]
    [InlineData("ADMIN", DevicePrivilege.Admin)]
    [InlineData("normal", DevicePrivilege.User)]
    [InlineData("other", DevicePrivilege.User)]
    [InlineData(null, DevicePrivilege.User)]
    public void MapFromHikvisionUserType_ShouldMapBackCorrectly(string? userType, DevicePrivilege expectedPrivilege)
    {
        var privilege = DevicePrivilegeMapper.MapFromHikvisionUserType(userType);
        privilege.Should().Be(expectedPrivilege);
    }
}
