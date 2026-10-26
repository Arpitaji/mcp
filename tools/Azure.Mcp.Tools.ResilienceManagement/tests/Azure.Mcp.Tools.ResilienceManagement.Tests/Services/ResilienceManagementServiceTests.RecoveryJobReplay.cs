// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Mcp.Tools.ResilienceManagement.Services;
using Xunit;

namespace Azure.Mcp.Tools.ResilienceManagement.Tests.Services;

public sealed partial class ResilienceManagementServiceTests
{
    [Theory]
    [InlineData("retry")]
    [InlineData("resume")]
    public async Task RecoveryJobMutation_Accepted_SubmitsExactlyOnce(string action)
    {
        var handler = new RecoveryJobMutationHandler(
            action,
            RecoveryJobMutationResponse.Accepted);
        ResilienceManagementService service = CreateService(handler);

        await InvokeRecoveryJobMutationAsync(service, action);

        Assert.Equal(1, handler.MutationSubmissions);
        Assert.Equal(1, handler.ReadAttempts);
        Assert.All(handler.RequestUris, AssertCurrentRecoveryContract);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("resume")]
    public async Task RecoveryJobMutation_LostResponse_SubmitsExactlyOnce(string action)
    {
        var handler = new RecoveryJobMutationHandler(
            action,
            RecoveryJobMutationResponse.Lost);
        ResilienceManagementService service = CreateService(handler);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => InvokeRecoveryJobMutationAsync(service, action));

        Assert.True(exception is RequestFailedException or HttpRequestException);
        Assert.Equal(1, handler.MutationSubmissions);
        Assert.All(handler.RequestUris, AssertCurrentRecoveryContract);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("resume")]
    public async Task RecoveryJobMutation_TransientFailure_SubmitsExactlyOnce(string action)
    {
        var handler = new RecoveryJobMutationHandler(
            action,
            RecoveryJobMutationResponse.Transient);
        ResilienceManagementService service = CreateService(handler);

        RequestFailedException exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => InvokeRecoveryJobMutationAsync(service, action));

        Assert.Equal(503, exception.Status);
        Assert.Equal(1, handler.MutationSubmissions);
        Assert.All(handler.RequestUris, AssertCurrentRecoveryContract);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("resume")]
    public async Task RecoveryJobMutation_ReadBeforeSubmissionRemainsRetryable(string action)
    {
        var handler = new RecoveryJobMutationHandler(
            action,
            RecoveryJobMutationResponse.Accepted,
            transientReadFailures: 1);
        ResilienceManagementService service = CreateService(handler);

        await InvokeRecoveryJobMutationAsync(service, action);

        Assert.Equal(2, handler.ReadAttempts);
        Assert.Equal(1, handler.MutationSubmissions);
        Assert.All(handler.RequestUris, AssertCurrentRecoveryContract);
    }

    private static async Task InvokeRecoveryJobMutationAsync(
        ResilienceManagementService service,
        string action)
    {
        if (action == "retry")
        {
            _ = await service.RetryRecoveryJobAsync(
                "sg1",
                "plan1",
                RecoveryJobName,
                cancellationToken: TestContext.Current.CancellationToken);
            return;
        }

        _ = await service.ResumeRecoveryJobAsync(
            "sg1",
            "plan1",
            RecoveryJobName,
            "Approved",
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static void AssertCurrentRecoveryContract(Uri uri) =>
        Assert.Equal(
            ResilienceManagementApiVersions.Current,
            System.Web.HttpUtility.ParseQueryString(uri.Query)["api-version"]);
}
