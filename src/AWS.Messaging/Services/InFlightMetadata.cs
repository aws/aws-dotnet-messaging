// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Services;

/// <summary>
/// Metadata for a received message that is being handled by the framework
/// </summary>
internal class InFlightMetadata
{
    private readonly TimeProvider _timeProvider;

    internal InFlightMetadata(DateTimeOffset expectedVisibilityTimeoutExpiration, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        ExpectedVisibilityTimeoutExpiration = expectedVisibilityTimeoutExpiration;
    }

    /// <summary>
    /// The timestamp that the message's visibility timeout is expected to expire
    /// </summary>
    internal DateTimeOffset ExpectedVisibilityTimeoutExpiration { get; set; }

    /// <summary>
    /// Updates the <see cref="ExpectedVisibilityTimeoutExpiration"/> to given number of seconds from now
    /// </summary>
    /// <param name="newVisibilityTimeoutWindowSeconds">How many seconds from now the message is now expected to become visible again</param>
    internal void UpdateExpectedVisibilityTimeoutExpiration(int newVisibilityTimeoutWindowSeconds)
    {
        ExpectedVisibilityTimeoutExpiration = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(newVisibilityTimeoutWindowSeconds);
    }

    /// <summary>
    /// Determines if a given message's visibility timeout expiration timestamp is within the
    /// threshold where the framework should extend it
    /// </summary>
    /// <param name="expirationThresholdSeconds">
    /// How many seconds before which a message will become visibile again its visibility timeout should be extended
    /// </param>
    /// <returns>True if the message's visibility timeout should be extended per the specified threshold, false otherwise</returns>
    internal bool IsMessageVisibilityTimeoutExpiring(int expirationThresholdSeconds)
    {
        var timeUntilExpiration = ExpectedVisibilityTimeoutExpiration - _timeProvider.GetUtcNow();

        if (timeUntilExpiration.TotalSeconds <= expirationThresholdSeconds)
        {
            return true;
        }

        return false;
    }
}
