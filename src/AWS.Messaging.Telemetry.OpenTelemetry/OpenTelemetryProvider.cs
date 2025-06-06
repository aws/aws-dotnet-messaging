// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace AWS.Messaging.Telemetry.OpenTelemetry;

/// <summary>
/// Creates OpenTelemetry traces
/// </summary>
public class OpenTelemetryProvider : ITelemetryProvider
{
    private static readonly ActivitySource _activitySource = new ActivitySource(Constants.SourceName, TelemetryKeys.AWSMessagingAssemblyVersion);

    /// <inheritdoc/>
    public ITelemetryTrace Trace(string traceName)
    {
        var activity = _activitySource.StartActivity(traceName, ActivityKind.Producer);
        if (activity != null)
        {
            return new OpenTelemetryTrace(activity);
        }

        // If we initially failed to create an activity, attempt to force creation with 
        // a link to the current activity, see https://opentelemetry.io/docs/instrumentation/net/manual/#creating-new-root-activities
        var parentActivity = Activity.Current;
        Activity.Current = null;
        ActivityLink[]? links = null;
        if (parentActivity != null)
        {
            links = new[] { new ActivityLink(parentActivity.Context) };
        }

        activity = _activitySource.StartActivity(traceName, ActivityKind.Producer, parentContext: default, links: links);

        return new OpenTelemetryTrace(activity, parentActivity);
    }

    /// <inheritdoc/>
    public ITelemetryTrace Trace(string traceName, MessageEnvelope envelope)
    {
        var propogatedContext = Propagators.DefaultTextMapPropagator.Extract(default, envelope, ExtractTraceContextFromEnvelope);
        Baggage.Current = propogatedContext.Baggage;

        // Create links list for both propagated context and current activity
        var links = new List<ActivityLink>();
        if (propogatedContext.ActivityContext.IsValid())
        {
            links.Add(new ActivityLink(propogatedContext.ActivityContext));
        }

        // Create a new root activity with link to producer
        var activity = _activitySource.StartActivity(
            traceName,
            ActivityKind.Consumer,
            default(ActivityContext),
            null,
            links);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Ok);
            return new OpenTelemetryTrace(activity);
        }

        // If we initially failed to create an activity, attempt to force creation
        var parentActivity = Activity.Current;
        Activity.Current = null;

        // Add link to parent activity if it exists
        if (parentActivity != null)
        {
            links.Add(new ActivityLink(parentActivity.Context));
        }

        activity = _activitySource.StartActivity(
            traceName,
            ActivityKind.Consumer,
            default(ActivityContext),
            null,
            links);

        return new OpenTelemetryTrace(activity, parentActivity);
    }

    /// <summary>
    /// Extracts propagation context from a <see cref="MessageEnvelope"/>, meant to be used with <see cref="TextMapPropagator"/>
    /// </summary>
    /// <param name="envelope">Inbound message envelope</param>
    /// <param name="key">Context key</param>
    /// <returns>Context value</returns>
    private IEnumerable<string> ExtractTraceContextFromEnvelope(MessageEnvelope envelope, string key)
    {
        if (envelope.Metadata.TryGetValue(key, out var jsonElement))
        {
            return new string[] { jsonElement.ToString() };
        }

        return Enumerable.Empty<string>();
    }
}
