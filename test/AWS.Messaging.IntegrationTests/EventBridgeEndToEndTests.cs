// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.EventBridge.Model;
using Amazon.EventBridge;
using Amazon.SQS.Model;
using Amazon.SQS;
using AWS.Messaging.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AWS.Messaging.IntegrationTests.Handlers;
using AWS.Messaging.Services;
using AWS.Messaging.Tests.Common;
using Microsoft.Extensions.Hosting;
using System.Threading;

namespace AWS.Messaging.IntegrationTests;

public class EventBridgeEndToEndTests : IAsyncLifetime
{
    private readonly TestAwsBackend _backend;
    private readonly IAmazonEventBridge _eventBridgeClient;
    private readonly IAmazonSQS _sqsClient;
    private ServiceProvider _serviceProvider;
    private string _eventBusArn;
    private string _resourceName;
    private string _sqsQueueUrl;

    public EventBridgeEndToEndTests()
    {
        _backend = new TestAwsBackend();
        _sqsClient = _backend.CreateSqsClient();
        _eventBridgeClient = _backend.CreateEventBridgeClient();
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

        var accountId = await _backend.GetAccountIdAsync();
        await _eventBridgeClient.PutRuleAsync(new PutRuleRequest
        {
            Name = _resourceName,
            EventBusName = _resourceName,
            EventPattern =
            @$"{{
              ""account"": [""{accountId}""],
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
        serviceCollection.AddSingleton<TempStorage<ChatMessage>>();
        serviceCollection.AddSingleton<IAmazonSQS>(_sqsClient);
        serviceCollection.AddSingleton<IAmazonEventBridge>(_eventBridgeClient);
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddEventBridgePublisher<ChatMessage>(_eventBusArn);
            builder.AddMessageSource("/aws/messaging");
            builder.AddSQSPoller(_sqsQueueUrl, options =>
            {
                options.VisibilityTimeoutExtensionThreshold = 3;
            });
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishAndProcessMessage()
    {
        var publishStartTime = DateTime.UtcNow;
        var publisher = _serviceProvider.GetRequiredService<IMessagePublisher>();
        await publisher.PublishAsync(new ChatMessage
        {
            MessageDescription = "Test1"
        });
        var publishEndTime = DateTime.UtcNow;

        var pump = _serviceProvider.GetRequiredService<IHostedService>() as MessagePumpService;
        Assert.NotNull(pump);
        var source = new CancellationTokenSource();

        await pump.StartAsync(source.Token);

        var tempStorage = _serviceProvider.GetRequiredService<TempStorage<ChatMessage>>();
        source.CancelAfter(60000);
        await AsyncTestUtilities.WaitUntilAsync(() => tempStorage.Messages.Count >= 1, source.Token);
        source.Cancel();

        var messageEnvelope = Assert.Single(tempStorage.Messages);
        Assert.False(string.IsNullOrEmpty(messageEnvelope.Id));
        Assert.Equal("/aws/messaging", messageEnvelope.Source.ToString());
        Assert.True(messageEnvelope.TimeStamp > publishStartTime);
        Assert.True(messageEnvelope.TimeStamp < publishEndTime);
        Assert.Equal("Test1", messageEnvelope.Message.MessageDescription);
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
        }
        catch { }
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
