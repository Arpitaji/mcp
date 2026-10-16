// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Tools.ResilienceManagement.Models;
using Azure.Mcp.Tools.ResilienceManagement.Options.Goals.Assignments;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.ResilienceManagement.Commands.Goals.Assignments;

[CommandMetadata(
    Id = "8f14c46f-d573-421f-b9e7-622960bbb9c7",
    Name = "refresh-resources",
    Title = "Refresh Resilience Goal Assignment Resources",
    Description = "Starts Goal Resource discovery and reassessment for a resilience goal assignment and returns the ARM operation ID and current completion state.",
    OperationPlane = ToolOperationPlane.Control,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class GoalAssignmentRefreshResourcesCommand(
    ILogger<GoalAssignmentRefreshResourcesCommand> logger,
    IResilienceManagementService resilienceManagementService)
    : AuthenticatedCommand<GoalAssignmentActionOptions, GoalAssignmentRefreshResourcesCommand.GoalAssignmentRefreshResourcesCommandResult>
{
    private readonly ILogger<GoalAssignmentRefreshResourcesCommand> _logger = logger;
    private readonly IResilienceManagementService _resilienceManagementService = resilienceManagementService;

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context,
        GoalAssignmentActionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            GoalAssignmentOperationResult result =
                await _resilienceManagementService.RefreshGoalAssignmentResourcesAsync(
                    options.ServiceGroup,
                    options.GoalAssignment,
                    options.Tenant,
                    cancellationToken);
            context.Response.Results = ResponseResult.Create(
                new GoalAssignmentRefreshResourcesCommandResult(result),
                ResilienceManagementJsonContext.Default.GoalAssignmentRefreshResourcesCommandResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error refreshing goal assignment resources. ServiceGroup: {ServiceGroup}, GoalAssignment: {GoalAssignment}.",
                options.ServiceGroup,
                options.GoalAssignment);
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override string GetErrorMessage(Exception ex) => ex switch
    {
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Conflict =>
            "Goal Resources cannot be refreshed while another assignment operation is in progress.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Forbidden =>
            "Authorization failed refreshing Goal Resources.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.NotFound =>
            "The goal assignment was not found.",
        RequestFailedException =>
            "The Goal Resource refresh failed. Verify the assignment state and try again.",
        _ => base.GetErrorMessage(ex),
    };

    public sealed record GoalAssignmentRefreshResourcesCommandResult(
        GoalAssignmentOperationResult Operation);
}
