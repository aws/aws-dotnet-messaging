// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// The response for an SQS SendMessageBatch action.
/// Every entry passed to SendBatchAsync appears, by its batch entry Id, in exactly one of
/// <see cref="Successful"/>, <see cref="Failed"/> or <see cref="Unprocessed"/>.
/// </summary>
public class SQSSendBatchResponse
{
    /// <summary>
    /// The list of successfully sent messages in the batch.
    /// </summary>
    public List<SQSSendBatchResponseEntry> Successful { get; set; } = new();

    /// <summary>
    /// The list of messages that failed to send in the batch.
    /// </summary>
    public List<SQSSendBatchResponseFailedEntry> Failed { get; set; } = new();

    /// <summary>
    /// The messages in the batch that have no result from SQS, each with the <see cref="SQSSendBatchUnprocessedReason"/>
    /// that tells whether it may have reached SQS.
    /// <para>
    /// When a SendMessageBatch request throws, this list on <see cref="FailedToPublishBatchException.PartialResponse"/> holds
    /// the messages of the earlier requests that SQS left out of its responses as <see cref="SQSSendBatchUnprocessedReason.NotReported"/>,
    /// followed by the messages of the request that threw as <see cref="SQSSendBatchUnprocessedReason.OutcomeUnknown"/>, followed by
    /// the messages of the later requests, which were never sent, as <see cref="SQSSendBatchUnprocessedReason.NotSent"/>.
    /// </para>
    /// <para>
    /// This list can also be filled when SendBatchAsync returns normally: a message that SQS left out of its response
    /// is added as <see cref="SQSSendBatchUnprocessedReason.NotReported"/>.
    /// </para>
    /// </summary>
    public List<SQSSendBatchResponseUnprocessedEntry> Unprocessed { get; set; } = new();
}

/// <summary>
/// Represents a successfully sent message in a batch response.
/// </summary>
public class SQSSendBatchResponseEntry
{
    /// <summary>
    /// The correlation identifier for this entry, matching the <c>Id</c> used in the batch request entry.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// An identifier for the message assigned by SQS.
    /// <para>
    /// For more information, see <a href="https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-queue-message-identifiers.html">Queue
    /// and Message Identifiers</a> in the <i>Amazon SQS Developer Guide</i>.
    /// </para>
    /// </summary>
    public string? MessageId { get; set; }
}

/// <summary>
/// Represents a message that failed to send in a batch response.
/// </summary>
public class SQSSendBatchResponseFailedEntry
{
    /// <summary>
    /// The correlation identifier for this entry, matching the <c>Id</c> used in the batch request entry.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// An error code representing why the action failed on this entry.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// A message explaining why the action failed on this entry.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Specifies whether the error happened due to the caller of the batch API action.
    /// </summary>
    public bool SenderFault { get; set; }
}

/// <summary>
/// Represents a message in a batch response that has no result from SQS.
/// </summary>
public class SQSSendBatchResponseUnprocessedEntry
{
    /// <summary>
    /// The correlation identifier for this entry, matching the <c>Id</c> used in the batch request entry.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Why the message has no result, which tells whether it may have reached SQS.
    /// </summary>
    public SQSSendBatchUnprocessedReason Reason { get; set; }
}

/// <summary>
/// The reason a message appears in <see cref="SQSSendBatchResponse.Unprocessed"/>.
/// </summary>
public enum SQSSendBatchUnprocessedReason
{
    /// <summary>
    /// The message may or may not have reached SQS. It was in a SendMessageBatch request that threw.
    /// <para>
    /// A request that threw may still have been delivered to SQS (for example, when it timed out after SQS accepted it),
    /// or SQS may have rejected the whole request. Sending the message again can deliver it twice. On a FIFO queue, SQS drops
    /// the copy only if the entry keeps the same explicit <see cref="SQSMessageOptions.MessageDeduplicationId"/> and is sent
    /// again within the 5-minute deduplication interval. Content-based deduplication does not help, because every send creates
    /// a new message envelope, so the message body differs.
    /// </para>
    /// </summary>
    OutcomeUnknown,

    /// <summary>
    /// The message was never sent to SQS. It was in a later SendMessageBatch request that was not sent because
    /// an earlier request threw.
    /// <para>
    /// Sending the message again cannot create a duplicate.
    /// </para>
    /// </summary>
    NotSent,

    /// <summary>
    /// The message may or may not have reached SQS. SQS accepted the SendMessageBatch request it was in, but left
    /// the message out of its response.
    /// <para>
    /// SQS reports every message of a request it accepts, so this is rare. Sending the message again can deliver it
    /// twice, as with <see cref="OutcomeUnknown"/>.
    /// </para>
    /// </summary>
    NotReported
}
