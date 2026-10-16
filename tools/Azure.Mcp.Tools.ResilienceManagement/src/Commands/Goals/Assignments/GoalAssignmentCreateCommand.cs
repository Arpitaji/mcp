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
    Id = "8a68b97e-2818-4bd8-9da2-4a0ff2511550",
    Name = "create",
    Title = "Create or Update Resilience Goal Assignment",
    Description = "Creates or idempotently updates a resilience goal assignment, waits for provisioning to complete, and returns the authoritative assignment. When goal templates are deprecated, omit --goal-template-id to use the service group ID as the provider-supported placeholder.",
    OperationPlane = ToolOperationPlane.Control,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class GoalAssignmentCreateCommand(
    ILogger<GoalAssignmentCreateCommand> logger,
    IResilienceManagementService resilienceManagementService)
    : AuthenticatedCommand<GoalAssignmentCreateOptions, GoalAssignmentCreateCommand.GoalAssignmentCreateCommandResult>
{
    private readonly ILogger<GoalAssignmentCreateCommand> _logger = logger;
    private readonly IResilienceManagementService _resilienceManagementService = resilienceManagementService;

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context,
        GoalAssignmentCreateOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            GoalAssignmentInfo assignment =
                await _resilienceManagementService.CreateGoalAssignmentAsync(
                    options.ServiceGroup,
                    options.GoalAssignment,
                    options.GoalTemplateId,
                    options.Tenant,
                    cancellationToken);
            context.Response.Results = ResponseResult.Create(
                new GoalAssignmentCreateCommandResult(assignment),
                ResilienceManagementJsonContext.Default.GoalAssignmentCreateCommandResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error creating goal assignment. ServiceGroup: {ServiceGroup}, GoalAssignment: {GoalAssignment}.",
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
        TimeoutException => "The goal assignment create request timed out. Check the assignment state before trying again.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Conflict =>
            "The goal assignment conflicts with the current provider state.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Forbidden =>
            "Authorization failed creating the goal assignment. Verify you have the required permissions.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.NotFound =>
            "The service group or goal template was not found.",
        RequestFailedException =>
            "The goal assignment request failed. Verify the service group, goal template, and enrollment.",
        _ => base.GetErrorMessage(ex),
    };

    public sealed record GoalAssignmentCreateCommandResult(GoalAssignmentInfo GoalAssignment);
}
