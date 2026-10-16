// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Mcp.Tools.ResilienceManagement.Models;
using Azure.Mcp.Tools.ResilienceManagement.Options.Goals.Assignments;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Azure.ResourceManager.ResilienceManagement;
using Azure.ResourceManager.ResilienceManagement.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.ResilienceManagement.Commands.Goals.Assignments;

[CommandMetadata(
    Id = "35889030-e4b2-4df4-8f90-eebd61ee40ba",
    Name = "update-resources",
    Title = "Update Resilience Goal Assignment Resources",
    Description = "Submits a bulk Goal Resource update for a resilience goal assignment and returns the ARM operation ID and current completion state. The JSON payload is validated before any request is sent.",
    OperationPlane = ToolOperationPlane.Control,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class GoalAssignmentUpdateResourcesCommand(
    ILogger<GoalAssignmentUpdateResourcesCommand> logger,
    IResilienceManagementService resilienceManagementService)
    : AuthenticatedCommand<GoalAssignmentUpdateResourcesOptions, GoalAssignmentUpdateResourcesCommand.GoalAssignmentUpdateResourcesCommandResult>
{
    private const int MaxPayloadLength = 1_048_576;
    private readonly ILogger<GoalAssignmentUpdateResourcesCommand> _logger = logger;
    private readonly IResilienceManagementService _resilienceManagementService = resilienceManagementService;

    public override void ValidateOptions(
        GoalAssignmentUpdateResourcesOptions options,
        ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);
        try
        {
            _ = CreateContent(options);
        }
        catch (ArgumentException ex)
        {
            validationResult.AddError(ex.Message, "Invalid Goal Resource update.");
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context,
        GoalAssignmentUpdateResourcesOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            GoalAssignmentOperationResult result =
                await _resilienceManagementService.UpdateGoalAssignmentResourcesAsync(
                    options.ServiceGroup,
                    options.GoalAssignment,
                    CreateContent(options),
                    options.Tenant,
                    cancellationToken);
            context.Response.Results = ResponseResult.Create(
                new GoalAssignmentUpdateResourcesCommandResult(result),
                ResilienceManagementJsonContext.Default.GoalAssignmentUpdateResourcesCommandResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating goal assignment resources. ServiceGroup: {ServiceGroup}, GoalAssignment: {GoalAssignment}.",
                options.ServiceGroup,
                options.GoalAssignment);
            HandleException(context, ex);
        }

        return context.Response;
    }

    internal static UpdateGoalResourceContent CreateContent(
        GoalAssignmentUpdateResourcesOptions options)
    {
        if (Encoding.UTF8.GetByteCount(options.Resources ?? string.Empty) > MaxPayloadLength)
        {
            throw new ArgumentException("The Goal Resource JSON payload must not exceed 1 MB.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                options.Resources ??
                throw new ArgumentException("Goal Resources are required."));
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                throw new ArgumentException("Goal Resources must be a non-empty JSON array.");
            }

            var resources = new List<GoalMembersData>();
            var resourceArmIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                ValidateResource(item, options, resourceArmIds);
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(item.GetRawText()));
                var model = new GoalMembersData();
                GoalMembersData parsed = ((IJsonModel<GoalMembersData>)model).Create(
                    ref reader,
                    ModelReaderWriterOptions.Json) ??
                    throw new ArgumentException("A Goal Resource could not be parsed.");
                resources.Add(parsed);
            }

            return new UpdateGoalResourceContent(resources);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Goal Resources must be valid JSON.", ex);
        }
    }

    private static void ValidateResource(
        JsonElement item,
        GoalAssignmentUpdateResourcesOptions options,
        HashSet<string> resourceArmIds)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty("resourceArmId", out JsonElement resourceArmId) ||
            resourceArmId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(resourceArmId.GetString()))
        {
            throw new ArgumentException(
                "Each Goal Resource must contain a non-empty properties.resourceArmId.");
        }

        if (!resourceArmIds.Add(resourceArmId.GetString()!))
        {
            throw new ArgumentException(
                $"Goal Resource '{resourceArmId.GetString()}' appears more than once.");
        }

        if (item.TryGetProperty("id", out JsonElement id) &&
            id.ValueKind != JsonValueKind.Null)
        {
            string expectedPrefix =
                $"/providers/Microsoft.Management/serviceGroups/{options.ServiceGroup}/" +
                "providers/Microsoft.AzureResilienceManagement/goalAssignments/" +
                $"{options.GoalAssignment}/goalResources/";
            if (id.ValueKind != JsonValueKind.String ||
                !id.GetString()!.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Supplied Goal Resource ids must belong to the selected service group and goal assignment.");
            }
        }
    }

    protected override HttpStatusCode GetStatusCode(Exception ex) =>
        ex is ArgumentException
            ? HttpStatusCode.BadRequest
            : base.GetStatusCode(ex);

    protected override string GetErrorMessage(Exception ex) => ex switch
    {
        ArgumentException argumentException => argumentException.Message,
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Conflict =>
            "Goal Resources cannot be updated while another assignment operation is in progress.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Forbidden =>
            "Authorization failed updating Goal Resources.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.NotFound =>
            "The goal assignment was not found.",
        RequestFailedException =>
            "The Goal Resource update failed. Verify the resource payload and assignment state.",
        _ => base.GetErrorMessage(ex),
    };

    public sealed record GoalAssignmentUpdateResourcesCommandResult(
        GoalAssignmentOperationResult Operation);
}
