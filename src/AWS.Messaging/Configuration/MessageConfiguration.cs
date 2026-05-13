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
    // Dictionary caches for O(1) lookups, built lazily on first query.
    // Mappings are only added during startup (MessageBusBuilder.Build) and
    // never modified at runtime, so lazy initialization is safe.
    private Dictionary<Type, PublisherMapping>? _publisherMappingsByType;
    private Dictionary<Type, SubscriberMapping>? _subscriberMappingsByType;
    private Dictionary<string, SubscriberMapping>? _subscriberMappingsById;

    /// <inheritdoc/>
    public IList<PublisherMapping> PublisherMappings { get; } = new List<PublisherMapping>();

    /// <inheritdoc/>
    public PublisherMapping? GetPublisherMapping(Type messageType)
    {
        _publisherMappingsByType ??= BuildPublisherIndex();
        _publisherMappingsByType.TryGetValue(messageType, out var mapping);
        return mapping;
    }

    /// <inheritdoc/>
    public IList<SubscriberMapping> SubscriberMappings { get; } = new List<SubscriberMapping>();

    /// <inheritdoc/>
    public SubscriberMapping? GetSubscriberMapping(Type messageType)
    {
        _subscriberMappingsByType ??= BuildSubscriberTypeIndex();
        _subscriberMappingsByType.TryGetValue(messageType, out var mapping);
        return mapping;
    }

    /// <inheritdoc/>
    public SubscriberMapping? GetSubscriberMapping(string messageTypeIdentifier)
    {
        _subscriberMappingsById ??= BuildSubscriberIdIndex();
        _subscriberMappingsById.TryGetValue(messageTypeIdentifier, out var mapping);
        return mapping;
    }

    private Dictionary<Type, PublisherMapping> BuildPublisherIndex()
    {
        var dict = new Dictionary<Type, PublisherMapping>(PublisherMappings.Count);
        foreach (var mapping in PublisherMappings)
        {
            dict.TryAdd(mapping.MessageType, mapping);
        }
        return dict;
    }

    private Dictionary<Type, SubscriberMapping> BuildSubscriberTypeIndex()
    {
        var dict = new Dictionary<Type, SubscriberMapping>(SubscriberMappings.Count);
        foreach (var mapping in SubscriberMappings)
        {
            dict.TryAdd(mapping.MessageType, mapping);
        }
        return dict;
    }

    private Dictionary<string, SubscriberMapping> BuildSubscriberIdIndex()
    {
        var dict = new Dictionary<string, SubscriberMapping>(SubscriberMappings.Count, StringComparer.Ordinal);
        foreach (var mapping in SubscriberMappings)
        {
            dict.TryAdd(mapping.MessageTypeIdentifier, mapping);
        }
        return dict;
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
