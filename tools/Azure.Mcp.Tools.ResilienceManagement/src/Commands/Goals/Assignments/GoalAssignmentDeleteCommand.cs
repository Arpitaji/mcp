// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Serialization;
using Azure.Mcp.Tools.ResilienceManagement.Options.Goals.Assignments;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.ResilienceManagement.Commands.Goals.Assignments;

[CommandMetadata(
    Id = "00e52dae-e33c-42cc-814c-5bac1d8e8f91",
    Name = "delete",
    Title = "Delete Resilience Goal Assignment",
    Description = "Deletes the named goal assignment and all provider-owned Goal Resource children. Use only during explicitly approved cleanup.",
    OperationPlane = ToolOperationPlane.Control,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class GoalAssignmentDeleteCommand(
    ILogger<GoalAssignmentDeleteCommand> logger,
    IResilienceManagementService resilienceManagementService)
    : AuthenticatedCommand<GoalAssignmentActionOptions, GoalAssignmentDeleteCommand.GoalAssignmentDeleteCommandResult>
{
    private readonly ILogger<GoalAssignmentDeleteCommand> _logger = logger;
    private readonly IResilienceManagementService _resilienceManagementService = resilienceManagementService;

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context,
        GoalAssignmentActionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            bool deleted = await _resilienceManagementService.DeleteGoalAssignmentAsync(
                options.ServiceGroup,
                options.GoalAssignment,
                options.Tenant,
                cancellationToken);
            context.Response.Results = ResponseResult.Create(
                new GoalAssignmentDeleteCommandResult(deleted, options.GoalAssignment),
                ResilienceManagementJsonContext.Default.GoalAssignmentDeleteCommandResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deleting goal assignment. ServiceGroup: {ServiceGroup}, GoalAssignment: {GoalAssignment}.",
                options.ServiceGroup,
                options.GoalAssignment);
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override HttpStatusCode GetStatusCode(Exception ex) => ex is TimeoutException
        ? HttpStatusCode.GatewayTimeout
        : base.GetStatusCode(ex);

    protected override string GetErrorMessage(Exception ex) => ex switch
    {
        TimeoutException => "The goal assignment delete request timed out. Check whether the assignment still exists.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Conflict =>
            "The goal assignment cannot be deleted while another assignment operation is in progress.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Forbidden =>
            "Authorization failed deleting the goal assignment.",
        RequestFailedException =>
            "The goal assignment delete request failed. Verify the assignment and service group.",
        _ => base.GetErrorMessage(ex),
    };

    public sealed record GoalAssignmentDeleteCommandResult(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool Deleted,
        string GoalAssignment);
}
