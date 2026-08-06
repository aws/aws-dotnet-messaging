// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Identifies the type of outer wrapper around the CloudEvents envelope.
/// </summary>
internal enum WrapperType
{
    /// <summary>
    /// The message body is the CloudEvents envelope itself (no wrapper).
    /// This is the default/fallback when no other wrapper is detected.
    /// </summary>
    Sqs = 0,

    /// <summary>
    /// The message is wrapped in an SNS notification envelope.
    /// </summary>
    Sns = 1,

    /// <summary>
    /// The message is wrapped in an EventBridge event envelope.
    /// </summary>
    EventBridge = 2
}
