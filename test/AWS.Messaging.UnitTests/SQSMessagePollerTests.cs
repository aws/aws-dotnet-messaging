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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests;

public class SQSMessagePollerTests
{
    private const string TEST_QUEUE_URL = "queueUrl";

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
    /// Tests that configuring a poller with <see cref="SQSMessagePollerConfiguration.MaxNumberOfConcurrentMessages"/>
    /// set to a value greater than SQS's current limit of 10 will only recieve 10 messages at a time.
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

        var messagePoller = CreateSQSMessagePoller(client);

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
    /// Tests that calling <see cref="IMessagePoller.ExtendMessageVisibilityTimeoutAsync"/> calls
    /// SQS's ChangeMessageVisibilityBatch with an expected request.
    /// </summary>
    [Fact]
    public async Task SQSMessagePoller_ExtendMessageVisibility_Success()
    {
        var client = new Mock<IAmazonSQS>();

        client.Setup(x => x.ChangeMessageVisibilityBatchAsync(It.IsAny<ChangeMessageVisibilityBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeMessageVisibilityBatchResponse { Failed = new List<BatchResultErrorEntry>() }, TimeSpan.FromMilliseconds(50));

        var messagePoller = CreateSQSMessagePoller(client);

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
                request.Entries.Any(entry => entry.Id == "1" && entry.ReceiptHandle == "rh1") &&
                request.Entries.Any(entry => entry.Id == "2" && entry.ReceiptHandle == "rh2")),
            It.IsAny<CancellationToken>()));
    }

    /// <summary>
    /// Helper function that initializes and starts a <see cref="MessagePumpService"/> with
    /// a mocked SQS client, then cancels after 500ms
    /// </summary>
    /// <param name="mockSqsClient">Mocked SQS client</param>
    /// <param name="options">SQS MessagePoller options</param>
    private async Task RunSQSMessagePollerTest(Mock<IAmazonSQS> mockSqsClient, Action<SQSMessagePollerOptions>? options = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller(TEST_QUEUE_URL, options);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });

        serviceCollection.AddSingleton(mockSqsClient.Object);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var pump = serviceProvider.GetService<IHostedService>() as MessagePumpService;

        if (pump == null)
        {
            Assert.Fail($"Unable to get the {nameof(MessagePumpService)} from the service provider.");
        }

        var source = new CancellationTokenSource();
        source.CancelAfter(500);

        await pump.StartAsync(source.Token);
    }

    /// <summary>
    /// Helper function that initializes an SQSMessagePoller
    /// </summary>
    /// <param name="mockSqsClient">Mocked SQS client</param>
    private IMessagePoller CreateSQSMessagePoller(Mock<IAmazonSQS> mockSqsClient)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller(TEST_QUEUE_URL);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });

        serviceCollection.AddSingleton(mockSqsClient.Object);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var messagePollerFactory = serviceProvider.GetService<IMessagePollerFactory>();
        Assert.NotNull(messagePollerFactory);

        var messagePoller = messagePollerFactory.CreateMessagePoller(new SQSMessagePollerConfiguration(TEST_QUEUE_URL));
        Assert.NotNull(messagePoller);

        return messagePoller;
    }
}
