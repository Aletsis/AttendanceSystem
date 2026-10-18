using System.Net;
using System.Text;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Infrastructure.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Adapters;

public class HikvisionDeviceClientTests
{
    private readonly Mock<ILogger<HikvisionDeviceClient>> _loggerMock = new();

    private const string DeviceInfoXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<DeviceInfo>
    <deviceName>Terminal Acceso</deviceName>
    <serialNumber>DS-K1T804-XYZ</serialNumber>
    <model>DS-K1T804</model>
    <firmwareVersion>V1.3.4</firmwareVersion>
</DeviceInfo>";

    private async Task<HikvisionDeviceClient> CreateConnectedClientWithHandler(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://192.168.1.100")
        };
        var client = new HikvisionDeviceClient(_loggerMock.Object, httpClient);
        await client.ConnectAsync("192.168.1.100", 80, "admin", "12345");
        return client;
    }

    [Fact]
    public async Task ClearLogsAsync_AlwaysReturnsFalse_ToPreventAccidentalLogErasure()
    {
        // Arrange (H03 / H16)
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("deviceInfo")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(DeviceInfoXml, Encoding.UTF8, "application/xml")
            });

        var client = await CreateConnectedClientWithHandler(handlerMock.Object);

        // Act
        var result = await client.ClearLogsAsync("DEV01");

        // Assert
        result.Should().BeFalse("Hikvision no soporta borrado remoto selectivo y debe prevenir borrado accidental");
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.AbsolutePath.Contains("deviceInfo")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAttendanceLogsAsync_WhenNoMatches_ReturnsSuccessEmpty()
    {
        // Arrange
        var xmlResponse = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<AcsEventSearchResult>
    <responseStatusStrg>NO MATCH</responseStatusStrg>
    <numOfMatches>0</numOfMatches>
    <totalMatches>0</totalMatches>
</AcsEventSearchResult>";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("deviceInfo")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(DeviceInfoXml, Encoding.UTF8, "application/xml")
            });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("AcsEvent")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(xmlResponse, Encoding.UTF8, "application/xml")
            });

        var client = await CreateConnectedClientWithHandler(handlerMock.Object);

        // Act
        var result = await client.GetAttendanceLogsAsync("DEV01", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        // Assert
        result.Status.Should().Be(DeviceReadStatus.SuccessEmpty);
        result.Records.Should().BeEmpty();
    }

    private static string BuildAcsEventsXml(string statusStr, int numOfMatches, int totalMatches, int startId, int count)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine("<AcsEventSearchResult>");
        sb.AppendLine($"    <responseStatusStrg>{statusStr}</responseStatusStrg>");
        sb.AppendLine($"    <numOfMatches>{numOfMatches}</numOfMatches>");
        sb.AppendLine($"    <totalMatches>{totalMatches}</totalMatches>");
        sb.AppendLine("    <MatchList>");
        for (int i = 0; i < count; i++)
        {
            sb.AppendLine("        <AcsEvent>");
            sb.AppendLine($"            <employeeNoString>{startId + i}</employeeNoString>");
            sb.AppendLine("            <time>2026-10-17T08:00:00Z</time>");
            sb.AppendLine("            <major>5</major>");
            sb.AppendLine("            <minor>75</minor>");
            sb.AppendLine("        </AcsEvent>");
        }
        sb.AppendLine("    </MatchList>");
        sb.AppendLine("</AcsEventSearchResult>");
        return sb.ToString();
    }

    [Fact]
    public async Task GetAttendanceLogsAsync_WhenMultiPage_PaginatesCorrectlyAcrossPages()
    {
        // Arrange: Página 1 devuelve 50 registros (total 75), Página 2 devuelve 25 registros
        var page1Xml = BuildAcsEventsXml("MORE", 50, 75, 101, 50);
        var page2Xml = BuildAcsEventsXml("OK", 25, 75, 151, 25);

        int acsCallCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("deviceInfo")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(DeviceInfoXml, Encoding.UTF8, "application/xml")
            });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("AcsEvent")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                acsCallCount++;
                var content = acsCallCount == 1 ? page1Xml : page2Xml;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(content, Encoding.UTF8, "application/xml")
                };
            });

        var client = await CreateConnectedClientWithHandler(handlerMock.Object);

        // Act
        var result = await client.GetAttendanceLogsAsync("DEV01", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        // Assert
        result.Status.Should().Be(DeviceReadStatus.SuccessWithData);
        result.Records.Should().HaveCount(75);
        acsCallCount.Should().Be(2, "debe haber realizado exactamente 2 llamadas paginadas de eventos");
    }

    [Fact]
    public async Task GetAttendanceLogsAsync_WhenMiddlePageFails_ReturnsPartialStatusWithExistingRecords()
    {
        // Arrange: Página 1 devuelve 50 registros, Página 2 falla con 500 InternalServerError
        var page1Xml = BuildAcsEventsXml("MORE", 50, 100, 101, 50);

        int acsCallCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("deviceInfo")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(DeviceInfoXml, Encoding.UTF8, "application/xml")
            });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsolutePath.Contains("AcsEvent")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                acsCallCount++;
                if (acsCallCount == 1)
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(page1Xml, Encoding.UTF8, "application/xml")
                    };
                }
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError
                };
            });

        var client = await CreateConnectedClientWithHandler(handlerMock.Object);

        // Act
        var result = await client.GetAttendanceLogsAsync("DEV01", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        // Assert
        result.Status.Should().Be(DeviceReadStatus.Partial, "debe marcarse como parcial si una página subsiguiente falla");
        result.Records.Should().HaveCount(50);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
