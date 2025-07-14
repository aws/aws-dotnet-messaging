// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Publishers.EventBridge;

/// <summary>
/// The EventBridge message publisher allows publishing messages to Amazon EventBridge.
/// </summary>
internal class EventBridgePublisher : IMessagePublisher, IEventBridgePublisher
{
    private readonly IAWSClientProvider _awsClientProvider;
    private readonly ILogger<IMessagePublisher> _logger;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly IEnvelopeSerializer _envelopeSerializer;
    private readonly ITelemetryFactory _telemetryFactory;
    private IAmazonEventBridge? _eventBridgeClient;

    /// <summary>
    /// Creates an instance of <see cref="EventBridgePublisher"/>.
    /// </summary>
    public EventBridgePublisher(
        IAWSClientProvider awsClientProvider,
        ILogger<IMessagePublisher> logger,
        IMessageConfiguration messageConfiguration,
        IEnvelopeSerializer envelopeSerializer,
        ITelemetryFactory telemetryFactory)
    {
        _awsClientProvider = awsClientProvider;
        _logger = logger;
        _messageConfiguration = messageConfiguration;
        _envelopeSerializer = envelopeSerializer;
        _telemetryFactory = telemetryFactory;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <exception cref="FailedToPublishException">If the message failed to publish.</exception>
    /// <exception cref="InvalidMessageException">If the message is null or invalid.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<IPublishResponse> PublishAsync<T>(T message, CancellationToken token = default)
    {
        return await PublishAsync(message, null, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="message">The application message that will be serialized and sent to an event bus</param>
    /// <param name="eventBridgeOptions">Contains additional parameters that can be set while sending a message to EventBridge</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
    /// <exception cref="FailedToPublishException">If the message failed to publish.</exception>
    /// <exception cref="InvalidMessageException">If the message is null or invalid.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<EventBridgePublishResponse> PublishAsync<T>(T message, EventBridgeOptions? eventBridgeOptions, CancellationToken token = default)
    {
        using (var trace = _telemetryFactory.Trace("Publish to AWS EventBridge"))
        {
            try
            {
                trace.AddMetadata(TelemetryKeys.ObjectType, typeof(T).FullName!);

                _logger.LogDebug("Publishing the message of type '{MessageType}' using the {PublisherType}.", typeof(T), nameof(EventBridgePublisher));

                if (message == null)
                {
                    _logger.LogError("A message of type '{MessageType}' has a null value.", typeof(T));
                    throw new InvalidMessageException("The message cannot be null.");
                }

                var publisherMapping = GetPublisherMapping(trace, typeof(T));
                var eventBusName = eventBridgeOptions?.EventBusName ?? publisherMapping.PublisherConfiguration.PublisherEndpoint;

                if (string.IsNullOrEmpty(eventBusName))
                {
                    _logger.LogError("Unable to determine a destination event bus for message of type '{MessageType}'.", typeof(T));
                    throw new InvalidPublisherEndpointException($"Unable to determine a destination event bus for message of type '{typeof(T)}'.");
                }

                trace.AddMetadata(TelemetryKeys.EventBusName, eventBusName);

                _logger.LogDebug("Creating the message envelope for the message of type '{MessageType}'.", typeof(T));
                var messageEnvelope = await _envelopeSerializer.CreateEnvelopeAsync(message);

                trace.AddMetadata(TelemetryKeys.MessageId, messageEnvelope.Id);
                trace.RecordTelemetryContext(messageEnvelope);

                var messageBody = await _envelopeSerializer.SerializeAsync(messageEnvelope);

                IAmazonEventBridge client;
                if (eventBridgeOptions?.OverrideClient != null)
                {
                    // Use the client that the user specified for this event
                    client = eventBridgeOptions.OverrideClient;
                }
                else // use the publisher-level client
                {
                    // If we haven't resolved the client yet for this publisher, do so now
                    _eventBridgeClient ??= _awsClientProvider.GetServiceClient<IAmazonEventBridge>();

                    client = _eventBridgeClient;
                }

                _logger.LogDebug("Sending the message of type '{MessageType}' to EventBridge. Publisher Endpoint: {Endpoint}", typeof(T), eventBusName);
                var request = CreatePutEventsRequest(publisherMapping, messageEnvelope.Source?.ToString(), messageBody, eventBridgeOptions, eventBusName);
                var putEventsResponse = await client.PutEventsAsync(request, token);
                var firstEntry = putEventsResponse.Entries.First(); // only 1 message is published, so we only expect 1 result
                var publishResponse = new EventBridgePublishResponse()
                {
                    MessageId = firstEntry.EventId
                };

                if (string.IsNullOrWhiteSpace(firstEntry.ErrorCode))
                {
                    _logger.LogDebug("The message of type '{MessageType}' has been pushed successfully to EventBridge as event-id '{EventId}'.", typeof(T), publishResponse.MessageId);
                    return publishResponse;
                }
                _logger.LogDebug("The message of type '{MessageType}' has been pushed to EventBridge but failed with '{ErrorCode}'.", typeof(T), firstEntry.ErrorCode);
                throw new EventBridgePutEventsException(firstEntry.ErrorMessage, firstEntry.ErrorCode);
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                if (ex is EventBridgePutEventsException)
                    throw new FailedToPublishException("Message failed to publish.", ex);
                throw;
            }
        }
    }

    private PutEventsRequest CreatePutEventsRequest(PublisherMapping publisherMapping, string? source, string messageBody, EventBridgeOptions? eventBridgeOptions, string eventBusName)
    {
        var publisherConfiguration = (EventBridgePublisherConfiguration)publisherMapping.PublisherConfiguration;

        var requestEntry = new PutEventsRequestEntry
        {
            EventBusName = eventBusName,
            DetailType = publisherMapping.MessageTypeIdentifier,
            Detail = messageBody
        };

        var putEventsRequest = new PutEventsRequest
        {
            // Give precedence to the endpoint ID if specified on the message-specific eventBridgeOptions,
            // otherwise fallback to the publisher level
            EndpointId = eventBridgeOptions?.EndpointID ?? publisherConfiguration.EndpointID,
            Entries = new() { requestEntry }
        };

        if (!string.IsNullOrEmpty(eventBridgeOptions?.Source))
            requestEntry.Source = eventBridgeOptions.Source;
        else if(!string.IsNullOrEmpty(source))
            requestEntry.Source = source;

        if (!string.IsNullOrEmpty(eventBridgeOptions?.TraceHeader))
            requestEntry.TraceHeader = eventBridgeOptions.TraceHeader;

        if (eventBridgeOptions != null && eventBridgeOptions.Time != DateTimeOffset.MinValue)
            requestEntry.Time = eventBridgeOptions.Time.DateTime;

        if (!string.IsNullOrEmpty(eventBridgeOptions?.DetailType))
            requestEntry.DetailType = eventBridgeOptions.DetailType;

        if (eventBridgeOptions?.Resources?.Any() ?? false)
            requestEntry.Resources = eventBridgeOptions.Resources;

        return putEventsRequest;
    }

    private PublisherMapping GetPublisherMapping(ITelemetryTrace trace, Type messageType)
    {
        var mapping = _messageConfiguration.GetPublisherMapping(messageType);
        if (mapping is null)
        {
            _logger.LogError("Cannot find a configuration for the message of type '{MessageType}'.", messageType.FullName);
            throw new MissingMessageTypeConfigurationException($"The framework is not configured to accept messages of type '{messageType.FullName}'.");
        }
        if (mapping.PublishTargetType != PublisherTargetType.EVENTBRIDGE_PUBLISHER)
        {
            _logger.LogError("Messages of type '{MessageType}' are not configured for publishing to EventBridge.", messageType.FullName);
            throw new MissingMessageTypeConfigurationException($"Messages of type '{messageType.FullName}' are not configured for publishing to EventBridge.");
        }

        trace.AddMetadata(TelemetryKeys.MessageType, mapping.MessageTypeIdentifier);

        return mapping;
    }
}
