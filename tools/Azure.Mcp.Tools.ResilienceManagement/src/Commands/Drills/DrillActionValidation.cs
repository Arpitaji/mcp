// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.ResourceManager.ResilienceManagement;
using Microsoft.Mcp.Core.Commands;

namespace Azure.Mcp.Tools.ResilienceManagement.Commands.Drills;

internal static class DrillActionValidation
{
    private static readonly ResourceType ServiceGroupResourceType = new("Microsoft.Management/serviceGroups");

    public static void ValidateSelectedResourceIds(
        IReadOnlyList<string>? resourceIds,
        string serviceGroup,
        ValidationResult validationResult,
        string drill,
        DrillSelectedResourceKind? expectedKind = null)
    {
        if (resourceIds is null)
        {
            return;
        }

        if (resourceIds.Count == 0)
        {
            validationResult.Errors.Add("Selected resource IDs must not be empty when supplied.");
            return;
        }

        DrillSelectedResourceKind? actualKind = null;
        foreach (string resourceId in resourceIds)
        {
            DrillSelectedResourceKind? candidate =
                GetSelectedResourceKind(resourceId, serviceGroup, drill);
            if (candidate is null)
            {
                validationResult.Errors.Add(
                    "Each selected resource ID must be a full drill-resource ID under the requested drill " +
                    "or a qualified RO recovery-resource ID under a recovery plan in the requested service group, " +
                    "with a GUID resource name.");
                return;
            }

            if (actualKind is not null && actualKind != candidate)
            {
                validationResult.Errors.Add(
                    "Selected resource IDs cannot mix drill resources and RO recovery resources.");
                return;
            }

            actualKind = candidate;
        }

        if (expectedKind is not null && actualKind != expectedKind)
        {
            string expected = expectedKind == DrillSelectedResourceKind.DrillResource
                ? "drill-resource IDs for forward fault injection or failover"
                : "qualified RO recovery-resource IDs for reverse operations";
            validationResult.Errors.Add($"Selected resource IDs must use {expected}.");
        }
    }

    public static bool IsDrillResourceId(string resourceId, string serviceGroup, string drill)
        => GetSelectedResourceKind(resourceId, serviceGroup, drill) ==
            DrillSelectedResourceKind.DrillResource;

    internal static DrillSelectedResourceKind? GetSelectedResourceKind(
        string resourceId,
        string serviceGroup,
        string drill)
    {
        try
        {
            var parsed = new ResourceIdentifier(resourceId);
            ResourceIdentifier? parentId = parsed.Parent;
            ResourceIdentifier? serviceGroupId = parentId?.Parent;
            if (serviceGroupId?.ResourceType != ServiceGroupResourceType ||
                !string.Equals(serviceGroupId.Name, serviceGroup, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(parsed.Name, out _))
            {
                return null;
            }

            if (parsed.ResourceType == DrillTargetResource.ResourceType &&
                parentId?.ResourceType == ResilienceManagementDrillResource.ResourceType &&
                string.Equals(parentId.Name, drill, StringComparison.OrdinalIgnoreCase))
            {
                return DrillSelectedResourceKind.DrillResource;
            }

            return parsed.ResourceType == RecoveryMembersResource.ResourceType &&
                parentId?.ResourceType == RecoveryPlanResource.ResourceType
                    ? DrillSelectedResourceKind.RecoveryResource
                    : null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static readonly HashSet<string> s_modes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Failover",
        "TestFailover"
    };

    private static readonly HashSet<string> s_attestations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Success",
        "Failed"
    };

    public static void ValidateResourceNames(string serviceGroup, string drill, ValidationResult validationResult)
    {
        if (serviceGroup.Length is < 1 or > 90 || !serviceGroup.All(IsValidServiceGroupNameCharacter))
        {
            validationResult.Errors.Add("The service group name must be 1 to 90 characters and contain only ASCII letters, numbers, hyphens, underscores, periods, or parentheses.");
        }

        if (drill.Length is < 1 or > 260 || !drill.All(IsValidDrillNameCharacter))
        {
            validationResult.Errors.Add("The drill name must be 1 to 260 characters and contain only ASCII letters, numbers, hyphens, underscores, or periods.");
        }
    }

    public static void ValidateMode(string mode, ValidationResult validationResult)
    {
        if (!s_modes.Contains(mode))
        {
            validationResult.Errors.Add("The drill mode must be either Failover or TestFailover.");
        }
    }

    public static void ValidateAttestation(string attestation, ValidationResult validationResult)
    {
        if (!s_attestations.Contains(attestation))
        {
            validationResult.Errors.Add("The drill attestation must be either Success or Failed.");
        }
    }

    public static string NormalizeMode(string mode) =>
        mode.Equals("Failover", StringComparison.OrdinalIgnoreCase) ? "Failover" : "TestFailover";

    public static string NormalizeAttestation(string attestation) =>
        attestation.Equals("Success", StringComparison.OrdinalIgnoreCase) ? "Success" : "Failed";

    private static bool IsValidDrillNameCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';

    private static bool IsValidServiceGroupNameCharacter(char character) =>
        IsValidDrillNameCharacter(character) || character is '(' or ')';
}

internal enum DrillSelectedResourceKind
{
    DrillResource,
    RecoveryResource
}
