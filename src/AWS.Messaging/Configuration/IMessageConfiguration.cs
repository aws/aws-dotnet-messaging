// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Configuration.Internal;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.Services.Backoff;
using AWS.Messaging.Services.Backoff.Policies;
using AWS.Messaging.Services.Backoff.Policies.Options;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Interface for the message configuration.
/// </summary>
public interface IMessageConfiguration
{
    /// <summary>
    /// Maps the message types to a publisher configuration
    /// </summary>
    IList<PublisherMapping> PublisherMappings { get; }

    /// <summary>
    /// Returns back the publisher mapping for the given message type.
    /// </summary>
    /// <param name="messageType">The Type of the message</param>
    /// <returns>The <see cref="PublisherMapping"/> containing routing info for the specified message type.</returns>
    PublisherMapping? GetPublisherMapping(Type messageType);

    /// <summary>
    /// Maps the message types to a subscriber configuration
    /// </summary>
    IList<SubscriberMapping> SubscriberMappings { get; }

    /// <summary>
    /// Returns back the subscriber mapping for the given message type.
    /// </summary>
    /// <param name="messageType">The Type of the message</param>
    /// <returns>The <see cref="SubscriberMapping"/> containing routing info for the specified message type.</returns>
    SubscriberMapping? GetSubscriberMapping(Type messageType);

    /// <summary>
    /// Returns back the subscriber mapping for the given message type identifier.
    /// </summary>
    /// <param name="messageTypeIdentifier">The language agnostic identifier for the application message</param>
    /// <returns>The <see cref="SubscriberMapping"/> containing routing info for the specified message type.</returns>
    SubscriberMapping? GetSubscriberMapping(string messageTypeIdentifier);

    /// <summary>
    /// Maps the middleware types to be used in order of execution.
    /// </summary>
    IList<SubscriberMiddleware> SubscriberMiddleware { get; }

    /// <summary>
    /// List of configurations for subscriber to poll for messages from an AWS service endpoint.
    /// </summary>
    IList<IMessagePollerConfiguration> MessagePollerConfigurations { get; set; }

    /// <summary>
    /// Holds an instance of <see cref="SerializationOptions"/> to control the serialization/de-serialization of the application message.
    /// </summary>
    SerializationOptions SerializationOptions { get; }

    /// <summary>
    /// Holds instances of <see cref="ISerializationCallback"/> that lets users inject their own metadata to incoming and outgoing messages.
    /// </summary>
    IList<ISerializationCallback> SerializationCallbacks { get; }

    /// <summary>
    /// The relative or absolute Uri to be used as a message source.
    /// This source is added globally to any message sent through the framework.
    /// </summary>
    string? Source { get; set; }

    /// <summary>
    /// A suffix to append to the user-defined <see cref="Source"/> or
    /// computed by the framework in the absence of a user-defined one.
    /// </summary>
    string? SourceSuffix { get; set; }

    /// <summary>
    /// Controls the visibility of data messages in the logging framework, exception handling and other areas.
    /// If this is enabled, messages sent by this framework will be visible in plain text across the framework's components.
    /// This means any sensitive user data sent by this framework will be visible in logs, any exceptions thrown and others.
    /// </summary>
    bool LogMessageContent { get; set; }

    /// <summary>
    /// Sets the Backoff Policy to be used with <see cref="BackoffHandler"/>.
    /// </summary>
    BackoffPolicy BackoffPolicy { get; set; }

    /// <summary>
    /// Holds an instance of <see cref="IntervalBackoffOptions"/> to control the behavior of <see cref="IntervalBackoffPolicy"/>.
    /// </summary>
    IntervalBackoffOptions IntervalBackoffOptions { get; }

    /// <summary>
    /// Holds an instance of <see cref="CappedExponentialBackoffOptions"/> to control the behavior of <see cref="CappedExponentialBackoffPolicy"/>.
    /// </summary>
    CappedExponentialBackoffOptions CappedExponentialBackoffOptions { get; }

    /// <summary>
    /// Holds an instance of <see cref="PollingControlToken"/> to control behaviour of <see cref="IMessagePoller"/>
    /// </summary>
    PollingControlToken PollingControlToken { get; }
}
