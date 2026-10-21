// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.\r
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using AWS.Messaging.Configuration;
using AWS.Messaging.Services.Backoff;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests.Backoff;

public class BackoffHandlerTests
{
    private readonly Mock<IBackoffPolicy> _backoffPolicy = new();
    private readonly Mock<ILogger<BackoffHandler>> _logger = new();

    [Fact]
    public async Task RetryAsync_NoException()
    {
        var source = new CancellationTokenSource();
        var sqsMessagePollerConfiguration = new SQSMessagePollerConfiguration("queueURL");
        var backoffHandler = new BackoffHandler(_backoffPolicy.Object, _logger.Object, TimeProvider.System);

        var response = await backoffHandler.BackoffAsync<bool>(() => Task.FromResult(true),
            sqsMessagePollerConfiguration,
            source.Token);

        Assert.True(response);
        _backoffPolicy.Verify(x => x.ShouldBackoff(It.IsAny<Exception>(), sqsMessagePollerConfiguration), Times.Never);
        _backoffPolicy.Verify(x => x.RetrieveBackoffTime(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RetryAsync_ShouldNotBackoff()
    {
        var source = new CancellationTokenSource();
        var sqsMessagePollerConfiguration = new SQSMessagePollerConfiguration("queueURL");
        var backoffHandler = new BackoffHandler(_backoffPolicy.Object, _logger.Object, TimeProvider.System);
        _backoffPolicy
            .Setup(x =>
                x.ShouldBackoff(It.IsAny<Exception>(), sqsMessagePollerConfiguration))
            .Returns(false);

        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await backoffHandler.BackoffAsync<bool>(() => throw new Exception("Failed to process."),
                sqsMessagePollerConfiguration,
                source.Token);
        });

        _backoffPolicy.Verify(x => x.ShouldBackoff(It.IsAny<Exception>(), sqsMessagePollerConfiguration), Times.Once);
        _backoffPolicy.Verify(X => X.RetrieveBackoffTime(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    [InlineData(5, 6)]
    public async Task RetryAsync_IntervalBackoff(int backoffSeconds, int retries)
    {
        var source = new CancellationTokenSource();
        var fakeTimeProvider = new FakeTimeProvider();
        var sqsMessagePollerConfiguration = new SQSMessagePollerConfiguration("queueURL");
        var backoffHandler = new BackoffHandler(_backoffPolicy.Object, _logger.Object, fakeTimeProvider);
        _backoffPolicy
            .Setup(x =>
                x.ShouldBackoff(It.IsAny<Exception>(), sqsMessagePollerConfiguration))
            .Returns(true);
        _backoffPolicy
            .Setup(x => x.RetrieveBackoffTime(It.IsAny<int>()))
            .Returns(TimeSpan.FromSeconds(1));

        // Run BackoffAsync on a background task; advance fake time to drive each retry cycle
        var backoffTask = Task.Run(async () =>
        {
            try
            {
                await backoffHandler.BackoffAsync<bool>(() => throw new Exception("Failed to process."),
                    sqsMessagePollerConfiguration,
                    source.Token);
            }
            catch (TaskCanceledException) { }
        });

        // Advance fake time one second at a time to trigger retries, then cancel
        for (int i = 0; i < backoffSeconds; i++)
        {
            await Task.Delay(10); // give the background task time to enter Task.Delay
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        await source.CancelAsync();
        await backoffTask;

        _backoffPolicy.Verify(X => X.RetrieveBackoffTime(It.IsAny<int>()), Times.AtMost(retries));
    }
}
