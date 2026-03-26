// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Services;

namespace AWS.Messaging.Publishers.SQS
{
    /// <summary>
    /// This interface allows sending messages from application code to Amazon SQS.
    /// It exposes the <see cref="SendAsync{T}(T, SQSOptions?, CancellationToken)"/> method which takes in a user-defined message, and <see cref="SQSOptions"/> to set additional parameters while sending messages to SQS.
    /// It also exposes <see cref="SendBatchAsync{T}(IEnumerable{T}, SQSOptions?, CancellationToken)"/> and
    /// <see cref="SendBatchAsync{T}(IEnumerable{SQSBatchEntry{T}}, SQSOptions?, CancellationToken)"/> methods
    /// for sending up to multiple messages in batches using the SQS SendMessageBatch API.
    /// Using dependency injection, this interface is available to inject anywhere in the code.
    /// </summary>
    public interface ISQSPublisher : ICommandPublisher
    {
        /// <summary>
        /// Sends the application message to SQS.
        /// </summary>
        /// <param name="message">The application message that will be serialized and sent to an SQS queue</param>
        /// <param name="sqsOptions">Contains additional parameters that can be set while sending a message to an SQS queue</param>
        /// <param name="token">The cancellation token used to cancel the request.</param>
        Task<SQSSendResponse> SendAsync<T>(T message, SQSOptions? sqsOptions, CancellationToken token = default);

        /// <summary>
        /// Sends a batch of application messages to SQS using the SendMessageBatch API.
        /// All messages share the same <see cref="SQSOptions"/>.
        /// Messages are automatically chunked into groups of 10 (the SQS maximum per batch request).
        /// </summary>
        /// <typeparam name="T">The .NET type of the application message.</typeparam>
        /// <param name="messages">The application messages that will be serialized and sent to an SQS queue.</param>
        /// <param name="sqsOptions">Contains additional parameters that apply to all messages in the batch, including
        /// <see cref="SQSOptions.QueueUrl"/> and <see cref="SQSOptions.OverrideClient"/> as well as
        /// message-level properties like <see cref="SQSOptions.MessageGroupId"/>.</param>
        /// <param name="token">The cancellation token used to cancel the request.</param>
        /// <returns>A <see cref="SQSSendBatchResponse"/> containing the results for each message in the batch.</returns>
        Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<T> messages, SQSOptions? sqsOptions = null, CancellationToken token = default);

        /// <summary>
        /// Sends a batch of application messages to SQS using the SendMessageBatch API,
        /// with per-message <see cref="SQSOptions"/> allowing different options (e.g. <see cref="SQSOptions.MessageGroupId"/>)
        /// for each message in the batch.
        /// Messages are automatically chunked into groups of 10 (the SQS maximum per batch request).
        /// </summary>
        /// <typeparam name="T">The .NET type of the application message.</typeparam>
        /// <param name="entries">The batch entries, each containing a message and optional per-message SQS options.</param>
        /// <param name="sqsOptions">Contains batch-level parameters such as <see cref="SQSOptions.QueueUrl"/>
        /// and <see cref="SQSOptions.OverrideClient"/>. Message-level properties on this parameter serve as
        /// defaults and are overridden by per-entry options when specified.</param>
        /// <param name="token">The cancellation token used to cancel the request.</param>
        /// <returns>A <see cref="SQSSendBatchResponse"/> containing the results for each message in the batch.</returns>
        Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<SQSBatchEntry<T>> entries, SQSOptions? sqsOptions = null, CancellationToken token = default);
    }
}
