// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.\r
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Configuration;

/// <summary>
/// Internal configuration for polling a single type of messages from SQS
/// </summary>
public class SingleTypeSQSMessagePollerConfiguration : SQSMessagePollerConfiguration
{
    /// <summary>
    /// Construct an instance of <see cref="SingleTypeSQSMessagePollerConfiguration" />
    /// </summary>
    /// <param name="queueUrl">The SQS QueueUrl to poll messages from.</param>
    /// <param name="singleMessageType">The single message type to poll for.</param>
    public SingleTypeSQSMessagePollerConfiguration(string queueUrl, Type singleMessageType) : base(queueUrl)
    {
        SingleMessageType = singleMessageType;
    }

    /// <summary>
    /// Th poller will only attempt to process messages for the specified message type.
    /// This is useful for queues that are dedicated to a single message type or when enabling raw JSON ingestion
    /// without requiring type-based routing.
    /// </summary>
    public Type SingleMessageType { get; init; }

    /// <summary>
    /// Message type identifier to use for the single message type poller.
    /// If not set, the framework will use the configured subscriber mapping for <see cref="SingleMessageType"/>.
    /// </summary>
    internal string? SingleMessageTypeIdentifier { get; init; }

    /// <summary>
    /// Controls how inbound messages are interpreted when this poller is configured for a single message type.
    /// When true (default), inbound messages are expected to be in the CloudEvents envelope format.
    /// When false, inbound messages are expected to be a raw payload of the single message type.
    /// </summary>
    internal bool UsesMessageEnvelope { get; init; } = true;
}
