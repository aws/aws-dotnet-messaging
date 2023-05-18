// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Services;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Serialization;

/// <summary>
/// The default implementation of <see cref="IEnvelopeSerializer"/> used by the framework.
/// </summary>
internal class EnvelopeSerializer : IEnvelopeSerializer
{
    private Uri? MessageSource { get; set; }
    private const string CLOUD_EVENT_SPEC_VERSION = "1.0";

    private readonly IMessageConfiguration _messageConfiguration;
    private readonly IMessageSerializer _messageSerializer;
    private readonly IDateTimeHandler _dateTimeHandler;
    private readonly IMessageIdGenerator _messageIdGenerator;
    private readonly IMessageSourceHandler _messageSourceHandler;
    private readonly ILogger<EnvelopeSerializer> _logger;

    public EnvelopeSerializer(
        ILogger<EnvelopeSerializer> logger,
        IMessageConfiguration messageConfiguration,
        IMessageSerializer messageSerializer,
        IDateTimeHandler dateTimeHandler,
        IMessageIdGenerator messageIdGenerator,
        IMessageSourceHandler messageSourceHandler)
    {
        _logger = logger;
        _messageConfiguration = messageConfiguration;
        _messageSerializer = messageSerializer;
        _dateTimeHandler = dateTimeHandler;
        _messageIdGenerator = messageIdGenerator;
        _messageSourceHandler = messageSourceHandler;
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
            Version = CLOUD_EVENT_SPEC_VERSION,
            MessageTypeIdentifier = publisherMapping.MessageTypeIdentifier,
            TimeStamp = timeStamp,
            Message = message
        };
    }

    /// <summary>
    /// Serializes the <see cref="MessageEnvelope{T}"/> into a raw string representing a JSON blob
    /// </summary>
    /// <typeparam name="T">The .NET type of the underlying application message held by <see cref="MessageEnvelope{T}.Message"/></typeparam>
    /// <param name="envelope">The <see cref="MessageEnvelope{T}"/> instance that will be serialized</param>
    public async ValueTask<string> SerializeAsync<T>(MessageEnvelope<T> envelope)
    {
        try
        {
            await InvokePreSerializationCallback(envelope);
            var message = envelope.Message ?? throw new ArgumentNullException("The underlying application message cannot be null");

            // This blob serves as an intermediate data container because the underlying application message
            // must be serialized separately as the _messageSerializer can have a user injected implementation.
            var blob = new JsonObject
            {
                ["id"] = envelope.Id,
                ["source"] = envelope.Source?.ToString(),
                ["specversion"] = envelope.Version,
                ["type"] = envelope.MessageTypeIdentifier,
                ["time"] = envelope.TimeStamp,
                ["data"] = _messageSerializer.Serialize(message)
            };

            var jsonString = blob.ToJsonString();
            var serializedMessage = await InvokePostSerializationCallback(jsonString);

            _logger.LogTrace("Serialized the MessageEnvelope object as the following raw string:\n{SerializedMessage}", serializedMessage);
            return serializedMessage;
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
            sqsMessage.Body = await InvokePreDeserializationCallback(sqsMessage.Body);
            var messageEnvelopeConfiguration = GetMessageEnvelopeConfiguration(sqsMessage);
            var intermediateEnvelope = JsonSerializer.Deserialize<MessageEnvelope<string>>(messageEnvelopeConfiguration.MessageEnvelopeBody!)!;
            ValidateMessageEnvelope(intermediateEnvelope);
            var messageTypeIdentifier = intermediateEnvelope.MessageTypeIdentifier;
            var subscriberMapping = _messageConfiguration.GetSubscriberMapping(messageTypeIdentifier);
            if (subscriberMapping is null)
            {
                _logger.LogError("{MessageConfiguration} does not have a valid subscriber mapping for message ID '{MessageTypeIdentifier}'", nameof(_messageConfiguration), messageTypeIdentifier);
                throw new InvalidDataException($"{nameof(_messageConfiguration)} does not have a valid subscriber mapping for {nameof(messageTypeIdentifier)} '{messageTypeIdentifier}'");
            }

            var messageType = subscriberMapping.MessageType;
            var message = _messageSerializer.Deserialize(intermediateEnvelope.Message, messageType);
            var messageEnvelopeType = typeof(MessageEnvelope<>).MakeGenericType(messageType);

            if (Activator.CreateInstance(messageEnvelopeType) is not MessageEnvelope finalMessageEnvelope)
            {
                _logger.LogError("Failed to create a messageEnvelope of type '{MessageEnvelopeType}'", messageEnvelopeType.FullName);
                throw new InvalidOperationException($"Failed to create a {nameof(MessageEnvelope)} of type '{messageEnvelopeType.FullName}'");
            }

            finalMessageEnvelope.Id = intermediateEnvelope.Id;
            finalMessageEnvelope.Source = intermediateEnvelope.Source;
            finalMessageEnvelope.Version = intermediateEnvelope.Version;
            finalMessageEnvelope.MessageTypeIdentifier = intermediateEnvelope.MessageTypeIdentifier;
            finalMessageEnvelope.TimeStamp = intermediateEnvelope.TimeStamp;
            finalMessageEnvelope.Metadata = intermediateEnvelope.Metadata;
            finalMessageEnvelope.SQSMetadata = messageEnvelopeConfiguration.SQSMetadata;
            finalMessageEnvelope.SNSMetadata = messageEnvelopeConfiguration.SNSMetadata;
            finalMessageEnvelope.EventBridgeMetadata = messageEnvelopeConfiguration.EventBridgeMetadata;
            finalMessageEnvelope.SetMessage(message);

            await InvokePostDeserializationCallback(finalMessageEnvelope);
            var result = new ConvertToEnvelopeResult(finalMessageEnvelope, subscriberMapping);

            _logger.LogTrace("Created a generic {MessageEnvelopeName} of type '{MessageEnvelopeType}'", nameof(MessageEnvelope), result.Envelope.GetType());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create a {MessageEnvelopeName}", nameof(MessageEnvelope));
            throw new FailedToCreateMessageEnvelopeException($"Failed to create {nameof(MessageEnvelope)}", ex);
        }
    }

    private void ValidateMessageEnvelope<T>(MessageEnvelope<T>? messageEnvelope)
    {
        if (messageEnvelope is null)
        {
            _logger.LogError("{MessageEnvelope} cannot be null", nameof(messageEnvelope));
            throw new InvalidDataException($"{nameof(messageEnvelope)} cannot be null");
        }

        var strBuilder = new StringBuilder();

        if (string.IsNullOrEmpty(messageEnvelope.Id))
            strBuilder.Append($"{nameof(messageEnvelope.Id)} cannot be null or empty.{Environment.NewLine}");

        if (messageEnvelope.Source is null)
            strBuilder.Append($"{nameof(messageEnvelope.Source)} cannot be null.{Environment.NewLine}");

        if (string.IsNullOrEmpty(messageEnvelope.Version))
            strBuilder.Append($"{nameof(messageEnvelope.Version)} cannot be null or empty.{Environment.NewLine}");

        if (string.IsNullOrEmpty(messageEnvelope.MessageTypeIdentifier))
            strBuilder.Append($"{nameof(messageEnvelope.MessageTypeIdentifier)} cannot be null or empty.{Environment.NewLine}");

        if (messageEnvelope.TimeStamp == DateTimeOffset.MinValue)
            strBuilder.Append($"{nameof(messageEnvelope.TimeStamp)} is not set.");

        if (messageEnvelope.Message is null)
            strBuilder.Append($"{nameof(messageEnvelope.Message)} cannot be null.{Environment.NewLine}");

        var validationFailures = strBuilder.ToString();
        if (!string.IsNullOrEmpty(validationFailures))
        {
            _logger.LogError("MessageEnvelope instance is not valid" + Environment.NewLine +"{ValidationFailures}", validationFailures);
            throw new InvalidDataException($"MessageEnvelope instance is not valid{Environment.NewLine}{validationFailures}");
        }
    }

    private MessageEnvelopeConfiguration GetMessageEnvelopeConfiguration(Message sqsMessage)
    {
        var envelopeConfiguration = new MessageEnvelopeConfiguration();
        envelopeConfiguration.MessageEnvelopeBody = sqsMessage.Body;

        using (var document = JsonDocument.Parse(sqsMessage.Body))
        {
            var root = document.RootElement;
            // Check if the SQS message body contains an outer envelope injected by SNS.
            if (root.TryGetProperty("Type", out var messageType) && string.Equals("Notification", messageType.GetString()))
            {
                // Retrieve the inner message envelope.
                envelopeConfiguration.MessageEnvelopeBody = GetJsonPropertyAsString(root, "Message");
                if (string.IsNullOrEmpty(envelopeConfiguration.MessageEnvelopeBody))
                {
                    _logger.LogError("Failed to create a message envelope configuration because the SNS message envelope does not contain a valid message property.");
                    throw new FailedToCreateMessageEnvelopeConfigurationException("The SNS message envelope does not contain a valid message property.");
                }
                SetSNSMetadata(envelopeConfiguration, root);
            }
            // Check if the SQS message body contains an outer envelope injected by EventBridge.
            else if (root.TryGetProperty("detail", out var innerEnvelope)
                && root.TryGetProperty("id", out var _)
                && root.TryGetProperty("version", out var _)
                && root.TryGetProperty("region", out var _))
            {
                // Retrieve the inner message envelope.
                envelopeConfiguration.MessageEnvelopeBody = innerEnvelope.GetString();
                if (string.IsNullOrEmpty(envelopeConfiguration.MessageEnvelopeBody))
                {
                    _logger.LogError("Failed to create a message envelope configuration because the EventBridge message envelope does not contain a valid 'detail' property.");
                    throw new FailedToCreateMessageEnvelopeConfigurationException("The EventBridge message envelope does not contain a valid 'detail' property.");
                }
                SetEventBridgeMetadata(envelopeConfiguration, root);
            }
        }

        SetSQSMetadata(envelopeConfiguration, sqsMessage);
        return envelopeConfiguration;
    }

    private void SetSQSMetadata(MessageEnvelopeConfiguration envelopeConfiguration, Message sqsMessage)
    {
        envelopeConfiguration.SQSMetadata = new SQSMetadata
        {
            MessageID = sqsMessage.MessageId,
            MessageAttributes = sqsMessage.MessageAttributes,
            ReceiptHandle = sqsMessage.ReceiptHandle
        };
        if (sqsMessage.Attributes.TryGetValue("MessageGroupId", out var attribute))
        {
            envelopeConfiguration.SQSMetadata.MessageGroupId = attribute;
        }
        if (sqsMessage.Attributes.TryGetValue("MessageDeduplicationId", out var messageAttribute))
        {
            envelopeConfiguration.SQSMetadata.MessageDeduplicationId = messageAttribute;
        }
    }

    private void SetSNSMetadata(MessageEnvelopeConfiguration envelopeConfiguration, JsonElement root)
    {
        envelopeConfiguration.SNSMetadata = new SNSMetadata
        {
            MessageId = GetJsonPropertyAsString(root, "MessageId"),
            TopicArn = GetJsonPropertyAsString(root, "TopicArn"),
            Subject = GetJsonPropertyAsString(root, "Subject"),
            UnsubscribeURL = GetJsonPropertyAsString(root, "UnsubscribeURL"),
            Timestamp = GetJsonPropertyAsDateTimeOffset(root, "Timestamp")
        };
        if (root.TryGetProperty("MessageAttributes", out var messageAttributes))
        {
            envelopeConfiguration.SNSMetadata.MessageAttributes = messageAttributes.Deserialize<Dictionary<string, Amazon.SimpleNotificationService.Model.MessageAttributeValue>>();
        }
    }

    private void SetEventBridgeMetadata(MessageEnvelopeConfiguration envelopeConfiguration, JsonElement root)
    {
        envelopeConfiguration.EventBridgeMetadata = new EventBridgeMetadata
        {
            EventId = GetJsonPropertyAsString(root, "id"),
            Source = GetJsonPropertyAsString(root, "source"),
            DetailType = GetJsonPropertyAsString(root, "detail-type"),
            Time = GetJsonPropertyAsDateTimeOffset(root, "time"),
            AWSAccount = GetJsonPropertyAsString(root, "account"),
            AWSRegion = GetJsonPropertyAsString(root, "region"),
            Resources = GetJsonPropertyAsList<string>(root, "resources")
        };
    }

    private string? GetJsonPropertyAsString(JsonElement node, string propertyName)
    {
        if (node.TryGetProperty(propertyName, out var propertyValue))
        {
            return propertyValue.GetString();
        }
        return null;
    }

    private DateTimeOffset GetJsonPropertyAsDateTimeOffset(JsonElement node, string propertyName)
    {
        if (node.TryGetProperty(propertyName, out var propertyValue))
        {
            return JsonSerializer.Deserialize<DateTimeOffset>(propertyValue);
        }
        return DateTimeOffset.MinValue;
    }

    private List<T>? GetJsonPropertyAsList<T>(JsonElement node, string propertyName)
    {
        if (node.TryGetProperty(propertyName, out var propertyValue))
        {
            return JsonSerializer.Deserialize<List<T>>(propertyValue);
        }
        return null;
    }

    private async ValueTask InvokePreSerializationCallback(MessageEnvelope messageEnvelope)
    {
        foreach (var serializationCallback in _messageConfiguration.SerializationCallbacks)
        {
            await serializationCallback.PreSerializationAsync(messageEnvelope);
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
