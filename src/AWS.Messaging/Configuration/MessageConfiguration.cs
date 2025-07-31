// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Configuration.Internal;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services.Backoff.Policies.Options;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Implementation of <see cref="IMessageConfiguration"/>.
/// </summary>
public class MessageConfiguration : IMessageConfiguration
{
    /// <inheritdoc/>
    public IList<PublisherMapping> PublisherMappings { get; } = new List<PublisherMapping>();

    /// <inheritdoc/>
    public PublisherMapping? GetPublisherMapping(Type messageType)
    {
        var publisherMapping = PublisherMappings.FirstOrDefault(x => messageType == x.MessageType);
        return publisherMapping;
    }

    /// <inheritdoc/>
    public IList<SubscriberMapping> SubscriberMappings { get; } = new List<SubscriberMapping>();

    /// <inheritdoc/>
    public SubscriberMapping? GetSubscriberMapping(Type messageType)
    {
        var subscriberMapping = SubscriberMappings.FirstOrDefault(x => messageType == x.MessageType);
        return subscriberMapping;
    }

    /// <inheritdoc/>
    public SubscriberMapping? GetSubscriberMapping(string messageTypeIdentifier)
    {
        var subscriberMapping = SubscriberMappings.FirstOrDefault(x => messageTypeIdentifier == x.MessageTypeIdentifier);
        return subscriberMapping;
    }

    /// <inheritdoc/>
    public IList<SubscriberMiddleware> SubscriberMiddleware { get; } = new List<SubscriberMiddleware>();

    /// <inheritdoc/>
    public IList<IMessagePollerConfiguration> MessagePollerConfigurations { get; set; } = new List<IMessagePollerConfiguration>();

    /// <inheritdoc/>
    public SerializationOptions SerializationOptions { get; } = new SerializationOptions();

    /// <inheritdoc/>
    public IList<ISerializationCallback> SerializationCallbacks { get; } = new List<ISerializationCallback>();

    /// <inheritdoc/>
    public string? Source { get; set; }

    /// <inheritdoc/>
    public string? SourceSuffix { get; set; }

    /// <inheritdoc/>
    public bool LogMessageContent { get; set; }

    /// <inheritdoc/>
    public BackoffPolicy BackoffPolicy { get; set; } = BackoffPolicy.CappedExponential;

    /// <inheritdoc/>
    public IntervalBackoffOptions IntervalBackoffOptions { get; set; } = new();

    /// <inheritdoc/>
    public CappedExponentialBackoffOptions CappedExponentialBackoffOptions { get; set; } = new();

    /// <inheritdoc/>
    public PollingControlToken PollingControlToken { get; set; } = new();
}
