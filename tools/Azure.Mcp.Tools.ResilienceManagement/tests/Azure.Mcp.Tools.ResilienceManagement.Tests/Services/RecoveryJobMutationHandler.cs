// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Services;

internal enum RecoveryJobMutationResponse
{
    Accepted,
    Lost,
    Transient
}

internal sealed class RecoveryJobMutationHandler(
    string action,
    RecoveryJobMutationResponse mutationResponse,
    int transientReadFailures = 0) : HttpMessageHandler
{
    private const string RecoveryJobId =
        "/providers/Microsoft.Management/serviceGroups/sg1/providers/Microsoft.AzureResilienceManagement/recoveryPlans/plan1/recoveryJobs/11111111-1111-1111-1111-111111111111";
    private int _remainingReadFailures = transientReadFailures;

    internal int MutationSubmissions { get; private set; }
    internal int ReadAttempts { get; private set; }
    internal List<Uri> RequestUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri!;
        RequestUris.Add(uri);
        if (request.Method == HttpMethod.Get &&
            string.Equals(uri.AbsolutePath, RecoveryJobId, StringComparison.OrdinalIgnoreCase))
        {
            ReadAttempts++;
            if (_remainingReadFailures-- > 0)
            {
                return Task.FromResult(CreateTransientResponse());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "id": "{{RecoveryJobId}}",
                      "name": "11111111-1111-1111-1111-111111111111",
                      "type": "Microsoft.AzureResilienceManagement/recoveryPlans/recoveryJobs",
                      "properties": {
                        "status": "Failed",
                        "operation": "Failover",
                        "provisioningState": "Succeeded"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        }

        if (request.Method == HttpMethod.Post &&
            uri.AbsolutePath.EndsWith($"/{action}", StringComparison.OrdinalIgnoreCase))
        {
            MutationSubmissions++;
            return mutationResponse switch
            {
                RecoveryJobMutationResponse.Accepted =>
                    Task.FromResult(CreateAcceptedResponse(uri)),
                RecoveryJobMutationResponse.Transient =>
                    Task.FromResult(CreateTransientResponse()),
                RecoveryJobMutationResponse.Lost =>
                    Task.FromException<HttpResponseMessage>(
                        new HttpRequestException("The mutation response was lost.")),
                _ => throw new ArgumentOutOfRangeException(nameof(mutationResponse))
            };
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage CreateAcceptedResponse(Uri requestUri)
    {
        var statusUri = new Uri(
            $"{requestUri.Scheme}://{requestUri.Authority}/providers/Microsoft.AzureResilienceManagement/locations/eastus2euap/operationStatuses/00000000-0000-0000-0000-000000000001?api-version=2026-10-01");
        var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("Azure-AsyncOperation", statusUri.AbsoluteUri);
        response.Headers.TryAddWithoutValidation("Operation-Location", statusUri.AbsoluteUri);
        response.Headers.Location = statusUri;
        return response;
    }

    private static HttpResponseMessage CreateTransientResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error":{"code":"Unavailable","message":"retry"}}""")
        };
        response.Headers.TryAddWithoutValidation("x-ms-retry-after-ms", "0");
        return response;
    }
}
