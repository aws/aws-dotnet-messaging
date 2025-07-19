// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Publishers;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.Telemetry;
using AWS.Messaging.Telemetry.OpenTelemetry;
using AWS.Messaging.UnitTests.MessageHandlers;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace AWS.Messaging.UnitTests;

public class OpenTelemetryTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly MessageRoutingPublisher _publisher;
    private readonly HandlerInvoker _handler;
    private readonly SubscriberMapping _subscriberMapping;
    private readonly Mock<IAmazonSQS> _amazonSqsClient;

    /// <summary>
    /// Initializes all the services needed to publish and handle
    /// messages without actually using SQS
    /// </summary>
    public OpenTelemetryTests()
    {
        var envelopeSerializer = new Mock<IEnvelopeSerializer>();
        var messageConfiguration = new Mock<IMessageConfiguration>();
        var messagePublisherLogger = new Mock<ILogger<IMessagePublisher>>();
        var sqsPublisherLogger = new Mock<ILogger<ISQSPublisher>>();
        var publisherMapping = new PublisherMapping(typeof(ChatMessage), new SQSPublisherConfiguration("endpoint"), PublisherTargetType.SQS_PUBLISHER);
        _subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

        envelopeSerializer.SetReturnsDefault(ValueTask.FromResult(new MessageEnvelope<ChatMessage>()
        {
            Id = "1234",
            Source = new Uri("/aws/messaging/unittest", UriKind.Relative)
        }));

        messageConfiguration.Setup(x => x.GetPublisherMapping(typeof(ChatMessage))).Returns(publisherMapping);
        messageConfiguration.Setup(x => x.GetSubscriberMapping(typeof(ChatMessage))).Returns(_subscriberMapping);

        _amazonSqsClient = new Mock<IAmazonSQS>();
        _amazonSqsClient.Setup(clnt => clnt.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageResponse
        {
            MessageId = "MessageId"
        });
        var services = new ServiceCollection();
        services.AddSingleton(_amazonSqsClient.Object);
        services.AddSingleton(messagePublisherLogger.Object);
        services.AddSingleton(sqsPublisherLogger.Object);
        services.AddSingleton(messageConfiguration.Object);
        services.AddSingleton(envelopeSerializer.Object);
        services.AddSingleton<IAWSClientProvider, AWSClientProvider>();
        services.AddSingleton<ITelemetryFactory, DefaultTelemetryFactory>();
        services.AddSingleton<ITelemetryProvider, OpenTelemetryProvider>();
        services.AddSingleton<ChatMessageHandler>();

        _serviceProvider = services.BuildServiceProvider();

        _publisher = new MessageRoutingPublisher(
            _serviceProvider,
            messageConfiguration.Object,
            messagePublisherLogger.Object,
            new DefaultTelemetryFactory(_serviceProvider));

        _handler = new HandlerInvoker(
            _serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(_serviceProvider),
            new MessageConfiguration());
    }

    /// <summary>
    /// Verifies that the expected traces and tags are created when publishing a message
    /// </summary>
    [Fact]
    public async Task OpenTelemetry_Publisher_ExpectedTracesAndTags()
    {
        var activities = new List<Activity>();


        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(Constants.SourceName)
            .ConfigureResource(resource => resource.AddService("unittest"))
            .AddInMemoryExporter(activities).Build())
        {

            await _publisher.PublishAsync(new ChatMessage { MessageDescription = "Test Description" });
        }

        Assert.Equal(2, activities.Count);
        Assert.Equal("AWS.Messaging: Publish to AWS SQS", activities[0].OperationName);
        Assert.Equal(4, activities[0].Tags.Count());
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.objectType", "AWS.Messaging.UnitTests.Models.ChatMessage"), activities[0].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.messageType", "AWS.Messaging.UnitTests.Models.ChatMessage"), activities[0].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.sqs.queueurl", "endpoint"), activities[0].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.messageId", "1234"), activities[0].Tags);

        Assert.Equal("AWS.Messaging: Routing message to AWS service", activities[1].OperationName);
        Assert.Equal(2, activities[1].Tags.Count());
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.objectType", "AWS.Messaging.UnitTests.Models.ChatMessage"), activities[1].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.publishTargetType", "SQS"), activities[1].Tags);
    }

    /// <summary>
    /// Verifies that when we need to manipulate <see cref="Activity.Current"/> in order
    /// to force creation of our activity, that we reset the original activity at the end.
    /// </summary>
    [Fact]
    public async Task OpenTelemetry_Publisher_ResetsParentActivity()
    {
        var activities = new List<Activity>();

        // Start a non-MPF activity
        var existingActivity = new Activity("current").Start();
        Activity.Current = existingActivity;

        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(Constants.SourceName)
            .ConfigureResource(resource => resource.AddService("unittest"))
            .AddInMemoryExporter(activities).Build())
        {
            await _publisher.PublishAsync(new ChatMessage { MessageDescription = "Test Description" });
        }

        // We expect the top-level MPF activity to reset Activity.Current once disposed
        Assert.Equal(existingActivity, Activity.Current);
        existingActivity.Stop();
    }

    /// <summary>
    /// Verifies that the expected traces and tags are created when handling a message
    /// </summary>
    [Fact]
    public async Task OpenTelemetry_Handler_ExpectedTracesAndTags()
    {
        var activities = new List<Activity>();
        var envelope = new MessageEnvelope<ChatMessage>()
        {
            MessageTypeIdentifier = "AWS.Messaging.UnitTests.Models.ChatMessage",
            Id = "1234",
            SQSMetadata = new SQSMetadata()
            {
                MessageID = "4567"
            },
            Message = new ChatMessage { MessageDescription = "Test Description" }
        };

        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(Constants.SourceName)
            .ConfigureResource(resource => resource.AddService("unittest"))
            .AddInMemoryExporter(activities).Build())
        {

            await _handler.InvokeAsync(envelope, _subscriberMapping);
        }

        Assert.Single(activities);
        Assert.Equal("AWS.Messaging: Processing message", activities[0].OperationName);
        Assert.Equal(4, activities[0].Tags.Count());
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.messageId", "1234"), activities[0].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.messageType", "AWS.Messaging.UnitTests.Models.ChatMessage"), activities[0].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.handlerType", "AWS.Messaging.UnitTests.MessageHandlers.ChatMessageHandler"), activities[0].Tags);
        Assert.Contains(new KeyValuePair<string, string?>("aws.messaging.sqs.messageId", "4567"), activities[0].Tags);
    }

    /// <summary>
    /// Verifies that the handler trace has the correct link to the trace context
    /// included in the message envelope
    /// </summary>
    [Fact]
    public async Task OpenTelemetry_Handler_ParentFromEnvelope()
    {
        var activities = new List<Activity>();
        var envelope = new MessageEnvelope<ChatMessage>()
        {
            MessageTypeIdentifier = "AWS.Messaging.UnitTests.Models.ChatMessage",
            Id = "1234",
            SQSMetadata = new SQSMetadata()
            {
                MessageID = "4567"
            },
            Metadata = new Dictionary<string, JsonElement>
            {
                { "traceparent", JsonDocument.Parse("\"00-d2d8865217873923d2d74cf680a30ac3-d63e320582f9ff94-01\"").RootElement }
            },
            Message = new ChatMessage { MessageDescription = "Test Description" }
        };

        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(Constants.SourceName)
            .ConfigureResource(resource => resource.AddService("unittest"))
            .AddInMemoryExporter(activities).Build())
        {
            await _handler.InvokeAsync(envelope, _subscriberMapping);
        }

        Assert.Single(activities);
        var activity = activities[0];
        Assert.Equal("AWS.Messaging: Processing message", activity.OperationName);

        // Verify the activity is a root activity (no parent)
        Assert.Null(activity.ParentId);

        // Verify the activity has exactly one link to the trace context from the envelope
        Assert.Single(activity.Links);
        var link = activity.Links.Single();
        Assert.Equal("d2d8865217873923d2d74cf680a30ac3", link.Context.TraceId.ToString());
        Assert.Equal("d63e320582f9ff94", link.Context.SpanId.ToString());
    }
}
