// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Azure.ResourceManager;
using Azure.ResourceManager.ResilienceManagement;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Services;

public sealed class ContractRequestPolicyTests
{
    private const string Endpoint = "https://eastus2euap.management.azure.com";
    private const string RoutingLocation = "test-stamp";
    private const string GoalPath =
        "/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.AzureResilienceManagement/goalAssignments/goal1";
    private const string DrillPath =
        "/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.AzureResilienceManagement/drills/drill1";
    private const string RecoveryPlanPath =
        "/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.AzureResilienceManagement/recoveryPlans/plan1";

    [Theory]
    [InlineData("PUT", "", """{"properties":{"serviceLevelResources":[]}}""")]
    [InlineData("GET", "", null)]
    [InlineData("GET", "/goalResources/00000000-0000-0000-0000-000000000001", null)]
    [InlineData("POST", "/recommendCapacity", """{"resourceIds":["/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Storage/storageAccounts/a1"]}""")]
    [InlineData("POST", "/refreshGoalResources", null)]
    [InlineData("POST", "/updateGoalResources", """{"resources":[]}""")]
    public async Task GoalPolicy_PreservesTransportContract(
        string method,
        string suffix,
        string? body)
    {
        BinaryData? replacement = method == "PUT"
            ? BinaryData.FromString(body!)
            : null;
        CapturedRequest request = await SendAsync(
            GoalPath + suffix,
            method,
            body,
            new GoalAssignmentContractRequestPolicy(replacement));

        Assert.Equal(new HttpMethod(method), request.Method);
        Assert.Equal(Endpoint, request.Uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal(GoalPath + suffix, request.Uri.AbsolutePath);
        Assert.Equal(
            ResilienceManagementApiVersions.Current,
            System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["api-version"]);
        Assert.Equal(RoutingLocation, request.RoutingLocation);
        Assert.Equal(body, request.Body);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/drillResources/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/drillRuns/run1")]
    [InlineData("POST", "/drillRuns/run1/failover")]
    [InlineData("POST", "/drillRuns/run1/reprotect")]
    public async Task DrillPolicy_PreservesTransportContract(
        string method,
        string suffix)
    {
        const string body = """{"properties":{}}""";
        string? requestBody = method == "POST" ? body : null;
        CapturedRequest request = await SendAsync(
            DrillPath + suffix,
            method,
            requestBody,
            new DrillContractRequestPolicy());

        Assert.Equal(new HttpMethod(method), request.Method);
        Assert.Equal(Endpoint, request.Uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal(DrillPath + suffix, request.Uri.AbsolutePath);
        Assert.Equal(
            ResilienceManagementApiVersions.Current,
            System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["api-version"]);
        Assert.Equal(RoutingLocation, request.RoutingLocation);
        Assert.Equal(requestBody, request.Body);
    }

    [Fact]
    public async Task DrillPolicy_PatchUsesCanonicalBodyAndCurrentContract()
    {
        const string originalBody = """{"properties":{"sliMonitoringProperties":{"slis":[]}}}""";
        const string canonicalBody = """{"properties":{"sliMonitoringProperties":{"slis":[{"sliId":"/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.Monitor/slis/availability","type":"Availability"}]}}}""";

        CapturedRequest request = await SendAsync(
            DrillPath,
            "PATCH",
            originalBody,
            new DrillContractRequestPolicy(
                BinaryData.FromString(canonicalBody)));

        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal(canonicalBody, request.Body);
        Assert.Equal(
            ResilienceManagementApiVersions.Current,
            System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["api-version"]);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/recoveryResources/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/failover")]
    [InlineData("GET", "/recoveryJobs/00000000-0000-0000-0000-000000000002")]
    [InlineData("GET", "/recoveryJobs/00000000-0000-0000-0000-000000000002/recoveryJobResources/00000000-0000-0000-0000-000000000003")]
    public async Task RecoveryPolicy_UsesCurrentContractForParentChildrenAndActions(
        string method,
        string suffix)
    {
        CapturedRequest request = await SendAsync(
            RecoveryPlanPath + suffix,
            method,
            body: null,
            new RecoveryContractRequestPolicy());

        Assert.Equal(new HttpMethod(method), request.Method);
        Assert.Equal(
            $"{Endpoint}{RecoveryPlanPath}{suffix}?api-version={ResilienceManagementApiVersions.Current}",
            request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task RecoveryPolicy_UsesCurrentContractForOperationStatusPolling()
    {
        const string path =
            "/providers/Microsoft.AzureResilienceManagement/locations/eastus2euap/operationStatuses/00000000-0000-0000-0000-000000000004";
        CapturedRequest request = await SendAsync(
            path,
            "GET",
            body: null,
            new RecoveryContractRequestPolicy());

        Assert.Equal(
            $"{Endpoint}{path}?api-version={ResilienceManagementApiVersions.Current}",
            request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task RecoverySdkParentGet_UsesCurrentContractAtTransport()
    {
        using var handler = new ContractCaptureHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        ArmClientOptions options =
            ResilienceManagementService.CreateRecoveryContractArmClientOptions();
        options.Transport = new HttpClientTransport(client);
        TokenCredential credential = Substitute.For<TokenCredential>();
        credential.GetTokenAsync(
                Arg.Any<TokenRequestContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<AccessToken>(
                new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1))));
        var armClient = new ArmClient(credential, defaultSubscriptionId: null, options);
        ResourceIdentifier recoveryPlanId =
            RecoveryPlanResource.CreateResourceIdentifier("sg1", "plan1");

        await Assert.ThrowsAsync<RequestFailedException>(
            () => armClient.GetRecoveryPlanResource(recoveryPlanId).GetAsync(
                TestContext.Current.CancellationToken));

        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(RecoveryPlanPath, request.Uri.AbsolutePath);
        Assert.Equal(
            ResilienceManagementApiVersions.Current,
            System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["api-version"]);
    }

    [Fact]
    public void NonIdempotentClientOptions_DisableAutomaticReplay()
    {
        ArmClientOptions drillOptions =
            ResilienceManagementService.CreateDrillContractArmClientOptions(
                disableRetries: true);
        ArmClientOptions goalOptions =
            ResilienceManagementService.CreateGoalContractArmClientOptions(
                disableRetries: true);
        ArmClientOptions recoveryOptions =
            ResilienceManagementService.CreateRecoveryContractArmClientOptions(
                disableRetries: true);

        Assert.Equal(0, drillOptions.Retry.MaxRetries);
        Assert.Equal(0, goalOptions.Retry.MaxRetries);
        Assert.Equal(0, recoveryOptions.Retry.MaxRetries);
        Assert.Equal(
            0,
            ResilienceManagementService.CreateNonIdempotentArmClientOptions()
                .Retry.MaxRetries);
    }

    private static async Task<CapturedRequest> SendAsync(
        string path,
        string method,
        string? body,
        HttpPipelinePolicy contractPolicy)
    {
        using var handler = new ContractCaptureHandler();
        using var client = new HttpClient(handler);
        var options = new ArmClientOptions
        {
            Transport = new HttpClientTransport(client)
        };
        HttpPipeline pipeline = HttpPipelineBuilder.Build(
            options,
            new ResilienceManagementRoutingPolicy(Endpoint, RoutingLocation),
            contractPolicy);
        using HttpMessage message = pipeline.CreateMessage();
        message.Request.Method = RequestMethod.Parse(method);
        message.Request.Uri.Reset(
            new Uri($"https://management.azure.com{path}?api-version=2026-04-01-preview"));
        if (body is not null)
        {
            message.Request.Content = RequestContent.Create(BinaryData.FromString(body));
            message.Request.Headers.SetValue("Content-Type", "application/json");
        }

        await pipeline.SendAsync(message, TestContext.Current.CancellationToken);
        return Assert.Single(handler.Requests);
    }
}
