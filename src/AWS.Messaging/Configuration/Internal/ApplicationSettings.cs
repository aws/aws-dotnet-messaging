// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Configuration.Internal;

/// <summary>
/// Represents the configuration of the Message Processing Framework.
/// </summary>
public class ApplicationSettings
{
    /// <summary>
    /// The name of the section representing the configuration
    /// of Message Processing Framework.
    /// </summary>
    public const string SectionName = "AWS.Messaging";

    /// <summary>
    /// The list of configured SQS Publishers.
    /// </summary>
    public ICollection<SQSPublisher> SQSPublishers { get; set; } = default!;

    /// <summary>
    /// The list of configured SNS Publishers.
    /// </summary>
    public ICollection<SNSPublisher> SNSPublishers { get; set; } = default!;

    /// <summary>
    /// The list of configured EventBridge Publishers.
    /// </summary>
    public ICollection<EventBridgePublisher> EventBridgePublishers { get; set; } = default!;

    /// <summary>
    /// The list of configured message handlers.
    /// </summary>
    public ICollection<MessageHandler> MessageHandlers { get; set; } = default!;

    /// <summary>
    /// The list of configured SQS Pollers.
    /// </summary>
    public ICollection<SQSPoller> SQSPollers { get; set; } = default!;

    /// <summary>
    /// Represents an SQS publisher configuration.
    /// </summary>
    public class SQSPublisher
    {
        /// <summary>
        /// The fully qualified name of the .NET message type.
        /// </summary>
        public string MessageType { get; set; } = default!;
        /// <summary>
        /// The SQS queue URL that this message will be published to.
        /// </summary>
        public string QueueUrl { get; set; } = default!;
        /// <summary>
        /// The language agnostic message type identifier.
        /// </summary>
        public string? MessageTypeIdentifier { get; set; }
    }

    /// <summary>
    /// Represents an SNS publisher configuration.
    /// </summary>
    public class SNSPublisher
    {
        /// <summary>
        /// The fully qualified name of the .NET message type.
        /// </summary>
        public string MessageType { get; set; } = default!;
        /// <summary>
        /// The SNS topic URL that this message will be published to.
        /// </summary>
        public string TopicUrl { get; set; } = default!;
        /// <summary>
        /// The language agnostic message type identifier.
        /// </summary>
        public string? MessageTypeIdentifier { get; set; }
    }

    /// <summary>
    /// Represents an EventBridge publisher configuration.
    /// </summary>
    public class EventBridgePublisher
    {
        /// <summary>
        /// The fully qualified name of the .NET message type.
        /// </summary>
        public string MessageType { get; set; } = default!;
        /// <summary>
        /// The EventBridge event bus name that this message will be published to.
        /// </summary>
        public string EventBusName { get; set; } = default!;
        /// <summary>
        /// The language agnostic message type identifier.
        /// </summary>
        public string? MessageTypeIdentifier { get; set; }
        /// <summary>
        /// Contains additional properties that can be set while configuring a EventBridge publisher.
        /// </summary>
        public EventBridgePublishOptions? Options { get; set; } = default!;
    }

    /// <summary>
    /// Represents a message handler configuration.
    /// </summary>
    public class MessageHandler
    {
        /// <summary>
        /// The fully qualified name of the .NET message handler type.
        /// </summary>
        public string HandlerType { get; set; } = default!;
        /// <summary>
        /// The fully qualified name of the .NET message type.
        /// </summary>
        public string MessageType { get; set; } = default!;
        /// <summary>
        /// The language agnostic message type identifier.
        /// </summary>
        public string? MessageTypeIdentifier { get; set; }
    }

    /// <summary>
    /// Represents an SQS poller configuration.
    /// </summary>
    public class SQSPoller
    {
        /// <summary>
        /// The SQS queue URL that this message will be published to.
        /// </summary>
        public string QueueUrl { get; set; } = default!;
        /// <summary>
        /// Configuration for polling messages from SQS
        /// </summary>
        public SQSMessagePollerOptions? Options { get; set; } = default!;
    }
}
