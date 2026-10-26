// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Services;

internal sealed class ContractCaptureHandler(HttpStatusCode statusCode = HttpStatusCode.OK)
    : HttpMessageHandler
{
    internal List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues(
                "x-ms-rhub-routing-location",
                out IEnumerable<string>? locations)
                    ? locations.Single()
                    : null,
            request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken)));

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("{}")
        };
    }
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    string? RoutingLocation,
    string? Body);
