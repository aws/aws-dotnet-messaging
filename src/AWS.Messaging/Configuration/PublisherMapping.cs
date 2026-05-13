// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Configuration;

/// <summary>
/// This maps a message type to a message publisher.
/// </summary>
public class PublisherMapping
{
    /// <inheritdoc/>
    public string PublishTargetType { get; init; }

    /// <inheritdoc/>
    public Type MessageType { get; init; }

    /// <inheritdoc/>
    public string MessageTypeIdentifier { get; init; }

    /// <inheritdoc/>
    public IMessagePublisherConfiguration PublisherConfiguration { get; init; }

    /// <inheritdoc/>
    public Action<object, object>? ConfigureOptions { get; init; } = null;

    /// <summary>
    /// Creates a mapping object for the specified message type as well as the AWS service to publisher to.
    /// This object will be used internally by the framework to properly route the user-defined message.
    /// </summary>
    /// <param name="messageType">The Type of the message</param>
    /// <param name="publisherConfiguration">The publisher configuration</param>
    /// <param name="publishTargetType">The type of publisher to use</param>
    /// <param name="messageTypeIdentifier">The language-agnostic message type identifier. If not specified, the .NET type will be used.</param>
    /// <param name="configureOptions">An optional action to configure publisher-specific options.</param>
    public PublisherMapping(Type messageType, IMessagePublisherConfiguration publisherConfiguration, string publishTargetType, string? messageTypeIdentifier = null, Action<object, object>? configureOptions = null)
    {
        PublishTargetType = publishTargetType;
        MessageType = messageType;
        MessageTypeIdentifier =
            !string.IsNullOrEmpty(messageTypeIdentifier) ?
            messageTypeIdentifier :
            messageType.FullName ?? throw new InvalidMessageTypeException("Unable to retrieve the Full Name of the provided Message Type.");
        PublisherConfiguration = publisherConfiguration;
        ConfigureOptions = configureOptions;
    }
}
