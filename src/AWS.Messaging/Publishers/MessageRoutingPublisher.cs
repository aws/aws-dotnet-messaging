// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

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

                switch (mapping.PublishTargetType)
                {
                    case PublisherTargetType.SQS_PUBLISHER:
                        SQSOptions? sqsOptions = null;
                        if (mapping.ConfigureOptions != null)
                        {
                            sqsOptions = new SQSOptions();
                            await mapping.ConfigureOptions(_serviceProvider, message!, sqsOptions, token);
                        }

                        var sqsPublisher = _serviceProvider.GetRequiredService<ISQSPublisher>();
                        return await sqsPublisher.SendAsync(message, sqsOptions, token);

                    case PublisherTargetType.SNS_PUBLISHER:
                        SNSOptions? snsOptions = null;
                        if (mapping.ConfigureOptions != null)
                        {
                            snsOptions = new SNSOptions();
                            await mapping.ConfigureOptions(_serviceProvider, message!, snsOptions, token);
                        }

                        var snsPublisher = _serviceProvider.GetRequiredService<ISNSPublisher>();
                        return await snsPublisher.PublishAsync(message, snsOptions, token);

                    case PublisherTargetType.EVENTBRIDGE_PUBLISHER:
                        EventBridgeOptions? ebOptions = null;
                        if (mapping.ConfigureOptions != null)
                        {
                            ebOptions = new EventBridgeOptions();
                            await mapping.ConfigureOptions(_serviceProvider, message!, ebOptions, token);
                        }

                        var ebPublisher = _serviceProvider.GetRequiredService<IEventBridgePublisher>();
                        return await ebPublisher.PublishAsync(message, ebOptions, token);

                    default:
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
