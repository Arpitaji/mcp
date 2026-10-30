// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.ResilienceManagement.Options.Goals.Assignments;

public sealed class GoalAssignmentCreateOptions : IGoalAssignmentOptions
{
    [Option(Description = ResilienceManagementOptionDescriptions.ServiceGroup)]
    public required string ServiceGroup { get; set; }

    [Option(Description = ResilienceManagementOptionDescriptions.GoalAssignment)]
    public required string GoalAssignment { get; set; }

    [Option(Description = "Whether the goal requires zonal resiliency. Exactly one of this option and --require-regional-resiliency must be true.")]
    public required bool? RequireZonalResiliency { get; set; }

    [Option(Description = "Whether the goal requires regional resiliency. Exactly one of this option and --require-zonal-resiliency must be true.")]
    public required bool? RequireRegionalResiliency { get; set; }

    [Option(Description = "ISO 8601 target recovery point objective. Required for a Regional goal and rejected for a Zonal goal.")]
    public string? RegionalRecoveryPointObjective { get; set; }

    [Option(Description = "ISO 8601 target recovery time objective. Required for a Regional goal and rejected for a Zonal goal.")]
    public string? RegionalRecoveryTimeObjective { get; set; }

    [Option(Description = "ARM IDs of service-level indicator resources associated with the assignment. Omit for no service-level targets.")]
    public string[]? ServiceLevelResourceIds { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }
}
