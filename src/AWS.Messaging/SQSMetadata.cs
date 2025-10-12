// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;

namespace AWS.Messaging
{
    /// <summary>
    /// Contains metadata related to Amazon SQS.
    /// </summary>
    public class SQSMetadata
    {
        /// <summary>
        /// The unique identifier for the SQS message.
        /// </summary>
        public string? MessageID { get; set; }

        /// <summary>
        /// The ReceiptHandle is returned when messages are receieved from SQS. The ReceiptHandle is required to delete messages or extend the message's visibility timeout.
        /// </summary>
        public string? ReceiptHandle { get; set; }

        /// <summary>
        /// Specifies the token used for de-duplication of sent messages. This parameter applies only to FIFO (first-in-first-out) queues.
        /// </summary>
        public string? MessageDeduplicationId { get; set; }

        /// <summary>
        /// The tag that specifies that a message belongs to a specific message group. This parameter applies only to FIFO (first-in-first-out) queues.
        /// </summary>
        public string? MessageGroupId { get; set; }

        /// <summary>
        /// The time at which the message was sent to the queue.
        /// </summary>
        public DateTimeOffset? SentTimestamp { get; set; }

        /// <summary>
        /// Each message attribute consists of a Name, Type, and Value.For more information, see Amazon SQS message attributes (https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-message-metadata.html#sqs-message-attributes)
        /// </summary>
        public Dictionary<string, MessageAttributeValue>? MessageAttributes { get; set; }
    }
}
