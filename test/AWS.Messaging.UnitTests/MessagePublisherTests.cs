// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using AWS.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using AWS.Messaging.Publishers;
using System.Threading.Tasks;
using AWS.Messaging.UnitTests.Models;
using Amazon.SQS;
using AWS.Messaging.Serialization;
using System.Threading;
using Amazon.SQS.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using AWS.Messaging.Publishers.EventBridge;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Publishers.SNS;
using SQSBatchEntryIdsNotDistinctException = Amazon.SQS.Model.BatchEntryIdsNotDistinctException;
using SQSBatchResultErrorEntry = Amazon.SQS.Model.BatchResultErrorEntry;

namespace AWS.Messaging.UnitTests;

public class MessagePublisherTests
{
    private readonly Mock<IMessageConfiguration> _messageConfiguration;
    private readonly Mock<ILogger<IMessagePublisher>> _messagePublisherLogger;
    private readonly Mock<ILogger<ISQSPublisher>> _sqsPublisherLogger;
    private readonly Mock<IAmazonSQS> _sqsClient;
    private readonly Mock<IAmazonSimpleNotificationService> _snsClient;
    private readonly Mock<IAmazonEventBridge> _eventBridgeClient;
    private readonly Mock<IEnvelopeSerializer> _envelopeSerializer;
    private readonly ChatMessage _chatMessage;

    public MessagePublisherTests()
    {
        _messageConfiguration = new Mock<IMessageConfiguration>();
        _messagePublisherLogger = new Mock<ILogger<IMessagePublisher>>();
        _sqsPublisherLogger = new Mock<ILogger<ISQSPublisher>>();

        _sqsClient = new Mock<IAmazonSQS>();
        _sqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()));

        _snsClient = new Mock<IAmazonSimpleNotificationService>();
        _snsClient.Setup(x => x.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()));

        _eventBridgeClient = new Mock<IAmazonEventBridge>();
        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()));

        _envelopeSerializer = new Mock<IEnvelopeSerializer>();
        _envelopeSerializer.SetReturnsDefault(ValueTask.FromResult(new MessageEnvelope<ChatMessage>()
        {
            Id = "1234",
            Source = new Uri("/aws/messaging/unittest", UriKind.Relative)
        }));


        _chatMessage = new ChatMessage
        {
            MessageDescription = "Test Description"
        };
    }

    [Fact]
    public async Task SQSPublisher_HappyPath()
    {
        var serviceProvider = SetupSQSPublisherDIServices();

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );
        _sqsClient.Setup(x =>
            x.SendMessageAsync(
                It.Is<SendMessageRequest>(request =>
                    request.QueueUrl.Equals("endpoint")),
                It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageResponse()
        {
            MessageId = "MessageId"
        });

        var result = await messagePublisher.PublishAsync(_chatMessage);

        _sqsClient.Verify(x =>
                x.SendMessageAsync(
                    It.Is<SendMessageRequest>(request =>
                        request.QueueUrl.Equals("endpoint")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));
        Assert.Equal("MessageId", result.MessageId);
    }

    [Fact]
    public async Task RoutingPublisher_TelemetryHappyPath()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();

        _sqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageResponse()
        {
            MessageId = "MessageId"
        });
        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            telemetryFactory.Object
        );

        var publishResponse = await messagePublisher.PublishAsync(_chatMessage);

        telemetryFactory.Verify(x =>
                x.Trace(
                    It.Is<string>(request =>
                        request.Equals("Routing message to AWS service"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.ObjectType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));
        Assert.Equal("MessageId", publishResponse.MessageId);
    }

    [Fact]
    public async Task RoutingPublisher_TelemetryThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();

        _sqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("Telemetry exception"));
        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            telemetryFactory.Object
        );

        await Assert.ThrowsAsync<Exception>(() => messagePublisher.PublishAsync(_chatMessage));

        telemetryTrace.Verify(x =>
                x.AddException(
                    It.Is<Exception>(request =>
                        request.Message.Equals("Telemetry exception")),
                    It.IsAny<bool>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_TelemetryHappyPath()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();
        _sqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageResponse()
        {
            MessageId = "MessageId"
        });

        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new SQSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _sqsPublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );

        var sendResult = await messagePublisher.SendAsync(_chatMessage);

        telemetryFactory.Verify(x =>
                x.Trace(
                    It.Is<string>(request =>
                        request.Equals("Publish to AWS SQS"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.ObjectType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.MessageType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.QueueUrl)),
                    It.Is<string>(request =>
                        request.Equals("endpoint"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.MessageId)),
                    It.Is<string>(request =>
                        request.Equals("1234"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.RecordTelemetryContext(
                    It.IsAny<MessageEnvelope>()),
            Times.Exactly(1));
        Assert.Equal("MessageId", sendResult.MessageId);
    }

    [Fact]
    public async Task SQSPublisher_TelemetryThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();

        _sqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("Telemetry exception"));
        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new SQSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _sqsPublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );

        await Assert.ThrowsAsync<Exception>(() => messagePublisher.SendAsync(_chatMessage));

        telemetryTrace.Verify(x =>
                x.AddException(
                    It.Is<Exception>(request =>
                        request.Message.Equals("Telemetry exception")),
                    It.IsAny<bool>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_MappingNotFound()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await Assert.ThrowsAsync<MissingMessageTypeConfigurationException>(() => messagePublisher.PublishAsync(_chatMessage));
    }

    [Fact]
    public async Task SQSPublisher_InvalidMessage()
    {
        var serviceProvider = SetupSQSPublisherDIServices();

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await Assert.ThrowsAsync<InvalidMessageException>(() => messagePublisher.PublishAsync<ChatMessage?>(null));
    }

    private IServiceProvider SetupSQSPublisherDIServices(string queueUrl = "endpoint")
    {
        var publisherConfiguration = new SQSPublisherConfiguration(queueUrl);
        var publisherMapping = new PublisherMapping(typeof(ChatMessage), publisherConfiguration, PublisherTargetType.SQS_PUBLISHER);

        _messageConfiguration.Setup(x => x.GetPublisherMapping(typeof(ChatMessage))).Returns(publisherMapping);

        var services = new ServiceCollection();
        services.AddSingleton<IAmazonSQS>(_sqsClient.Object);
        services.AddSingleton<ILogger<ISQSPublisher>>(_sqsPublisherLogger.Object);
        services.AddSingleton<IMessageConfiguration>(_messageConfiguration.Object);
        services.AddSingleton<IEnvelopeSerializer>(_envelopeSerializer.Object);
        services.AddSingleton<IAWSClientProvider, AWSClientProvider>();
        services.AddSingleton<ITelemetryFactory, DefaultTelemetryFactory>();

        return services.BuildServiceProvider();
    }

    private ISQSPublisher SetupSQSPublisher(IServiceProvider serviceProvider)
    {
        return new SQSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _sqsPublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            (ITelemetryFactory)serviceProvider.GetService(typeof(ITelemetryFactory))!
        );
    }

    /// <summary>
    /// Makes the mocked envelope serializer create a new envelope with a unique Id ("envelope-1", "envelope-2", ...)
    /// for every <see cref="ChatMessage"/>, like the default message Id generator, instead of sharing the "1234" envelope.
    /// </summary>
    private void SetupUniqueEnvelopeIds()
    {
        var envelopeCount = 0;
        _envelopeSerializer
            .Setup(x => x.CreateEnvelopeAsync(It.IsAny<ChatMessage>()))
            .Returns(() => ValueTask.FromResult(new MessageEnvelope<ChatMessage>
            {
                Id = $"envelope-{++envelopeCount}",
                Source = new Uri("/aws/messaging/unittest", UriKind.Relative)
            }));
    }

    /// <summary>
    /// Builds an SQS response that reports every entry of <paramref name="request"/> as successful,
    /// with the SQS MessageId "msg-{entry Id}".
    /// Like SQS, throws <see cref="SQSBatchEntryIdsNotDistinctException"/> if two entries of the request share an Id.
    /// </summary>
    private static SendMessageBatchResponse SucceedAllEntries(SendMessageBatchRequest request)
    {
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        if (request.Entries.Any(e => !entryIds.Add(e.Id)))
        {
            throw new SQSBatchEntryIdsNotDistinctException("Two or more batch entries in the request have the same Id.");
        }

        return new SendMessageBatchResponse
        {
            Successful = request.Entries.Select(e => new SendMessageBatchResultEntry
            {
                Id = e.Id,
                MessageId = $"msg-{e.Id}"
            }).ToList(),
            Failed = new List<SQSBatchResultErrorEntry>()
        };
    }

    /// <summary>
    /// Makes the mocked SQS client report every entry of every SendMessageBatch request as successful.
    /// </summary>
    /// <returns>The requests sent to SQS, in order.</returns>
    private List<SendMessageBatchRequest> SetupSendMessageBatchSucceedsForAllEntries()
    {
        var sentRequests = new List<SendMessageBatchRequest>();
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) =>
            {
                sentRequests.Add(req);
                return SucceedAllEntries(req);
            });
        return sentRequests;
    }

    /// <summary>
    /// Creates <paramref name="count"/> batch entries with the caller Ids "order-0", "order-1", ...
    /// </summary>
    private static List<SQSBatchEntry<ChatMessage>> CreateEntriesWithIds(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new SQSBatchEntry<ChatMessage>(new ChatMessage { MessageDescription = $"Order {i}" }) { Id = $"order-{i}" })
            .ToList();
    }

    /// <summary>
    /// Makes the mocked SQS client report every entry of the first SendMessageBatch request as successful
    /// and throw <paramref name="exception"/> from every later request.
    /// </summary>
    private void SetupSendMessageBatchThrowsAfterFirstRequest(Exception exception)
    {
        var requestCount = 0;
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) =>
            {
                requestCount++;
                if (requestCount > 1)
                {
                    throw exception;
                }
                return SucceedAllEntries(req);
            });
    }

    /// <summary>
    /// Asserts that <paramref name="response"/> reports each Id in <paramref name="entryIds"/> exactly once across
    /// Successful, Failed and Unprocessed, and reports no other Id. An Id given twice must be reported twice.
    /// </summary>
    private static void AssertEveryEntryIsReportedOnce(IEnumerable<string> entryIds, SQSSendBatchResponse response)
    {
        var reportedIds = response.Successful.Select(s => s.Id!)
            .Concat(response.Failed.Select(f => f.Id!))
            .Concat(response.Unprocessed.Select(u => u.Id!));
        Assert.Equal(
            entryIds.OrderBy(id => id, StringComparer.Ordinal),
            reportedIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Returns the batch entry Id and reason of each entry in <see cref="SQSSendBatchResponse.Unprocessed"/>, in order.
    /// </summary>
    private static IEnumerable<(string Id, SQSSendBatchUnprocessedReason Reason)> GetUnprocessedIdsAndReasons(SQSSendBatchResponse response)
    {
        return response.Unprocessed.Select(u => (u.Id!, u.Reason));
    }

    /// <summary>
    /// Pairs the caller Ids "order-{first}" ... "order-{first + count - 1}" from <see cref="CreateEntriesWithIds"/>
    /// with <paramref name="reason"/>.
    /// </summary>
    private static IEnumerable<(string Id, SQSSendBatchUnprocessedReason Reason)> OrderIdsWithReason(
        int first, int count, SQSSendBatchUnprocessedReason reason)
    {
        return Enumerable.Range(first, count).Select(i => ($"order-{i}", reason));
    }

    /// <summary>
    /// Verifies that the SQS publisher logged exactly one warning whose message contains <paramref name="text"/>.
    /// </summary>
    private void VerifySingleWarningContaining(string text)
    {
        _sqsPublisherLogger.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(text)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    /// <summary>
    /// Verifies that the SQS publisher logged no error.
    /// </summary>
    private void VerifyNoErrorLogged()
    {
        _sqsPublisherLogger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never());
    }

    [Fact]
    public async Task SQSPublisher_UnsupportedPublisher()
    {
        var serviceProvider = SetupSQSPublisherDIServices();

        var publisherMapping = new PublisherMapping(typeof(ChatMessage), null!, "NEW_PUBLISHER");
        _messageConfiguration.Setup(x => x.GetPublisherMapping(typeof(ChatMessage))).Returns(publisherMapping);

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await Assert.ThrowsAsync<UnsupportedPublisherException>(() => messagePublisher.PublishAsync(_chatMessage));
    }

    /// <summary>
    /// Asserts that we can override the QueueURL for a specific message
    /// </summary>
    [Fact]
    public async Task SQSPublisher_MessageSpecificQueueUrl()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        _sqsClient.Setup(x =>
            x.SendMessageAsync(
                It.Is<SendMessageRequest>(request =>
                    request.QueueUrl.Equals("overrideEndpoint")),
                It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageResponse()
        {
            MessageId = "MessageId"
        });

        var sendResult = await messagePublisher.SendAsync(_chatMessage,
            new SQSOptions
            {
                QueueUrl = "overrideEndpoint"
            });

        // Assert we used the override endpoint specified above
        _sqsClient.Verify(x =>
                x.SendMessageAsync(
                    It.Is<SendMessageRequest>(request =>
                        request.QueueUrl.Equals("overrideEndpoint")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the endpoint configured for this message type via SetupSQSPublisherDIServices
        _sqsClient.VerifyNoOtherCalls();
        Assert.Equal("MessageId", sendResult.MessageId);
    }

    /// <summary>
    /// Asserts that we can override the SQS client for a specific message
    /// </summary>
    [Fact]
    public async Task SQSPublisher_OverrideClient()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        var overrideSQSClient = new Mock<IAmazonSQS>();
        overrideSQSClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageResponse()
        {
            MessageId = "MessageId"
        });

        var sendResult = await messagePublisher.SendAsync(_chatMessage,
            new SQSOptions
            {
                OverrideClient = overrideSQSClient.Object
            });

        // Assert that the override client was invoked
        overrideSQSClient.Verify(x =>
                x.SendMessageAsync(
                    It.IsAny<SendMessageRequest>(),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the default client
        _sqsClient.VerifyNoOtherCalls();

        Assert.Equal("MessageId", sendResult.MessageId);
    }

    /// <summary>
    /// Asserts that the expected exception is thrown with the queueURL is specified on neither
    /// the configuration nor the message-specific override
    /// </summary>
    [Fact]
    public async Task SQSPublisher_NoDestination_ThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices("");
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        await Assert.ThrowsAsync<InvalidPublisherEndpointException>(() => messagePublisher.SendAsync(_chatMessage, new SQSOptions()));
    }

    private IServiceProvider SetupSNSPublisherDIServices(string topicArn = "endpoint")
    {
        var publisherConfiguration = new SNSPublisherConfiguration(topicArn);
        var publisherMapping = new PublisherMapping(typeof(ChatMessage), publisherConfiguration, PublisherTargetType.SNS_PUBLISHER);

        _messageConfiguration.Setup(x => x.GetPublisherMapping(typeof(ChatMessage))).Returns(publisherMapping);

        var services = new ServiceCollection();
        services.AddSingleton<IAmazonSimpleNotificationService>(_snsClient.Object);
        services.AddSingleton<ILogger<IMessagePublisher>>(_messagePublisherLogger.Object);
        services.AddSingleton<IMessageConfiguration>(_messageConfiguration.Object);
        services.AddSingleton<IEnvelopeSerializer>(_envelopeSerializer.Object);
        services.AddSingleton<IAWSClientProvider, AWSClientProvider>();
        services.AddSingleton<ITelemetryFactory, DefaultTelemetryFactory>();

        return services.BuildServiceProvider();
    }

    private ISNSPublisher SetupSNSPublisher(IServiceProvider serviceProvider)
    {
        return new SNSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _messagePublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            (ITelemetryFactory)serviceProvider.GetService(typeof(ITelemetryFactory))!
        );
    }

    [Fact]
    public async Task SNSPublisher_HappyPath()
    {
        var serviceProvider = SetupSNSPublisherDIServices();

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );
        _snsClient.Setup(x => x.PublishAsync(It.Is<PublishRequest>(request =>
                request.TopicArn.Equals("endpoint")),
            It.IsAny<CancellationToken>())).ReturnsAsync(new PublishResponse()
        {
            MessageId = "MessageId"
        });

        var publishResult = await messagePublisher.PublishAsync(_chatMessage);

        _snsClient.Verify(x =>
                x.PublishAsync(
                    It.Is<PublishRequest>(request =>
                        request.TopicArn.Equals("endpoint")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));
        Assert.Equal("MessageId", publishResult.MessageId);
    }

    [Fact]
    public async Task SNSPublisher_TelemetryHappyPath()
    {
        var serviceProvider = SetupSNSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();
        _snsClient.Setup(x =>
            x.PublishAsync(
                It.IsAny<PublishRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new PublishResponse()
        {
            MessageId = "MessageId"
        });

        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new SNSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _messagePublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );

        await messagePublisher.PublishAsync(_chatMessage);

        telemetryFactory.Verify(x =>
                x.Trace(
                    It.Is<string>(request =>
                        request.Equals("Publish to AWS SNS"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.ObjectType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.MessageType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.TopicUrl)),
                    It.Is<string>(request =>
                        request.Equals("endpoint"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.MessageId)),
                    It.Is<string>(request =>
                        request.Equals("1234"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.RecordTelemetryContext(
                    It.IsAny<MessageEnvelope>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SNSPublisher_TelemetryThrowsException()
    {
        var serviceProvider = SetupSNSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();

        _snsClient.Setup(x => x.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("Telemetry exception"));
        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new SNSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _messagePublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );

        await Assert.ThrowsAsync<Exception>(() => messagePublisher.PublishAsync(_chatMessage));

        telemetryTrace.Verify(x =>
                x.AddException(
                    It.Is<Exception>(request =>
                        request.Message.Equals("Telemetry exception")),
                    It.IsAny<bool>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SNSPublisher_InvalidMessage()
    {
        var serviceProvider = SetupSNSPublisherDIServices();

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await Assert.ThrowsAsync<InvalidMessageException>(() => messagePublisher.PublishAsync<ChatMessage?>(null));
    }

    /// <summary>
    /// Asserts that we can override the topic ARN for a specific message
    /// </summary>
    [Fact]
    public async Task SNSPublisher_MessageSpecificTopicArn()
    {
        var serviceProvider = SetupSNSPublisherDIServices();
        var messagePublisher = SetupSNSPublisher(serviceProvider);
        _snsClient.Setup(x =>
            x.PublishAsync(
                It.Is<PublishRequest>(request =>
                    request.TopicArn.Equals("overrideTopicArn")),
                It.IsAny<CancellationToken>())).ReturnsAsync(new PublishResponse()
        {
            MessageId = "MessageId"
        });

        var publishResponse = await messagePublisher.PublishAsync(_chatMessage,
            new SNSOptions
            {
                TopicArn = "overrideTopicArn"
            });

        // Assert we used the override topic arn specified above
        _snsClient.Verify(x =>
                x.PublishAsync(
                    It.Is<PublishRequest>(request =>
                        request.TopicArn.Equals("overrideTopicArn")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the topic arn configured for this message type via SetupSNSPublisherDIServices
        _snsClient.VerifyNoOtherCalls();
        Assert.Equal("MessageId", publishResponse.MessageId);
    }

    /// <summary>
    /// Asserts that we can override the SNS client for a specific message
    /// </summary>
    [Fact]
    public async Task SNSPublisher_OverrideClient()
    {
        var serviceProvider = SetupSNSPublisherDIServices();
        var messagePublisher = SetupSNSPublisher(serviceProvider);

        var overrideSNSClient = new Mock<IAmazonSimpleNotificationService>();
        overrideSNSClient.Setup(x => x.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PublishResponse());

        await messagePublisher.PublishAsync(_chatMessage,
            new SNSOptions
            {
                OverrideClient = overrideSNSClient.Object
            });

        // Assert that the override client was invoked
        overrideSNSClient.Verify(x =>
                x.PublishAsync(
                    It.IsAny<PublishRequest>(),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the default client
        _snsClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Asserts that the expected exception is thrown with the topicArn is specified on neither
    /// the configuration nor the message-specific override
    /// </summary>
    [Fact]
    public async Task SNSPublisher_NoDestination_ThrowsException()
    {
        var serviceProvider = SetupSNSPublisherDIServices("");
        var messagePublisher = SetupSNSPublisher(serviceProvider);

        await Assert.ThrowsAsync<InvalidPublisherEndpointException>(() => messagePublisher.PublishAsync(_chatMessage, new SNSOptions()));
    }

    private IServiceProvider SetupEventBridgePublisherDIServices(string eventBusName, string? endpointID = null)
    {
        var publisherConfiguration = new EventBridgePublisherConfiguration(eventBusName)
        {
            EndpointID = endpointID
        };

        var publisherMapping = new PublisherMapping(typeof(ChatMessage), publisherConfiguration, PublisherTargetType.EVENTBRIDGE_PUBLISHER);

        _messageConfiguration.Setup(x => x.GetPublisherMapping(typeof(ChatMessage))).Returns(publisherMapping);

        var services = new ServiceCollection();
        services.AddSingleton<IAmazonEventBridge>(_eventBridgeClient.Object);
        services.AddSingleton<ILogger<IMessagePublisher>>(_messagePublisherLogger.Object);
        services.AddSingleton<IMessageConfiguration>(_messageConfiguration.Object);
        services.AddSingleton<IEnvelopeSerializer>(_envelopeSerializer.Object);
        services.AddSingleton<IAWSClientProvider, AWSClientProvider>();
        services.AddSingleton<ITelemetryFactory, DefaultTelemetryFactory>();

        return services.BuildServiceProvider();
    }

    private IEventBridgePublisher SetupEventBridgePublisher(IServiceProvider serviceProvider)
    {
        return new EventBridgePublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _messagePublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            (ITelemetryFactory)serviceProvider.GetService(typeof(ITelemetryFactory))!
        );
    }

    [Fact]
    public async Task EventBridgePublisher_HappyPath()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");
        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        var publishResponse = await messagePublisher.PublishAsync(_chatMessage);

        _eventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.Is<PutEventsRequest>(request =>
                        request.Entries[0].EventBusName.Equals("event-bus-123") && string.IsNullOrEmpty(request.EndpointId)
                                                                                && request.Entries[0].DetailType.Equals("AWS.Messaging.UnitTests.Models.ChatMessage") && request.Entries[0].Source.Equals("/aws/messaging/unittest")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        Assert.Equal("ReturnedEventId", publishResponse.MessageId);
    }

    [Fact]
    public async Task EventBridgePublisher_UnhappyPath()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");
        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    ErrorMessage = "ErrorMessage",
                    ErrorCode = "ErrorCode"
                }
            }
        });

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        var publishResponse = await Assert.ThrowsAsync<FailedToPublishException>(async () => await messagePublisher.PublishAsync(_chatMessage));

        _eventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.Is<PutEventsRequest>(request =>
                        request.Entries[0].EventBusName.Equals("event-bus-123") && string.IsNullOrEmpty(request.EndpointId)
                                                                                && request.Entries[0].DetailType.Equals("AWS.Messaging.UnitTests.Models.ChatMessage") && request.Entries[0].Source.Equals("/aws/messaging/unittest")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        Assert.Equal("Message failed to publish.", publishResponse.Message);
        Assert.Equal("ErrorMessage", publishResponse.InnerException!.Message);
        Assert.Equal("ErrorCode", ((EventBridgePutEventsException)publishResponse.InnerException).ErrorCode);
    }

    [Fact]
    public async Task EventBridgePublisher_TelemetryHappyPath()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();

        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new EventBridgePublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _messagePublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );
        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });

        await messagePublisher.PublishAsync(_chatMessage);

        telemetryFactory.Verify(x =>
                x.Trace(
                    It.Is<string>(request =>
                        request.Equals("Publish to AWS EventBridge"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.ObjectType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.MessageType)),
                    It.Is<string>(request =>
                        request.Equals("AWS.Messaging.UnitTests.Models.ChatMessage"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.EventBusName)),
                    It.Is<string>(request =>
                        request.Equals("event-bus-123"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.AddMetadata(
                    It.Is<string>(request =>
                        request.Equals(TelemetryKeys.MessageId)),
                    It.Is<string>(request =>
                        request.Equals("1234"))),
            Times.Exactly(1));

        telemetryTrace.Verify(x =>
                x.RecordTelemetryContext(
                    It.IsAny<MessageEnvelope>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task EventBridgePublisher_TelemetryThrowsException()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();

        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("Telemetry exception"));
        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);
        telemetryTrace.Setup(x => x.AddMetadata(It.IsAny<string>(), It.IsAny<string>()));

        var messagePublisher = new EventBridgePublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _messagePublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );

        await Assert.ThrowsAsync<Exception>(() => messagePublisher.PublishAsync(_chatMessage));

        telemetryTrace.Verify(x =>
                x.AddException(
                    It.Is<Exception>(request =>
                        request.Message.Equals("Telemetry exception")),
                    It.IsAny<bool>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task EventBridgePublisher_GlobalEP()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123", "endpoint.123");
        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await messagePublisher.PublishAsync(_chatMessage);

        _eventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.Is<PutEventsRequest>(request =>
                        request.Entries[0].EventBusName.Equals("event-bus-123") && request.EndpointId.Equals("endpoint.123")
                                                                                && request.Entries[0].DetailType.Equals("AWS.Messaging.UnitTests.Models.ChatMessage") && request.Entries[0].Source.Equals("/aws/messaging/unittest")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task EventBridgePublisher_OptionSource()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");
        var messagePublisher = SetupEventBridgePublisher(serviceProvider);

        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });

        await messagePublisher.PublishAsync(_chatMessage,
            new EventBridgeOptions
            {
                Source = "/aws/custom"
            });

        _eventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.Is<PutEventsRequest>(request =>
                        request.Entries[0].EventBusName.Equals("event-bus-123") && string.IsNullOrEmpty(request.EndpointId)
                                                                                && request.Entries[0].DetailType.Equals("AWS.Messaging.UnitTests.Models.ChatMessage") && request.Entries[0].Source.Equals("/aws/custom")),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task EventBridgePublisher_SetOptions()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");
        var messagePublisher = SetupEventBridgePublisher(serviceProvider);

        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });
        DateTimeOffset dateTimeOffset = new DateTimeOffset(2015, 2, 17, 0, 0, 0, TimeSpan.Zero);

        await messagePublisher.PublishAsync(_chatMessage,
            new EventBridgeOptions
            {
                TraceHeader = "trace-header1",
                Time = dateTimeOffset
            });

        _eventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.Is<PutEventsRequest>(request =>
                        request.Entries[0].EventBusName.Equals("event-bus-123") && string.IsNullOrEmpty(request.EndpointId)
                                                                                && request.Entries[0].TraceHeader.Equals("trace-header1") && request.Entries[0].Time!.Value.Year == dateTimeOffset.Year),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task EventBridgePublisher_InvalidMessage()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("event-bus-123");

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await Assert.ThrowsAsync<InvalidMessageException>(() => messagePublisher.PublishAsync<ChatMessage?>(null));
    }

    /// <summary>
    /// Asserts that we can override the EventBridge destination for a specific message
    /// </summary>
    [Fact]
    public async Task EventBridgePublisher_MessageSpecificTopicArn()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("defaultBus", "defaultEndpoint");
        var messagePublisher = SetupEventBridgePublisher(serviceProvider);
        _eventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });
        await messagePublisher.PublishAsync(_chatMessage,
            new EventBridgeOptions
            {
                EventBusName = "overrideBus",
                EndpointID = "overrideEndpoint"
            });

        // Assert we used the event bus and endpointspecified above
        _eventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.Is<PutEventsRequest>(request =>
                        request.Entries[0].EventBusName.Equals("overrideBus") &&
                        request.EndpointId == "overrideEndpoint"),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the desination configured for this message type via SetupEventBridgePublisherDIServices
        _eventBridgeClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Asserts that we can override the SNS client for a specific message
    /// </summary>
    [Fact]
    public async Task EventBridgePublisher_OverrideClient()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("defaultBus", "defaultEndpoint");
        var messagePublisher = SetupEventBridgePublisher(serviceProvider);

        var overrideEventBridgeClient = new Mock<IAmazonEventBridge>();
        overrideEventBridgeClient.Setup(x => x.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutEventsResponse
        {
            Entries = new List<PutEventsResultEntry>
            {
                new()
                {
                    EventId = "ReturnedEventId"
                }
            }
        });
        await messagePublisher.PublishAsync(_chatMessage,
            new EventBridgeOptions
            {
                OverrideClient = overrideEventBridgeClient.Object
            });

        // Assert that the override client was invoked
        overrideEventBridgeClient.Verify(x =>
                x.PutEventsAsync(
                    It.IsAny<PutEventsRequest>(),
                    It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the default client
        _eventBridgeClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Asserts that the expected exception is thrown with the event bus is specified on neither
    /// the configuration nor the message-specific override
    /// </summary>
    [Fact]
    public async Task EventBridgePublisher_NoDestination_ThrowsException()
    {
        var serviceProvider = SetupEventBridgePublisherDIServices("");
        var messagePublisher = SetupEventBridgePublisher(serviceProvider);

        await Assert.ThrowsAsync<InvalidPublisherEndpointException>(() => messagePublisher.PublishAsync(_chatMessage, new EventBridgeOptions()));
    }

    [Fact]
    public async Task PublishToFifoQueue_WithoutMessageGroupId_ThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices("endpoint.fifo");

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        var sqsMessagePublisher = new SQSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _sqsPublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        await Assert.ThrowsAsync<InvalidFifoPublishingRequestException>(() => messagePublisher.PublishAsync<ChatMessage?>(new ChatMessage()));
        await Assert.ThrowsAsync<InvalidFifoPublishingRequestException>(() => sqsMessagePublisher.SendAsync<ChatMessage?>(new ChatMessage(), new SQSOptions()));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_HappyPath()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(request =>
                    request.QueueUrl.Equals("endpoint") &&
                    request.Entries.Count == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) => SucceedAllEntries(req));

        var messages = new List<ChatMessage>
        {
            new() { MessageDescription = "Message 1" },
            new() { MessageDescription = "Message 2" },
            new() { MessageDescription = "Message 3" }
        };

        var result = await messagePublisher.SendBatchAsync(messages);

        Assert.Equal(3, result.Successful.Count);
        Assert.Empty(result.Failed);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "envelope-1", "envelope-2", "envelope-3" }, result);
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_PartialFailure()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // SQS reports the first entry as successful and the second as failed
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) => new SendMessageBatchResponse
            {
                Successful = new List<SendMessageBatchResultEntry>
                {
                    new() { Id = req.Entries[0].Id, MessageId = "msg1" }
                },
                Failed = new List<SQSBatchResultErrorEntry>
                {
                    new() { Id = req.Entries[1].Id, Code = "InternalError", Message = "Something went wrong", SenderFault = false }
                }
            });

        var messages = new List<ChatMessage>
        {
            new() { MessageDescription = "Message 1" },
            new() { MessageDescription = "Message 2" }
        };

        var result = await messagePublisher.SendBatchAsync(messages);

        Assert.Single(result.Successful);
        Assert.Equal("envelope-1", result.Successful[0].Id);
        Assert.Equal("msg1", result.Successful[0].MessageId);
        Assert.Single(result.Failed);
        Assert.Equal("envelope-2", result.Failed[0].Id);
        Assert.Equal("InternalError", result.Failed[0].Code);
        Assert.Equal("Something went wrong", result.Failed[0].Message);
        Assert.False(result.Failed[0].SenderFault);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "envelope-1", "envelope-2" }, result);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_AutoChunking()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // Set up to return success for any batch
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) =>
            new SendMessageBatchResponse
            {
                Successful = req.Entries.Select(e => new SendMessageBatchResultEntry
                {
                    Id = e.Id,
                    MessageId = $"msg-{e.Id}"
                }).ToList(),
                Failed = new List<SQSBatchResultErrorEntry>()
            });

        // 15 messages should result in 2 batch calls (10 + 5)
        var messages = Enumerable.Range(1, 15)
            .Select(i => new ChatMessage { MessageDescription = $"Message {i}" })
            .ToList();

        var result = await messagePublisher.SendBatchAsync(messages);

        Assert.Equal(15, result.Successful.Count);
        Assert.Empty(result.Failed);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(Enumerable.Range(1, 15).Select(i => $"envelope-{i}"), result);

        // Verify we made exactly 2 batch calls
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify first chunk had 10 entries
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req => req.Entries.Count == 10),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // Verify second chunk had 5 entries
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req => req.Entries.Count == 5),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_EmptyList()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        var messages = new List<ChatMessage>();

        var result = await messagePublisher.SendBatchAsync(messages);

        Assert.Empty(result.Successful);
        Assert.Empty(result.Failed);

        // Verify no SQS calls were made
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_FifoValidation()
    {
        var serviceProvider = SetupSQSPublisherDIServices("endpoint.fifo");
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        var messages = new List<ChatMessage>
        {
            new() { MessageDescription = "Message 1" }
        };

        // No MessageGroupId should throw
        await Assert.ThrowsAsync<InvalidFifoPublishingRequestException>(
            () => messagePublisher.SendBatchAsync(messages));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_FifoWithPerEntryMessageGroupId()
    {
        var serviceProvider = SetupSQSPublisherDIServices("endpoint.fifo");
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req =>
                    req.Entries.All(e => e.MessageGroupId == "group1")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) => SucceedAllEntries(req));

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Message 1" },
                new SQSMessageOptions { MessageGroupId = "group1" }),
            new(new ChatMessage { MessageDescription = "Message 2" },
                new SQSMessageOptions { MessageGroupId = "group1" })
        };

        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(2, result.Successful.Count);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "envelope-1", "envelope-2" }, result);
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req =>
                    req.Entries.All(e => e.MessageGroupId == "group1")),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_PerMessageOptions()
    {
        var serviceProvider = SetupSQSPublisherDIServices("endpoint.fifo");
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        SetupSendMessageBatchSucceedsForAllEntries();

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Message 1" },
                new SQSMessageOptions { MessageGroupId = "groupA" }),
            new(new ChatMessage { MessageDescription = "Message 2" },
                new SQSMessageOptions { MessageGroupId = "groupB" })
        };

        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(2, result.Successful.Count);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "envelope-1", "envelope-2" }, result);

        // Verify entries had different message group IDs
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req =>
                    req.Entries.Any(e => e.MessageGroupId == "groupA") &&
                    req.Entries.Any(e => e.MessageGroupId == "groupB")),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_OverrideClient()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        var overrideSQSClient = new Mock<IAmazonSQS>();
        overrideSQSClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) => SucceedAllEntries(req));

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Message 1" })
        };

        var result = await messagePublisher.SendBatchAsync(entries, new SQSBatchOptions
        {
            OverrideClient = overrideSQSClient.Object
        });

        Assert.Single(result.Successful);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "envelope-1" }, result);

        // Assert that the override client was invoked
        overrideSQSClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        // And not the default client
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_MessageSpecificQueueUrl()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req => req.QueueUrl.Equals("overrideEndpoint")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) => SucceedAllEntries(req));

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Message 1" })
        };

        var result = await messagePublisher.SendBatchAsync(entries, new SQSBatchOptions
        {
            QueueUrl = "overrideEndpoint"
        });

        Assert.Single(result.Successful);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "envelope-1" }, result);

        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.Is<SendMessageBatchRequest>(req => req.QueueUrl.Equals("overrideEndpoint")),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_NullMessageThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Valid" }),
            new() { Message = null! }
        };

        await Assert.ThrowsAsync<InvalidMessageException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>(entries));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_PartialResultsOnChunkFailure()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        var callCount = 0;
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First chunk succeeds
                    return new SendMessageBatchResponse
                    {
                        Successful = req.Entries.Select(e => new SendMessageBatchResultEntry
                        {
                            Id = e.Id,
                            MessageId = $"msg-{e.Id}"
                        }).ToList(),
                        Failed = new List<SQSBatchResultErrorEntry>()
                    };
                }
                // Second chunk throws AmazonSQSException
                throw new AmazonSQSException("Service unavailable");
            });

        // 15 messages = 2 chunks (10 + 5). First chunk will succeed, second will throw.
        var messages = Enumerable.Range(1, 15)
            .Select(i => new ChatMessage { MessageDescription = $"Message {i}" })
            .ToList();

        var ex = await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => messagePublisher.SendBatchAsync(messages));

        // Verify the exception carries partial results from the first successful chunk
        Assert.NotNull(ex.PartialResponse);
        Assert.Equal(10, ex.PartialResponse.Successful.Count);
        Assert.Empty(ex.PartialResponse.Failed);
        // The second chunk (messages 11-15) threw, so its outcome is unknown
        Assert.Equal(
            Enumerable.Range(11, 5).Select(i => ($"envelope-{i}", SQSSendBatchUnprocessedReason.OutcomeUnknown)),
            GetUnprocessedIdsAndReasons(ex.PartialResponse));
        AssertEveryEntryIsReportedOnce(Enumerable.Range(1, 15).Select(i => $"envelope-{i}"), ex.PartialResponse);
        Assert.IsType<AmazonSQSException>(ex.InnerException);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_NullMessagesCollectionThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => messagePublisher.SendBatchAsync((IEnumerable<ChatMessage>)null!));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_NullEntriesCollectionThrowsException()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>((IEnumerable<SQSBatchEntry<ChatMessage>>)null!));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_CallerIdIsSentAndReturnedInSuccessful()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();
        var sentRequests = SetupSendMessageBatchSucceedsForAllEntries();

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Order 1" }) { Id = "order-1" },
            new(new ChatMessage { MessageDescription = "Order 2" }) { Id = "order-2" }
        };

        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(new[] { "order-1", "order-2" }, Assert.Single(sentRequests).Entries.Select(e => e.Id));
        Assert.Equal(new[] { "order-1", "order-2" }, result.Successful.Select(s => s.Id!));
        Assert.Equal(new[] { "msg-order-1", "msg-order-2" }, result.Successful.Select(s => s.MessageId!));
        Assert.Empty(result.Failed);
        Assert.Empty(result.Unprocessed);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_CallerIdIsReturnedInFailed()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageBatchResponse
        {
            Successful = new List<SendMessageBatchResultEntry>
            {
                new() { Id = "order-1", MessageId = "msg-order-1" }
            },
            Failed = new List<SQSBatchResultErrorEntry>
            {
                new() { Id = "order-2", Code = "InternalError", Message = "Something went wrong", SenderFault = false }
            }
        });

        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Order 1" }) { Id = "order-1" },
            new(new ChatMessage { MessageDescription = "Order 2" }) { Id = "order-2" }
        };

        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal("order-1", Assert.Single(result.Successful).Id);
        var failure = Assert.Single(result.Failed);
        Assert.Equal("order-2", failure.Id);
        Assert.Equal("InternalError", failure.Code);
        Assert.Equal("Something went wrong", failure.Message);
        Assert.False(failure.SenderFault);
        Assert.Empty(result.Unprocessed);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_EntriesWithoutIdUseEnvelopeId()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();
        var sentRequests = SetupSendMessageBatchSucceedsForAllEntries();

        // The second entry has no Id, so it uses the envelope Id created for it ("envelope-2")
        var entries = new List<SQSBatchEntry<ChatMessage>>
        {
            new(new ChatMessage { MessageDescription = "Order 1" }) { Id = "order-1" },
            new(new ChatMessage { MessageDescription = "No Id" }),
            new(new ChatMessage { MessageDescription = "Order 3" }) { Id = "order-3" }
        };

        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(new[] { "order-1", "envelope-2", "order-3" }, Assert.Single(sentRequests).Entries.Select(e => e.Id));
        Assert.Equal(new[] { "order-1", "envelope-2", "order-3" }, result.Successful.Select(s => s.Id!));
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(new[] { "order-1", "envelope-2", "order-3" }, result);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_DuplicateIdsInSameRequestFailThatRequest()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();
        SetupSendMessageBatchSucceedsForAllEntries();

        // 12 entries = 2 chunks (10 + 2). Two entries of the first chunk share the Id "dup", so SQS rejects that request.
        var entries = CreateEntriesWithIds(12);
        entries[0].Id = "dup";
        entries[5].Id = "dup";

        var ex = await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>(entries));

        Assert.IsType<SQSBatchEntryIdsNotDistinctException>(ex.InnerException);
        Assert.NotNull(ex.PartialResponse);
        Assert.Empty(ex.PartialResponse.Successful);
        Assert.Empty(ex.PartialResponse.Failed);
        Assert.Equal(
            entries.Take(10).Select(e => (e.Id!, SQSSendBatchUnprocessedReason.OutcomeUnknown))
                .Concat(OrderIdsWithReason(10, 2, SQSSendBatchUnprocessedReason.NotSent)),
            GetUnprocessedIdsAndReasons(ex.PartialResponse));
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), ex.PartialResponse);
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_DuplicateIdsInDifferentRequestsAreBothSent()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();
        var sentRequests = SetupSendMessageBatchSucceedsForAllEntries();

        // 11 entries = 2 chunks (10 + 1). The first entry of each chunk has the Id "dup".
        var entries = CreateEntriesWithIds(11);
        entries[0].Id = "dup";
        entries[10].Id = "dup";

        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(new[] { "dup", "dup" }, sentRequests.Select(r => r.Entries[0].Id));
        Assert.Equal(2, result.Successful.Count(s => s.Id == "dup"));
        Assert.Empty(result.Failed);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), result);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_ChunkFailureReportsFailingAndLaterChunksAsUnprocessed()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();
        SetupSendMessageBatchThrowsAfterFirstRequest(new AmazonSQSException("Service unavailable"));

        // 25 entries = 3 chunks (10 + 10 + 5). The second chunk throws, so the third is never sent.
        var entries = CreateEntriesWithIds(25);
        var ex = await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>(entries));

        Assert.IsType<AmazonSQSException>(ex.InnerException);
        Assert.NotNull(ex.PartialResponse);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"order-{i}"), ex.PartialResponse.Successful.Select(s => s.Id!));
        Assert.Empty(ex.PartialResponse.Failed);
        Assert.Equal(
            OrderIdsWithReason(10, 10, SQSSendBatchUnprocessedReason.OutcomeUnknown)
                .Concat(OrderIdsWithReason(20, 5, SQSSendBatchUnprocessedReason.NotSent)),
            GetUnprocessedIdsAndReasons(ex.PartialResponse));
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), ex.PartialResponse);
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_FirstChunkFailureReportsAllEntriesAsUnprocessed()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Service unavailable"));

        // 12 entries = 2 chunks (10 + 2). The first chunk throws, so the second is never sent.
        var entries = CreateEntriesWithIds(12);
        var ex = await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>(entries));

        Assert.NotNull(ex.PartialResponse);
        Assert.Empty(ex.PartialResponse.Successful);
        Assert.Empty(ex.PartialResponse.Failed);
        Assert.Equal(
            OrderIdsWithReason(0, 10, SQSSendBatchUnprocessedReason.OutcomeUnknown)
                .Concat(OrderIdsWithReason(10, 2, SQSSendBatchUnprocessedReason.NotSent)),
            GetUnprocessedIdsAndReasons(ex.PartialResponse));
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), ex.PartialResponse);
        _sqsClient.Verify(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_EntryMissingFromSQSResponseIsUnprocessed()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // SQS reports "order-0" but says nothing about "order-1"
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageBatchResponse
        {
            Successful = new List<SendMessageBatchResultEntry>
            {
                new() { Id = "order-0", MessageId = "msg-order-0" }
            },
            Failed = new List<SQSBatchResultErrorEntry>()
        });

        var entries = CreateEntriesWithIds(2);
        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal("order-0", Assert.Single(result.Successful).Id);
        Assert.Empty(result.Failed);
        Assert.Equal(
            OrderIdsWithReason(1, 1, SQSSendBatchUnprocessedReason.OutcomeUnknown),
            GetUnprocessedIdsAndReasons(result));
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), result);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_ResponseIdNotInRequestIsIgnored()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // SQS reports "order-0", says nothing about "order-1", and reports three Ids that were not in the request.
        // Ids are compared exactly, so "ORDER-1" does not match "order-1".
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageBatchResponse
        {
            Successful = new List<SendMessageBatchResultEntry>
            {
                new() { Id = "order-0", MessageId = "msg-order-0" },
                new() { Id = "not-in-request-1", MessageId = "msg-not-in-request-1" },
                new() { Id = "ORDER-1", MessageId = "msg-ORDER-1" }
            },
            Failed = new List<SQSBatchResultErrorEntry>
            {
                new() { Id = "not-in-request-2", Code = "InternalError", Message = "Something went wrong", SenderFault = false }
            }
        });

        var entries = CreateEntriesWithIds(2);
        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal("order-0", Assert.Single(result.Successful).Id);
        Assert.Empty(result.Failed);
        Assert.Equal(
            OrderIdsWithReason(1, 1, SQSSendBatchUnprocessedReason.OutcomeUnknown),
            GetUnprocessedIdsAndReasons(result));
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), result);
        VerifySingleWarningContaining("'not-in-request-1'");
        VerifySingleWarningContaining("'not-in-request-2'");
        VerifySingleWarningContaining("'ORDER-1'");

        // The ignored failed result for "not-in-request-2" is not logged as an error
        VerifyNoErrorLogged();
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_ResultForAlreadyReportedIdIsIgnored()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // SQS reports "order-0" as successful twice, and "order-1" as both successful and failed.
        // Only the first result for each Id counts.
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new SendMessageBatchResponse
        {
            Successful = new List<SendMessageBatchResultEntry>
            {
                new() { Id = "order-0", MessageId = "msg-order-0" },
                new() { Id = "order-0", MessageId = "msg-order-0-again" },
                new() { Id = "order-1", MessageId = "msg-order-1" }
            },
            Failed = new List<SQSBatchResultErrorEntry>
            {
                new() { Id = "order-1", Code = "InternalError", Message = "Something went wrong", SenderFault = false }
            }
        });

        var entries = CreateEntriesWithIds(2);
        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(
            new[] { ("order-0", "msg-order-0"), ("order-1", "msg-order-1") },
            result.Successful.Select(s => (s.Id!, s.MessageId!)));
        Assert.Empty(result.Failed);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), result);
        VerifySingleWarningContaining("'order-0'");
        VerifySingleWarningContaining("'order-1'");

        // The ignored failed result for "order-1" is not logged as an error
        VerifyNoErrorLogged();
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_AllEntriesFailedWithNullSuccessfulList()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // The AWS SDK leaves a response list null when SQS does not return it
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) => new SendMessageBatchResponse
            {
                Successful = null,
                Failed = req.Entries.Select(e => new SQSBatchResultErrorEntry
                {
                    Id = e.Id,
                    Code = "InternalError",
                    Message = "Something went wrong",
                    SenderFault = false
                }).ToList()
            });

        var entries = CreateEntriesWithIds(3);
        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Empty(result.Successful);
        Assert.Equal(entries.Select(e => e.Id!), result.Failed.Select(f => f.Id!));
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), result);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_AllEntriesSucceededWithNullFailedList()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        // The AWS SDK leaves a response list null when SQS does not return it
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) =>
            {
                var response = SucceedAllEntries(req);
                response.Failed = null;
                return response;
            });

        var entries = CreateEntriesWithIds(3);
        var result = await messagePublisher.SendBatchAsync<ChatMessage>(entries);

        Assert.Equal(entries.Select(e => e.Id!), result.Successful.Select(s => s.Id!));
        Assert.Empty(result.Failed);
        Assert.Empty(result.Unprocessed);
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), result);
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_EveryEntryAppearsInExactlyOneList()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var messagePublisher = SetupSQSPublisher(serviceProvider);
        SetupUniqueEnvelopeIds();

        var requestCount = 0;
        _sqsClient.Setup(x =>
            x.SendMessageBatchAsync(
                It.IsAny<SendMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageBatchRequest req, CancellationToken _) =>
            {
                requestCount++;
                if (requestCount > 1)
                {
                    throw new AmazonSQSException("Service unavailable");
                }

                // First chunk: "order-3" fails, the other 9 succeed
                return new SendMessageBatchResponse
                {
                    Successful = req.Entries
                        .Where(e => e.Id != "order-3")
                        .Select(e => new SendMessageBatchResultEntry { Id = e.Id, MessageId = $"msg-{e.Id}" })
                        .ToList(),
                    Failed = new List<SQSBatchResultErrorEntry>
                    {
                        new() { Id = "order-3", Code = "InternalError", Message = "Something went wrong", SenderFault = false }
                    }
                };
            });

        var entries = CreateEntriesWithIds(25);

        var ex = await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>(entries));

        var partial = ex.PartialResponse!;
        AssertEveryEntryIsReportedOnce(entries.Select(e => e.Id!), partial);
        Assert.Equal(9, partial.Successful.Count);
        Assert.Equal("order-3", Assert.Single(partial.Failed).Id);
        Assert.Equal(
            OrderIdsWithReason(10, 10, SQSSendBatchUnprocessedReason.OutcomeUnknown)
                .Concat(OrderIdsWithReason(20, 5, SQSSendBatchUnprocessedReason.NotSent)),
            GetUnprocessedIdsAndReasons(partial));
    }

    [Fact]
    public async Task SQSPublisher_SendBatch_TelemetryRecordsOriginalExceptionOnce()
    {
        var serviceProvider = SetupSQSPublisherDIServices();
        var telemetryFactory = new Mock<ITelemetryFactory>();
        var telemetryTrace = new Mock<ITelemetryTrace>();
        telemetryFactory.Setup(x => x.Trace(It.IsAny<string>())).Returns(telemetryTrace.Object);

        var messagePublisher = new SQSPublisher(
            (IAWSClientProvider)serviceProvider.GetService(typeof(IAWSClientProvider))!,
            _sqsPublisherLogger.Object,
            _messageConfiguration.Object,
            _envelopeSerializer.Object,
            telemetryFactory.Object
        );
        SetupUniqueEnvelopeIds();
        var sqsException = new AmazonSQSException("Service unavailable");
        SetupSendMessageBatchThrowsAfterFirstRequest(sqsException);

        await Assert.ThrowsAsync<FailedToPublishBatchException>(
            () => messagePublisher.SendBatchAsync<ChatMessage>(CreateEntriesWithIds(25)));

        telemetryTrace.Verify(x => x.AddException(It.IsAny<Exception>(), It.IsAny<bool>()), Times.Once());
        telemetryTrace.Verify(x => x.AddException(sqsException, It.IsAny<bool>()), Times.Once());
        telemetryTrace.Verify(x =>
                x.AddException(It.Is<Exception>(e => e is FailedToPublishBatchException), It.IsAny<bool>()),
            Times.Never());
    }

    [Fact]
    public async Task PublishToFifoTopic_WithoutMessageGroupId_ThrowsException()
    {
        var serviceProvider = SetupSNSPublisherDIServices("endpoint.fifo");

        var messagePublisher = new MessageRoutingPublisher(
            serviceProvider,
            _messageConfiguration.Object,
            _messagePublisherLogger.Object,
            new DefaultTelemetryFactory(serviceProvider)
        );

        var snsMessagePublisher = SetupSNSPublisher(serviceProvider);

        await Assert.ThrowsAsync<InvalidFifoPublishingRequestException>(() => messagePublisher.PublishAsync<ChatMessage?>(new ChatMessage()));
        await Assert.ThrowsAsync<InvalidFifoPublishingRequestException>(() => snsMessagePublisher.PublishAsync<ChatMessage?>(new ChatMessage(), new SNSOptions()));
    }
}
