// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Xunit;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Services;

public sealed partial class ResilienceManagementServiceTests
{
    private const string UsagePlanSubscription = "00000000-0000-0000-0000-000000000000";
    private const string UsagePlanResourceGroup = "rg";
    private const string UsagePlanName = "plan";
    private const string UsagePlanEnrollmentName = "enrollment";

    [Theory]
    [InlineData("https://eastus2euap.management.azure.com")]
    [InlineData(null)]
    public async Task GetUsagePlanEnrollment_UsesConfiguredEndpointAndPreservesProductionDefault(
        string? endpoint)
    {
        string? oldEndpoint = Environment.GetEnvironmentVariable(
            ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable);
        string? oldLocation = Environment.GetEnvironmentVariable(
            ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable,
                endpoint);
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable,
                null);
            using var handler = new UsagePlanRoutingHandler();
            ResilienceManagementService service = CreateService(handler);

            await service.GetUsagePlanEnrollmentAsync(
                UsagePlanResourceGroup,
                UsagePlanName,
                UsagePlanEnrollmentName,
                UsagePlanSubscription,
                cancellationToken: TestContext.Current.CancellationToken);

            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                endpoint ?? "https://management.azure.com",
                request.Uri.GetLeftPart(UriPartial.Authority));
            Assert.Null(request.RoutingLocation);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable,
                oldEndpoint);
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable,
                oldLocation);
        }
    }

    [Fact]
    public async Task CreateUsagePlanEnrollment_RoutesPutPollingAndFinalGetToCanary()
    {
        const string endpoint = "https://eastus2euap.management.azure.com";
        string? oldEndpoint = Environment.GetEnvironmentVariable(
            ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable);
        string? oldLocation = Environment.GetEnvironmentVariable(
            ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable,
                endpoint);
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable,
                null);
            using var handler = new UsagePlanRoutingHandler(useLongRunningOperation: true);
            ResilienceManagementService service = CreateService(handler);

            await service.CreateUsagePlanEnrollmentAsync(
                UsagePlanResourceGroup,
                UsagePlanName,
                UsagePlanEnrollmentName,
                "service-group",
                UsagePlanSubscription,
                cancellationToken: TestContext.Current.CancellationToken);

            RecordedRequest put = Assert.Single(
                handler.Requests,
                request => request.Method == HttpMethod.Put);
            Assert.Contains(
                "/providers/Microsoft.Management/serviceGroups/service-group",
                put.Content,
                StringComparison.Ordinal);
            Assert.Contains(
                handler.Requests,
                request => request.Uri.AbsolutePath.Contains(
                    "/operationStatuses/",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                handler.Requests,
                request =>
                    request.Method == HttpMethod.Get &&
                    request.Uri.AbsolutePath.EndsWith(
                        $"/enrollments/{UsagePlanEnrollmentName}",
                        StringComparison.OrdinalIgnoreCase));
            Assert.All(
                handler.Requests,
                request =>
                {
                    Assert.Equal(endpoint, request.Uri.GetLeftPart(UriPartial.Authority));
                    Assert.Null(request.RoutingLocation);
                });
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.EndpointEnvironmentVariable,
                oldEndpoint);
            Environment.SetEnvironmentVariable(
                ResilienceManagementRoutingPolicy.RoutingLocationEnvironmentVariable,
                oldLocation);
        }
    }

    private sealed class UsagePlanRoutingHandler(bool useLongRunningOperation = false)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? content = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues(
                    "x-ms-rhub-routing-location",
                    out IEnumerable<string>? values)
                    ? Assert.Single(values)
                    : null,
                content));

            if (request.RequestUri!.AbsolutePath.Contains(
                    "/operationStatuses/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    """{"status":"Succeeded"}""");
            }

            if (request.Method == HttpMethod.Put && useLongRunningOperation)
            {
                var response = JsonResponse(HttpStatusCode.Accepted, "{}");
                response.Headers.TryAddWithoutValidation(
                    "Azure-AsyncOperation",
                    "https://management.azure.com/subscriptions/" +
                    $"{UsagePlanSubscription}/providers/Microsoft.AzureResilienceManagement/" +
                    "locations/eastus2euap/operationStatuses/test?api-version=2025-08-01");
                response.Headers.TryAddWithoutValidation("Retry-After", "0");
                return response;
            }

            return JsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "id": "/subscriptions/{{UsagePlanSubscription}}/resourceGroups/{{UsagePlanResourceGroup}}/providers/Microsoft.AzureResilienceManagement/usagePlans/{{UsagePlanName}}/enrollments/{{UsagePlanEnrollmentName}}",
                  "name": "{{UsagePlanEnrollmentName}}",
                  "type": "Microsoft.AzureResilienceManagement/usagePlans/enrollments",
                  "properties": {
                    "serviceGroupId": "/providers/Microsoft.Management/serviceGroups/service-group",
                    "provisioningState": "Succeeded"
                  }
                }
                """);
        }

        private static HttpResponseMessage JsonResponse(
            HttpStatusCode statusCode,
            string content) =>
            new(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? RoutingLocation,
        string? Content);
}
