// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Configuration.Internal;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services.Backoff.Policies.Options;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Wraps an existing <see cref="IMessageConfiguration"/> but restricts subscriber mapping resolution
/// to a single configured subscriber mapping. This is used to create a poller instance that only
/// processes one message type.
/// </summary>
internal class SingleTypeMessageConfiguration : IMessageConfiguration
{
    private readonly IMessageConfiguration _inner;
    private readonly SubscriberMapping _subscriberMapping;

    /// <summary>
    /// Construct an instance of <see cref="SingleTypeMessageConfiguration"/>
    /// </summary>
    public SingleTypeMessageConfiguration(
        IMessageConfiguration inner,
        SubscriberMapping subscriberMapping)
    {
        _inner = inner;
        _subscriberMapping = subscriberMapping;
    }

    /// <inheritdoc/>
    public IList<PublisherMapping> PublisherMappings => _inner.PublisherMappings;

    /// <inheritdoc/>
    public PublisherMapping? GetPublisherMapping(Type messageType) => _inner.GetPublisherMapping(messageType);

    /// <inheritdoc/>
    public IList<SubscriberMapping> SubscriberMappings => new[] { _subscriberMapping };

    /// <inheritdoc/>
    public SubscriberMapping? GetSubscriberMapping(Type messageType)
        => messageType == _subscriberMapping.MessageType ? _subscriberMapping : null;

    /// <inheritdoc/>
    public SubscriberMapping? GetSubscriberMapping(string messageTypeIdentifier)
        => string.Equals(messageTypeIdentifier, _subscriberMapping.MessageTypeIdentifier, StringComparison.Ordinal)
            ? _subscriberMapping
            : null;

    /// <inheritdoc/>
    public IList<SubscriberMiddleware> SubscriberMiddleware => _inner.SubscriberMiddleware;

    /// <inheritdoc/>
    public IList<IMessagePollerConfiguration> MessagePollerConfigurations
    {
        get => _inner.MessagePollerConfigurations;
        set => _inner.MessagePollerConfigurations = value;
    }

    /// <inheritdoc/>
    public SerializationOptions SerializationOptions => _inner.SerializationOptions;

    /// <inheritdoc/>
    public IList<ISerializationCallback> SerializationCallbacks => _inner.SerializationCallbacks;

    /// <inheritdoc/>
    public string? Source
    {
        get => _inner.Source;
        set => _inner.Source = value;
    }

    /// <inheritdoc/>
    public string? SourceSuffix
    {
        get => _inner.SourceSuffix;
        set => _inner.SourceSuffix = value;
    }

    /// <inheritdoc/>
    public bool LogMessageContent
    {
        get => _inner.LogMessageContent;
        set => _inner.LogMessageContent = value;
    }

    /// <inheritdoc/>
    public BackoffPolicy BackoffPolicy
    {
        get => _inner.BackoffPolicy;
        set => _inner.BackoffPolicy = value;
    }

    /// <inheritdoc/>
    public IntervalBackoffOptions IntervalBackoffOptions => _inner.IntervalBackoffOptions;

    /// <inheritdoc/>
    public CappedExponentialBackoffOptions CappedExponentialBackoffOptions => _inner.CappedExponentialBackoffOptions;

    /// <inheritdoc/>
    public PollingControlToken PollingControlToken => _inner.PollingControlToken;
}
