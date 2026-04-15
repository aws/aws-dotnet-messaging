// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging;

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
