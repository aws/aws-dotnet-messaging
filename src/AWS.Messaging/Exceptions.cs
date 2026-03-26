// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Configuration;
using AWS.Messaging.Publishers.EventBridge;
using AWS.Messaging.Serialization;
using Microsoft.Extensions.Configuration;

namespace AWS.Messaging;

/// <summary>
/// A wrapper for the exceptions thrown by AWS Messaging.
/// </summary>
public abstract class AWSMessagingException : Exception
{
    /// <summary>
    /// Creates an instance of <see cref="AWSMessagingException"/>.
    /// </summary>
    public AWSMessagingException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the message type full name cannot be retrieved.
/// </summary>
public class InvalidMessageTypeException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidMessageTypeException"/>.
    /// </summary>
    public InvalidMessageTypeException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the publisher endpoint is not valid.
/// </summary>
public class InvalidPublisherEndpointException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidPublisherEndpointException"/>.
    /// </summary>
    public InvalidPublisherEndpointException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// An exception thrown with the configuration for messaging is invalid.
/// </summary>
public class ConfigurationException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="AWS.Messaging.ConfigurationException" />
    /// </summary>
    public ConfigurationException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the publisher type full name cannot be retrieved.
/// </summary>
public class InvalidPublisherTypeException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidPublisherTypeException"/>.
    /// </summary>
    public InvalidPublisherTypeException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the a publisher mapping cannot be found for a specific message type.
/// </summary>
public class MissingMessageTypeConfigurationException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="MissingMessageTypeConfigurationException"/>.
    /// </summary>
    public MissingMessageTypeConfigurationException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if a publisher type outside of <see cref="PublisherTargetType"/> is provided.
/// </summary>
public class UnsupportedPublisherException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="UnsupportedPublisherException"/>.
    /// </summary>
    public UnsupportedPublisherException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the subscriber endpoint is not valid.
/// </summary>
public class InvalidSubscriberEndpointException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidSubscriberEndpointException"/>.
    /// </summary>
    public InvalidSubscriberEndpointException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if failed to deserialize the application message while invoking <see cref="IMessageSerializer.Deserialize(string, Type)"/>
/// </summary>
public class FailedToDeserializeApplicationMessageException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="FailedToDeserializeApplicationMessageException"/>.
    /// </summary>
    public FailedToDeserializeApplicationMessageException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if failed to serialize the application message while invoking <see cref="IMessageSerializer.Serialize(object)"/>
/// </summary>
public class FailedToSerializeApplicationMessageException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="FailedToSerializeApplicationMessageException"/>.
    /// </summary>
    public FailedToSerializeApplicationMessageException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if an exception occurs while publishing the message.
/// </summary>
public class FailedToPublishException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="FailedToPublishException"/>.
    /// </summary>
    public FailedToPublishException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the message being sent is invalid.
/// </summary>
public class InvalidMessageException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidMessageException"/>.
    /// </summary>
    public InvalidMessageException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if failed to create a <see cref="MessageEnvelope"/>
/// </summary>
public class FailedToCreateMessageEnvelopeException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="FailedToCreateMessageEnvelopeException"/>
    /// </summary>
    public FailedToCreateMessageEnvelopeException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if failed to serialize a <see cref="MessageEnvelope"/>
/// </summary>
public class FailedToSerializeMessageEnvelopeException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="FailedToSerializeMessageEnvelopeException"/>
    /// </summary>
    public FailedToSerializeMessageEnvelopeException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if failed to create a <see cref="MessageEnvelopeConfiguration"/> instance.
/// </summary>
public class FailedToCreateMessageEnvelopeConfigurationException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="FailedToCreateMessageEnvelopeConfigurationException"/>
    /// </summary>
    public FailedToCreateMessageEnvelopeConfigurationException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown when attempting to perform an SQS operation on a message without a valid <see cref="MessageEnvelope.SQSMetadata"/>
/// </summary>
public class MissingSQSMetadataException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="MissingSQSMetadataException"/>.
    /// </summary>
    public MissingSQSMetadataException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an <see cref="SQSMessagePollerOptions" /> is configured with one or more invalid values
/// </summary>
public class InvalidSQSMessagePollerOptionsException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidSQSMessagePollerOptionsException"/>.
    /// </summary>
    public InvalidSQSMessagePollerOptionsException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown when the backoff policy options are configured with one or more invalid values.
/// </summary>
public class InvalidBackoffOptionsException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidBackoffOptionsException"/>.
    /// </summary>
    public InvalidBackoffOptionsException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown during message handling if unable to find an <see cref="IMessageHandler{T}.HandleAsync(MessageEnvelope{T}, CancellationToken)"/>
/// method on the handler type for a given message
/// </summary>
public class InvalidMessageHandlerSignatureException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidMessageHandlerSignatureException"/>.
    /// </summary>
    public InvalidMessageHandlerSignatureException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if could not retrieve the AWS service client from the DI container.
/// </summary>
public class FailedToFindAWSServiceClientException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidMessageHandlerSignatureException"/>.
    /// </summary>
    public FailedToFindAWSServiceClientException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the provided message handler is not valid for the provided message type.
/// </summary>
public class InvalidMessageHandlerTypeException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidMessageHandlerTypeException"/>.
    /// </summary>
    public InvalidMessageHandlerTypeException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if the provided <see cref="IConfiguration"/> is invalid.
/// </summary>
public class InvalidAppSettingsConfigurationException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidAppSettingsConfigurationException"/>.
    /// </summary>
    public InvalidAppSettingsConfigurationException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an invalid SQS queue ARN is encountered.
/// </summary>
public class InvalidSQSQueueArnException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidSQSQueueArnException"/>.
    /// </summary>
    public InvalidSQSQueueArnException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown when the publish request to a FIFO endpoint is invalid.
/// </summary>
public class InvalidFifoPublishingRequestException : AWSMessagingException
{
    /// <summary>
    /// Creates an instance of <see cref="InvalidFifoPublishingRequestException"/>.
    /// </summary>
    public InvalidFifoPublishingRequestException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Thrown if an exception occurs while publishing a batch of messages.
/// Contains partial results from chunks that may have already succeeded before the failure.
/// </summary>
public class FailedToPublishBatchException : FailedToPublishException
{
    /// <summary>
    /// The partial response containing results from batch chunks that were sent before the failure occurred.
    /// Earlier chunks may have succeeded even though a later chunk failed.
    /// </summary>
    public Publishers.SQS.SQSSendBatchResponse? PartialResponse { get; }

    /// <summary>
    /// Creates an instance of <see cref="FailedToPublishBatchException"/>.
    /// </summary>
    public FailedToPublishBatchException(string message, Exception? innerException = null, Publishers.SQS.SQSSendBatchResponse? partialResponse = null)
        : base(message, innerException)
    {
        PartialResponse = partialResponse;
    }
}

/// <summary>
/// Thrown if the <see cref="EventBridgePublishResponse"/> contains a message with an error code
/// </summary>
public class EventBridgePutEventsException : AWSMessagingException
{
    /// <summary>
    /// The error code from the EventBridge SDK
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Creates an instance of <see cref="EventBridgePutEventsException"/>.
    /// </summary>
    public EventBridgePutEventsException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}

