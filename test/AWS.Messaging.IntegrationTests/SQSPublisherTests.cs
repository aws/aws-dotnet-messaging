using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using AWS.Messaging.IntegrationTests.Models;
using System.Text.Json;
using AWS.Messaging.IntegrationTests.Handlers;
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


    public async Task DisposeAsync()
    {
        try
        {
            await _sqsClient.DeleteQueueAsync(_sqsQueueUrl);
        }
        catch { }
    }
}
