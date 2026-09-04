// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Services;
using AWS.Messaging.UnitTests.MessageHandlers;
using AWS.Messaging.UnitTests.Models;
using AWS.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using AWS.Messaging.Tests.Common.Services;
using AWS.Messaging.SQS;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace AWS.Messaging.UnitTests;

public class SQSMessagePollerTests
{
    private const string TEST_QUEUE_URL = "queueUrl";
    private InMemoryLogger? _inMemoryLogger;
    private readonly ServiceCollection _serviceCollection;

    public SQSMessagePollerTests()
    {
        _serviceCollection = new ServiceCollection();
    }

    /// <summary>
    /// Tests that starting an SQS poller with default settings begins polling SQS
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_Defaults_PollsSQS()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50));

        await RunSQSMessagePollerTest(client);

        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    /// <summary>
    /// Tests that starting an SQS poller with <see cref="PollingControlToken.IsPollingEnabled"/>
    /// set to false, will not poll any messages.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_PollingControlStopped_DoesNotPollSQS()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50));
        var pollingControlToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        pollingControlToken.StopPolling();

        await RunSQSMessagePollerTest(client, pollingControlToken: pollingControlToken);

        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that starting an SQS poller with <see cref="PollingControlToken.IsPollingEnabled"/>
    /// set to false, will not poll any messages first. Then when changing the value to true
    /// polling resumes and messages are received.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_PollingControlRestarted_PollsSQS()
    {
        var client = new Mock<IAmazonSQS>();
        var messageReceived = new TaskCompletionSource<bool>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50))
            .Callback(() => messageReceived.TrySetResult(true));

        var pollingControlToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        pollingControlToken.StopPolling();

        var source = new CancellationTokenSource();
        var pump = BuildMessagePumpService(client, options => { options.WaitTimeSeconds = 1; }, pollingControlToken: pollingControlToken);
        var task = pump.StartAsync(source.Token);

        // Verify no messages are received while polling is stopped
        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // Start polling and wait for a message to be received
        pollingControlToken.StartPolling();

        // Wait for a message to be received with a timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await messageReceived.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timed out waiting for message to be received after polling was restarted");
        }

        // Verify that messages were received
        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());

        source.Cancel();
        await task;
    }

    /// <summary>
    /// Tests that a poller configured with its own stopped <see cref="SQSMessagePollerOptions.PollingControlToken"/>
    /// does not poll SQS, even when no bus-scoped token is configured.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_PerPollerPollingControlStopped_DoesNotPollSQS()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50));
        var perPollerToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        perPollerToken.StopPolling();

        await RunSQSMessagePollerTest(client, options => options.PollingControlToken = perPollerToken);

        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that a poller's own <see cref="SQSMessagePollerOptions.PollingControlToken"/> takes precedence over the
    /// bus-scoped token: a stopped per-poller token prevents polling even while the bus-scoped token is running.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_PerPollerToken_OverridesBusScopedToken()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50));

        // Bus-scoped token is running, but the per-poller token is stopped and must win.
        var busScopedToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        var perPollerToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        perPollerToken.StopPolling();

        await RunSQSMessagePollerTest(client, options => options.PollingControlToken = perPollerToken, pollingControlToken: busScopedToken);

        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that two pollers on the same message bus, each with its own <see cref="SQSMessagePollerOptions.PollingControlToken"/>,
    /// are paused and resumed independently. Stopping one poller's token leaves the other draining its queue.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_TwoPollers_IndependentPerPollerTokens()
    {
        const string runningQueueUrl = "runningQueueUrl";
        const string stoppedQueueUrl = "stoppedQueueUrl";

        var client = new Mock<IAmazonSQS>();
        var runningQueuePolled = new TaskCompletionSource<bool>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50))
            .Callback<ReceiveMessageRequest, CancellationToken>((request, _) =>
            {
                if (request.QueueUrl == runningQueueUrl) runningQueuePolled.TrySetResult(true);
            });

        var runningToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        var stoppedToken = new PollingControlToken
        {
            PollingWaitTime = TimeSpan.FromMilliseconds(25)
        };
        stoppedToken.StopPolling();

        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller(runningQueueUrl, options => options.PollingControlToken = runningToken);
            builder.AddSQSPoller(stoppedQueueUrl, options => options.PollingControlToken = stoppedToken);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });
        _serviceCollection.AddSingleton(client.Object);

        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var pump = serviceProvider.GetService<IHostedService>() as MessagePumpService;
        Assert.NotNull(pump);

        var source = new CancellationTokenSource();
        var task = pump.StartAsync(source.Token);

        // Wait until the running poller has actually polled, rather than relying on a fixed delay.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await runningQueuePolled.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timed out waiting for the running poller to poll SQS");
        }

        source.Cancel();
        await task;

        client.Verify(x => x.ReceiveMessageAsync(
            It.Is<ReceiveMessageRequest>(request => request.QueueUrl == runningQueueUrl), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        client.Verify(x => x.ReceiveMessageAsync(
            It.Is<ReceiveMessageRequest>(request => request.QueueUrl == stoppedQueueUrl), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// Tests that configuring a poller with <see cref="SQSMessagePollerConfiguration.MaxNumberOfConcurrentMessages"/>
    /// set to a value greater than SQS's current limit of 10 will only receive 10 messages at a time.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_ManyConcurrentMessages_DoesNotExceedSQSMax()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse(), TimeSpan.FromMilliseconds(50));

        await RunSQSMessagePollerTest(client, options => options.MaxNumberOfConcurrentMessages = 50);

        client.Verify(x => x.ReceiveMessageAsync(
            It.Is<ReceiveMessageRequest>(request => request.MaxNumberOfMessages == 10), It.IsAny<CancellationToken>()), Times.AtLeastOnce());

        client.Verify(x => x.ReceiveMessageAsync(
            It.Is<ReceiveMessageRequest>(request => request.MaxNumberOfMessages != 10), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// Tests that calling <see cref="IMessagePoller.DeleteMessagesAsync"/> calls
    /// SQS's DeleteMessageBatch with an expected request.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_DeleteMessages_Success()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageBatchResponse { Failed = new List<BatchResultErrorEntry>() });

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if(messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = new List<MessageEnvelope>()
        {
            new MessageEnvelope<ChatMessage> { Id = "1", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh1"} },
            new MessageEnvelope<ChatMessage> { Id = "2", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh2"} }
        };

        await messagePoller.DeleteMessagesAsync(messageEnvelopes);

        client.Verify(x => x.DeleteMessageBatchAsync(
            It.Is<DeleteMessageBatchRequest>(request =>
                request.QueueUrl == TEST_QUEUE_URL &&
                request.Entries.Count == 2 &&
                request.Entries.Any(entry => entry.Id == "1" && entry.ReceiptHandle == "rh1") &&
                request.Entries.Any(entry => entry.Id == "2" && entry.ReceiptHandle == "rh2")),
            It.IsAny<CancellationToken>()));
    }

    /// <summary>
    /// Tests that a transient (non-fatal) exception thrown while deleting messages is retried
    /// via the backoff handler rather than silently abandoning the delete, which would otherwise
    /// leave the already-handled message to be redelivered by SQS.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_DeleteMessages_RetriesOnTransientException()
    {
        var client = new Mock<IAmazonSQS>();

        var attempts = 0;
        client.Setup(x => x.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()))
            .Returns<DeleteMessageBatchRequest, CancellationToken>((request, token) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    // A throttling exception is non-fatal, so the delete should be retried
                    throw new AmazonSQSException("Rate exceeded") { ErrorCode = "RequestThrottled" };
                }

                return Task.FromResult(new DeleteMessageBatchResponse { Failed = new List<BatchResultErrorEntry>() });
            });

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if (messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = new List<MessageEnvelope>()
        {
            new MessageEnvelope<ChatMessage> { Id = "1", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh1"} }
        };

        await messagePoller.DeleteMessagesAsync(messageEnvelopes);

        // The first attempt threw a transient exception and the second succeeded
        Assert.Equal(2, attempts);
        client.Verify(x => x.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Tests that an exception that is fatal to the delete itself (for example, the receipt handle is
    /// no longer valid because the message was already deleted) is neither retried nor rethrown to stop the poller.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_DeleteMessages_DoesNotRetryOrThrowOnDeleteFatalException()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReceiptHandleIsInvalidException("The receipt handle is not valid"));

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if (messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = new List<MessageEnvelope>()
        {
            new MessageEnvelope<ChatMessage> { Id = "1", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh1"} }
        };

        // Should not throw
        await messagePoller.DeleteMessagesAsync(messageEnvelopes);

        // Should not be retried since the delete can never succeed for this exception
        client.Verify(x => x.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    /// <summary>
    /// Tests that a fatal exception thrown while deleting messages is rethrown to stop the poller,
    /// consistent with the receive path.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_DeleteMessages_RethrowsFatalException()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QueueDoesNotExistException("The specified queue does not exist"));

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if (messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = new List<MessageEnvelope>()
        {
            new MessageEnvelope<ChatMessage> { Id = "1", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh1"} }
        };

        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => messagePoller.DeleteMessagesAsync(messageEnvelopes));
    }

    /// <summary>
    /// Tests that calling <see cref="IMessagePoller.ExtendMessageVisibilityTimeoutAsync"/> calls
    /// SQS's ChangeMessageVisibilityBatch with an expected request.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_ExtendMessageVisibility_Success()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.ChangeMessageVisibilityBatchAsync(It.IsAny<ChangeMessageVisibilityBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeMessageVisibilityBatchResponse { Failed = new List<BatchResultErrorEntry>() }, TimeSpan.FromMilliseconds(50));

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if (messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = new List<MessageEnvelope>()
        {
            new MessageEnvelope<ChatMessage> { Id = "1", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh1"} },
            new MessageEnvelope<ChatMessage> { Id = "2", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh2"} }
        };

        await messagePoller.ExtendMessageVisibilityTimeoutAsync(messageEnvelopes);

        client.Verify(x => x.ChangeMessageVisibilityBatchAsync(
            It.Is<ChangeMessageVisibilityBatchRequest>(request =>
                request.QueueUrl == TEST_QUEUE_URL &&
                request.Entries.Count == 2 &&
                request.Entries.Any(entry => entry.Id == "batchNum_0_messageId_1" && entry.ReceiptHandle == "rh1") &&
                request.Entries.Any(entry => entry.Id == "batchNum_1_messageId_2" && entry.ReceiptHandle == "rh2")),
            It.IsAny<CancellationToken>()));
    }

    /// <summary>
    /// Tests that calling <see cref="IMessagePoller.ExtendMessageVisibilityTimeoutAsync"/> calls
    /// SQS's ChangeMessageVisibilityBatch with a request that has more than 10 entires.
    /// <see cref="ExtendMessageVisibilityTimeoutAsync"/> is expected create multiple <see cref="ChangeMessageVisibilityBatchRequest"/>
    /// when there are more than 10 messages since <see cref="ChangeMessageVisibilityBatchRequest"/> can only handle 10 entries.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_ExtendMessageVisibility_RequestHasMoreThan10Entries()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.ChangeMessageVisibilityBatchAsync(It.Is<ChangeMessageVisibilityBatchRequest>(x => x.Entries.Count > 10), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Request contains more than 10 entries.") { ErrorCode = "AWS.SimpleQueueService.TooManyEntriesInBatchRequest" });

        client.Setup(x => x.ChangeMessageVisibilityBatchAsync(It.Is<ChangeMessageVisibilityBatchRequest>(x => x.Entries.Count <= 10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeMessageVisibilityBatchResponse { Failed = new List<BatchResultErrorEntry>() }, TimeSpan.FromMilliseconds(50));

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if (messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = Enumerable.Range(0, 15).Select(x => new MessageEnvelope<ChatMessage> { Id = $"{x + 1}", SQSMetadata = new SQSMetadata { ReceiptHandle = $"rh{x + 1}" } }).Cast<MessageEnvelope>().ToList();

        await messagePoller.ExtendMessageVisibilityTimeoutAsync(messageEnvelopes);

        Assert.NotNull(_inMemoryLogger);
        Assert.DoesNotContain(_inMemoryLogger.Logs, (x => x.Exception is AmazonSQSException ex && ex.ErrorCode.Equals("AWS.SimpleQueueService.TooManyEntriesInBatchRequest")));

    }

    /// <summary>
    /// Tests that <see cref="IMessagePoller.ExtendMessageVisibilityTimeoutAsync"/> does
    /// not log errors for the case where we fail to extend the message visibility timeout for a
    /// given message because it was recently deleted.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_ExtendsAlreadyDeleteMessage_OnlyLogsTrace()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.ChangeMessageVisibilityBatchAsync(It.IsAny<ChangeMessageVisibilityBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeMessageVisibilityBatchResponse
            {
                Failed = new List<BatchResultErrorEntry>()
                {
                    new BatchResultErrorEntry()
                    {
                        Id = "batchNum_0_messageId_1",
                        Code = "ReceiptHandleIsInvalid",
                        Message = "Message does not exist or is not available for visibility timeout change"
                    },
                    new BatchResultErrorEntry()
                    {
                        Id = "batchNum_0_messageId_2",
                        Code = "ReceiptHandleIsInvalid",
                        Message = "Something else"
                    }
                }
            }, TimeSpan.FromMilliseconds(50));

        var messagePoller = CreateSQSMessagePoller(client) as ISQSMessageCommunication;
        if (messagePoller == null)
        {
            Assert.Fail("Failed to cast message poller to ISQSMessageCommunication");
        }

        var messageEnvelopes = new List<MessageEnvelope>()
        {
            new MessageEnvelope<ChatMessage> { Id = "1", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh1"} },
            new MessageEnvelope<ChatMessage> { Id = "2", SQSMetadata = new SQSMetadata { ReceiptHandle ="rh2"} }
        };

        await messagePoller.ExtendMessageVisibilityTimeoutAsync(messageEnvelopes);

        Assert.NotNull(_inMemoryLogger);

        // Don't expect to see message 1 in the error logs, since this is the case where it was deleted before or while extending visibility
        Assert.DoesNotContain(_inMemoryLogger.Logs, (x => x.Message.Contains("batchNum_0_messageId_1")));
        // But we should see an entry for message 2, which failed to extend visibility for a different reason
        Assert.Single(_inMemoryLogger.Logs, (x => x.Message.Contains("batchNum_0_messageId_2") && x.LogLevel == LogLevel.Error));
    }

    /// <summary>
    /// Tests that the SQS poller rethrows a fatal exception
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_RethrowsFatalException()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QueueDoesNotExistException(""));

        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => RunSQSMessagePollerTest(client));
    }

    /// <summary>
    /// Tests that the SQS poller does not throw and continues for a non-fatal exception
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_ContinuesForNonFatalException()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OverLimitException(""));

        await RunSQSMessagePollerTest(client);

        client.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task SQSMessagePollerFactory_SingleMessageType_UsesRawOnly_WhenUsesMessageEnvelopeFalse()
    {
        // Raw JSON payload without CloudEvents envelope and without a 'type' discriminator.
        const string rawJson = "{\"MessageDescription\":\"hello\"}";

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        // Add a second mapping to make raw ingestion ambiguous without the single-type poller.
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller<ChatMessage>(TEST_QUEUE_URL, messageEnvelopeMode: MessageEnvelopeMode.NotSupported);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
            builder.AddMessageHandler<AddressInfoHandler, AddressInfo>();
        });

        serviceCollection.AddSingleton(new Mock<IAmazonSQS>().Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetRequiredService<IMessageConfiguration>();
        var pollerConfiguration = messageConfiguration.MessagePollerConfigurations.OfType<SQSMessagePollerConfiguration>().Single();

        var messagePollerFactory = serviceProvider.GetRequiredService<IMessagePollerFactory>();
        var poller = messagePollerFactory.CreateMessagePoller(pollerConfiguration);

        var sqsPoller = Assert.IsType<SQSMessagePoller>(poller);
        var envelopeSerializer = sqsPoller.EnvelopeSerializer;

        var result = await envelopeSerializer.ConvertToEnvelopeAsync(new Message
        {
            MessageId = "m-1",
            ReceiptHandle = "rh-1",
            Body = rawJson
        });

        Assert.Equal(typeof(ChatMessage), result.Mapping.MessageType);
        var envelope = Assert.IsType<MessageEnvelope<ChatMessage>>(result.Envelope);
        Assert.Equal("hello", envelope.Message.MessageDescription);
    }

    [Fact]
    public void SQSMessagePollerFactory_SingleMessageType_Throws_WhenIdentifierResolvesToDifferentMessageType()
    {
        const string addressInfoIdentifier = "address-info";

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller<ChatMessage>(TEST_QUEUE_URL, messageTypeIdentifier: addressInfoIdentifier, messageEnvelopeMode: MessageEnvelopeMode.NotSupported);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
            builder.AddMessageHandler<AddressInfoHandler, AddressInfo>(messageTypeIdentifier: addressInfoIdentifier);
        });

        serviceCollection.AddSingleton(new Mock<IAmazonSQS>().Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetRequiredService<IMessageConfiguration>();
        var pollerConfiguration = messageConfiguration.MessagePollerConfigurations.OfType<SQSMessagePollerConfiguration>().Single();

        var messagePollerFactory = serviceProvider.GetRequiredService<IMessagePollerFactory>();

        Assert.Throws<ConfigurationException>(() => messagePollerFactory.CreateMessagePoller(pollerConfiguration));
    }

    [Fact]
    public async Task SQSMessagePollerFactory_SingleMessageType_RequiresEnvelope_ByDefault()
    {
        // Raw JSON payload without CloudEvents envelope.
        const string rawJson = "{\"MessageDescription\":\"hello\"}";

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        // Raw payload ingestion is disabled by default. We do not override it here.
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller<ChatMessage>(TEST_QUEUE_URL);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });

        serviceCollection.AddSingleton(new Mock<IAmazonSQS>().Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetRequiredService<IMessageConfiguration>();
        var pollerConfiguration = messageConfiguration.MessagePollerConfigurations.OfType<SQSMessagePollerConfiguration>().Single();

        var messagePollerFactory = serviceProvider.GetRequiredService<IMessagePollerFactory>();
        var poller = messagePollerFactory.CreateMessagePoller(pollerConfiguration);

        var sqsPoller = Assert.IsType<SQSMessagePoller>(poller);
        var envelopeSerializer = sqsPoller.EnvelopeSerializer;

        await Assert.ThrowsAsync<FailedToCreateMessageEnvelopeException>(async () =>
        {
            await envelopeSerializer.ConvertToEnvelopeAsync(new Message
            {
                MessageId = "m-1",
                ReceiptHandle = "rh-1",
                Body = rawJson
            });
        });
    }


    /// <summary>
    /// Helper function that initializes and starts a <see cref="MessagePumpService"/> with
    /// a mocked SQS client, then cancels after 500ms
    /// </summary>
    /// <param name="mockSqsClient">Mocked SQS client</param>
    /// <param name="options">SQS MessagePoller options</param>
    /// <param name="pollingControlToken">Bus-scoped polling control token to start or stop message receipt</param>
    private async Task RunSQSMessagePollerTest(Mock<IAmazonSQS> mockSqsClient, Action<SQSMessagePollerOptions>? options = null, PollingControlToken? pollingControlToken = null)
    {
        var pump = BuildMessagePumpService(mockSqsClient, options, pollingControlToken);

        var source = new CancellationTokenSource();
        source.CancelAfter(500);

        await pump.StartAsync(source.Token);
    }

    /// <summary>
    /// Helper function that initializes but does not start a <see cref="MessagePumpService"/> with
    /// a mocked SQS client
    /// </summary>
    /// <param name="mockSqsClient">Mocked SQS client</param>
    /// <param name="options">SQS MessagePoller options</param>
    /// <param name="pollingControlToken">Bus-scoped polling control token to start or stop message receipt</param>
    private MessagePumpService BuildMessagePumpService(Mock<IAmazonSQS> mockSqsClient, Action<SQSMessagePollerOptions>? options = null, PollingControlToken? pollingControlToken = null)
    {
        _serviceCollection.AddLogging();

        _serviceCollection.AddAWSMessageBus(builder =>
        {
            if (pollingControlToken is not null) builder.ConfigurePollingControlToken(pollingControlToken);
            builder.AddSQSPoller(TEST_QUEUE_URL, options);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });

        _serviceCollection.AddSingleton(mockSqsClient.Object);

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var pump = serviceProvider.GetService<IHostedService>() as MessagePumpService;

        if (pump == null)
        {
            Assert.Fail($"Unable to get the {nameof(MessagePumpService)} from the service provider.");
        }

        return pump;
    }

    /// <summary>
    /// Helper function that initializes an SQSMessagePoller
    /// </summary>
    /// <param name="mockSqsClient">Mocked SQS client</param>
    private IMessagePoller CreateSQSMessagePoller(Mock<IAmazonSQS> mockSqsClient)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(x => x.AddInMemoryLogger());

        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller(TEST_QUEUE_URL);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });

        serviceCollection.AddSingleton(mockSqsClient.Object);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        _inMemoryLogger = serviceProvider.GetRequiredService<InMemoryLogger>();
        var messagePollerFactory = serviceProvider.GetService<IMessagePollerFactory>();
        Assert.NotNull(messagePollerFactory);

        var messagePoller = messagePollerFactory.CreateMessagePoller(new SQSMessagePollerConfiguration(TEST_QUEUE_URL));
        Assert.NotNull(messagePoller);

        return messagePoller;
    }
}
