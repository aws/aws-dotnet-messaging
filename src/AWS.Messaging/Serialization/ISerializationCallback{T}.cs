// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization
{
    /// <summary>
    /// A type-specific serialization callback that is only invoked when the message being serialized
    /// matches the type <typeparamref name="T"/>. This provides direct typed access to the message payload
    /// via <see cref="MessageEnvelope{T}.Message"/> without requiring any casting.
    /// <para/>
    /// This is useful for extracting values from the message to set as envelope metadata,
    /// such as CloudEvents extension attributes like <c>subject</c>.
    /// <para/>
    /// For cross-cutting concerns that apply to all message types (e.g., encryption, logging),
    /// use the non-generic <see cref="ISerializationCallback"/> instead.
    /// </summary>
    /// <typeparam name="T">The .NET type of the message this callback handles.</typeparam>
    public interface ISerializationCallback<T>
    {
        /// <summary>
        /// This is invoked before the message envelope is serialized and published to an endpoint.
        /// It is only called when the message type matches <typeparamref name="T"/>.
        /// </summary>
        /// <param name="messageEnvelope">The typed message envelope containing the application message and CloudEvents metadata.</param>
        ValueTask PreSerializationAsync(MessageEnvelope<T> messageEnvelope);
    }
}
