// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization.Handlers;
using AWS.Messaging.Services;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Envelope deserializer used for single-type SQS pollers configured to receive raw (non-CloudEvents) payloads.
/// Converts raw JSON message bodies directly into message envelopes with minimal CloudEvents metadata.
/// </summary>
internal sealed class SingleTypeSqsPollerEnvelopeDeserializer : IEnvelopeDeserializer
{
    private readonly ILogger<SingleTypeSqsPollerEnvelopeDeserializer> _logger;
    private readonly IMessageSerializer _messageSerializer;
    private readonly IDateTimeHandler _dateTimeHandler;
    private readonly SubscriberMapping _subscriberMapping;

    public SingleTypeSqsPollerEnvelopeDeserializer(
        ILogger<SingleTypeSqsPollerEnvelopeDeserializer> logger,
        IMessageSerializer messageSerializer,
        IDateTimeHandler dateTimeHandler,
        SubscriberMapping subscriberMapping)
    {
        _logger = logger;
        _messageSerializer = messageSerializer;
        _dateTimeHandler = dateTimeHandler;
        _subscriberMapping = subscriberMapping;
    }

    /// <inheritdoc/>
    public ValueTask<ConvertToEnvelopeResult> ConvertToEnvelopeAsync(Message message)
    {
        var payload = message.Body ?? string.Empty;

        var envelope = _subscriberMapping.MessageEnvelopeFactory();

        // Populate minimum envelope fields. These aren't present in raw messages.
        envelope.Id = message.MessageId ?? Guid.NewGuid().ToString("D");
        envelope.Source = new Uri("/aws/messaging/raw", UriKind.Relative);
        envelope.Version = Constants.CLOUD_EVENT_SPEC_VERSION;
        envelope.MessageTypeIdentifier = _subscriberMapping.MessageTypeIdentifier;
        envelope.TimeStamp = _dateTimeHandler.GetUtcNow();
        envelope.DataContentType = "application/json";

        try
        {
            var deserialized = _messageSerializer.Deserialize(payload, _subscriberMapping.MessageType);
            envelope.SetMessage(deserialized);
        }
        catch (Exception deserializeEx)
        {
            _logger.LogError(deserializeEx, "Failed to deserialize raw payload for message type '{MessageType}'.", _subscriberMapping.MessageType);
            throw new FailedToCreateMessageEnvelopeException($"Failed to deserialize raw payload into '{_subscriberMapping.MessageType.FullName}'", deserializeEx);
        }

        // Attach basic SQS metadata.
        envelope.SQSMetadata = MessageMetadataHandler.CreateSQSMetadata(message);

        return new ValueTask<ConvertToEnvelopeResult>(new ConvertToEnvelopeResult(envelope, _subscriberMapping));
    }
}
