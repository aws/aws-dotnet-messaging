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

    [Fact]
    public async Task PublishMessageBatch_WithCallerIds_ReportsResultsUnderThoseIds()
    {
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        // 15 entries = 2 requests (10 + 5). Each message carries its Id so received messages can be matched back.
        var ids = Enumerable.Range(0, 15).Select(i => $"order-{i}").ToList();
        var entries = ids
            .Select(id => new SQSBatchEntry<ChatMessage>(new ChatMessage { MessageDescription = id }) { Id = id })
            .ToList();

        var batchResponse = await sqsPublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(ids, batchResponse.Successful.Select(s => s.Id!));
        Assert.Empty(batchResponse.Failed);
        Assert.Empty(batchResponse.Unprocessed);

        // The SQS MessageId reported for each caller Id is the one SQS delivers with that message
        var delivered = await GetDescriptionsByMessageIdAsync(await ReceiveMessagesAsync(ids.Count));
        Assert.Equal(ids.Count, delivered.Count);
        foreach (var success in batchResponse.Successful)
        {
            Assert.Equal(success.Id, delivered[success.MessageId!]);
        }
    }

    [Fact]
    public async Task PublishMessageBatch_RequestRejectedMidBatch_ReportsPartialResults()
    {
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        // 25 entries = 3 requests (10 + 10 + 5). SQS rejects the whole second request because "order.12" is not a
        // valid batch entry Id, so the third request is never sent.
        var ids = Enumerable.Range(0, 25).Select(i => i == 12 ? "order.12" : $"order-{i}").ToList();
        var entries = ids
            .Select(id => new SQSBatchEntry<ChatMessage>(new ChatMessage { MessageDescription = id }) { Id = id })
            .ToList();

        var ex = await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => sqsPublisher.SendBatchAsync<ChatMessage>(entries));

        Assert.IsType<InvalidBatchEntryIdException>(ex.InnerException);
        var partial = ex.PartialResponse!;
        Assert.Equal(ids.Take(10), partial.Successful.Select(s => s.Id!));
        Assert.Empty(partial.Failed);
        Assert.Equal(
            ids.Skip(10).Take(10).Select(id => (id, SQSSendBatchUnprocessedReason.OutcomeUnknown))
                .Concat(ids.Skip(20).Select(id => (id, SQSSendBatchUnprocessedReason.NotSent))),
            partial.Unprocessed.Select(u => (u.Id!, u.Reason)));

        // Only the messages of the first request were delivered
        var delivered = await GetDescriptionsByMessageIdAsync(await ReceiveMessagesAsync(10));
        Assert.Equal(ids.Take(10).OrderBy(id => id), delivered.Values.OrderBy(id => id));
    }

    [Fact]
    public async Task PublishMessageBatch_PerEntryFailure_IsReportedUnderItsId()
    {
        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();

        // SQS rejects only the entry with an out-of-range delay; the rest of the request is delivered
        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "order-0" }) { Id = "order-0" },
            new(new ChatMessage { MessageDescription = "order-1" }, new SQSMessageOptions { DelaySeconds = 1000 }) { Id = "order-1" },
            new(new ChatMessage { MessageDescription = "order-2" }) { Id = "order-2" }
        };

        var batchResponse = await sqsPublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(new[] { "order-0", "order-2" }, batchResponse.Successful.Select(s => s.Id));
        var failure = Assert.Single(batchResponse.Failed);
        Assert.Equal("order-1", failure.Id);
        Assert.Equal("InvalidParameterValue", failure.Code);
        Assert.True(failure.SenderFault);
        Assert.Empty(batchResponse.Unprocessed);

        var delivered = await GetDescriptionsByMessageIdAsync(await ReceiveMessagesAsync(2));
        Assert.Equal(new[] { "order-0", "order-2" }, delivered.Values.OrderBy(id => id));
    }

    /// <summary>
    /// Receives messages until <paramref name="expectedCount"/> have arrived (or attempts run out), then long-polls once
    /// more so that any message beyond the expected ones is also returned.
    /// </summary>
    private async Task<List<Message>> ReceiveMessagesAsync(int expectedCount)
    {
        var receivedMessages = new List<Message>();
        for (var i = 0; i < 15 && receivedMessages.Count < expectedCount; i++)
        {
            var receiveResponse = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 5
            });
            receivedMessages.AddRange(receiveResponse.Messages ?? new List<Message>());
        }

        var extraResponse = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _sqsQueueUrl,
            MaxNumberOfMessages = 10,
            WaitTimeSeconds = 2
        });
        receivedMessages.AddRange(extraResponse.Messages ?? new List<Message>());

        return receivedMessages;
    }

    /// <summary>
    /// Maps each received message's SQS MessageId to the <see cref="ChatMessage.MessageDescription"/> it carries.
    /// </summary>
    private async Task<Dictionary<string, string>> GetDescriptionsByMessageIdAsync(IEnumerable<Message> messages)
    {
        var envelopeSerializer = _serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var descriptions = new Dictionary<string, string>();
        foreach (var message in messages)
        {
            var result = await envelopeSerializer.ConvertToEnvelopeAsync(message);
            var envelope = Assert.IsType<MessageEnvelope<ChatMessage>>(result.Envelope);
            descriptions.Add(message.MessageId, envelope.Message.MessageDescription);
        }
        return descriptions;
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
