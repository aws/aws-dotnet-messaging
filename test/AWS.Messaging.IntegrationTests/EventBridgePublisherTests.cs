using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using AWS.Messaging.IntegrationTests.Models;
using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using System.Collections.Generic;
using Amazon.SQS.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using AWS.Messaging.IntegrationTests.Handlers;
using AWS.Messaging.Serialization;

namespace AWS.Messaging.IntegrationTests;

public class EventBridgePublisherTests : IAsyncLifetime
{
    private readonly IAmazonEventBridge _eventBridgeClient;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonSecurityTokenService _stsClient;
    private ServiceProvider _serviceProvider;
    private string _eventBusArn;
    private string _resourceName;
    private string _sqsQueueUrl;

    public EventBridgePublisherTests()
    {
        _sqsClient = new AmazonSQSClient();
        _eventBridgeClient = new AmazonEventBridgeClient();
        _stsClient = new AmazonSecurityTokenServiceClient();
        _serviceProvider = default!;
        _eventBusArn = string.Empty;
        _resourceName = string.Empty;
        _sqsQueueUrl = string.Empty;
    }

    public async Task InitializeAsync()
    {
        _resourceName = $"MPFTest-{Guid.NewGuid().ToString().Split('-').Last()}";
        var createQueueResponse = await _sqsClient.CreateQueueAsync(_resourceName);
        _sqsQueueUrl = createQueueResponse.QueueUrl;
        var getQueueAttributesResponse = await _sqsClient.GetQueueAttributesAsync(_sqsQueueUrl, new List<string> { "QueueArn" });
        var sqsQueueArn = getQueueAttributesResponse.QueueARN;
        await _sqsClient.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = _sqsQueueUrl,
            Attributes = new Dictionary<string, string>
            {
                { "Policy",
                    @$"{{
                        ""Version"": ""2008-10-17"",
                        ""Statement"": [
                            {{
                                ""Effect"": ""Allow"",
                                ""Principal"": {{
                                    ""Service"": ""events.amazonaws.com""
                                }},
                                ""Action"": ""SQS:SendMessage"",
                                ""Resource"": ""{sqsQueueArn}""
                            }}
                        ]
                    }}"
                }
            }
        });

        var createEventBusResponse = await _eventBridgeClient.CreateEventBusAsync(
            new CreateEventBusRequest
            {
                Name = _resourceName
            });
        _eventBusArn = createEventBusResponse.EventBusArn;

        var getCallerIdentityResponse = await _stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest());
        await _eventBridgeClient.PutRuleAsync(new PutRuleRequest
        {
            Name = _resourceName,
            EventBusName = _resourceName,
            EventPattern =
            @$"{{
              ""account"": [""{getCallerIdentityResponse.Account}""],
              ""source"": [""/aws/messaging""]
            }}"
        });

        await _eventBridgeClient.PutTargetsAsync(new PutTargetsRequest
        {
            EventBusName = _resourceName,
            Targets = new List<Target>
            {
                new Target
                {
                    Arn = sqsQueueArn,
                    Id = _resourceName
                }
            },
            Rule = _resourceName
        });

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddEventBridgePublisher<ChatMessage>(_eventBusArn);
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

        var envelopeDeserializer = _serviceProvider.GetRequiredService<IEnvelopeDeserializer>();

        // Use the EnvelopeDeserializer to convert the message
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(message);
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
        Assert.Equal("Test1", chatMessage.MessageDescription);;
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _eventBridgeClient.RemoveTargetsAsync(new RemoveTargetsRequest
            {
                EventBusName = _resourceName,
                Force = true,
                Ids = new List<string> { _resourceName },
                Rule = _resourceName
            });
        } catch { }
        try
        {
            await _eventBridgeClient.DeleteRuleAsync(new DeleteRuleRequest
            {
                EventBusName = _resourceName,
                Name = _resourceName
            });
        }
        catch { }
        try
        {
            await _eventBridgeClient.DeleteEventBusAsync(new DeleteEventBusRequest
            {
                Name = _resourceName
            });
        }
        catch { }
        try
        {
            await _sqsClient.DeleteQueueAsync(_sqsQueueUrl);
        }
        catch { }
    }
}
