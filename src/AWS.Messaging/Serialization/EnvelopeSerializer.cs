// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
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

    private readonly IMessageTypeClassifier _classifier;
    private readonly ISQSWrapperReader _sqsReader;

    public EnvelopeSerializer(
        ILogger<EnvelopeSerializer> logger,
        IMessageConfiguration messageConfiguration,
        IMessageSerializer messageSerializer,
        IDateTimeHandler dateTimeHandler,
        IMessageIdGenerator messageIdGenerator,
        IMessageSourceHandler messageSourceHandler,
        IServiceProvider serviceProvider,
        IMessageTypeClassifier classifier,
        ISQSWrapperReader sqsReader)
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
        _classifier = classifier;
        _sqsReader = sqsReader;
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
            var (envelopeUtf8, metadata, rentedOuter) = await ParseOuterWrapper(sqsMessage);

            try
            {
                var (envelope, subscriberMapping) = DeserializeEnvelope(envelopeUtf8.Span);

                envelope.SQSMetadata = metadata.SQSMetadata;
                envelope.SNSMetadata = metadata.SNSMetadata;
                envelope.EventBridgeMetadata = metadata.EventBridgeMetadata;

                await InvokePostDeserializationCallback(envelope);
                return new ConvertToEnvelopeResult(envelope, subscriberMapping);
            }
            finally
            {
                if (rentedOuter != null)
                    ArrayPool<byte>.Shared.Return(rentedOuter);
            }
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
        var (envelopeUtf8, metadata, rentedOuter) = ParseOuterWrapperCore(sqsMessage);

        try
        {
            var (envelope, subscriberMapping) = DeserializeEnvelope(envelopeUtf8.Span);

            envelope.SQSMetadata = metadata.SQSMetadata;
            envelope.SNSMetadata = metadata.SNSMetadata;
            envelope.EventBridgeMetadata = metadata.EventBridgeMetadata;

            return new ConvertToEnvelopeResult(envelope, subscriberMapping);
        }
        finally
        {
            if (rentedOuter != null)
                ArrayPool<byte>.Shared.Return(rentedOuter);
        }
    }

    private static bool IsJsonContentType(string? dataContentType)
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

    private (MessageEnvelope Envelope, SubscriberMapping Mapping) DeserializeEnvelope(ReadOnlySpan<byte> utf8Envelope)
    {
        var reader = new Utf8JsonReader(utf8Envelope);

        // CloudEvent properties
        string? id = null, source = null, specVersion = null, type = null, dataContentType = null;
        DateTimeOffset? time = null;

        // Track data element byte range for deferred deserialization
        int dataStart = -1, dataLength = 0;
        bool dataIsString = false;
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
                specVersion = reader.GetString();
            }
            else if (reader.ValueTextEquals("time"u8))
            {
                reader.Read();
                time = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals("datacontenttype"u8))
            {
                reader.Read();
                dataContentType = reader.GetString();
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
                metadata ??= new Dictionary<string, JsonElement>();
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
        var envelope = subscriberMapping.MessageEnvelopeFactory.Invoke();

        envelope.Id = id;
        envelope.Source = new Uri(source, UriKind.RelativeOrAbsolute);
        envelope.Version = specVersion;
        envelope.MessageTypeIdentifier = type;
        envelope.TimeStamp = time.Value;
        envelope.DataContentType = dataContentType;

        if (metadata is not null)
        {
            foreach (var kvp in metadata)
                envelope.Metadata[kvp.Key] = kvp.Value;
        }

        // Deserialize the payload
        object message;
        bool isJsonContent = IsJsonContentType(dataContentType);

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

    private ValueTask<(ReadOnlyMemory<byte> EnvelopeUtf8, MessageMetadata Metadata, byte[]? RentedBuffer)> ParseOuterWrapper(Message sqsMessage)
    {
        // When no serialization callbacks are registered (the common case),
        // avoid the async state machine entirely — pure synchronous compute.
        if (_messageConfiguration.SerializationCallbacks.Count == 0)
        {
            return new ValueTask<(ReadOnlyMemory<byte>, MessageMetadata, byte[]?)>(
                ParseOuterWrapperCore(sqsMessage));
        }

        return ParseOuterWrapperAsync(sqsMessage);
    }

    private async ValueTask<(ReadOnlyMemory<byte> EnvelopeUtf8, MessageMetadata Metadata, byte[]? RentedBuffer)> ParseOuterWrapperAsync(Message sqsMessage)
    {
        sqsMessage.Body = await InvokePreDeserializationCallback(sqsMessage.Body);
        return ParseOuterWrapperCore(sqsMessage);
    }

    private (ReadOnlyMemory<byte> EnvelopeUtf8, MessageMetadata Metadata, byte[]? RentedBuffer) ParseOuterWrapperCore(Message sqsMessage)
    {
        // Convert to UTF-8 once — this buffer is used by the classifier and wrapper readers,
        // and for the SQS path it IS the envelope bytes fed to DeserializeEnvelope.
        var bodyLength = Encoding.UTF8.GetByteCount(sqsMessage.Body);
        byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLength);
        Encoding.UTF8.GetBytes(sqsMessage.Body, rented.AsSpan(0, bodyLength));
        var utf8Body = rented.AsMemory(0, bodyLength);

        var classification = _classifier.Classify(utf8Body.Span);

        if (classification.WrapperType == WrapperType.Sqs)
        {
            // Fast path: body IS the envelope — pass the rented buffer through directly
            var (innerUtf8, metadata) = _sqsReader.Extract(utf8Body, sqsMessage);
            return (innerUtf8, metadata, rented);
        }

        // SNS or EventBridge: delegate to the matched reader for metadata + body extraction
        var reader = _classifier.GetReader(classification.WrapperType);
        var (wrapperUtf8, wrapperMetadata) = reader.Extract(utf8Body.Span, sqsMessage);

        // The wrapper reader rents its own buffer from ArrayPool for the inner body,
        // so we can return the outer buffer now.
        ArrayPool<byte>.Shared.Return(rented);

        // Extract the pool-rented backing array so the caller can return it after deserialization.
        // Both SNS (CopyString) and EventBridge (Rent+CopyTo) paths produce pool-rented arrays.
        byte[]? innerRented = null;
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(wrapperUtf8, out var segment) && segment.Array != null)
        {
            innerRented = segment.Array;
        }

        return (wrapperUtf8, wrapperMetadata, innerRented);
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

    private ValueTask InvokePreSerializationCallback(MessageEnvelope messageEnvelope)
    {
        if (_messageConfiguration.SerializationCallbacks.Count == 0)
            return default;

        return InvokePreSerializationCallbackAsync(messageEnvelope);
    }

    private async ValueTask InvokePreSerializationCallbackAsync(MessageEnvelope messageEnvelope)
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

    private ValueTask<string> InvokePostSerializationCallback(string message)
    {
        if (_messageConfiguration.SerializationCallbacks.Count == 0)
            return new ValueTask<string>(message);

        return InvokePostSerializationCallbackAsync(message);
    }

    private async ValueTask<string> InvokePostSerializationCallbackAsync(string message)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            message = await serializationCallback.PostSerializationAsync(message);
        }
        return message;
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
