// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Supports deserialization of SQS messages into <see cref="MessageEnvelope"/>
/// </summary>
public interface IEnvelopeDeserializer
{
    /// <summary>
    /// Takes an SQS <see cref="Message"/> and converts the <see cref="Message.Body"/> into a <see cref="MessageEnvelope"/>
    /// </summary>
    /// <param name="message">The SQS <see cref="Message"/> sent by the user</param>
    ValueTask<ConvertToEnvelopeResult> ConvertToEnvelopeAsync(Message message);
}
