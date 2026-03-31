// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS;

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// Contains batch-level properties that apply to an entire batch send operation to SQS.
/// <para>
/// This class is used on the <see cref="ISQSPublisher.SendBatchAsync{T}(IEnumerable{SQSBatchEntry{T}}, SQSBatchOptions?, CancellationToken)"/>
/// method to override the queue URL or SQS client for the batch. Per-message properties such as
/// <see cref="SQSMessageOptions.MessageGroupId"/> should be set on each <see cref="SQSBatchEntry{T}.Options"/> instead.
/// </para>
/// </summary>
public class SQSBatchOptions
{
    /// <summary>
    /// The SQS queue URL which the publisher will use for the batch. This can be used to override the queue URL
    /// that is configured for a given message type when publishing a batch.
    /// </summary>
    public string? QueueUrl { get; set; }

    /// <summary>
    /// An alternative SQS client that can be used to publish this batch,
    /// instead of the client provided by the registered <see cref="Configuration.IAWSClientProvider"/> implementation.
    /// </summary>
    public IAmazonSQS? OverrideClient { get; set; }
}
