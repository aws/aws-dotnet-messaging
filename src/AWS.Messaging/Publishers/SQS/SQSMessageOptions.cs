// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// Contains per-message properties that can be set when sending individual messages
/// within a batch to an SQS queue.
/// <para>
/// This class is used on <see cref="SQSBatchEntry{T}.Options"/> to specify message-level
/// settings such as <see cref="MessageGroupId"/> and <see cref="MessageDeduplicationId"/>.
/// </para>
/// </summary>
public class SQSMessageOptions
{
    /// <summary>
    /// The length of time, in seconds, for which to delay a specific message.
    /// Its valid values are between 0 to 900.
    /// Messages with a positive DelaySeconds value become available for processing after the delay period is finished.
    /// If you don't specify a value, the default value for the queue applies.
    /// When you set FifoQueue, you can't set DelaySeconds per message. You can set this parameter only on a queue level.
    /// </summary>
    public int? DelaySeconds { get; set; }

    /// <summary>
    /// Each message attribute consists of a Name, Type, and Value.
    /// For more information, see <see href="https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-message-metadata.html#sqs-message-attributes">the Amazon SQS developer guide.</see>
    /// </summary>
    public Dictionary<string, MessageAttributeValue>? MessageAttributes { get; set; }

    /// <summary>
    /// This parameter applies only to FIFO(first-in-first-out) queues and is used for deduplication of sent messages.
    /// If a message with a particular MessageDeduplicationId is sent successfully, any messages sent with the same
    /// MessageDeduplicationId are accepted successfully but aren't delivered during the 5-minute deduplication interval.
    /// For more information, see <see href="https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/FIFO-queues-exactly-once-processing.html">Exactly-once processing</see>
    /// in the Amazon SQS Developer Guide.
    /// <para>
    /// Each message in a batch should have a unique MessageDeduplicationId. Setting the same deduplication ID
    /// across multiple messages in a batch would incorrectly indicate they are duplicates of each other.
    /// </para>
    /// </summary>
    public string? MessageDeduplicationId { get; set; }

    /// <summary>
    /// This parameter applies only to FIFO(first-in-first-out) queues and specifies that a message belongs to a specific message group.
    /// Messages that belong to the same message group are processed in a FIFO manner
    /// (however, messages in different message groups might be processed out of order).
    /// To interleave multiple ordered streams within a single queue, use MessageGroupId values
    /// (for example, session data for multiple users).
    /// </summary>
    public string? MessageGroupId { get; set; }
}
