// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// Represents a single entry in a batch send operation to SQS.
/// Pairs an application message with optional per-message <see cref="SQSOptions"/>.
/// </summary>
/// <typeparam name="T">The .NET type of the application message.</typeparam>
public class SQSBatchEntry<T>
{
    /// <summary>
    /// Creates an instance of <see cref="SQSBatchEntry{T}"/>.
    /// </summary>
    public SQSBatchEntry()
    {
    }

    /// <summary>
    /// Creates an instance of <see cref="SQSBatchEntry{T}"/> with the specified message and options.
    /// </summary>
    /// <param name="message">The application message to send.</param>
    /// <param name="options">Optional per-message SQS options.</param>
    public SQSBatchEntry(T message, SQSOptions? options = null)
    {
        Message = message;
        Options = options;
    }

    /// <summary>
    /// The application message that will be serialized and sent to an SQS queue.
    /// </summary>
    public T Message { get; set; } = default!;

    /// <summary>
    /// Optional per-message SQS options such as <see cref="SQSOptions.MessageGroupId"/>,
    /// <see cref="SQSOptions.MessageDeduplicationId"/>, <see cref="SQSOptions.DelaySeconds"/>,
    /// and <see cref="SQSOptions.MessageAttributes"/>.
    /// <para>
    /// Note: <see cref="SQSOptions.QueueUrl"/> and <see cref="SQSOptions.OverrideClient"/> are only
    /// used from the shared <see cref="SQSOptions"/> parameter passed to
    /// <see cref="ISQSPublisher.SendBatchAsync{T}(IEnumerable{T}, SQSOptions?, CancellationToken)"/>.
    /// If set on individual entries, they will be ignored.
    /// </para>
    /// </summary>
    public SQSOptions? Options { get; set; }
}
