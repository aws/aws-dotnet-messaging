// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// Represents a single entry in a batch send operation to SQS.
/// Pairs an application message with optional per-message <see cref="SQSMessageOptions"/>.
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
    public SQSBatchEntry(T message, SQSMessageOptions? options = null)
    {
        Message = message;
        Options = options;
    }

    /// <summary>
    /// An optional identifier, chosen by the caller, used to correlate this entry with its result in
    /// <see cref="SQSSendBatchResponse"/>. When null, the framework uses the generated <see cref="MessageEnvelope.Id"/>,
    /// which callers cannot match back to their input.
    /// <para>
    /// The Id is sent to SQS unchanged; only null is replaced, so an empty string is sent as an empty Id.
    /// SQS requires it to be 1-80 characters long and to contain only ASCII letters, digits, hyphens and underscores,
    /// and rejects a request in which two entries share an Id.
    /// SQS does not detect a duplicate Id when the two entries land in different requests of 10: the Id then
    /// appears twice in the results, and the results cannot be told apart.
    /// Keep Ids unique within a single SendBatchAsync call so that every result matches exactly one entry.
    /// </para>
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// The application message that will be serialized and sent to an SQS queue.
    /// </summary>
    public T Message { get; set; } = default!;

    /// <summary>
    /// Optional per-message options such as <see cref="SQSMessageOptions.MessageGroupId"/>,
    /// <see cref="SQSMessageOptions.MessageDeduplicationId"/>, <see cref="SQSMessageOptions.DelaySeconds"/>,
    /// and <see cref="SQSMessageOptions.MessageAttributes"/>.
    /// </summary>
    public SQSMessageOptions? Options { get; set; }
}
