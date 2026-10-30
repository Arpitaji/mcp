// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Tools.ResilienceManagement.Commands.Goals.Assignments;
using Azure.Mcp.Tools.ResilienceManagement.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Goals.Assignments;

public sealed class GoalAssignmentCreateCommandTests : GoalAssignmentCommandTestsBase<GoalAssignmentCreateCommand, GoalAssignmentInfo>
{
    protected override string CommandName => "create";
    protected override GoalAssignmentInfo ExpectedResult => new("assignment-id", "assignment1",
        new("Succeeded", true));
    protected override string[] AdditionalArguments =>
        ["--require-zonal-resiliency", "true", "--require-regional-resiliency", "false", "--service-level-resource-ids", ServiceLevelResourceId];
    protected override Task<GoalAssignmentInfo> ConfigureServiceCall() =>
        Service.CreateGoalAssignmentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

    protected override void AssertPayloadArguments(object?[] arguments)
    {
        Assert.Equal(true, arguments[2]);
        Assert.Equal(false, arguments[3]);
        Assert.Null(arguments[4]);
        Assert.Null(arguments[5]);
        Assert.Equal([ServiceLevelResourceId], Assert.IsAssignableFrom<IReadOnlyList<string>>(arguments[6]));
    }

    protected override void AssertSerializedResult(JsonElement result)
    {
        var assignment = result.GetProperty("goalAssignment");
        Assert.Equal("assignment1", assignment.GetProperty("name").GetString());
        Assert.Equal("assignment-id", assignment.GetProperty("id").GetString());
        Assert.True(assignment.GetProperty("properties").GetProperty("requireZonalResiliency").GetBoolean());
    }

    [Theory]
    [InlineData("--service-group sg1 --goal-assignment assignment1")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency invalid")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency true")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-regional-resiliency true")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency false --require-regional-resiliency false")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency true --require-regional-resiliency true")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency false --require-regional-resiliency true")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency false --require-regional-resiliency true --regional-recovery-point-objective PT5M")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency true --require-regional-resiliency false --regional-recovery-point-objective PT5M --regional-recovery-time-objective PT1H")]
    [InlineData("--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency true --require-regional-resiliency false --service-level-resource-ids invalid")]
    public async Task ExecuteAsync_RejectsMissingBooleanOrInvalidInputWithoutServiceCall(string arguments)
    {
        var response = await ExecuteCommandAsync(arguments);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Empty(Service.ReceivedCalls());
    }

    [Fact]
    public void Constructor_AdvertisesRequiredNonNullableBooleanIntent()
    {
        var zonal = Assert.Single(
            CommandDefinition.Options,
            option => option.Name == "--require-zonal-resiliency");
        var regional = Assert.Single(
            CommandDefinition.Options,
            option => option.Name == "--require-regional-resiliency");

        Assert.True(zonal.Required);
        Assert.Equal(typeof(bool), zonal.ValueType);
        Assert.True(regional.Required);
        Assert.Equal(typeof(bool), regional.ValueType);
    }

    [Fact]
    public async Task FailedOperationWithSuccessfulHttpStatus_IsNotSuccess()
    {
        Service.CreateGoalAssignmentAsync(
            "sg1", "goal1", true, false, null, null, null, null,
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(200, "provider-detail", "RHUBUserErrorServiceGroupMembersLimitExceeded", null));

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "goal1",
            "--require-zonal-resiliency", "true",
            "--require-regional-resiliency", "false");

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Contains("500-member", response.Message);
        Assert.DoesNotContain("provider-detail", response.Message);
        Assert.Null(response.Results);
    }

    [Fact]
    public async Task ExecuteAsync_OmittedTargetsRemainNullAndTrueIsPreserved()
    {
        ConfigureServiceCall().Returns(ExpectedResult);

        var response = await ExecuteCommandAsync(
            "--service-group sg1 --goal-assignment assignment1 --require-zonal-resiliency true --require-regional-resiliency false");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).CreateGoalAssignmentAsync(
            "sg1", "assignment1", true, false, null, null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RegionalGoalPreservesObjectives()
    {
        ConfigureServiceCall().Returns(ExpectedResult);

        var response = await ExecuteCommandAsync(
            "--service-group", "sg1",
            "--goal-assignment", "assignment1",
            "--require-zonal-resiliency", "false",
            "--require-regional-resiliency", "true",
            "--regional-recovery-point-objective", "PT5M",
            "--regional-recovery-time-objective", "PT1H");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).CreateGoalAssignmentAsync(
            "sg1", "assignment1", false, true, "PT5M", "PT1H", null, null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDuplicateTargetsWithoutCallingService()
    {
        var response = await ExecuteCommandAsync("--service-group", "sg1", "--goal-assignment", "assignment1",
            "--require-zonal-resiliency", "true", "--require-regional-resiliency", "false",
            "--service-level-resource-ids", ServiceLevelResourceId, ServiceLevelResourceId.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Empty(Service.ReceivedCalls());
    }

    [Theory]
    [InlineData(ResourceId)]
    [InlineData("/providers/Microsoft.Management/serviceGroups/sg2/providers/Microsoft.Monitor/slis/sli1")]
    [InlineData("/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.Monitor/other/sli1")]
    public async Task ExecuteAsync_RejectsWrongGroupOrTypeWithoutCallingService(string id)
    {
        var response = await ExecuteCommandAsync("--service-group", "sg1", "--goal-assignment", "assignment1",
            "--require-zonal-resiliency", "true", "--require-regional-resiliency", "false",
            "--service-level-resource-ids", id);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Empty(Service.ReceivedCalls());
    }
}
