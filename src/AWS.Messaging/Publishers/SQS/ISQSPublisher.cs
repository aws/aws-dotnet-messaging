// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Services;

namespace AWS.Messaging.Publishers.SQS
{
    /// <summary>
    /// This interface allows sending messages from application code to Amazon SQS.
    /// It exposes the <see cref="SendAsync{T}(T, SQSOptions?, CancellationToken)"/> method which takes in a user-defined message, and <see cref="SQSOptions"/> to set additional parameters while sending messages to SQS.
    /// It also exposes <see cref="SendBatchAsync{T}(IEnumerable{T}, CancellationToken)"/> and
    /// <see cref="SendBatchAsync{T}(IEnumerable{SQSBatchEntry{T}}, SQSBatchOptions?, CancellationToken)"/> methods
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
        /// Messages are automatically chunked into groups of 10 (the SQS maximum per batch request).
        /// <para>
        /// This is a convenience overload for sending simple batches where no per-message options
        /// are needed. For FIFO queues or when per-message options are required, use
        /// <see cref="SendBatchAsync{T}(IEnumerable{SQSBatchEntry{T}}, SQSBatchOptions?, CancellationToken)"/> instead.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The .NET type of the application message.</typeparam>
        /// <param name="messages">The application messages that will be serialized and sent to an SQS queue.</param>
        /// <param name="token">The cancellation token used to cancel the request.</param>
        /// <returns>A <see cref="SQSSendBatchResponse"/> containing the results for each message in the batch.</returns>
        Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<T> messages, CancellationToken token = default);

        /// <summary>
        /// Sends a batch of application messages to SQS using the SendMessageBatch API,
        /// with per-message <see cref="SQSMessageOptions"/> allowing different options (e.g. <see cref="SQSMessageOptions.MessageGroupId"/>)
        /// for each message in the batch.
        /// Messages are automatically chunked into groups of 10 (the SQS maximum per batch request).
        /// </summary>
        /// <typeparam name="T">The .NET type of the application message.</typeparam>
        /// <param name="entries">The batch entries, each containing a message and optional per-message options.</param>
        /// <param name="batchOptions">Optional batch-level parameters such as <see cref="SQSBatchOptions.QueueUrl"/>
        /// and <see cref="SQSBatchOptions.OverrideClient"/>.</param>
        /// <param name="token">The cancellation token used to cancel the request.</param>
        /// <returns>A <see cref="SQSSendBatchResponse"/> containing the results for each message in the batch.</returns>
        Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<SQSBatchEntry<T>> entries, SQSBatchOptions? batchOptions = null, CancellationToken token = default);
    }
}
