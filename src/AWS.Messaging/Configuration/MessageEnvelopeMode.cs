// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.\r
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Configuration;

/// <summary>
/// The mode indicating how message envelopes are used in message transport.
/// </summary>
public enum MessageEnvelopeMode
{
    /// <summary>
    /// Message envelopes are used in message transport.
    /// </summary>
    Supported,
    /// <summary>
    /// Message envelopes are not used in message transport.
    /// </summary>
    NotSupported
}
