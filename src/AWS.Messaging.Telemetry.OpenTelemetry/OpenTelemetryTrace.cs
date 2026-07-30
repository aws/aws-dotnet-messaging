// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.Json;
using AWS.Messaging.Internal;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

namespace AWS.Messaging.Telemetry.OpenTelemetry;

/// <summary>
/// An OpenTelemetry trace (wrapper around a <see cref="Activity"/>)
/// </summary>
public class OpenTelemetryTrace : ITelemetryTrace
{
    private readonly Activity? _activity;
    private readonly Activity? _parentToRestore;

    /// <summary>
    /// Creates a new trace
    /// </summary>
    /// <param name="activity">New trace</param>
    /// <param name="parentToRestore">Optional parent activity that will be set as <see cref="Activity.Current"/> when this trace is disposed</param>
    public OpenTelemetryTrace(Activity? activity, Activity? parentToRestore = null)
    {
        _activity = activity;
        _parentToRestore = parentToRestore;
    }

    /// <inheritdoc/>
    public void AddException(Exception exception, bool fatal = true)
    {
        var tags = new TagList();
        _activity?.AddException(exception, in tags, default);

        if (fatal)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        }
    }

    /// <inheritdoc/>
    public void AddMetadata(string key, object value)
    {
        if (_activity != null && _activity.IsAllDataRequested)
        {
            _activity.SetTag(key, value);
        }
    }

    /// <inheritdoc/>
    public void RecordTelemetryContext(MessageEnvelope envelope)
    {
        ActivityContext contextToInject = default;
        if (_activity != null)
        {
            contextToInject = _activity.Context;
        }
        // Even if an "AWS.Messaging" activity was not created, we still
        // propogate the current activity (if it exists) through the message envelope
        else if (Activity.Current != null)
        {
            contextToInject = Activity.Current.Context;
        }

        Propagators.DefaultTextMapPropagator.Inject(new PropagationContext(contextToInject, Baggage.Current), envelope, InjectTraceContextIntoEnvelope);
    }

    /// <summary>
    /// Stores propagation context in the <see cref="MessageEnvelope"/>, meant to be used with <see cref="TextMapPropagator"/>
    /// </summary>
    /// <param name="envelope">Outbound message envelope</param>
    /// <param name="key">Context key</param>
    /// <param name="value">Context value</param>
    private void InjectTraceContextIntoEnvelope(MessageEnvelope envelope, string key, string value)
    {
        envelope.Metadata[key] = JsonSerializer.SerializeToElement(value, typeof(string), MessagingJsonSerializerContext.Default);
    }

    private bool _disposed;

    /// <summary>
    /// Disposes the inner <see cref="Activity"/>, and also restores the parent activity if set
    /// </summary>
    /// <param name="disposing">Indicates whether the call comes from Dispose (true) or a finalizer (false)</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _activity?.Dispose();
            if (_parentToRestore != null)
            {
                Activity.Current = _parentToRestore;
            }
            _disposed = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
