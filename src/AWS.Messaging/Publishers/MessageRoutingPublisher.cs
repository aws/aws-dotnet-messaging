// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Frozen;
using AWS.Messaging.Configuration;
using AWS.Messaging.Publishers.EventBridge;
using AWS.Messaging.Publishers.SNS;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Services;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Publishers;

/// <summary>
/// The message routing publisher allows publishing messages from application code to configured AWS services.
/// It exposes the <see cref="PublishAsync{T}(T, CancellationToken)"/> method which takes in a user-defined message
/// and looks up the corresponding <see cref="PublisherMapping"/> in order to route it to the appropriate AWS services.
/// </summary>
internal class MessageRoutingPublisher : IMessagePublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly ILogger<IMessagePublisher> _logger;
    private readonly ITelemetryFactory _telemetryFactory;

    /// <summary>
    /// Creates an instance of <see cref="MessageRoutingPublisher"/>.
    /// </summary>
    public MessageRoutingPublisher(
        IServiceProvider serviceProvider,
        IMessageConfiguration messageConfiguration,
        ILogger<IMessagePublisher> logger,
        ITelemetryFactory telemetryFactory)
    {
        _serviceProvider = serviceProvider;
        _messageConfiguration = messageConfiguration;
        _logger = logger;
        _telemetryFactory = telemetryFactory;
    }

    private static readonly FrozenDictionary<string, Type> s_publisherTypeMapping = new Dictionary<string, Type>
        {
            { PublisherTargetType.SQS_PUBLISHER, typeof(SQSPublisher) },
            { PublisherTargetType.SNS_PUBLISHER, typeof(SNSPublisher) },
            { PublisherTargetType.EVENTBRIDGE_PUBLISHER, typeof(EventBridgePublisher) }
        }.ToFrozenDictionary();

    /// <summary>
    /// This dictionary serves as a method to cache created instances of <see cref="ICommandPublisher"/>,
    /// to avoid having to create a new instance any time a message is sent.
    /// </summary>
    private readonly ConcurrentDictionary<Type, ICommandPublisher> _commandPublisherInstances = new();

    /// <summary>
    /// This dictionary serves as a method to cache created instances of <see cref="IEventPublisher"/>,
    /// to avoid having to create a new instance any time a message is published.
    /// </summary>
    private readonly ConcurrentDictionary<Type, IEventPublisher> _eventPublisherInstances = new();

    /// <summary>
    /// Publishes a user-defined message to an AWS service based on the
    /// configuration done during startup. It retrieves the <see cref="PublisherMapping"/> corresponding to the
    /// message type, which contains the routing information of the provided message.
    /// The method wraps the message in a <see cref="MessageEnvelope"/> which contains metadata
    /// that enables the proper transportation of the message throughout the framework.
    /// </summary>
    /// <param name="message">The message to be sent.</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
    public async Task<IPublishResponse> PublishAsync<T>(T message, CancellationToken token = default)
    {
        using (var trace = _telemetryFactory.Trace("Routing message to AWS service"))
        {
            try
            {
                trace.AddMetadata(TelemetryKeys.ObjectType, typeof(T).FullName!);

                var mapping = _messageConfiguration.GetPublisherMapping(typeof(T));
                if (mapping == null)
                {
                    _logger.LogError("The framework is not configured to publish messages of type '{MessageType}'.", typeof(T).FullName);
                    throw new MissingMessageTypeConfigurationException($"The framework is not configured to publish messages of type '{typeof(T).FullName}'.");
                }

                trace.AddMetadata(TelemetryKeys.PublishTargetType, mapping.PublishTargetType);

                if (s_publisherTypeMapping.TryGetValue(mapping.PublishTargetType, out var publisherType))
                {
                    if (typeof(ICommandPublisher).IsAssignableFrom(publisherType))
                    {
                        var publisher = _commandPublisherInstances.GetOrAdd(publisherType, _ => (ICommandPublisher) ActivatorUtilities.CreateInstance(_serviceProvider, publisherType));
                        return await publisher.SendAsync(message, token);
                    }
                    else if (typeof(IEventPublisher).IsAssignableFrom(publisherType))
                    {
                        var publisher = _eventPublisherInstances.GetOrAdd(publisherType, _ => (IEventPublisher) ActivatorUtilities.CreateInstance(_serviceProvider, publisherType));
                        return await publisher.PublishAsync(message, token);
                    }
                    else
                    {
                        _logger.LogError("The message publisher corresponding to the type '{PublishTargetType}' is invalid " +
                                         "and does not implement the interface '{CommandInterfaceType}' or '{EventInterfaceType}'.", mapping.PublishTargetType, typeof(ICommandPublisher), typeof(IEventPublisher));
                        throw new InvalidPublisherTypeException($"The message publisher corresponding to the type '{mapping.PublishTargetType}' is invalid " +
                                                                $"and does not implement the interface '{typeof(ICommandPublisher)}' or '{typeof(IEventPublisher)}'.");
                    }
                }
                else
                {
                    _logger.LogError("The publisher type '{PublishTargetType}' is not supported.", mapping.PublishTargetType);
                    throw new UnsupportedPublisherException($"The publisher type '{mapping.PublishTargetType}' is not supported.");
                }
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                throw;
            }
        }
    }
}
