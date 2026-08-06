// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization;

/// <summary>
/// Supports serialization of <see cref="MessageEnvelope"/> into different .NET types
/// </summary>
public interface IEnvelopeSerializer
{
    /// <summary>
    /// Serializes <see cref="MessageEnvelope{T}"/> into a string.
    /// </summary>
    /// <param name="envelope"><see cref="MessageEnvelope{T}"/></param>
    ValueTask<string> SerializeAsync<T>(MessageEnvelope<T> envelope);

    /// <summary>
    /// Creates a <see cref="MessageEnvelope{T}"/>
    /// </summary>
    /// <typeparam name="T">The .NET type of the underlying application message.</typeparam>
    /// <param name="message">The application message sent by the user</param>
    ValueTask<MessageEnvelope<T>> CreateEnvelopeAsync<T>(T message);
}
