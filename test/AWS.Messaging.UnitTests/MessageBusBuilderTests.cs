// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.EventBridge;
using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using AWS.Messaging.Configuration;
using AWS.Messaging.Lambda;
using AWS.Messaging.Publishers.EventBridge;
using AWS.Messaging.Publishers.SNS;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.Services.Backoff;
using AWS.Messaging.Services.Backoff.Policies;
using AWS.Messaging.UnitTests.MessageHandlers;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests;

public class MessageBusBuilderTests
{
    private readonly IServiceCollection _serviceCollection;

    public MessageBusBuilderTests()
    {
        _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddDefaultAWSOptions(new AWSOptions
        {
            Region = Amazon.RegionEndpoint.USWest2
        });
    }

    [Fact]
    public void BuildMessageBus()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<OrderInfo>("sqsQueueUrl");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messagePublisher = serviceProvider.GetService<IMessagePublisher>();
        Assert.NotNull(messagePublisher);

        CheckRequiredServices(serviceProvider);
    }

    [Fact]
    public void BuildMessageBus_MultipleInvocations()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<OrderInfo>("sqsQueueUrl");
            builder.AddMessageHandler<AddressInfoHandler, AddressInfo>();
        });

        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messagePublisher = serviceProvider.GetService<IMessagePublisher>();
        Assert.NotNull(messagePublisher);

        CheckRequiredServices(serviceProvider);

        var mesageConfiguration = serviceProvider.GetRequiredService<IMessageConfiguration>();
        Assert.Equal(2, mesageConfiguration.SubscriberMappings.Count);
        Assert.Equal(typeof(AddressInfo), mesageConfiguration.SubscriberMappings[0].MessageType);
        Assert.Equal(typeof(AddressInfoHandler), mesageConfiguration.SubscriberMappings[0].HandlerType);
        Assert.Equal(typeof(ChatMessage), mesageConfiguration.SubscriberMappings[1].MessageType);
        Assert.Equal(typeof(ChatMessageHandler), mesageConfiguration.SubscriberMappings[1].HandlerType);
    }

    [Fact]
    public void MessageBus_ConfigureBackoffPolicy_Default()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.NotNull(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.NotNull(backoffPolicy);

        Assert.IsType<CappedExponentialBackoffPolicy>(backoffPolicy);
    }

    [Fact]
    public void MessageBus_ConfigureBackoffPolicy_NoBackoffHandler()
    {
        _serviceCollection.AddAWSMessageBus(builder => { });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.Null(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.Null(backoffPolicy);
    }

    [Fact]
    public void MessageBus_ConfigureBackoffPolicy_NoBackoffPolicy()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");

            builder.ConfigureBackoffPolicy(options =>
            {
                options.UseNoBackoff();
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.NotNull(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.NotNull(backoffPolicy);

        Assert.IsType<NoBackoffPolicy>(backoffPolicy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MessageBus_ConfigureBackoffPolicy_IntervalBackoffPolicy(int retry)
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");

            builder.ConfigureBackoffPolicy(options =>
            {
                options.UseIntervalBackoff();
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.NotNull(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.NotNull(backoffPolicy);

        Assert.IsType<IntervalBackoffPolicy>(backoffPolicy);

        Assert.True(TimeSpan.FromSeconds(1) >= backoffPolicy.RetrieveBackoffTime(retry));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MessageBus_ConfigureBackoffPolicy_IntervalBackoffPolicy_WithOptions(int interval)
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");

            builder.ConfigureBackoffPolicy(options =>
            {
                options.UseIntervalBackoff(x =>
                {
                    x.FixedInterval = interval;
                });
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.NotNull(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.NotNull(backoffPolicy);

        Assert.IsType<IntervalBackoffPolicy>(backoffPolicy);

        Assert.True(TimeSpan.FromSeconds(interval) >= backoffPolicy.RetrieveBackoffTime(It.IsAny<int>()));
    }

    [Fact]
    public void MessageBus_ConfigureBackoffPolicy_CappedExponentialBackoffPolicy()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");

            builder.ConfigureBackoffPolicy(options =>
            {
                options.UseCappedExponentialBackoff();
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.NotNull(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.NotNull(backoffPolicy);

        Assert.IsType<CappedExponentialBackoffPolicy>(backoffPolicy);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    [InlineData(7, 10)]
    [InlineData(8, 20)]
    public void MessageBus_ConfigureBackoffPolicy_CappedExponentialBackoffPolicy_WithOptions(int retry, int cap)
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");

            builder.ConfigureBackoffPolicy(options =>
            {
                options.UseCappedExponentialBackoff(x =>
                {
                    x.CapBackoffTime = cap;
                });
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var backoffHandler = serviceProvider.GetService<IBackoffHandler>();
        Assert.NotNull(backoffHandler);

        var backoffPolicy = serviceProvider.GetService<IBackoffPolicy>();
        Assert.NotNull(backoffPolicy);

        Assert.IsType<CappedExponentialBackoffPolicy>(backoffPolicy);

        Assert.True(TimeSpan.FromSeconds(cap) >= backoffPolicy.RetrieveBackoffTime(retry));
    }

    [Fact]
    public void MessageBus_NoBuild_NoServices()
    {
        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        Assert.Null(messageConfiguration);

        var messagePublisher = serviceProvider.GetService<IMessagePublisher>();
        Assert.Null(messagePublisher);
    }

    [Fact]
    public void MessageBus_AddSQSQueue()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<OrderInfo>("sqsQueueUrl");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var sqsClient = serviceProvider.GetService<IAmazonSQS>();
        var sqsPublisher = serviceProvider.GetService<ISQSPublisher>();
        Assert.NotNull(sqsClient);
        Assert.NotNull(sqsPublisher);

        var snsClient = serviceProvider.GetService<IAmazonSimpleNotificationService>();
        var snsPublisher = serviceProvider.GetService<ISNSPublisher>();
        Assert.Null(snsClient);
        Assert.Null(snsPublisher);

        var eventBridgeClient = serviceProvider.GetService<IAmazonEventBridge>();
        var eventBridgePublisher = serviceProvider.GetService<IEventBridgePublisher>();
        Assert.Null(eventBridgeClient);
        Assert.Null(eventBridgePublisher);
    }

    [Fact]
    public void MessageBus_AddSNSTopic()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSNSPublisher<OrderInfo>("snsTopicUrl");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var snsClient = serviceProvider.GetService<IAmazonSimpleNotificationService>();
        var snsPublisher = serviceProvider.GetService<ISNSPublisher>();
        Assert.NotNull(snsClient);
        Assert.NotNull(snsPublisher);

        var sqsClient = serviceProvider.GetService<IAmazonSQS>();
        var sqsPublisher = serviceProvider.GetService<ISQSPublisher>();
        Assert.Null(sqsClient);
        Assert.Null(sqsPublisher);

        var eventBridgeClient = serviceProvider.GetService<IAmazonEventBridge>();
        var eventBridgePublisher = serviceProvider.GetService<IEventBridgePublisher>();
        Assert.Null(eventBridgeClient);
        Assert.Null(eventBridgePublisher);
    }

    [Fact]
    public void MessageBus_AddEventBus()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddEventBridgePublisher<OrderInfo>("eventBusName");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        var publisherMapping = messageConfiguration?.GetPublisherMapping(typeof(OrderInfo))!;
        var publisherConfiguration = (EventBridgePublisherConfiguration)publisherMapping.PublisherConfiguration;
        Assert.NotNull(publisherConfiguration);
        Assert.Equal("eventBusName", publisherConfiguration.PublisherEndpoint);
        Assert.True(string.IsNullOrEmpty(publisherConfiguration.EndpointID));

        var eventBridgeClient = serviceProvider.GetService<IAmazonEventBridge>();
        var eventBridgePublisher = serviceProvider.GetService<IEventBridgePublisher>();
        Assert.NotNull(eventBridgeClient);
        Assert.NotNull(eventBridgePublisher);

        var sqsClient = serviceProvider.GetService<IAmazonSQS>();
        var sqsPublisher = serviceProvider.GetService<ISQSPublisher>();
        Assert.Null(sqsClient);
        Assert.Null(sqsPublisher);

        var snsClient = serviceProvider.GetService<IAmazonSimpleNotificationService>();
        var snsPublisher = serviceProvider.GetService<ISNSPublisher>();
        Assert.Null(snsClient);
        Assert.Null(snsPublisher);
    }

    [Fact]
    public void MessageBus_AddEventBus_WithEndpointID()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddEventBridgePublisher<OrderInfo>("eventBusName", options: new EventBridgePublishOptions
            {
                EndpointID = "endpoint.123"
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        var publisherMapping = messageConfiguration?.GetPublisherMapping(typeof(OrderInfo))!;
        var publisherConfiguration = (EventBridgePublisherConfiguration)publisherMapping.PublisherConfiguration;
        Assert.NotNull(publisherConfiguration);
        Assert.Equal("eventBusName", publisherConfiguration.PublisherEndpoint);
        Assert.Equal("endpoint.123", publisherConfiguration.EndpointID);

        var eventBridgeClient = serviceProvider.GetService<IAmazonEventBridge>();
        var eventBridgePublisher = serviceProvider.GetService<IEventBridgePublisher>();
        Assert.NotNull(eventBridgeClient);
        Assert.NotNull(eventBridgePublisher);

        var sqsClient = serviceProvider.GetService<IAmazonSQS>();
        var sqsPublisher = serviceProvider.GetService<ISQSPublisher>();
        Assert.Null(sqsClient);
        Assert.Null(sqsPublisher);

        var snsClient = serviceProvider.GetService<IAmazonSimpleNotificationService>();
        var snsPublisher = serviceProvider.GetService<ISNSPublisher>();
        Assert.Null(snsClient);
        Assert.Null(snsPublisher);
    }

    [Fact]
    public void MessageBus_AddMessageHandler()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("sqsQueueUrl");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageHandler = serviceProvider.GetService<ChatMessageHandler>();
        Assert.NotNull(messageHandler);
    }

    [Fact]
    public void MessageBus_NoMessageHandler()
    {
        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageHandler = serviceProvider.GetService<ChatMessageHandler>();
        Assert.Null(messageHandler);
    }

    [Fact]
    public void MessageBus_AddSerializerOptions()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.ConfigureSerializationOptions(options =>
            {
                options.SystemTextJsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        Assert.NotNull(messageConfiguration);

        var jsonSerializerOptions = messageConfiguration.SerializationOptions.SystemTextJsonOptions;
        Assert.NotNull(jsonSerializerOptions);
        Assert.Equal(JsonNamingPolicy.CamelCase, jsonSerializerOptions.PropertyNamingPolicy);
    }

    /// <summary>
    /// Asserts that adding a SQS Poller will add the required
    /// factories and services to the service provider
    /// </summary>
    [Fact]
    public void MessageBus_AddSQSPoller()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        // Verify that a singleton MessagePumpService was added
        var messagePumpService = serviceProvider.GetServices<IHostedService>().OfType<MessagePumpService>().Single();
        Assert.NotNull(messagePumpService);

        // Verify that an SQS client was added
        var sqsClient = serviceProvider.GetService<IAmazonSQS>();
        Assert.NotNull(sqsClient);

        // Verify that the default factories used for subscribing to messages were added
        var messageManagerFactory = serviceProvider.GetService<IMessageManagerFactory>();
        Assert.NotNull(messageManagerFactory);
        Assert.IsType<DefaultMessageManagerFactory>(messageManagerFactory);

        var messagePollerFactory = serviceProvider.GetService<IMessagePollerFactory>();
        Assert.NotNull(messagePollerFactory);
        Assert.IsType<DefaultMessagePollerFactory>(messagePollerFactory);

        // Verify that the helper to invoke message handlers was added
        var handlerInvoker = serviceProvider.GetService<IHandlerInvoker>();
        Assert.NotNull(handlerInvoker);
        Assert.IsType<HandlerInvoker>(handlerInvoker);

        // Verify that the message framework configuration object exists
        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        Assert.NotNull(messageConfiguration);
        Assert.Single(messageConfiguration.MessagePollerConfigurations);

        // ...and contains a single poller configuration
        var configuration = messageConfiguration.MessagePollerConfigurations[0];
        Assert.NotNull(configuration);

        // ...of the expected type, with expected default parameters
        if (configuration is SQSMessagePollerConfiguration sqsConfiguration)
        {
            Assert.Equal("queueUrl", sqsConfiguration.SubscriberEndpoint);
            Assert.Equal(10, sqsConfiguration.MaxNumberOfConcurrentMessages);
        }
        else
        {
            Assert.Fail($"Expected configuration to be of type {typeof(SQSMessagePollerConfiguration)}");
        }

        CheckRequiredServices(serviceProvider);
    }

    /// <summary>
    /// Slimmer variation on <see cref="MessageBus_AddSQSPoller"/> that tests AddSQSPoller
    /// with a non-default values for the SQS options
    [Fact]
    public void MessageBus_AddSQSPoller_NonDefaultOptions()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("queueUrl", options => {
                options.MaxNumberOfConcurrentMessages = 20;
                options.VisibilityTimeout = 5;
                options.VisibilityTimeoutExtensionThreshold = 3;
                options.VisibilityTimeoutExtensionHeartbeatInterval = 2;
                options.WaitTimeSeconds = 10;
            });
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        Assert.NotNull(messageConfiguration);
        Assert.Single(messageConfiguration.MessagePollerConfigurations);

        var configuration = messageConfiguration.MessagePollerConfigurations[0];
        Assert.NotNull(configuration);

        if (configuration is SQSMessagePollerConfiguration sqsConfiguration)
        {
            Assert.Equal("queueUrl", sqsConfiguration.SubscriberEndpoint);
            Assert.Equal(20, sqsConfiguration.MaxNumberOfConcurrentMessages);
            Assert.Equal(5, sqsConfiguration.VisibilityTimeout);
            Assert.Equal(3, sqsConfiguration.VisibilityTimeoutExtensionThreshold);
            Assert.Equal(2, sqsConfiguration.VisibilityTimeoutExtensionHeartbeatInterval);
            Assert.Equal(10, sqsConfiguration.WaitTimeSeconds);
        }
        else
        {
            Assert.Fail($"Expected configuration to be of type {typeof(SQSMessagePollerConfiguration)}");
        }
    }

    [Fact]
    public void MessageBus_AddSerializationCallback()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSNSPublisher<OrderInfo>("snsTopicUrl");
            builder.AddSerializationCallback(new Mock<ISerializationCallback>().Object);
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();

        Assert.NotNull(messageConfiguration);
        Assert.Single(messageConfiguration.SerializationCallbacks);
    }

    /// <summary>
    /// Verifies that <see cref="SQSMessagePollerConfiguration"/> and
    /// <see cref="SQSMessagePollerOptions"/> are kept in sync.
    /// </summary>
    [Fact]
    public void SQSMessagePollerConfiguration_SQSMessagePollerOptions_InSync()
    {
        var internalConfigurationMembers = typeof(SQSMessagePollerConfiguration).GetProperties();
        var publicConfigurationMembers = typeof(SQSMessagePollerOptions).GetProperties();

        // The expected difference of 1 is for the queueURL property, which exists in the internal configuration
        // but not the public options because it is required to be set via the constructor.
        if (internalConfigurationMembers.Count() - 1 != publicConfigurationMembers.Count())
        {
            Assert.Fail($"There is a mismatch in the number of properties on {nameof(SQSMessagePollerConfiguration)} and {nameof(SQSMessagePollerOptions)}. " +
                $"Ensure that new public properties to configure SQS polling are added to both classes, " +
                $"and then is set appropriately in {nameof(MessageBusBuilder.AddSQSPoller)} in {nameof(MessageBusBuilder)}.");
        }
    }

    /// <summary>
    /// Test cases for <see cref="SQSMessagePollerConfiguration_SQSMessagePollerOptions_Invalid"/>
    /// </summary>
    public static IEnumerable<object[]> GetInvalidSQSMessagePollerOptionsCases()
    {
        // MaxNumberOfConcurrentMessages must be postive
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.MaxNumberOfConcurrentMessages = -1) };
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.MaxNumberOfConcurrentMessages = 0) };

        // VisibilityTimeout must be between 0 seconds and 12 hours inclusive
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.VisibilityTimeout = -1) };
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.VisibilityTimeout = (int)TimeSpan.FromHours(12).TotalSeconds + 1) };

        // WaitTimeSeconds must be between 0 and 20 seconds inclusive
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.WaitTimeSeconds = -1) };
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.WaitTimeSeconds = 21) };

        // VisibilityTimeoutExtensionThreshold must be postive
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.VisibilityTimeoutExtensionThreshold = -1) };
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.VisibilityTimeoutExtensionThreshold = 0) };

        // VisibilityTimeoutExtensionThreshold must be strictly less than than VisibilityTimeout
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) =>
        {
            options.VisibilityTimeout = 5;
            options.VisibilityTimeoutExtensionThreshold = 5;
        })};
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) =>
        {
            options.VisibilityTimeout = 4;
            options.VisibilityTimeoutExtensionThreshold = 5;
        })};

        // VisibilityTimeoutExtensionHeartbeatInterval must be postive
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.VisibilityTimeoutExtensionHeartbeatInterval = -1) };
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) => options.VisibilityTimeoutExtensionHeartbeatInterval = 0) };

        // VisibilityTimeoutExtensionHeartbeatInterval must be strictly less than than VisibilityTimeoutExtensionThreshold
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) =>
        {
            options.VisibilityTimeoutExtensionHeartbeatInterval = 5;
            options.VisibilityTimeoutExtensionThreshold = 5;
        })};
        yield return new object[] { new Action<SQSMessagePollerOptions>((options) =>
        {
            options.VisibilityTimeoutExtensionHeartbeatInterval  = 6;
            options.VisibilityTimeoutExtensionThreshold = 5;
        })};

    }

    /// <summary>
    /// Tests that an exception is thrown when configuring the SQS poller with invalid options
    /// </summary>
    /// <param name="options">Action that returns SQSMessagePollerOptions</param>
    [Theory]
    [MemberData(nameof(GetInvalidSQSMessagePollerOptionsCases))]
    public void SQSMessagePollerConfiguration_SQSMessagePollerOptions_Invalid(Action<SQSMessagePollerOptions> options)
    {
        Assert.Throws<InvalidSQSMessagePollerOptionsException>(() =>
            _serviceCollection.AddAWSMessageBus(builder =>
            {
                builder.AddSQSPoller("queueUrl", options);
            }));
    }

    /// <summary>
    /// Tests that an exception is thrown when configuring Lambda message processor with invalid options.
    /// </summary>
    [Fact]
    public void lambdaMessageProcessorConfiguration_LambdaMessagingOptions_Invalid()
    {
        Assert.Throws<InvalidLambdaMessagingOptionsException>(() =>
            _serviceCollection.AddAWSMessageBus(builder =>
            {
                builder.AddLambdaMessageProcessor(options =>
                {
                    // Any value <= 0 is invalid
                    options.MaxNumberOfConcurrentMessages = -1;
                });
            }));
    }

    [Fact]
    public void MessageBus_Default_RegistersStandardImplementations()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            // No experimental features enabled
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageSerializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        Assert.IsType<EnvelopeSerializer>(envelopeSerializer);
        Assert.IsType<MessageSerializer>(messageSerializer);
    }

    [Fact]
    public void MessageBus_EnableExperimentalFeatures_RegistersExperimentalImplementations()
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.EnableExperimentalFeatures();
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageSerializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        Assert.IsType<EnvelopeSerializerUtf8JsonWriter>(envelopeSerializer);
        Assert.IsType<MessageSerializerUtf8JsonWriter>(messageSerializer);
    }

    // These services must be present irrespective of whether publishers or subscribers are configured.
    private void CheckRequiredServices(ServiceProvider serviceProvider)
    {
        var messageConfiguration = serviceProvider.GetService<IMessageConfiguration>();
        Assert.NotNull(messageConfiguration);

        var messageSerializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(messageSerializer);

        var envelopeSerializer = serviceProvider.GetService<IEnvelopeSerializer>();
        Assert.NotNull(envelopeSerializer);

        var dateTimeHandler = serviceProvider.GetService<IDateTimeHandler>();
        Assert.NotNull(dateTimeHandler);

        var messageIdGenerator = serviceProvider.GetService<IMessageIdGenerator>();
        Assert.NotNull(messageIdGenerator);
    }
}
