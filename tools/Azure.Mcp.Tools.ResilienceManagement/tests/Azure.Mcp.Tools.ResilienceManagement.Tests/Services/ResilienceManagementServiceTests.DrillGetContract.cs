// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Mcp.Core.Services.Azure;
using Azure.Mcp.Tools.ResilienceManagement.Commands.Drills;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Services.Azure.Authentication;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Services;

public sealed partial class ResilienceManagementServiceTests
{
    [Theory]
    [InlineData("healthModelMonitoringProperties", null)]
    [InlineData("healthModelMonitoringProperties", "test-stamp")]
    [InlineData("sliMonitoringProperties", null)]
    [InlineData("sliMonitoringProperties", "test-stamp")]
    public async Task GetDrillCommand_UsesCurrentContractAndPreservesMonitoring(
        string monitoringProperty, string? routingLocation)
    {
        const string endpoint = "https://eastus2euap.management.azure.com";
        string? oldEndpoint = Environment.GetEnvironmentVariable(
            ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable);
        string? oldLocation = Environment.GetEnvironmentVariable(
            ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable, endpoint);
            Environment.SetEnvironmentVariable(ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable, routingLocation);
            using var handler = new DrillGetContractHandler(monitoringProperty);
            ResilienceManagementService service = CreateService(handler);
            Models.DrillInfo result = await service.GetDrillAsync(
                "sg1",
                "drill1",
                cancellationToken: TestContext.Current.CancellationToken);
            string json = JsonSerializer.Serialize(
                result,
                Commands.ResilienceManagementJsonContext.Default.DrillInfo);
            using JsonDocument serialized = JsonDocument.Parse(json);
            JsonElement drill = serialized.RootElement;
            Assert.Equal(DrillGetContractHandler.DrillId, drill.GetProperty("id").GetString());
            JsonElement properties = drill.GetProperty("properties");
            Assert.Equal("Succeeded", properties.GetProperty("provisioningState").GetString());
            Assert.Equal("Ready", properties.GetProperty("executionReadiness").GetString());
            Assert.Equal(JsonValueKind.Null, properties.GetProperty("monitoringProperties").ValueKind);
            JsonElement attention = properties.GetProperty("attentionReason");
            Assert.Equal("Complete", attention.GetProperty("initialConfig").GetString());
            Assert.Equal("Exists", attention.GetProperty("includedResourceInDrill").GetString());
            Assert.Equal("Exists", attention.GetProperty("healthModelExists").GetString());
            Assert.False(attention.GetProperty("monitoringSourceNotConfigured").GetBoolean());
            JsonElement monitoring = properties.GetProperty(monitoringProperty);
            JsonElement identity = monitoring.GetProperty("identity");
            Assert.Equal("SystemAssigned", identity.GetProperty("type").GetString());
            Assert.False(identity.TryGetProperty("userAssignedIdentity", out _));
            if (monitoringProperty == "healthModelMonitoringProperties")
            {
                Assert.Equal(DrillGetContractHandler.ModelId, monitoring.GetProperty("healthModelId").GetString());
            }
            else
            {
                JsonElement sli = Assert.Single(monitoring.GetProperty("slis").EnumerateArray());
                Assert.Equal(DrillGetContractHandler.SliId, sli.GetProperty("sliId").GetString());
                Assert.Equal("Availability", sli.GetProperty("type").GetString());
            }

            var request = Assert.Single(handler.Requests);
            Assert.Equal(endpoint, request.Uri.GetLeftPart(UriPartial.Authority));
            Assert.Equal(DrillGetContractHandler.DrillId, request.Uri.AbsolutePath);
            Assert.Equal("2026-10-31-preview",
                System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["api-version"]);
            Assert.Equal(routingLocation, request.RoutingLocation);
            Assert.False(request.HasContent);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable, oldEndpoint);
            Environment.SetEnvironmentVariable(ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable, oldLocation);
        }
    }

    [Theory]
    [InlineData("healthModelMonitoringProperties")]
    [InlineData("sliMonitoringProperties")]
    public async Task GetDrillContractFixture_AprilOmitsMonitoring(string monitoringProperty)
    {
        using var handler = new DrillGetContractHandler(monitoringProperty);
        using var client = new HttpClient(handler);
        using HttpResponseMessage response = await client.GetAsync(
            $"https://management.azure.com{DrillGetContractHandler.DrillId}?api-version=2026-04-01-preview",
            TestContext.Current.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement properties = document.RootElement.GetProperty("properties");
        Assert.False(properties.TryGetProperty(monitoringProperty, out _));
        Assert.False(properties.GetProperty("attentionReason").TryGetProperty("healthModelExists", out _));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/drillResources/resource1")]
    [InlineData("/drillRuns/run1")]
    [InlineData("/drillRuns/run1/failover")]
    [InlineData("/drillRuns/run1/reprotect")]
    public async Task DrillContractPolicy_UsesCurrentContractForParentChildrenAndActions(
        string suffix)
    {
        using var handler = new DrillGetContractHandler("healthModelMonitoringProperties");
        using var client = new HttpClient(handler);
        var options = new ArmClientOptions { Transport = new HttpClientTransport(client) };
        HttpPipeline pipeline = HttpPipelineBuilder.Build(options, new DrillContractRequestPolicy());
        using HttpMessage message = pipeline.CreateMessage();
        message.Request.Method = RequestMethod.Get;
        message.Request.Uri.Reset(new Uri(
            $"https://management.azure.com{DrillGetContractHandler.DrillId}{suffix}?api-version=2026-04-01-preview"));
        await pipeline.SendAsync(message, TestContext.Current.CancellationToken);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("2026-10-31-preview",
            System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["api-version"]);
        Assert.Equal(DrillGetContractHandler.DrillId + suffix, request.Uri.AbsolutePath);
        Assert.False(request.HasContent);
    }

    [Theory]
    [InlineData("FaultInjection", "Start", "DrillResource")]
    [InlineData("Failover", "Retry", "DrillResource")]
    [InlineData("FaultInjectionReverse", "Start", "RecoveryResource")]
    [InlineData("FailoverReverse", "Retry", "RecoveryResource")]
    public void ResolveFailoverSelectedResourceKind_UsesCurrentLifecycleStage(
        string stage,
        string verb,
        string expected)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            properties = new
            {
                supportedVerbsForStage = new[]
                {
                    new { drillRunStage = stage, supportedVerbs = new[] { verb } }
                },
                linkedJobIds = new[] { "job1" }
            }
        }));

        Assert.Equal(expected, ResilienceManagementService
            .ResolveFailoverSelectedResourceKind(document.RootElement)
            .ToString());
    }

    [Fact]
    public void CreateDrillRunReprotectWireContent_DoesNotRetainPriorSelection()
    {
        const string resourceId = "/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.AzureResilienceManagement/recoveryPlans/plan1/recoveryResources/22222222-2222-2222-2222-222222222222";
        BinaryData first = ResilienceManagementService.CreateDrillRunReprotectWireContent(
            [resourceId]);
        BinaryData retry = ResilienceManagementService.CreateDrillRunReprotectWireContent(
            null);

        Assert.Contains(resourceId, first.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("selectedResourceIds", retry.ToString(), StringComparison.Ordinal);
    }

    private static ResilienceManagementService CreateService(HttpMessageHandler handler)
    {
        var azureService = Substitute.For<IAzureService>();
        var cloudConfiguration = Substitute.For<IAzureCloudConfiguration>();
        cloudConfiguration.ArmEnvironment.Returns(ArmEnvironment.AzurePublicCloud);
        azureService.CloudConfiguration.Returns(cloudConfiguration);
        azureService.ResolveTenantIdAsync(
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns((string?)null);
        azureService.IsSubscriptionId(Arg.Any<string>())
            .Returns(call => Guid.TryParse(call.Arg<string>(), out _));
        azureService.When(service => service.ConfigureArmClientOptions(
            Arg.Any<ArmClientOptions>()))
            .Do(call => call.Arg<ArmClientOptions>().Transport =
                new HttpClientTransport(new HttpClient(handler)));
        azureService.GetTokenCredentialAsync(
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(new FakeTokenCredential());
        return new ResilienceManagementService(azureService);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
