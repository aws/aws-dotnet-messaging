// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// The response for an SQS SendMessageBatch action.
/// Contains lists of successfully sent and failed message entries.
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
