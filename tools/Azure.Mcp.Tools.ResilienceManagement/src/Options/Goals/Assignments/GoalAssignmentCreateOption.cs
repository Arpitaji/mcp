// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.ResilienceManagement.Options.Goals.Assignments;

public sealed class GoalAssignmentCreateOptions
{
    [Option(Description = ResilienceManagementOptionDescriptions.ServiceGroup)]
    public required string ServiceGroup { get; set; }

    [Option(Description = "The name of the goal assignment to create or update.")]
    public required string GoalAssignment { get; set; }

    [Option(Description = "The full ARM resource ID of the goal template. When omitted, the service group ID is used for deployments where goal templates are deprecated.")]
    public string? GoalTemplateId { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }
}
