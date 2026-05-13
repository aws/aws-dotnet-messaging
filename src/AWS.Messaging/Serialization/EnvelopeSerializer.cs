// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization.Helpers;
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
        _serviceProvider = serviceProvider;

        _messageSerializerUtf8Json = messageSerializer as IMessageSerializerUtf8JsonWriter;
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

        MessageSource ??= await _messageSourceHandler.ComputeMessageSource();

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

            var jsonString = Encoding.UTF8.GetString(buffer.WrittenSpan);
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

        ReadOnlySpan<char> contentType = dataContentType.AsSpan().Trim();

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
}
