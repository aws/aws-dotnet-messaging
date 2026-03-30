// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization
{
    /// <summary>
    /// This interface exposes serialization callbacks that lets users inject their own metadata to incoming and outgoing messages.
    /// It contains no-op default implementations for all methods so that user implementations of this interface only need to add overrides for specific methods.
    /// </summary>
    public interface ISerializationCallback
    {
        /// <summary>
        /// This can be used to set additional metadata to the message envelope before it serialized and published to an endpoint.
        /// </summary>
        /// <param name="messageEnvelope">This envelope adheres to the CloudEvents specification v1.0 and contains all the attributes that are marked as required by the spec. The CloudEvent spec can be found <see href="https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md"/>here.</param>
        ValueTask PreSerializationAsync(MessageEnvelope messageEnvelope)
        {
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// This can be used to set additional metadata to the message envelope before it is serialized and published to an endpoint.
        /// Unlike the non-generic <see cref="PreSerializationAsync(MessageEnvelope)"/>, this overload provides access to the
        /// typed <see cref="MessageEnvelope{T}"/>, allowing direct access to the message payload via <see cref="MessageEnvelope{T}.Message"/>.
        /// This is useful for extracting values from the message to set as envelope metadata (e.g., CloudEvents extension attributes like <c>subject</c>).
        /// </summary>
        /// <typeparam name="T">The .NET type of the underlying application message.</typeparam>
        /// <param name="messageEnvelope">The typed message envelope containing the application message and CloudEvents metadata.</param>
        ValueTask PreSerializationAsync<T>(MessageEnvelope<T> messageEnvelope)
        {
            return PreSerializationAsync((MessageEnvelope)messageEnvelope);
        }

        /// <summary>
        /// This can be used to encrypt or modify the serialized message envelope before publishing it to an endpoint.
        /// </summary>
        /// <param name="message">The serialized message envelope</param>
        /// <returns></returns>
        ValueTask<string> PostSerializationAsync(string message)
        {
            return new ValueTask<string>(message);
        }

        /// <summary>
        /// This can be used to decrypt or modify the serialized message after it is retrieved by the message poller.
        /// </summary>
        /// <param name="message">The serialized message retrieved by the message poller</param>
        ValueTask<string> PreDeserializationAsync(string message)
        {
            return new ValueTask<string>(message);
        }

        /// <summary>
        /// This can be used to set additional metadata to the message envelope after it is deserialized and handed over to the subscriber for further processing.
        /// </summary>
        /// <param name="messageEnvelope">This envelope adheres to the CloudEvents specification v1.0 and contains all the attributes that are marked as required by the spec. The CloudEvent spec can be found <see href="https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md"/>here.</param>
        ValueTask PostDeserializationAsync(MessageEnvelope messageEnvelope)
        {
            return ValueTask.CompletedTask;
        }
    }
}
