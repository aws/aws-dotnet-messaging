using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using AWS.Messaging.IntegrationTests.Models;
using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

namespace AWS.Messaging.IntegrationTests;

public class SNSPublisherTests : IAsyncLifetime
{
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly IAmazonSQS _sqsClient;
    private ServiceProvider _serviceProvider;
    private string _snsTopicArn;
    private string _sqsQueueUrl;

    public SNSPublisherTests()
    {
        _sqsClient = new AmazonSQSClient();
        _snsClient = new AmazonSimpleNotificationServiceClient();
        _serviceProvider = default!;
        _snsTopicArn = string.Empty;
        _sqsQueueUrl = string.Empty;
    }

    public async Task InitializeAsync()
    {
        var resourceName = $"MPFTest-{Guid.NewGuid().ToString().Split('-').Last()}";
        var createQueueResponse = await _sqsClient.CreateQueueAsync(resourceName);
        _sqsQueueUrl = createQueueResponse.QueueUrl;

        var createTopicResponse = await _snsClient.CreateTopicAsync(resourceName);
        _snsTopicArn = createTopicResponse.TopicArn;

        await _snsClient.SubscribeQueueAsync(_snsTopicArn, _sqsClient, _sqsQueueUrl);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSNSPublisher<ChatMessage>(_snsTopicArn);
            builder.AddMessageSource("/aws/messaging");
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

        // SNS adds an external envelope which we need to strip away
        var snsEnvelope = JsonSerializer.Deserialize<SNSEnvelope>(message.Body);
        Assert.NotNull(snsEnvelope);

        var envelope = JsonSerializer.Deserialize<MessageEnvelope<string>>(snsEnvelope.Message);
        Assert.NotNull(envelope);
        Assert.False(string.IsNullOrEmpty(envelope.Id));
        Assert.Equal("/aws/messaging", envelope.Source.ToString());
        Assert.True(envelope.TimeStamp > publishStartTime);
        Assert.True(envelope.TimeStamp < publishEndTime);

        var messageType = Type.GetType(envelope.MessageTypeIdentifier);
        Assert.NotNull(messageType);

        var chatMessageObject = JsonSerializer.Deserialize(envelope.Message, messageType);
        var chatMessage = Assert.IsType<ChatMessage>(chatMessageObject);
        Assert.Equal("Test1", chatMessage.MessageDescription);
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _snsClient.DeleteTopicAsync(new DeleteTopicRequest { TopicArn = _snsTopicArn });
        }
        catch { }
        try
        {
            await _sqsClient.DeleteQueueAsync(_sqsQueueUrl);
        }
        catch { }
    }
}
