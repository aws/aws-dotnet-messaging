// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using AWS.Messaging.IntegrationTests.Models;
using AWS.Messaging.IntegrationTests.Handlers;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Serialization;
using AWS.Messaging.Tests.Common;

namespace AWS.Messaging.IntegrationTests;

public class SQSBatchPublisherFifoTests : IAsyncLifetime
{
    private readonly IAmazonSQS _sqsClient;
    private ServiceProvider _serviceProvider;
    private string _sqsQueueUrl;

    public SQSBatchPublisherFifoTests()
    {
        _sqsClient = new AmazonSQSClient();
        _serviceProvider = default!;
        _sqsQueueUrl = string.Empty;
    }

    public async Task InitializeAsync()
    {
        _sqsQueueUrl = await AWSUtilities.CreateQueueAsync(_sqsClient, isFifo: true);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<ChatMessage>(_sqsQueueUrl);
            builder.AddMessageSource("/aws/messaging");
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishMessageBatch_FifoQueue_SharedGroupId()
    {
        var publishStartTime = DateTime.UtcNow;
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        var entries = Enumerable.Range(1, 5)
            .Select(i => new SQSBatchEntry<ChatMessage>(
                new ChatMessage { MessageDescription = $"FifoBatchTest{i}" },
                new SQSMessageOptions { MessageGroupId = "test-group" }))
            .ToList();

        var batchResponse = await sqsPublisher.SendBatchAsync<ChatMessage>(entries);
        var publishEndTime = DateTime.UtcNow;

        // Verify the batch response
        Assert.Equal(5, batchResponse.Successful.Count);
        Assert.Empty(batchResponse.Failed);

        // Wait to allow the published messages to propagate
        await Task.Delay(5000);

        // Receive all messages from the queue
        var receivedMessages = new List<Message>();
        for (var i = 0; i < 3; i++)
        {
            var receiveResponse = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MaxNumberOfMessages = 10
            });
            receivedMessages.AddRange(receiveResponse.Messages);
            if (receivedMessages.Count >= 5)
                break;
            await Task.Delay(1000);
        }

        Assert.Equal(5, receivedMessages.Count);

        var envelopeSerializer = _serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        foreach (var receivedMessage in receivedMessages)
        {
            var result = await envelopeSerializer.ConvertToEnvelopeAsync(receivedMessage);
            var envelope = result.Envelope as MessageEnvelope<ChatMessage>;

            Assert.NotNull(envelope);
            Assert.False(string.IsNullOrEmpty(envelope.Id));
            Assert.Equal("/aws/messaging", envelope.Source.ToString());
            Assert.True(envelope.TimeStamp > publishStartTime);
            Assert.True(envelope.TimeStamp < publishEndTime);
            Assert.NotNull(envelope.Message);
            Assert.StartsWith("FifoBatchTest", envelope.Message.MessageDescription);
        }
    }

    [Fact]
    public async Task PublishMessageBatch_FifoQueue_PerMessageGroupId()
    {
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "GroupA_Message1" },
                new SQSMessageOptions { MessageGroupId = "groupA" }),
            new(new ChatMessage { MessageDescription = "GroupA_Message2" },
                new SQSMessageOptions { MessageGroupId = "groupA" }),
            new(new ChatMessage { MessageDescription = "GroupB_Message1" },
                new SQSMessageOptions { MessageGroupId = "groupB" }),
            new(new ChatMessage { MessageDescription = "GroupB_Message2" },
                new SQSMessageOptions { MessageGroupId = "groupB" }),
        };

        var batchResponse = await sqsPublisher.SendBatchAsync<ChatMessage>(entries);

        // Verify the batch response
        Assert.Equal(4, batchResponse.Successful.Count);
        Assert.Empty(batchResponse.Failed);

        // Wait to allow the published messages to propagate
        await Task.Delay(5000);

        // Receive all messages from the queue
        var receivedMessages = new List<Message>();
        for (var i = 0; i < 3; i++)
        {
            var receiveResponse = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MaxNumberOfMessages = 10
            });
            receivedMessages.AddRange(receiveResponse.Messages);
            if (receivedMessages.Count >= 4)
                break;
            await Task.Delay(1000);
        }

        Assert.Equal(4, receivedMessages.Count);

        var envelopeSerializer = _serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageDescriptions = new List<string>();
        foreach (var receivedMessage in receivedMessages)
        {
            var result = await envelopeSerializer.ConvertToEnvelopeAsync(receivedMessage);
            var envelope = result.Envelope as MessageEnvelope<ChatMessage>;
            Assert.NotNull(envelope);
            Assert.NotNull(envelope.Message);
            messageDescriptions.Add(envelope.Message.MessageDescription);
        }

        // Verify all messages arrived
        Assert.Contains("GroupA_Message1", messageDescriptions);
        Assert.Contains("GroupA_Message2", messageDescriptions);
        Assert.Contains("GroupB_Message1", messageDescriptions);
        Assert.Contains("GroupB_Message2", messageDescriptions);
    }

    [Fact]
    public async Task PublishMessageBatch_FifoQueue_WithoutMessageGroupId_ThrowsException()
    {
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        var messages = new List<ChatMessage>
        {
            new() { MessageDescription = "ShouldFail" }
        };

        // Sending to a FIFO queue without MessageGroupId should throw
        await Assert.ThrowsAsync<InvalidFifoPublishingRequestException>(
            () => sqsPublisher.SendBatchAsync(messages));
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _sqsClient.DeleteQueueAsync(_sqsQueueUrl);
        }
        catch { }
    }
}
