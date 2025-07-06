// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Publishers.SNS;

/// <summary>
/// The SNS message publisher allows publishing messages to AWS Simple Notification Service.
/// </summary>
internal class SNSPublisher : IMessagePublisher, ISNSPublisher
{
    private readonly IAWSClientProvider _awsClientProvider;
    private readonly ILogger<IMessagePublisher> _logger;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly IEnvelopeSerializer _envelopeSerializer;
    private readonly ITelemetryFactory _telemetryFactory;
    private IAmazonSimpleNotificationService? _snsClient;

    private const string FIFO_SUFFIX = ".fifo";

    /// <summary>
    /// Creates an instance of <see cref="SNSPublisher"/>.
    /// </summary>
    public SNSPublisher(
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
    /// <param name="message">The application message that will be serialized and sent to an SNS topic</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
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
    /// <param name="message">The application message that will be serialized and sent to an SNS topic</param>
    /// <param name="snsOptions">Contains additional parameters that can be set while sending a message to an SNS topic</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
    /// <exception cref="FailedToPublishException">If the message failed to publish.</exception>
    /// <exception cref="InvalidMessageException">If the message is null or invalid.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<SNSPublishResponse> PublishAsync<T>(T message, SNSOptions? snsOptions, CancellationToken token = default)
    {
        using (var trace = _telemetryFactory.Trace("Publish to AWS SNS"))
        {
            try
            {
                trace.AddMetadata(TelemetryKeys.ObjectType, typeof(T).FullName!);

                _logger.LogDebug("Publishing the message of type '{MessageType}' using the {PublisherType}.", typeof(T), nameof(SNSPublisher));

                if (message == null)
                {
                    _logger.LogError("A message of type '{MessageType}' has a null value.", typeof(T));
                    throw new InvalidMessageException("The message cannot be null.");
                }

                var topicArn = GetPublisherEndpoint(trace, typeof(T), snsOptions);

                _logger.LogDebug("Creating the message envelope for the message of type '{MessageType}'.", typeof(T));
                var messageEnvelope = await _envelopeSerializer.CreateEnvelopeAsync(message);

                trace.AddMetadata(TelemetryKeys.MessageId, messageEnvelope.Id);
                trace.RecordTelemetryContext(messageEnvelope);

                var messageBody = await _envelopeSerializer.SerializeAsync(messageEnvelope);

                IAmazonSimpleNotificationService client;
                if (snsOptions?.OverrideClient != null)
                {
                    // Use the client that the user specified for this event
                    client = snsOptions.OverrideClient;
                }
                else // use the publisher-level client
                {
                    // If we haven't resolved the client yet for this publisher, do so now
                    _snsClient ??= _awsClientProvider.GetServiceClient<IAmazonSimpleNotificationService>();
                    client = _snsClient;
                }

                _logger.LogDebug("Sending the message of type '{MessageType}' to SNS. Publisher Endpoint: {Endpoint}", typeof(T), topicArn);
                var request = CreatePublishRequest(topicArn, messageBody, snsOptions);
                var publishResponse =await client.PublishAsync(request, token);
                _logger.LogDebug("The message of type '{MessageType}' has been pushed to SNS.", typeof(T));
                return new SNSPublishResponse
                {
                    MessageId = publishResponse.MessageId
                };
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                if (ex is AmazonSimpleNotificationServiceException) // if the exception inherits from the AmazonSimpleNotificationServiceException
                    throw new FailedToPublishException("Message failed to publish.", ex);
                throw;
            }
        }
    }

    private PublishRequest CreatePublishRequest(string topicArn, string messageBody, SNSOptions? snsOptions)
    {
        var request = new PublishRequest
        {
            TopicArn = topicArn,
            Message = messageBody,
        };

        if (topicArn.EndsWith(FIFO_SUFFIX) && string.IsNullOrEmpty(snsOptions?.MessageGroupId))
        {
            var errorMessage =
                $"You are attempting to publish to a FIFO SNS topic but the request does not include a message group ID. " +
                $"Please use {nameof(ISNSPublisher)} from the service collection to publish to FIFO topics. " +
                $"It exposes a {nameof(PublishAsync)} method that accepts {nameof(SNSOptions)} as a parameter. " +
                $"A message group ID must be specified via {nameof(SNSOptions.MessageGroupId)}. " +
                $"Additionally, {nameof(SNSOptions.MessageDeduplicationId)} must also be specified if content based de-duplication is not enabled on the topic.";

            _logger.LogError(errorMessage);
            throw new InvalidFifoPublishingRequestException(errorMessage);
        }

        if (snsOptions is null)
            return request;

        if (!string.IsNullOrEmpty(snsOptions.MessageDeduplicationId))
            request.MessageDeduplicationId = snsOptions.MessageDeduplicationId;

        if (!string.IsNullOrEmpty(snsOptions.MessageGroupId))
            request.MessageGroupId = snsOptions.MessageGroupId;

        if (snsOptions.MessageAttributes is not null)
            request.MessageAttributes = snsOptions.MessageAttributes;

        return request;
    }

    private string GetPublisherEndpoint(ITelemetryTrace trace, Type messageType, SNSOptions? snsOptions)
    {
        var mapping = _messageConfiguration.GetPublisherMapping(messageType);
        if (mapping is null)
        {
            _logger.LogError("Cannot find a configuration for the message of type '{MessageType}'.", messageType.FullName);
            throw new MissingMessageTypeConfigurationException($"The framework is not configured to accept messages of type '{messageType.FullName}'.");
        }
        if (mapping.PublishTargetType != PublisherTargetType.SNS_PUBLISHER)
        {
            _logger.LogError("Messages of type '{MessageType}' are not configured for publishing to SNS.", messageType.FullName);
            throw new MissingMessageTypeConfigurationException($"Messages of type '{messageType.FullName}' are not configured for publishing to SNS.");
        }

        var topicArn = mapping.PublisherConfiguration.PublisherEndpoint;

        // Check if the topic was overriden on this message-specific publishing options
        if (!string.IsNullOrEmpty(snsOptions?.TopicArn))
        {
            topicArn = snsOptions.TopicArn;
        }

        if (string.IsNullOrEmpty(topicArn))
        {
            _logger.LogError("Unable to determine a destination topic for message of type '{MessageType}'.", messageType.FullName);
            throw new InvalidPublisherEndpointException($"Unable to determine a destination topic for message of type '{messageType.FullName}'.");
        }

        trace.AddMetadata(TelemetryKeys.MessageType, mapping.MessageTypeIdentifier);
        trace.AddMetadata(TelemetryKeys.TopicUrl, topicArn);

        return topicArn;
    }
}
