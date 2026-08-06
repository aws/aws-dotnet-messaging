// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization.Helpers;
using AWS.Messaging.Serialization.Parsers;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Serialization;

/// <summary>
/// The performance based implementation of <see cref="IEnvelopeDeserializer"/> used by the framework.
/// </summary>
internal class EnvelopeDeserializer : IEnvelopeDeserializer
{
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly IMessageSerializer _messageSerializer;
    private readonly ILogger<EnvelopeDeserializer> _logger;

    private readonly IMessageSerializerUtf8JsonReader? _messageSerializerUtf8JsonReader;

    private readonly IMessageTypeClassifier _classifier;
    private readonly ISQSWrapperReader _sqsReader;

    public EnvelopeDeserializer(
        ILogger<EnvelopeDeserializer> logger,
        IMessageConfiguration messageConfiguration,
        IMessageSerializer messageSerializer,
        IMessageTypeClassifier classifier,
        ISQSWrapperReader sqsReader)
    {
        _logger = logger;
        _messageConfiguration = messageConfiguration;
        _messageSerializer = messageSerializer;
        _messageSerializerUtf8JsonReader = messageSerializer as IMessageSerializerUtf8JsonReader;
        _classifier = classifier;
        _sqsReader = sqsReader;
    }

    /// <inheritdoc/>
    public ValueTask<ConvertToEnvelopeResult> ConvertToEnvelopeAsync(Message sqsMessage)
    {
        // When no serialization callbacks are registered (the common case),
        // the entire deserialization pipeline is pure synchronous compute —
        // avoid the async state machine and its heap allocations entirely.
        if (_messageConfiguration.SerializationCallbacks.Count == 0)
        {
            try
            {
                return new ValueTask<ConvertToEnvelopeResult>(ConvertToEnvelopeCore(sqsMessage));
            }
            catch (JsonException) when (!_messageConfiguration.LogMessageContent)
            {
                _logger.LogError("Failed to create a {MessageEnvelopeName}", nameof(MessageEnvelope));
                throw new FailedToCreateMessageEnvelopeException($"Failed to create {nameof(MessageEnvelope)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a {MessageEnvelopeName}", nameof(MessageEnvelope));
                throw new FailedToCreateMessageEnvelopeException($"Failed to create {nameof(MessageEnvelope)}", ex);
            }
        }

        return ConvertToEnvelopeWithCallbacksAsync(sqsMessage);
    }

    private async ValueTask<ConvertToEnvelopeResult> ConvertToEnvelopeWithCallbacksAsync(Message sqsMessage)
    {
        try
        {
            using var poolManager = new ArrayPoolManager(initialRentCapacity: 2, clearRentedBuffers: true);
            var (envelopeUtf8, metadata) = await ParseOuterWrapper(sqsMessage, poolManager);

            var (envelope, subscriberMapping) = DeserializeEnvelope(envelopeUtf8.Span);

            envelope.SQSMetadata = metadata.SQSMetadata;
            envelope.SNSMetadata = metadata.SNSMetadata;
            envelope.EventBridgeMetadata = metadata.EventBridgeMetadata;

            await InvokePostDeserializationCallback(envelope);
            return new ConvertToEnvelopeResult(envelope, subscriberMapping);
        }
        catch (JsonException) when (!_messageConfiguration.LogMessageContent)
        {
            _logger.LogError("Failed to create a {MessageEnvelopeName}", nameof(MessageEnvelope));
            throw new FailedToCreateMessageEnvelopeException($"Failed to create {nameof(MessageEnvelope)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create a {MessageEnvelopeName}", nameof(MessageEnvelope));
            throw new FailedToCreateMessageEnvelopeException($"Failed to create {nameof(MessageEnvelope)}", ex);
        }
    }

    private ConvertToEnvelopeResult ConvertToEnvelopeCore(Message sqsMessage)
    {
        // Example 1: SNS-wrapped message in SQS
        /*
        sqsMessage.Body = {
            "Type": "Notification",
            "MessageId": "abc-123",
            "TopicArn": "arn:aws:sns:us-east-1:123456789012:MyTopic",
            "Message": {
                "id": "order-123",
                "source": "com.myapp.orders",
                "type": "OrderCreated",
                "time": "2024-03-21T10:00:00Z",
                "data": {
                    "orderId": "12345",
                    "amount": 99.99
                }
            }
        }
        */

        // Example 2: Raw SQS message
        /*
        sqsMessage.Body = {
            "id": "order-123",
            "source": "com.myapp.orders",
            "type": "OrderCreated",
            "time": "2024-03-21T10:00:00Z",
            "data": {
                "orderId": "12345",
                "amount": 99.99
            }
        }
        */

        using var poolManager = new ArrayPoolManager(initialRentCapacity: 2, clearRentedBuffers: true);
        var (envelopeUtf8, metadata) = ParseOuterWrapperCore(sqsMessage, poolManager);

        var (envelope, subscriberMapping) = DeserializeEnvelope(envelopeUtf8.Span);

        envelope.SQSMetadata = metadata.SQSMetadata;
        envelope.SNSMetadata = metadata.SNSMetadata;
        envelope.EventBridgeMetadata = metadata.EventBridgeMetadata;

        // Example 1 final return:
        // MessageBody = {
        //     "id": "order-123",
        //     "source": "com.myapp.orders",
        //     "type": "OrderCreated",
        //     "time": "2024-03-21T10:00:00Z",
        //     "data": { ... }
        // }
        // Metadata = {
        //     SNSMetadata: { TopicArn: "arn:aws...", MessageId: "abc-123" }
        // }

        // Example 2 final return:
        // MessageBody = {
        //     "id": "order-123",
        //     "source": "com.myapp.orders",
        //     "type": "OrderCreated",
        //     "time": "2024-03-21T10:00:00Z",
        //     "data": { ... }
        // }
        // Metadata = { } // Just basic SQS metadata

        return new ConvertToEnvelopeResult(envelope, subscriberMapping);
    }

    private static bool IsJsonContentType(string? dataContentType)
    {
        if (string.IsNullOrWhiteSpace(dataContentType))
        {
            // If dataContentType is not specified, it should be treated as "application/json"
            return true;
        }

        // Fast path: exact match with interned constant (most common case)
        if (ReferenceEquals(dataContentType, CloudEventConstants.ApplicationJson))
            return true;

        var contentType = dataContentType.AsSpan().Trim();

        // Remove parameters (anything after ';')
        var semicolonIndex = contentType.IndexOf(';');
        if (semicolonIndex >= 0)
            contentType = contentType.Slice(0, semicolonIndex).Trim();

        // Check "application/json" (case-insensitive)
        if (contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        // Find the '/' separator
        var slashIndex = contentType.IndexOf('/');
        if (slashIndex < 0
            || slashIndex == contentType.Length - 1
            || slashIndex != contentType.LastIndexOf('/'))
        {
            // If there are multiple slashes, ends with a slash or there are no slashes at all, it's not a valid content type
            return false;
        }

        var subtype = contentType.Slice(slashIndex + 1);

        // Check if the media subtype is "json" or ends with "+json"
        return subtype.Equals("json", StringComparison.OrdinalIgnoreCase)
            || subtype.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private (MessageEnvelope Envelope, SubscriberMapping Mapping) DeserializeEnvelope(ReadOnlySpan<byte> utf8Envelope)
    {
        var reader = new Utf8JsonReader(utf8Envelope);

        // CloudEvent properties
        string? id = null, source = null, specVersion = null, type = null, dataContentType = null;
        DateTimeOffset? time = null;

        // Track data element byte range for deferred deserialization
        int dataStart = -1, dataLength = 0;
        var dataIsString = false;
        string? dataStringValue = null;

        // Extension attributes (unknown properties)
        Dictionary<string, JsonElement>? metadata = null;

        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("type"u8))
            {
                reader.Read();
                type = reader.GetString();
            }
            else if (reader.ValueTextEquals("id"u8))
            {
                reader.Read();
                id = reader.GetString();
            }
            else if (reader.ValueTextEquals("source"u8))
            {
                reader.Read();
                source = reader.GetString();
            }
            else if (reader.ValueTextEquals("specversion"u8))
            {
                reader.Read();
                specVersion = reader.ValueTextEquals("1.0"u8)
                    ? CloudEventConstants.SpecVersion1_0
                    : reader.GetString();
            }
            else if (reader.ValueTextEquals("time"u8))
            {
                reader.Read();
                time = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals("datacontenttype"u8))
            {
                reader.Read();
                dataContentType = reader.TokenType != JsonTokenType.Null && reader.ValueTextEquals("application/json"u8)
                    ? CloudEventConstants.ApplicationJson
                    : reader.GetString();
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                {
                    dataIsString = true;
                    dataStringValue = reader.GetString();
                }
                else
                {
                    dataStart = (int)reader.TokenStartIndex;
                    reader.Skip();
                    dataLength = (int)reader.BytesConsumed - dataStart;
                }
            }
            else
            {
                // CloudEvents extension attribute — must preserve as JsonElement
                var propName = reader.GetString()!;
                reader.Read();
                using var subDoc = JsonDocument.ParseValue(ref reader);
                // Lazy init with capacity hint to reduce reallocations (most messages have 0-4 extension attributes)
                metadata ??= new Dictionary<string, JsonElement>(capacity: 4);
                metadata[propName] = subDoc.RootElement.Clone();
            }
        }

        // Validation
        if (type is null) throw new InvalidDataException("Message type identifier not found in envelope");
        if (id is null) throw new InvalidDataException("Required property 'id' is missing");
        if (source is null) throw new InvalidDataException("Required property 'source' is missing");
        if (specVersion is null) throw new InvalidDataException("Required property 'specversion' is missing");
        if (!time.HasValue) throw new InvalidDataException("Required property 'time' is missing");

        var subscriberMapping = GetAndValidateSubscriberMapping(type);
        var envelope = subscriberMapping.MessageEnvelopeFactory();

        envelope.Id = id;
        envelope.Source = new Uri(source, UriKind.RelativeOrAbsolute);
        envelope.Version = specVersion;
        envelope.MessageTypeIdentifier = type;
        envelope.TimeStamp = time.Value;
        envelope.DataContentType = dataContentType;

        if (metadata is not null)
        {
            // Use internal helper to avoid triggering lazy initialization of empty dictionary
            envelope.SetMetadataInternal(metadata);
        }

        // Deserialize the payload
        object message;
        var isJsonContent = IsJsonContentType(dataContentType);

        if (dataIsString)
        {
            // "data" was a JSON string value
            message = _messageSerializer.Deserialize(dataStringValue!, subscriberMapping.MessageType);
        }
        else if (dataStart >= 0)
        {
            // "data" was an object/array — deserialize directly from the byte slice
            var dataSlice = utf8Envelope.Slice(dataStart, dataLength);

            if (_messageSerializerUtf8JsonReader is not null && isJsonContent)
            {
                // Direct span-based deserialization — no JsonDocument or string intermediate
                message = _messageSerializerUtf8JsonReader.DeserializeFromUtf8Bytes(dataSlice, subscriberMapping.MessageType);
            }
            else
            {
                var dataString = Encoding.UTF8.GetString(dataSlice);
                message = _messageSerializer.Deserialize(dataString, subscriberMapping.MessageType);
            }
        }
        else
        {
            throw new InvalidDataException("Required property 'data' is missing");
        }

        envelope.SetMessage(message);
        return (envelope, subscriberMapping);
    }

    private async ValueTask<(ReadOnlyMemory<byte> EnvelopeUtf8, MessageMetadata Metadata)> ParseOuterWrapper(Message sqsMessage, ArrayPoolManager poolManager)
    {
        sqsMessage.Body = await InvokePreDeserializationCallback(sqsMessage.Body);
        return ParseOuterWrapperCore(sqsMessage, poolManager);
    }

    private (ReadOnlyMemory<byte> EnvelopeUtf8, MessageMetadata Metadata) ParseOuterWrapperCore(Message sqsMessage, ArrayPoolManager poolManager)
    {
        // Convert to UTF-8 once — this buffer is used by the classifier and wrapper readers,
        // and for the SQS path it IS the envelope bytes fed to DeserializeEnvelope.
        // Use GetMaxByteCount to avoid double-pass (GetByteCount + GetBytes)
        var rented = poolManager.Rent(Encoding.UTF8.GetMaxByteCount(sqsMessage.Body.Length));
        var actualBytes = Encoding.UTF8.GetBytes(sqsMessage.Body, rented);
        var utf8Body = rented.AsMemory(0, actualBytes);

        var classification = _classifier.Classify(utf8Body, poolManager);

        if (classification.WrapperType == WrapperType.Sqs)
        {
            // Fast path: body IS the envelope — rented buffer will be returned by poolManager
            var (innerUtf8, metadata) = _sqsReader.Extract(utf8Body, sqsMessage);
            return (innerUtf8, metadata);
        }

        // SNS fast path: classifier completed full extraction in single pass — skip second pass
        if (classification.CapturedMetadata is not null)
            return (classification.CapturedInnerBody, classification.CapturedMetadata);

        // SNS with MessageAttributes, or EventBridge: delegate to the matched reader for metadata + body extraction
        var reader = _classifier.GetReader(classification.WrapperType);
        var (wrapperUtf8, wrapperMetadata) = reader.Extract(utf8Body, sqsMessage, poolManager);

        return (wrapperUtf8, wrapperMetadata);
    }

    private SubscriberMapping GetAndValidateSubscriberMapping(string messageTypeIdentifier)
    {
        var subscriberMapping = _messageConfiguration.GetSubscriberMapping(messageTypeIdentifier);
        if (subscriberMapping is null)
        {
            var availableMappings = string.Join(", ",
                _messageConfiguration.SubscriberMappings.Select(m => m.MessageTypeIdentifier));

            _logger.LogError(
                "'{MessageTypeIdentifier}' is not a valid subscriber mapping. Available mappings: {AvailableMappings}",
                messageTypeIdentifier,
                string.IsNullOrEmpty(availableMappings) ? "none" : availableMappings);

            throw new InvalidDataException(
                $"'{messageTypeIdentifier}' is not a valid subscriber mapping. " +
                $"Available mappings: {(string.IsNullOrEmpty(availableMappings) ? "none" : availableMappings)}");
        }
        return subscriberMapping;
    }

    private ValueTask<string> InvokePreDeserializationCallback(string message)
    {
        if (_messageConfiguration.SerializationCallbacks.Count == 0)
            return new ValueTask<string>(message);

        return InvokePreDeserializationCallbackAsync(message);
    }

    private async ValueTask<string> InvokePreDeserializationCallbackAsync(string message)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            message = await serializationCallback.PreDeserializationAsync(message);
        }
        return message;
    }

    private ValueTask InvokePostDeserializationCallback(MessageEnvelope messageEnvelope)
    {
        if (_messageConfiguration.SerializationCallbacks.Count == 0)
            return default;

        return InvokePostDeserializationCallbackAsync(messageEnvelope);
    }

    private async ValueTask InvokePostDeserializationCallbackAsync(MessageEnvelope messageEnvelope)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            await serializationCallback.PostDeserializationAsync(messageEnvelope);
        }
    }
}
