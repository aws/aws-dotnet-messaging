// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AWS.Messaging.Configuration;
using AWS.Messaging.Services;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Serialization;

/// <summary>
/// This is the performance based implementation of <see cref="IMessageSerializer"/> used by the framework.
/// It uses System.Text.Json to serialize and deserialize messages.
/// </summary>
internal sealed partial class MessageSerializerUtf8JsonWriter : IMessageSerializer, IMessageSerializerUtf8JsonWriter
{
    private readonly ILogger<MessageSerializerUtf8JsonWriter> _logger;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly JsonSerializerContext? _jsonSerializerContext;

    public MessageSerializerUtf8JsonWriter(ILogger<MessageSerializerUtf8JsonWriter> logger, IMessageConfiguration messageConfiguration, IMessageJsonSerializerContextContainer jsonContextContainer)
    {
        _logger = logger;
        _messageConfiguration= messageConfiguration;
        _jsonSerializerContext = jsonContextContainer.GetJsonSerializerContext();
    }

    public string ContentType => "application/json";

    /// <inheritdoc/>
    /// <exception cref="FailedToDeserializeApplicationMessageException"></exception>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    public object Deserialize(string message, Type deserializedType)
    {
        try
        {
            var jsonSerializerOptions = _messageConfiguration.SerializationOptions.SystemTextJsonOptions;
            if (_messageConfiguration.LogMessageContent)
            {
                Logs.DeserializingMessageWithContent(_logger, deserializedType, message);
            }
            else
            {
                Logs.DeserializingMessage(_logger, deserializedType);
            }

            if (_jsonSerializerContext != null)
            {
                return JsonSerializer.Deserialize(message, deserializedType, _jsonSerializerContext) ?? throw new JsonException("The deserialized application message is null.");
            }
            else
            {
                return JsonSerializer.Deserialize(message, deserializedType, jsonSerializerOptions) ?? throw new JsonException("The deserialized application message is null.");
            }
        }
        catch (JsonException) when (!_messageConfiguration.LogMessageContent)
        {
            Logs.FailedToDeserializeMessage(_logger, deserializedType);
            throw new FailedToDeserializeApplicationMessageException($"Failed to deserialize application message into an instance of {deserializedType}.");
        }
        catch (Exception ex)
        {
            Logs.FailedToDeserializeMessageException(_logger, ex, deserializedType);
            throw new FailedToDeserializeApplicationMessageException($"Failed to deserialize application message into an instance of {deserializedType}.", ex);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="FailedToSerializeApplicationMessageException"></exception>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    public MessageSerializerResults Serialize(object message)
    {
        try
        {
            var jsonSerializerOptions = _messageConfiguration.SerializationOptions.SystemTextJsonOptions;

            string jsonString;
            Type messageType = message.GetType();

            if (_jsonSerializerContext != null)
            {
                jsonString = JsonSerializer.Serialize(message, messageType, _jsonSerializerContext);
            }
            else
            {
                jsonString = JsonSerializer.Serialize(message, jsonSerializerOptions);
            }

            if (_messageConfiguration.LogMessageContent)
            {
                Logs.SerializedMessageWithContent(_logger, jsonString);
            }
            else
            {
                Logs.SerializedMessage(_logger, jsonString.Length);
            }

            return new MessageSerializerResults(jsonString, ContentType);
        }
        catch (JsonException) when (!_messageConfiguration.LogMessageContent)
        {
            Logs.FailedToSerializeMessage(_logger);
            throw new FailedToSerializeApplicationMessageException("Failed to serialize application message into a string");
        }
        catch (Exception ex)
        {
            Logs.FailedToSerializeMessageException(_logger, ex);
            throw new FailedToSerializeApplicationMessageException("Failed to serialize application message into a string", ex);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="FailedToSerializeApplicationMessageException"></exception>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    public void SerializeToBuffer<T>(Utf8JsonWriter writer, T value)
    {
        try
        {
            var typeInfo = _jsonSerializerContext?.GetTypeInfo(typeof(T));
            var startPosition = writer.BytesCommitted;
            if (typeInfo is not null)
            {
                JsonSerializer.Serialize(writer, value, typeInfo);
            }
            else
            {
                // This is not AOT-friendly fallback, but it is necessary for scenarios where the JsonSerializerContext is not provided.
                JsonSerializer.Serialize(writer, value, _messageConfiguration.SerializationOptions.SystemTextJsonOptions);
            }

            Logs.SerializedMessage(_logger, writer.BytesCommitted - startPosition);
        }
        catch (JsonException) when (!_messageConfiguration.LogMessageContent)
        {
            Logs.FailedToSerializeMessage(_logger);
            throw new FailedToSerializeApplicationMessageException("Failed to serialize application message into a string");
        }
        catch (Exception ex)
        {
            Logs.FailedToSerializeMessageException(_logger, ex);
            throw new FailedToSerializeApplicationMessageException("Failed to serialize application message into a string", ex);
        }
    }

    internal static partial class Logs
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Trace, Message = "Deserializing the following message into type '{DeserializedType}':\n{Message}")]
        public static partial void DeserializingMessageWithContent(ILogger logger, Type deserializedType, string message);

        [LoggerMessage(EventId = 0, Level = LogLevel.Trace, Message = "Deserializing the following message into type '{DeserializedType}'")]
        public static partial void DeserializingMessage(ILogger logger, Type deserializedType);

        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "Failed to deserialize application message into an instance of {DeserializedType}.")]
        public static partial void FailedToDeserializeMessage(ILogger logger, Type deserializedType);

        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "Failed to deserialize application message into an instance of {DeserializedType}.")]
        public static partial void FailedToDeserializeMessageException(ILogger logger, Exception ex, Type deserializedType);

        [LoggerMessage(EventId = 0, Level = LogLevel.Trace, Message = "Serialized the message object as the following raw string:\n{JsonString}")]
        public static partial void SerializedMessageWithContent(ILogger logger, string jsonString);

        [LoggerMessage(EventId = 0, Level = LogLevel.Trace, Message = "Serialized the message object to a raw string with a content length of {ContentLength}.")]
        public static partial void SerializedMessage(ILogger logger, long contentLength);

        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "Failed to serialize application message into a string")]
        public static partial void FailedToSerializeMessage(ILogger logger);

        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "Failed to serialize application message into a string")]
        public static partial void FailedToSerializeMessageException(ILogger logger, Exception ex);
    }
}
