using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using AWS.Messaging.IntegrationTests.Models;
using System.Text.Json;
using AWS.Messaging.IntegrationTests.Handlers;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Serialization;

namespace AWS.Messaging.IntegrationTests;

public class SQSPublisherTests : IAsyncLifetime
{
    private readonly IAmazonSQS _sqsClient;
    private ServiceProvider _serviceProvider;
    private string _sqsQueueUrl;

    public SQSPublisherTests()
    {
        _sqsClient = new AmazonSQSClient();
        _serviceProvider = default!;
        _sqsQueueUrl = string.Empty;
    }

    public async Task InitializeAsync()
    {
        var createQueueResponse = await _sqsClient.CreateQueueAsync($"MPFTest-{Guid.NewGuid().ToString().Split('-').Last()}");
        _sqsQueueUrl = createQueueResponse.QueueUrl;

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
    public async Task PublishMessage()
    {
        var publishStartTime = DateTime.UtcNow;
        var publisher = _serviceProvider.GetRequiredService<IMessagePublisher>();
        await publisher.PublishAsync(new ChatMessage
        {
            MessageDescription = "Test1"
        });
        var publishEndTime = DateTime.UtcNow;

        // Wait to allow the published message to propagate through the system
        await Task.Delay(5000);

        var receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(_sqsQueueUrl);
        var message = Assert.Single(receiveMessageResponse.Messages);

        // Get the EnvelopeSerializer from the service provider
        var envelopeSerializer = _serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        // Use the EnvelopeSerializer to convert the message
        var result = await envelopeSerializer.ConvertToEnvelopeAsync(message);
        var envelope = result.Envelope as MessageEnvelope<ChatMessage>;

        Assert.NotNull(envelope);
        Assert.False(string.IsNullOrEmpty(envelope.Id));
        Assert.Equal("/aws/messaging", envelope.Source.ToString());
        Assert.True(envelope.TimeStamp > publishStartTime);
        Assert.True(envelope.TimeStamp < publishEndTime);
        Assert.Equal(typeof(ChatMessage).ToString(), envelope.MessageTypeIdentifier);

        var chatMessage = envelope.Message;
        Assert.NotNull(chatMessage);
        Assert.IsType<ChatMessage>(chatMessage);
        Assert.Equal("Test1", chatMessage.MessageDescription);
    }


    [Fact]
    public async Task PublishMessageBatch()
    {
        var publishStartTime = DateTime.UtcNow;
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        var messages = Enumerable.Range(1, 5)
            .Select(i => new ChatMessage { MessageDescription = $"BatchTest{i}" })
            .ToList();

        var batchResponse = await sqsPublisher.SendBatchAsync(messages);
        var publishEndTime = DateTime.UtcNow;

        // Verify the batch response
        Assert.Equal(5, batchResponse.Successful.Count);
        Assert.Empty(batchResponse.Failed);
        foreach (var entry in batchResponse.Successful)
        {
            Assert.False(string.IsNullOrEmpty(entry.MessageId));
            Assert.False(string.IsNullOrEmpty(entry.Id));
        }

        // Wait to allow the published messages to propagate
        await Task.Delay(5000);

        // Receive all messages from the queue
        var receivedMessages = new List<Message>();
        for (var i = 0; i < 3; i++) // Multiple receive calls since SQS may not return all at once
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
        for (var i = 0; i < receivedMessages.Count; i++)
        {
            var result = await envelopeSerializer.ConvertToEnvelopeAsync(receivedMessages[i]);
            var envelope = result.Envelope as MessageEnvelope<ChatMessage>;

            Assert.NotNull(envelope);
            Assert.False(string.IsNullOrEmpty(envelope.Id));
            Assert.Equal("/aws/messaging", envelope.Source.ToString());
            Assert.True(envelope.TimeStamp > publishStartTime);
            Assert.True(envelope.TimeStamp < publishEndTime);
            Assert.Equal(typeof(ChatMessage).ToString(), envelope.MessageTypeIdentifier);
            Assert.NotNull(envelope.Message);
            Assert.StartsWith("BatchTest", envelope.Message.MessageDescription);
        }
    }

    [Fact]
    public async Task PublishMessageBatch_WithAutoChunking()
    {
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        // Send 15 messages - should auto-chunk into 10 + 5
        var messages = Enumerable.Range(1, 15)
            .Select(i => new ChatMessage { MessageDescription = $"ChunkTest{i}" })
            .ToList();

        var batchResponse = await sqsPublisher.SendBatchAsync(messages);

        // Verify the batch response has all 15 successful
        Assert.Equal(15, batchResponse.Successful.Count);
        Assert.Empty(batchResponse.Failed);

        // Wait to allow the published messages to propagate
        await Task.Delay(5000);

        // Receive all messages from the queue. SQS may not return all messages in a single call
        // (max 10 per request, and may return fewer), so we poll repeatedly.
        var receivedMessages = new List<Message>();
        var maxAttempts = 15;
        for (var i = 0; i < maxAttempts && receivedMessages.Count < 15; i++)
        {
            var receiveResponse = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 5
            });
            receivedMessages.AddRange(receiveResponse.Messages);
        }

        Assert.Equal(15, receivedMessages.Count);
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
