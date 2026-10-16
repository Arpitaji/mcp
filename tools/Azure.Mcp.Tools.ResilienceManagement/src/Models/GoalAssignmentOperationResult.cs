// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.ResilienceManagement.Models;

public sealed record GoalAssignmentOperationResult(
    [property: JsonPropertyName("operationId")] string OperationId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("hasCompleted")] bool HasCompleted);
