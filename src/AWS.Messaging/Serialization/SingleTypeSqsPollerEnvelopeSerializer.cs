// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization.Handlers;
using AWS.Messaging.Services;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Envelope serializer used only for SQS pollers that are configured for a single message type.
/// It supports two modes:
/// - usesMessageEnvelope=true: inbound messages must be CloudEvents envelopes.
/// - usesMessageEnvelope=false: inbound messages must be raw payloads of the configured message type.
/// </summary>
internal sealed class SingleTypeSqsPollerEnvelopeSerializer : IEnvelopeSerializer
{
    private const string CLOUD_EVENT_SPEC_VERSION = "1.0";

    private readonly ILogger<SingleTypeSqsPollerEnvelopeSerializer> _logger;
    private readonly IEnvelopeSerializer _envelopedMessageSerializer;
    private readonly IMessageSerializer _messageSerializer;
    private readonly IDateTimeHandler _dateTimeHandler;
    private readonly SubscriberMapping _subscriberMapping;
    private readonly bool _usesMessageEnvelope;

    public SingleTypeSqsPollerEnvelopeSerializer(
        ILogger<SingleTypeSqsPollerEnvelopeSerializer> logger,
        IEnvelopeSerializer envelopedMessageSerializer,
        IMessageSerializer messageSerializer,
        IDateTimeHandler dateTimeHandler,
        SubscriberMapping subscriberMapping,
        bool usesMessageEnvelope)
    {
        _logger = logger;
        _envelopedMessageSerializer = envelopedMessageSerializer;
        _messageSerializer = messageSerializer;
        _dateTimeHandler = dateTimeHandler;
        _subscriberMapping = subscriberMapping;
        _usesMessageEnvelope = usesMessageEnvelope;
    }

    /// <inheritdoc/>
    public ValueTask<string> SerializeAsync<T>(MessageEnvelope<T> envelope)
        => _envelopedMessageSerializer.SerializeAsync(envelope);

    /// <inheritdoc/>
    public ValueTask<MessageEnvelope<T>> CreateEnvelopeAsync<T>(T message)
        => _envelopedMessageSerializer.CreateEnvelopeAsync(message);

    /// <inheritdoc/>
    public async ValueTask<ConvertToEnvelopeResult> ConvertToEnvelopeAsync(Message message)
    {
        if (_usesMessageEnvelope)
        {
            var result = await _envelopedMessageSerializer.ConvertToEnvelopeAsync(message);

            // Extra safety: if for any reason a different mapping was resolved, reject it.
            if (result.Mapping.MessageType != _subscriberMapping.MessageType
                || !string.Equals(result.Mapping.MessageTypeIdentifier, _subscriberMapping.MessageTypeIdentifier, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Received message resolved to subscriber mapping '{result.Mapping.MessageTypeIdentifier}', " +
                    $"but this poller is configured for '{_subscriberMapping.MessageTypeIdentifier}'.");
            }

            return result;
        }

        var payload = message.Body ?? string.Empty;

        var envelope = _subscriberMapping.MessageEnvelopeFactory.Invoke();

        // Populate minimum envelope fields. These aren't present in raw messages.
        envelope.Id = message.MessageId ?? Guid.NewGuid().ToString("D");
        envelope.Source = new Uri("/aws/messaging/raw", UriKind.Relative);
        envelope.Version = CLOUD_EVENT_SPEC_VERSION;
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

        return new ConvertToEnvelopeResult(envelope, _subscriberMapping);
    }
}
