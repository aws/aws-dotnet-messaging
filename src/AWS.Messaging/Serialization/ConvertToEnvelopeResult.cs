// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;
using AWS.Messaging.Configuration;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Indicates the result of <see cref="IEnvelopeDeserializer.ConvertToEnvelopeAsync(Message)"/>
/// </summary>
public class ConvertToEnvelopeResult
{
    /// <summary>
    /// The <see cref="MessageEnvelope"/> that is created by deserializing the service response.
    /// </summary>
    public MessageEnvelope Envelope { get; init; }

    /// <summary>
    /// The <see cref="SubscriberMapping"/> that was identified for the service response, and should be used for further processing of <see cref="Envelope"/>.
    /// </summary>
    public SubscriberMapping Mapping { get; init; }

    /// <summary>
    /// Creates an instance of <see cref="ConvertToEnvelopeResult"/>
    /// </summary>
    /// <param name="envelope">The <see cref="MessageEnvelope"/> that is created by deserializing the service response.</param>
    /// <param name="subscriberMapping">The <see cref="SubscriberMapping"/> that was identified for the service response, and should be used for further processing of <see cref="Envelope"/>.</param>
    public ConvertToEnvelopeResult(MessageEnvelope envelope, SubscriberMapping subscriberMapping)
    {
        Envelope = envelope;
        Mapping = subscriberMapping;
    }
}
