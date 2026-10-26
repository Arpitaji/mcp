// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Core.Pipeline;

namespace Azure.Mcp.Tools.ResilienceManagement.Services;

internal sealed class RecoveryContractRequestPolicy : HttpPipelineSynchronousPolicy
{
    public override void OnSendingRequest(HttpMessage message)
    {
        string path = message.Request.Uri.ToUri().AbsolutePath;
        if (!path.Contains(
                "/providers/Microsoft.AzureResilienceManagement/",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var query = System.Web.HttpUtility.ParseQueryString(message.Request.Uri.Query);
        query["api-version"] = ResilienceManagementApiVersions.Current;
        message.Request.Uri.Query = query.ToString() ?? string.Empty;
    }
}
