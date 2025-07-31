// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using AWS.Messaging.Configuration.Internal;
using AWS.Messaging.Publishers;
using AWS.Messaging.Publishers.EventBridge;
using AWS.Messaging.Publishers.SNS;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.Services.Backoff;
using AWS.Messaging.Services.Backoff.Policies;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace AWS.Messaging.Configuration;

/// <summary>
/// This <see cref="MessageBusBuilder"/> is used to configure the AWS messaging framework, including adding publishers and subscribers.
/// </summary>
public class MessageBusBuilder : IMessageBusBuilder
{
    private static readonly ConcurrentDictionary<IServiceCollection, MessageConfiguration> _messageConfigurations = new();
    private readonly MessageConfiguration _messageConfiguration;
    private readonly IList<ServiceDescriptor> _additionalServices = new List<ServiceDescriptor>();
    private readonly IServiceCollection _serviceCollection;

    /// <summary>
    /// Creates an instance of <see cref="MessageBusBuilder"/>.
    /// </summary>
    public MessageBusBuilder(IServiceCollection services)
    {
        _serviceCollection = services;
        if (_messageConfigurations.TryGetValue(services, out var config))
        {
            _messageConfiguration = config;
        }
        else
        {
            _messageConfiguration = new MessageConfiguration();
            _messageConfigurations[services] = _messageConfiguration;
        }
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddSQSPublisher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string? queueUrl, string? messageTypeIdentifier = null)
    {
        return AddSQSPublisher(typeof(TMessage), queueUrl, messageTypeIdentifier);
    }

    private IMessageBusBuilder AddSQSPublisher([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type messageType, string? queueUrl, string? messageTypeIdentifier = null)
    {
        var sqsPublisherConfiguration = new SQSPublisherConfiguration(queueUrl);
        return AddPublisher(messageType, sqsPublisherConfiguration, PublisherTargetType.SQS_PUBLISHER, messageTypeIdentifier);
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddSNSPublisher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string? topicUrl, string? messageTypeIdentifier = null)
    {
        return AddSNSPublisher(typeof(TMessage), topicUrl, messageTypeIdentifier);
    }

    private IMessageBusBuilder AddSNSPublisher([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type messageType, string? topicUrl, string? messageTypeIdentifier = null)
    {
        var snsPublisherConfiguration = new SNSPublisherConfiguration(topicUrl);
        return AddPublisher(messageType, snsPublisherConfiguration, PublisherTargetType.SNS_PUBLISHER, messageTypeIdentifier);
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddEventBridgePublisher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string? eventBusName, string? messageTypeIdentifier = null, EventBridgePublishOptions? options = null)
    {
        return AddEventBridgePublisher(typeof(TMessage), eventBusName, messageTypeIdentifier, options);
    }

    private IMessageBusBuilder AddEventBridgePublisher([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type messageType, string? eventBusName, string? messageTypeIdentifier = null, EventBridgePublishOptions? options = null)
    {
        var eventBridgePublisherConfiguration = new EventBridgePublisherConfiguration(eventBusName)
        {
            EndpointID = options?.EndpointID
        };
        return AddPublisher(messageType, eventBridgePublisherConfiguration, PublisherTargetType.EVENTBRIDGE_PUBLISHER, messageTypeIdentifier);
    }

    private IMessageBusBuilder AddPublisher([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type messageType, IMessagePublisherConfiguration publisherConfiguration, string publisherType, string? messageTypeIdentifier = null)
    {
        var publisherMapping = new PublisherMapping(messageType, publisherConfiguration, publisherType, messageTypeIdentifier);
        _messageConfiguration.PublisherMappings.Add(publisherMapping);
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddMessageHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] THandler, TMessage>(string? messageTypeIdentifier = null)
        where THandler : IMessageHandler<TMessage>
    {
        var subscriberMapping = SubscriberMapping.Create<THandler, TMessage>(messageTypeIdentifier);
        _messageConfiguration.SubscriberMappings.Add(subscriberMapping);
        return this;
    }

    private IMessageBusBuilder AddMessageHandler([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type handlerType, Type messageType, Func<MessageEnvelope> envelopeFactory, MethodInfo middlewareInvokeAsyncMehodInfo, string? messageTypeIdentifier = null)
    {
        var subscriberMapping = new SubscriberMapping(handlerType, messageType, envelopeFactory, middlewareInvokeAsyncMehodInfo, messageTypeIdentifier);
        _messageConfiguration.SubscriberMappings.Add(subscriberMapping);
        return this;
    }

    public IMessageBusBuilder AddMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMiddleware>(ServiceLifetime serviceLifetime = ServiceLifetime.Singleton)
        where TMiddleware : class, IMiddleware
    {
        var subsriberMiddleware = SubscriberMiddleware.Create<TMiddleware>(serviceLifetime);
        _messageConfiguration.SubscriberMiddleware.Add(subsriberMiddleware);
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddSQSPoller(string queueUrl, Action<SQSMessagePollerOptions>? options = null)
    {
        // Create the user-provided options class
        var sqsMessagePollerOptions = new SQSMessagePollerOptions();

        options?.Invoke(sqsMessagePollerOptions);

        sqsMessagePollerOptions.Validate();

        // Copy that to our internal options class
        var sqsMessagePollerConfiguration = new SQSMessagePollerConfiguration(queueUrl)
        {
            MaxNumberOfConcurrentMessages = sqsMessagePollerOptions.MaxNumberOfConcurrentMessages,
            VisibilityTimeout = sqsMessagePollerOptions.VisibilityTimeout,
            VisibilityTimeoutExtensionThreshold = sqsMessagePollerOptions.VisibilityTimeoutExtensionThreshold,
            VisibilityTimeoutExtensionHeartbeatInterval = sqsMessagePollerOptions.VisibilityTimeoutExtensionHeartbeatInterval,
            WaitTimeSeconds = sqsMessagePollerOptions.WaitTimeSeconds,
            IsExceptionFatal = sqsMessagePollerOptions.IsExceptionFatal
        };

        _messageConfiguration.MessagePollerConfigurations.Add(sqsMessagePollerConfiguration);
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder ConfigureSerializationOptions(Action<SerializationOptions> options)
    {
        options(_messageConfiguration.SerializationOptions);
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddSerializationCallback(ISerializationCallback serializationCallback)
    {
        _messageConfiguration.SerializationCallbacks.Add(serializationCallback);
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddMessageSource(string messageSource)
    {
        _messageConfiguration.Source = messageSource;
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddMessageSourceSuffix(string suffix)
    {
        _messageConfiguration.SourceSuffix = suffix;
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder ConfigurePollingControlToken(PollingControlToken pollingControlToken)
    {
        _messageConfiguration.PollingControlToken = pollingControlToken;
        return this;
    }

    /// <inheritdoc/>
    [RequiresDynamicCode("This method requires loading types dynamically as defined in the configuration system.")]
    [RequiresUnreferencedCode("This method requires loading types dynamically as defined in the configuration system.")]
    public IMessageBusBuilder LoadConfigurationFromSettings(IConfiguration configuration)
    {
        // This call needs to happen in this function so that the calling assembly is the customer's assembly.
        var callingAssembly = Assembly.GetCallingAssembly();

        var settings = configuration.GetSection(ApplicationSettings.SectionName).Get<ApplicationSettings>();
        if (settings is null)
            return this;

        if (settings.SQSPublishers != null)
        {
            foreach (var sqsPublisher in settings.SQSPublishers)
            {
                var messageType = GetTypeFromAssemblies(callingAssembly, sqsPublisher.MessageType)
                    ?? throw new InvalidAppSettingsConfigurationException($"Unable to find the provided message type '{sqsPublisher.MessageType}'.");

                AddSQSPublisher(messageType, sqsPublisher.QueueUrl, sqsPublisher.MessageTypeIdentifier);
            }
        }

        if (settings.SNSPublishers != null)
        {
            foreach (var snsPublisher in settings.SNSPublishers)
            {
                var messageType = GetTypeFromAssemblies(callingAssembly, snsPublisher.MessageType)
                    ?? throw new InvalidAppSettingsConfigurationException($"Unable to find the provided message type '{snsPublisher.MessageType}'.");

                AddSNSPublisher(messageType, snsPublisher.TopicUrl, snsPublisher.MessageTypeIdentifier);
            }
        }

        if (settings.EventBridgePublishers != null)
        {
            foreach (var eventBridgePublisher in settings.EventBridgePublishers)
            {
                var messageType = GetTypeFromAssemblies(callingAssembly, eventBridgePublisher.MessageType)
                    ?? throw new InvalidAppSettingsConfigurationException($"Unable to find the provided message type '{eventBridgePublisher.MessageType}'.");

                AddEventBridgePublisher(messageType, eventBridgePublisher.EventBusName, eventBridgePublisher.MessageTypeIdentifier, eventBridgePublisher.Options);
            }
        }

        if (settings.MessageHandlers != null)
        {
            // This is not Native AOT compatible but the method in general is marked
            // as not being Native AOT compatible due to loading dynamic types. So this
            // not being Native AOT compatible is okay.
            var middlwareInokeMethod = typeof(IMiddleware).GetMethod(nameof(IMiddleware.InvokeAsync))!;

            foreach (var messageHandler in settings.MessageHandlers)
            {
                var messageType = GetTypeFromAssemblies(callingAssembly, messageHandler.MessageType)
                    ?? throw new InvalidAppSettingsConfigurationException($"Unable to find the provided message type '{messageHandler.MessageType}'.");
                var handlerType = GetTypeFromAssemblies(callingAssembly, messageHandler.HandlerType)
                    ?? throw new InvalidAppSettingsConfigurationException($"Unable to find the provided message handler type '{messageHandler.HandlerType}'.");

                // This func is not Native AOT compatible but the method in general is marked
                // as not being Native AOT compatible due to loading dynamic types. So this
                // func not being Native AOT compatible is okay.
                MessageEnvelope envelopeFactory()
                {
                    var messageEnvelopeType = typeof(MessageEnvelope<>).MakeGenericType(messageType);
                    var envelope = Activator.CreateInstance(messageEnvelopeType);
                    if (envelope == null || envelope is not MessageEnvelope)
                    {
                        throw new InvalidOperationException($"Failed to create a {nameof(MessageEnvelope)} of type '{messageEnvelopeType.FullName}'");

                    }
                    return (MessageEnvelope)envelope;
                }

                // MakeGenericMethod is not Native AOT compatible but the method in general is marked
                // as not being Native AOT compatible due to loading dynamic types. So this
                // method not being Native AOT compatible is okay.
                var middlewareInvokeAsyncMethodInfo = middlwareInokeMethod.MakeGenericMethod(messageType);

                AddMessageHandler(handlerType, messageType, envelopeFactory, middlewareInvokeAsyncMethodInfo, messageHandler.MessageTypeIdentifier);
            }
        }

        if (settings.SQSPollers != null)
        {
            foreach (var sqsPoller in settings.SQSPollers)
            {
                Action<SQSMessagePollerOptions>? options = null;
                if (sqsPoller.Options != null)
                {
                    options =
                        options =>
                        {
                            options.MaxNumberOfConcurrentMessages = sqsPoller.Options.MaxNumberOfConcurrentMessages;
                            options.VisibilityTimeout = sqsPoller.Options.VisibilityTimeout;
                            options.WaitTimeSeconds = sqsPoller.Options.WaitTimeSeconds;
                            options.VisibilityTimeoutExtensionHeartbeatInterval = sqsPoller.Options.VisibilityTimeoutExtensionHeartbeatInterval;
                            options.VisibilityTimeoutExtensionThreshold = sqsPoller.Options.VisibilityTimeoutExtensionThreshold;
                        };
                }

                AddSQSPoller(sqsPoller.QueueUrl, options);
            }
        }

        if (settings.LogMessageContent != null)
            _messageConfiguration.LogMessageContent = settings.LogMessageContent ?? false;

        if (settings.BackoffPolicy != null)
        {
            _messageConfiguration.BackoffPolicy = settings.BackoffPolicy ?? BackoffPolicy.CappedExponential;

            if (settings.IntervalBackoffOptions != null)
            {
                _messageConfiguration.IntervalBackoffOptions = settings.IntervalBackoffOptions;
            }

            if (settings.CappedExponentialBackoffOptions != null)
            {
                _messageConfiguration.CappedExponentialBackoffOptions = settings.CappedExponentialBackoffOptions;
            }
        }

        return this;
    }

    [RequiresUnreferencedCode("This method requires loading types dynamically as defined in the configuration system.")]
    private Type? GetTypeFromAssemblies(Assembly callingAssembly, string typeValue)
    {
        if (typeValue.Contains(','))
        {
            return Type.GetType(typeValue);
        }
        else
        {
            return callingAssembly.GetType(typeValue);
        }
    }

    /// <inheritdoc/>
    public IMessageBusBuilder AddAdditionalService(ServiceDescriptor serviceDescriptor)
    {
        _additionalServices.Add(serviceDescriptor);
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder EnableMessageContentLogging()
    {
        _messageConfiguration.LogMessageContent = true;
        return this;
    }

    /// <inheritdoc/>
    public IMessageBusBuilder ConfigureBackoffPolicy(Action<BackoffPolicyBuilder> configure)
    {
        var builder = new BackoffPolicyBuilder(_messageConfiguration);

        configure(builder);
        return this;
    }

    internal void Build()
    {
        LoadConfigurationFromEnvironment();

        // Make sure there is at least the default null implementation of the logger to injected so that
        // the DI constructors can be satisfied.
        _serviceCollection.TryAdd(ServiceDescriptor.Singleton<ILoggerFactory, NullLoggerFactory>());
        _serviceCollection.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(NullLogger<>)));

        _serviceCollection.TryAddSingleton(_messageConfiguration.PollingControlToken);
        _serviceCollection.TryAddSingleton<IMessageConfiguration>(_messageConfiguration);
        _serviceCollection.TryAddSingleton<IMessageSerializer, MessageSerializer>();
        _serviceCollection.TryAddSingleton<IEnvelopeSerializer, EnvelopeSerializer>();
        _serviceCollection.TryAddSingleton<IDateTimeHandler, DateTimeHandler>();
        _serviceCollection.TryAddSingleton<IMessageIdGenerator, MessageIdGenerator>();
        _serviceCollection.TryAddSingleton<IAWSClientProvider, AWSClientProvider>();
        _serviceCollection.TryAddSingleton<IMessageSourceHandler, MessageSourceHandler>();
        _serviceCollection.TryAddSingleton<IEnvironmentManager, EnvironmentManager>();
        _serviceCollection.TryAddSingleton<IEC2InstanceMetadataManager, EC2InstanceMetadataManager>();
        _serviceCollection.TryAddSingleton<IECSContainerMetadataManager, ECSContainerMetadataManager>();
        _serviceCollection.AddHttpClient("ECSMetadataClient");
        _serviceCollection.TryAddSingleton<IMessageManagerFactory, DefaultMessageManagerFactory>();
        _serviceCollection.TryAddSingleton<IHandlerInvoker, HandlerInvoker>();
        _serviceCollection.TryAddSingleton<IMessagePollerFactory, DefaultMessagePollerFactory>();
        _serviceCollection.TryAddSingleton<ITelemetryFactory, DefaultTelemetryFactory>();

        if (_messageConfiguration.PublisherMappings.Any())
        {
            _serviceCollection.TryAddSingleton<IMessagePublisher, MessageRoutingPublisher>();

            if (_messageConfiguration.PublisherMappings.Any(x => x.PublishTargetType == PublisherTargetType.SQS_PUBLISHER))
            {
                _serviceCollection.TryAddAWSService<Amazon.SQS.IAmazonSQS>();
                _serviceCollection.TryAddSingleton<ISQSPublisher, SQSPublisher>();
            }

            if (_messageConfiguration.PublisherMappings.Any(x => x.PublishTargetType == PublisherTargetType.SNS_PUBLISHER))
            {
                _serviceCollection.TryAddAWSService<Amazon.SimpleNotificationService.IAmazonSimpleNotificationService>();
                _serviceCollection.TryAddSingleton<ISNSPublisher, SNSPublisher>();
            }

            if (_messageConfiguration.PublisherMappings.Any(x => x.PublishTargetType == PublisherTargetType.EVENTBRIDGE_PUBLISHER))
            {
                _serviceCollection.TryAddAWSService<Amazon.EventBridge.IAmazonEventBridge>();
                _serviceCollection.TryAddSingleton<IEventBridgePublisher, EventBridgePublisher>();
            }
        }

        if (_messageConfiguration.SubscriberMappings.Any())
        {
            _serviceCollection.TryAddAWSService<Amazon.SQS.IAmazonSQS>();

            foreach (var subscriberMapping in _messageConfiguration.SubscriberMappings)
            {
                _serviceCollection.TryAddScoped(subscriberMapping.HandlerType);
            }

            foreach (var subscriberMiddleware in _messageConfiguration.SubscriberMiddleware)
            {
                _serviceCollection.TryAdd(new ServiceDescriptor(subscriberMiddleware.Type, subscriberMiddleware.Type, subscriberMiddleware.ServiceLifetime));
            }
        }

        if (_messageConfiguration.MessagePollerConfigurations.Any())
        {
            _serviceCollection.AddHostedService<MessagePumpService>();

            if (_messageConfiguration.MessagePollerConfigurations.OfType<SQSMessagePollerConfiguration>().Any())
            {
                _serviceCollection.TryAddAWSService<Amazon.SQS.IAmazonSQS>();

                switch (_messageConfiguration.BackoffPolicy)
                {
                    case BackoffPolicy.None:
                        _serviceCollection.TryAddSingleton<IBackoffPolicy, NoBackoffPolicy>();
                        break;

                    case BackoffPolicy.Interval:
                        _serviceCollection.TryAddSingleton<IBackoffPolicy>(new IntervalBackoffPolicy(_messageConfiguration.IntervalBackoffOptions));
                        break;

                    case BackoffPolicy.CappedExponential:
                        _serviceCollection.TryAddSingleton<IBackoffPolicy>(new CappedExponentialBackoffPolicy(_messageConfiguration.CappedExponentialBackoffOptions));
                        break;

                    default:
                        throw new ConfigurationException("The specified backoff policy is currently unsupported.");
                }

                _serviceCollection.TryAddSingleton<IBackoffHandler, BackoffHandler>();
            }
        }

        foreach (var service in _additionalServices)
        {
            _serviceCollection.TryAdd(service);
        }
    }

    /// <summary>
    /// Retrieve Message Processing Framework configuration from environment variables.
    /// </summary>
    private void LoadConfigurationFromEnvironment()
    {
        var logMessageContentEnvVar = Environment.GetEnvironmentVariable("AWSMESSAGING_LOGMESSAGECONTENT");
        if (!string.IsNullOrEmpty(logMessageContentEnvVar) && bool.TryParse(logMessageContentEnvVar, out var logMessageContent))
            _messageConfiguration.LogMessageContent = logMessageContent;
    }
}
