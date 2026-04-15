// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization.Helpers;
using AWS.Messaging.Serialization.Parsers;
using AWS.Messaging.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Serialization;

/// <summary>
/// The performance based implementation of <see cref="IEnvelopeSerializer"/> used by the framework.
/// </summary>
internal class EnvelopeSerializer : IEnvelopeSerializer
{
    private Uri? MessageSource { get; set; }
    
    // Pre-encoded property names to avoid repeated encoding and allocations
    private static readonly JsonEncodedText s_idProp = JsonEncodedText.Encode("id");
    private static readonly JsonEncodedText s_sourceProp = JsonEncodedText.Encode("source");
    private static readonly JsonEncodedText s_specVersionProp = JsonEncodedText.Encode("specversion");
    private static readonly JsonEncodedText s_typeProp = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText s_timeProp = JsonEncodedText.Encode("time");
    private static readonly JsonEncodedText s_dataContentTypeProp = JsonEncodedText.Encode("datacontenttype");
    private static readonly JsonEncodedText s_dataProp = JsonEncodedText.Encode("data");

    private readonly IMessageConfiguration _messageConfiguration;
    private readonly IMessageSerializer _messageSerializer;
    private readonly IDateTimeHandler _dateTimeHandler;
    private readonly IMessageIdGenerator _messageIdGenerator;
    private readonly IMessageSourceHandler _messageSourceHandler;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EnvelopeSerializer> _logger;

    private readonly IMessageSerializerUtf8JsonWriter? _messageSerializerUtf8Json;
    private readonly IMessageSerializerUtf8JsonReader? _messageSerializerUtf8JsonReader;

    // Order matters for the SQS parser (must be last), but SNS and EventBridge parsers
    // can be in any order since they check for different, mutually exclusive properties
    private static readonly IMessageParser[] _parsers = new IMessageParser[]
    {
        new SNSMessageParser(), // Checks for SNS-specific properties (Type, TopicArn)
        new EventBridgeMessageParser(), // Checks for EventBridge properties (detail-type, detail)
        new SQSMessageParser() // Fallback parser - must be last
    };

    public EnvelopeSerializer(
        ILogger<EnvelopeSerializer> logger,
        IMessageConfiguration messageConfiguration,
        IMessageSerializer messageSerializer,
        IDateTimeHandler dateTimeHandler,
        IMessageIdGenerator messageIdGenerator,
        IMessageSourceHandler messageSourceHandler,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _messageConfiguration = messageConfiguration;
        _messageSerializer = messageSerializer;
        _dateTimeHandler = dateTimeHandler;
        _messageIdGenerator = messageIdGenerator;
        _messageSourceHandler = messageSourceHandler;

        _messageSerializerUtf8Json = messageSerializer as IMessageSerializerUtf8JsonWriter;
        _messageSerializerUtf8JsonReader = messageSerializer as IMessageSerializerUtf8JsonReader;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async ValueTask<MessageEnvelope<T>> CreateEnvelopeAsync<T>(T message)
    {
        var messageId = await _messageIdGenerator.GenerateIdAsync();
        var timeStamp = _dateTimeHandler.GetUtcNow();

        var publisherMapping = _messageConfiguration.GetPublisherMapping(typeof(T));
        if (publisherMapping is null)
        {
            _logger.LogError("Failed to create a message envelope because a valid publisher mapping for message type '{MessageType}' does not exist.", typeof(T));
            throw new FailedToCreateMessageEnvelopeException($"Failed to create a message envelope because a valid publisher mapping for message type '{typeof(T)}' does not exist.");
        }

        if (MessageSource is null)
        {
            MessageSource = await _messageSourceHandler.ComputeMessageSource();
        }

        return new MessageEnvelope<T>
        {
            Id = messageId,
            Source = MessageSource,
            Version = Constants.CLOUD_EVENT_SPEC_VERSION,
            MessageTypeIdentifier = publisherMapping.MessageTypeIdentifier,
            TimeStamp = timeStamp,
            Message = message
        };
    }

    private static readonly JsonWriterOptions s_serializerWriterOptions = new()
    {
        // We control the JSON shape here, so skip validation for performance
        SkipValidation = true,
    };

    /// <summary>
    /// Serializes the <see cref="MessageEnvelope{T}"/> into a raw string representing a JSON blob
    /// </summary>
    /// <typeparam name="T">The .NET type of the underlying application message held by <see cref="MessageEnvelope{T}.Message"/></typeparam>
    /// <param name="envelope">The <see cref="MessageEnvelope{T}"/> instance that will be serialized</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public async ValueTask<string> SerializeAsync<T>(MessageEnvelope<T> envelope)
    {
        try
        {
            await InvokePreSerializationCallback(envelope);
            await InvokeTypedPreSerializationCallbacks(envelope);
            var message = envelope.Message ?? throw new ArgumentNullException(nameof(envelope.Message), "The underlying application message cannot be null");

            using var buffer = new RentArrayBufferWriter(_messageConfiguration.SerializationOptions.RentedBufferOptions);
            using var writer = new Utf8JsonWriter(buffer, s_serializerWriterOptions);

            writer.WriteStartObject();

            writer.WriteString(s_idProp, envelope.Id);
            writer.WriteString(s_sourceProp, envelope.Source?.ToString());
            writer.WriteString(s_specVersionProp, envelope.Version);
            writer.WriteString(s_typeProp, envelope.MessageTypeIdentifier);
            writer.WriteString(s_timeProp, envelope.TimeStamp);

            if (_messageSerializerUtf8Json is not null)
            {
                writer.WriteString(s_dataContentTypeProp, _messageSerializerUtf8Json.ContentType);
                writer.WritePropertyName(s_dataProp);
                _messageSerializerUtf8Json.SerializeToBuffer(writer, message);
            }
            else
            {
                var response = _messageSerializer.Serialize(message);
                writer.WriteString(s_dataContentTypeProp, response.ContentType);
                writer.WritePropertyName(s_dataProp);
                if (IsJsonContentType(response.ContentType))
                {
                    writer.WriteRawValue(response.Data, skipInputValidation: true);
                }
                else
                {
                    writer.WriteStringValue(response.Data);
                }
            }

            // Write metadata as top-level properties
            foreach (var kvp in envelope.Metadata)
            {
                if (kvp.Key is not null &&
                    kvp.Value.ValueKind != JsonValueKind.Undefined &&
                    kvp.Value.ValueKind != JsonValueKind.Null &&
                    !s_knownEnvelopeProperties.Contains(kvp.Key))
                {
                    writer.WritePropertyName(kvp.Key);
                    kvp.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
            writer.Flush();

            var jsonString = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
            var serializedMessage = await InvokePostSerializationCallback(jsonString);

            if (_messageConfiguration.LogMessageContent)
            {
                _logger.LogTrace("Serialized the MessageEnvelope object as the following raw string:\n{SerializedMessage}", serializedMessage);
            }
            else
            {
                _logger.LogTrace("Serialized the MessageEnvelope object to a raw string");
            }
            return serializedMessage;
        }
        catch (JsonException) when (!_messageConfiguration.LogMessageContent)
        {
            _logger.LogError("Failed to serialize the MessageEnvelope into a raw string");
            throw new FailedToSerializeMessageEnvelopeException("Failed to serialize the MessageEnvelope into a raw string");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize the MessageEnvelope into a raw string");
            throw new FailedToSerializeMessageEnvelopeException("Failed to serialize the MessageEnvelope into a raw string", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ConvertToEnvelopeResult> ConvertToEnvelopeAsync(Message sqsMessage)
    {
        try
        {
            // Get the raw envelope JSON and metadata from the appropriate wrapper (SNS/EventBridge/SQS)
            var (envelopeJson, metadata) = await ParseOuterWrapper(sqsMessage);

            // Create and populate the envelope with the correct type
            var (envelope, subscriberMapping) = DeserializeEnvelope(envelopeJson);

            // Add metadata from outer wrapper
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

    private bool IsJsonContentType(string? dataContentType)
    {
        if (string.IsNullOrWhiteSpace(dataContentType))
        {
            // If dataContentType is not specified, it should be treated as "application/json"
            return true;
        }

        ReadOnlySpan<char> contentType = dataContentType.AsSpan().Trim();

        // Remove parameters (anything after ';')
        int semicolonIndex = contentType.IndexOf(';');
        if (semicolonIndex >= 0)
            contentType = contentType.Slice(0, semicolonIndex).Trim();

        // Check "application/json" (case-insensitive)
        if (contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        // Find the '/' separator
        int slashIndex = contentType.IndexOf('/');
        if (slashIndex < 0
            || slashIndex == contentType.Length - 1
            || slashIndex != contentType.LastIndexOf('/'))
        {
            // If there are multiple slashes, ends with a slash or there are no slashes at all, it's not a valid content type
            return false;
        }

        ReadOnlySpan<char> subtype = contentType.Slice(slashIndex + 1);

        // Check if the media subtype is "json" or ends with "+json"
        return subtype.Equals("json", StringComparison.OrdinalIgnoreCase)
            || subtype.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly FrozenSet<string> s_knownEnvelopeProperties = new HashSet<string> {
        "id",
        "source",
        "specversion",
        "type",
        "time",
        "datacontenttype",
        "data"
    }.ToFrozenSet();

    private (MessageEnvelope Envelope, SubscriberMapping Mapping) DeserializeEnvelope(string envelopeString)
    {
        using var document = JsonDocument.Parse(envelopeString);
        var root = document.RootElement;

        // Get the message type and lookup mapping first
        var messageType = root.GetProperty("type").GetString() ?? throw new InvalidDataException("Message type identifier not found in envelope");
        var subscriberMapping = GetAndValidateSubscriberMapping(messageType);

        var envelope = subscriberMapping.MessageEnvelopeFactory.Invoke();

        try
        {
            // Set envelope properties directly without delegate-based helpers
            // to avoid Func<JsonElement, T> allocations on every deserialization call.
            envelope.Id = GetRequiredString(root, "id");
            envelope.Source = new Uri(GetRequiredString(root, "source"), UriKind.RelativeOrAbsolute);
            envelope.Version = GetRequiredString(root, "specversion");
            envelope.MessageTypeIdentifier = messageType; // Already extracted above
            envelope.TimeStamp = GetRequiredDateTimeOffset(root, "time");
            envelope.DataContentType = root.TryGetProperty("datacontenttype", out var dctProp) ? dctProp.GetString() : null;

            // Handle metadata - copy any properties that aren't standard envelope properties
            foreach (var property in root.EnumerateObject())
            {
                if (!s_knownEnvelopeProperties.Contains(property.Name))
                {
                    envelope.Metadata[property.Name] = property.Value.Clone();
                }
            }

            // Deserialize the message content using the optimized element-based path when available,
            // avoiding the GetRawText() string allocation and re-parse.
            object message;
            if (_messageSerializerUtf8JsonReader is not null && IsJsonContentType(envelope.DataContentType))
            {
                if (!root.TryGetProperty("data", out var dataElement))
                    throw new InvalidDataException("Required property 'data' is missing");
                message = _messageSerializerUtf8JsonReader.DeserializeFromElement(dataElement, subscriberMapping.MessageType);
            }
            else
            {
                if (!root.TryGetProperty("data", out var dataElement))
                    throw new InvalidDataException("Required property 'data' is missing");
                var dataContent = IsJsonContentType(envelope.DataContentType)
                    ? dataElement.GetRawText()
                    : dataElement.GetString()!;
                message = _messageSerializer.Deserialize(dataContent, subscriberMapping.MessageType);
            }
            envelope.SetMessage(message);

            return (envelope, subscriberMapping);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize or validate MessageEnvelope");
            throw new InvalidDataException("MessageEnvelope instance is not valid", ex);
        }
    }

    /// <summary>
    /// Extracts a required string property from a JsonElement without delegate allocation.
    /// </summary>
    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property))
        {
            return property.GetString() ?? throw new InvalidDataException($"Required property '{propertyName}' is null");
        }
        throw new InvalidDataException($"Required property '{propertyName}' is missing");
    }

    /// <summary>
    /// Extracts a required DateTimeOffset property from a JsonElement without delegate allocation.
    /// </summary>
    private static DateTimeOffset GetRequiredDateTimeOffset(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property))
        {
            return property.GetDateTimeOffset();
        }
        throw new InvalidDataException($"Required property '{propertyName}' is missing");
    }

    private async Task<(string MessageBody, MessageMetadata Metadata)> ParseOuterWrapper(Message sqsMessage)
    {
        sqsMessage.Body = await InvokePreDeserializationCallback(sqsMessage.Body);

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

        var document = JsonDocument.Parse(sqsMessage.Body);

        try
        {
            string currentMessageBody = sqsMessage.Body;
            var combinedMetadata = new MessageMetadata();

            // Try each parser in order (avoid LINQ .Where() to prevent delegate allocation)
            foreach (var parser in _parsers)
            {
                if (!parser.CanParse(document.RootElement))
                    continue;

                var (messageBody, metadata) = parser.Parse(document.RootElement, sqsMessage);

                // Update the message body if this parser extracted a different inner message.
                // Skip the re-parse when the body hasn't changed (e.g. SQS fallback parser
                // returns GetRawText() of the same document that's already parsed).
                if (!string.IsNullOrEmpty(messageBody) &&
                    !string.Equals(messageBody, currentMessageBody, StringComparison.Ordinal))
                {
                    currentMessageBody = messageBody;
                    document.Dispose();
                    document = JsonDocument.Parse(messageBody);
                }

                // Combine metadata
                if (metadata.SQSMetadata != null) combinedMetadata.SQSMetadata = metadata.SQSMetadata;
                if (metadata.SNSMetadata != null) combinedMetadata.SNSMetadata = metadata.SNSMetadata;
                if (metadata.EventBridgeMetadata != null) combinedMetadata.EventBridgeMetadata = metadata.EventBridgeMetadata;
            }

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

            return (currentMessageBody, combinedMetadata);
        }
        finally
        {
            document.Dispose();
        }
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

    private async ValueTask InvokePreSerializationCallback(MessageEnvelope messageEnvelope)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            await serializationCallback.PreSerializationAsync(messageEnvelope);
        }
    }

    private async ValueTask InvokeTypedPreSerializationCallbacks<T>(MessageEnvelope<T> messageEnvelope)
    {
        var typedCallbacks = _serviceProvider.GetServices<ISerializationCallback<T>>();
        foreach (var callback in typedCallbacks)
        {
            await callback.PreSerializationAsync(messageEnvelope);
        }
    }

    private async ValueTask<string> InvokePostSerializationCallback(string message)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            message = await serializationCallback.PostSerializationAsync(message);
        }
        return message;
    }

    private async ValueTask<string> InvokePreDeserializationCallback(string message)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            message = await serializationCallback.PreDeserializationAsync(message);
        }
        return message;
    }

    private async ValueTask InvokePostDeserializationCallback(MessageEnvelope messageEnvelope)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            await serializationCallback.PostDeserializationAsync(messageEnvelope);
        }
    }
}
