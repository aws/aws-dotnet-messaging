// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging;

/// <summary>
/// This interface is implemented by the users of this library for each layer of middleware that should be processed.
/// </summary>
public interface IMiddleware
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="messageEnvelope">The message read from the message source wrapped around a message envelope containing message metadata.</param>
    /// <param name="next">Delegate to execute the next layer of middleware. When no further middleware remains, the delegate will execute the message handler.</param>
    /// <param name="token">The optional cancellation token.</param>
    /// <returns>
    /// The status of the processed message. For example whether the message was successfully processed.
    /// Default implementations should return the result returned from the next delegate.
    /// </returns>
    Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken token = default);
}

/// <summary>
/// The delegate used to invoke the next middleware layer or the message handler.
/// </summary>
/// <returns>
/// The status of the processed message. For example whether the message was successfully processed.
/// </returns>
public delegate Task<MessageProcessStatus> RequestDelegate();
