// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization;

/// <summary>
/// Contains interned string constants for common CloudEvents and AWS service values
/// to reduce string allocation overhead during deserialization.
/// </summary>
internal static class CloudEventConstants
{
    /// <summary>
    /// CloudEvents specification version 1.0
    /// </summary>
    public static readonly string SpecVersion1_0 = string.Intern("1.0");

    /// <summary>
    /// Standard JSON content type
    /// </summary>
    public static readonly string ApplicationJson = string.Intern("application/json");

    /// <summary>
    /// SNS notification type value
    /// </summary>
    public static readonly string SnsNotification = string.Intern("Notification");

    /// <summary>
    /// SNS subscription confirmation type value
    /// </summary>
    public static readonly string SnsSubscriptionConfirmation = string.Intern("SubscriptionConfirmation");

    /// <summary>
    /// SNS unsubscribe confirmation type value
    /// </summary>
    public static readonly string SnsUnsubscribeConfirmation = string.Intern("UnsubscribeConfirmation");
}
