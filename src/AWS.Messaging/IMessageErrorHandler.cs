// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging;

/// <summary>
/// Defines a handler for errors that occur during message processing in the subscriber pipeline.
/// Implementations can control retry behavior, override the message processing result, or handle exceptions.
/// </summary>
/// <remarks>
/// Register an error handler using <c>builder.AddMessageErrorHandler&lt;T&gt;()</c>.
/// Only one error handler may be registered. If an error handler is not registered, exceptions
/// thrown during message processing will result in a <see cref="MessageProcessStatus"/> of Failed.
/// </remarks>
public interface IMessageErrorHandler
{
    /// <summary>
    /// Handles errors that occur during message processing.
    /// </summary>
    /// <param name="messageEnvelope">The message being processed.</param>
    /// <param name="exception"><see cref="Exception"/> raised while processing message.</param>
    /// <param name="attempts">Number of attempts made at processing this message</param>
    /// <param name="token"><see cref="CancellationToken"/></param>
    /// <returns><see cref="MessageErrorHandlerResponse"/></returns>
    public ValueTask<MessageErrorHandlerResponse> OnHandleError<T>(MessageEnvelope<T> messageEnvelope, Exception exception, int attempts, CancellationToken token);
}

/// <summary>
/// Defines the possible responses from an <see cref="IMessageErrorHandler"/> when handling a message processing error.
/// </summary>
public enum MessageErrorHandlerResponse
{
    /// <summary>
    /// Failed response.
    /// </summary>
    Failed,

    /// <summary>
    /// Retry the message processing in the same process.
    /// </summary>
    Retry,

    /// <summary>
    /// Success response.
    /// </summary>
    Success
}
