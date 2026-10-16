// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Tools.ResilienceManagement.Commands;
using Azure.Mcp.Tools.ResilienceManagement.Commands.Goals.Assignments;
using Azure.Mcp.Tools.ResilienceManagement.Models;
using Azure.Mcp.Tools.ResilienceManagement.Options.Goals.Assignments;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Azure.ResourceManager.ResilienceManagement.Models;
using Microsoft.Mcp.Tests.Client;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Goals.Assignments;

public sealed class GoalAssignmentCreateCommandTests
    : CommandUnitTestsBase<GoalAssignmentCreateCommand, IResilienceManagementService>
{
    [Fact]
    public async Task ExecuteAsync_CreatesAssignment()
    {
        Service.CreateGoalAssignmentAsync(
            "sg1",
            "goal1",
            null,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GoalAssignmentInfo("id1", "goal1"));

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(
            response,
            ResilienceManagementJsonContext.Default.GoalAssignmentCreateCommandResult);
        Assert.Equal("goal1", result.GoalAssignment.Name);
    }

    [Fact]
    public async Task ExecuteAsync_PassesExplicitTemplate()
    {
        const string template = "/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.AzureResilienceManagement/goalTemplates/template1";
        Service.CreateGoalAssignmentAsync(
            "sg1",
            "goal1",
            template,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GoalAssignmentInfo("id1", "goal1"));

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1",
            "--goal-template-id", template);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).CreateGoalAssignmentAsync(
            "sg1",
            "goal1",
            template,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}

public sealed class GoalAssignmentUpdateResourcesCommandTests
    : CommandUnitTestsBase<GoalAssignmentUpdateResourcesCommand, IResilienceManagementService>
{
    private const string ResourceId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/storage1";
    private const string Resources =
        "[{\"properties\":{\"resourceArmId\":\"" + ResourceId + "\",\"highAvailabilityGoalParticipation\":\"Excluded\",\"highAvailabilityAttestationStatus\":\"NotAttested\",\"disasterRecoveryGoalParticipation\":\"Included\",\"disasterRecoveryAttestationStatus\":\"NotAttested\"}}]";

    [Fact]
    public async Task ExecuteAsync_SubmitsValidatedResources()
    {
        Service.UpdateGoalAssignmentResourcesAsync(
            "sg1",
            "goal1",
            Arg.Any<UpdateGoalResourceContent>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GoalAssignmentOperationResult(
                "operation-1",
                "Accepted",
                false));

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1",
            "--resources", Resources);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(
            response,
            ResilienceManagementJsonContext.Default.GoalAssignmentUpdateResourcesCommandResult);
        Assert.Equal("operation-1", result.Operation.OperationId);
        Assert.False(result.Operation.HasCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDuplicateResourceArmIds()
    {
        string resources =
            $"[{Resources[1..^1]},{Resources[1..^1]}]";

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1",
            "--resources", resources);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("more than once", response.Message);
        await Service.DidNotReceive().UpdateGoalAssignmentResourcesAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<UpdateGoalResourceContent>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateContent_RejectsForeignGoalResourceId()
    {
        var options = new GoalAssignmentUpdateResourcesOptions
        {
            ServiceGroup = "sg1",
            GoalAssignment = "goal1",
            Resources =
                "[{\"id\":\"/providers/Microsoft.Management/serviceGroups/other/providers/Microsoft.AzureResilienceManagement/goalAssignments/goal1/goalResources/member1\",\"properties\":{\"resourceArmId\":\"" +
                ResourceId +
                "\"}}]",
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => GoalAssignmentUpdateResourcesCommand.CreateContent(options));

        Assert.Contains("selected service group", exception.Message);
    }
}

public sealed class GoalAssignmentRefreshResourcesCommandTests
    : CommandUnitTestsBase<GoalAssignmentRefreshResourcesCommand, IResilienceManagementService>
{
    [Fact]
    public async Task ExecuteAsync_StartsRefresh()
    {
        Service.RefreshGoalAssignmentResourcesAsync(
            "sg1",
            "goal1",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GoalAssignmentOperationResult(
                "operation-2",
                "Accepted",
                false));

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(
            response,
            ResilienceManagementJsonContext.Default.GoalAssignmentRefreshResourcesCommandResult);
        Assert.Equal("operation-2", result.Operation.OperationId);
    }
}

public sealed class GoalAssignmentDeleteCommandTests
    : CommandUnitTestsBase<GoalAssignmentDeleteCommand, IResilienceManagementService>
{
    [Fact]
    public async Task ExecuteAsync_ReportsWhetherAssignmentExisted()
    {
        Service.DeleteGoalAssignmentAsync(
            "sg1",
            "goal1",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(false);

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(
            response,
            ResilienceManagementJsonContext.Default.GoalAssignmentDeleteCommandResult);
        Assert.False(result.Deleted);
        Assert.Equal("goal1", result.GoalAssignment);
    }
}
