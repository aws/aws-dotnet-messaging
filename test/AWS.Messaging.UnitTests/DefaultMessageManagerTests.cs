// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.SQS;
using AWS.Messaging.Tests.Common.Handlers;
using AWS.Messaging.Tests.Common.Models;
using AWS.Messaging.UnitTests.MessageHandlers;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests
{
    public class DefaultMessageManagerTests
    {
        /// <summary>
        /// Happy path test for a successful message handling, that it is deleted at at the end
        /// </summary>
        [Fact]
        public async Task DefaultMessageManager_ManagesMessageSuccess()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Success());

            var manager = new DefaultMessageManager(mockSQSMessageCommunication.Object, mockHandlerInvoker.Object, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration(), TimeProvider.System);

            var messsageEnvelope = new MessageEnvelope<ChatMessage> { Id = "1" };
            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            await manager.ProcessMessageAsync(messsageEnvelope, subscriberMapping);

            // Verify that the handler was invoked once with the expected message and mapping
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(messsageEnvelope, subscriberMapping, Times.Once());

            // Since the mock handler invoker returns success, verify that delete was called
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(messsageEnvelope, Times.Once());

            // Since the handler succeeds right away, verify the visiblity was never extended
            mockSQSMessageCommunication.VerifyExtendMessageVisibilityTimeoutAsync(new[] { messsageEnvelope }, Times.Never());

            // Verify that the active message count was deprecated back to 0
            Assert.Equal(0, manager.ActiveMessageCount);

            // Verify that there were no expected poller/handler calls
            mockSQSMessageCommunication.VerifyNoOtherCalls();
            mockHandlerInvoker.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Happy path test for a failed message handling, that it is not deleted from the queue at the end
        /// </summary>
        [Fact]
        public async Task DefaultMessageManager_ManagesMessageFailed()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Failed());

            var manager = new DefaultMessageManager(mockSQSMessageCommunication.Object, mockHandlerInvoker.Object, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration(), TimeProvider.System);

            var messsageEnvelope = new MessageEnvelope<ChatMessage> { Id = "1" };
            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            await manager.ProcessMessageAsync(messsageEnvelope, subscriberMapping);

            // Verify that the handler was invoked once with the expected message and mapping
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(messsageEnvelope, subscriberMapping, Times.Once());

            // Verify that the handler was invoked once with the expected message and mapping
            mockSQSMessageCommunication.VerifyReportMessageFailureAsync(messsageEnvelope, Times.Once());

            // Since the message handling failed, verify that delete was never called
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(messsageEnvelope, Times.Never());

            // Since the handler fails right away, verify the visiblity was never extended
            mockSQSMessageCommunication.VerifyExtendMessageVisibilityTimeoutAsync(new[] { messsageEnvelope }, Times.Never());

            // Verify that the active message count was deprecated back to 0
            Assert.Equal(0, manager.ActiveMessageCount);

            // Verify that there were no expected poller/handler calls
            mockSQSMessageCommunication.VerifyNoOtherCalls();
            mockHandlerInvoker.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Tests that the manager extends the visibility timeout when appropriate for a batch of in flight messages
        /// </summary>
        [Fact]
        public async Task DefaultMessageManager_ExtendsVisibilityTimeout_Batch()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Success(), messageHandlingDelay: TimeSpan.FromSeconds(3));

            var manager = new DefaultMessageManager(mockSQSMessageCommunication.Object, mockHandlerInvoker.Object, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration
            {
                VisibilityTimeout = 2,
                VisibilityTimeoutExtensionThreshold = 1,
                VisibilityTimeoutExtensionHeartbeatInterval = 1
            }, TimeProvider.System);

            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            // Start handling two messages at roughly the same time
            var message1 = new MessageEnvelope<ChatMessage>() { Id = "1" };
            var message2 = new MessageEnvelope<ChatMessage>() { Id = "2" };

            var message1Task = manager.ProcessMessageAsync(message1, subscriberMapping);
            var message2Task = manager.ProcessMessageAsync(message2, subscriberMapping);

            // Don't await the tasks yet,so we can assert that ActiveMessageCount was incremented while the handlers are still processing
            Assert.Equal(2, manager.ActiveMessageCount);

            // Now finish awaiting the handlers
            await Task.WhenAll(message1Task, message2Task);

            // Verify that the handler was invoked with the expected messages and mapping
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(message1, subscriberMapping, Times.Once());
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(message2, subscriberMapping, Times.Once());

            // Since the mock handler invoker returns success, verify that delete was called
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(message1, Times.Once());
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(message2, Times.Once());

            // Since the message handler takes 3 seconds, verify that the visibility was extended
            // The 1 to 3 allows for some instability around the second boundaries
            mockSQSMessageCommunication.VerifyExtendMessageVisibilityTimeoutAsync(new[] { message2, message1 }, Times.Between(1, 3, Moq.Range.Inclusive));

            // Verify that there were no other calls, which is guarding against
            // ExtendMessageVisibilityTimeoutAsync being called with only a single message since
            // we expect these to have the same lifecycle
            mockHandlerInvoker.VerifyNoOtherCalls();

            // Verify that the active message count was deprecated back to 0
            Assert.Equal(0, manager.ActiveMessageCount);
        }

        /// <summary>
        /// Tests that the manager extends the visibility timeout when appropriate for in flight messages
        /// that were received at different timestamps
        /// </summary>
        [Fact]
        public async Task DefaultMessageManager_ExtendsVisibilityTimeout_OnlyWhenNecessary()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Success(), messageHandlingDelay: TimeSpan.FromSeconds(4));

            var manager = new DefaultMessageManager(mockSQSMessageCommunication.Object, mockHandlerInvoker.Object, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration
            {
                VisibilityTimeout = 2,
                VisibilityTimeoutExtensionThreshold = 1,
                VisibilityTimeoutExtensionHeartbeatInterval = 1
            }, TimeProvider.System);

            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            // Start handling a single message
            var earlyMessage = new MessageEnvelope<ChatMessage>() { Id = "1" };
            var earlyTask = manager.ProcessMessageAsync(earlyMessage, subscriberMapping);

            // Delay, then start handling another message
            await Task.Delay(TimeSpan.FromSeconds(3));

            var laterMessage = new MessageEnvelope<ChatMessage>() { Id = "2" };
            var laterTask = manager.ProcessMessageAsync(laterMessage, subscriberMapping);

            // Now finish awaiting the handlers
            await Task.WhenAll(earlyTask, laterTask);

            // Verify that the handler was invoked with the expected messages and mapping
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(earlyMessage, subscriberMapping, Times.Once());
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(laterMessage, subscriberMapping, Times.Once());

            // Since the mock handler invoker returns success, verify that delete was called
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(earlyMessage, Times.Once());
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(laterMessage, Times.Once());

            // Since the message handler takes 3 seconds, verify the the visibility was extended
            // The 1 to 4 allows for some instability around the second boundaries
            mockSQSMessageCommunication.VerifyExtendMessageVisibilityTimeoutAsync(new[] { earlyMessage }, Times.Between(1, 4, Moq.Range.Inclusive));
            mockSQSMessageCommunication.VerifyExtendMessageVisibilityTimeoutAsync(new[] { laterMessage }, Times.Between(1, 4, Moq.Range.Inclusive));

            // Verify that there were no other calls, which is guarding against ExtendMessageVisibilityTimeoutAsync
            // being called with both messages since we never expect them to be batched together
            // T  | T+0   | T+1    | T+2    | T+3    | T+4    | T+5    | T + 6  | T + 7  |
            // M1 | Start |        | Extend | Extend | Finish |        |        |        |
            // M2 |       |                 | Start  |        | Extend | Extend | Finish |
            mockSQSMessageCommunication.VerifyNoOtherCalls();
            mockHandlerInvoker.VerifyNoOtherCalls();

            // Verify that the active message count was deprecated back to 0
            Assert.Equal(0, manager.ActiveMessageCount);
        }

        /// <summary>
        /// Queues many message handling tasks for a single message manager to ensure that it
        /// is managing its active message count in a thread safe manner
        /// </summary>
        [Fact]
        public async Task DefaultMessageManager_CountsActiveMessagesCorrectly()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Success(), TimeSpan.FromSeconds(1));

            var manager = new DefaultMessageManager(mockSQSMessageCommunication.Object, mockHandlerInvoker.Object, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration(), TimeProvider.System);
            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            var tasks = new List<Task>();

            for (int i = 0; i < 100; i++)
            {
                var messsageEnvelope = new MessageEnvelope<ChatMessage>()
                {
                    Id = i.ToString()
                };
                tasks.Add(manager.ProcessMessageAsync(messsageEnvelope, subscriberMapping));
            }

            await Task.WhenAll(tasks);

            // Verify that the active message count was deprecated back to 0
            Assert.Equal(0, manager.ActiveMessageCount);
        }

        [Fact]
        public async Task DefaultMessageManager_ProcessMessageGroup()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Success(), messageHandlingDelay: TimeSpan.FromSeconds(3));

            var manager = new DefaultMessageManager(mockSQSMessageCommunication.Object, mockHandlerInvoker.Object, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration
            {
                VisibilityTimeout = 2,
                VisibilityTimeoutExtensionThreshold = 1,
                VisibilityTimeoutExtensionHeartbeatInterval = 1
            }, TimeProvider.System);

            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            // Create 2 message groups "A" and "B"
            var message1 = new MessageEnvelope<ChatMessage>() { Id = "1" };
            var message2 = new MessageEnvelope<ChatMessage>() { Id = "2" };
            var messageGroupA = new List<ConvertToEnvelopeResult>
            {
                new ConvertToEnvelopeResult(message1, subscriberMapping),
                new ConvertToEnvelopeResult(message2, subscriberMapping),
            };

            var message3 = new MessageEnvelope<ChatMessage>() { Id = "3" };
            var message4 = new MessageEnvelope<ChatMessage>() { Id = "4" };
            var messageGroupB = new List<ConvertToEnvelopeResult>
            {
                new ConvertToEnvelopeResult(message3, subscriberMapping),
                new ConvertToEnvelopeResult(message4, subscriberMapping),
            };

            // Start handling two message groups at roughly the same time
            var messageATask = manager.ProcessMessageGroupAsync(messageGroupA, "A");
            var messageBTask = manager.ProcessMessageGroupAsync(messageGroupB, "B");

            // verify that the active message count was incremented once per each message group
            Assert.Equal(2, manager.ActiveMessageCount);

            // Now finish awaiting the processing of both message groups
            await Task.WhenAll(messageATask, messageBTask);

            // Verify that the handler was invoked with the expected messages and mapping
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(message1, subscriberMapping, Times.Once());
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(message2, subscriberMapping, Times.Once());
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(message3, subscriberMapping, Times.Once());
            mockHandlerInvoker.VerifyInvokeAsyncWasCalledWith(message4, subscriberMapping, Times.Once());

            // Since the mock handler invoker returns success, verify that delete was called
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(message1, Times.Once());
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(message2, Times.Once());
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(message3, Times.Once());
            mockSQSMessageCommunication.VerifyDeleteMessagesAsyncWasCalledWith(message4, Times.Once());

            // The exact set for which the visibility was extended is hard to determine so we assert on an arbitrary set of messages.
            mockSQSMessageCommunication.VerifyAnyExtendMessageVisibilityTimeoutAsync(Times.AtLeastOnce());

            // Verify that all handler invocations are accounted for
            mockHandlerInvoker.VerifyNoOtherCalls();

            // Verify that the active message count was decremented back to 0
            Assert.Equal(0, manager.ActiveMessageCount);
        }

        /// <summary>
        /// Verifies if handler invocation fails for any message in a group, then the entire group is skipped
        /// </summary>
        [Fact]
        public async Task DefaultMessageManager_MessageGroupIsSkipped()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<Tests.Common.Models.TempStorage<TransactionInfo>>();
            serviceCollection.AddAWSMessageBus(bus =>
            {
                bus.AddMessageHandler<TransactionInfoHandler, TransactionInfo>();
            });

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var handlerInvoker = serviceProvider.GetRequiredService<IHandlerInvoker>();
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();

            var messageManager = new DefaultMessageManager(mockSQSMessageCommunication.Object, handlerInvoker, new NullLogger<DefaultMessageManager>(), new MessageManagerConfiguration
            {
                VisibilityTimeout = 5,
                VisibilityTimeoutExtensionThreshold = 5,
                VisibilityTimeoutExtensionHeartbeatInterval = 5
            }, TimeProvider.System);

            var subscriberMapping = SubscriberMapping.Create<TransactionInfoHandler, TransactionInfo>();

            var messageGroup = new List<ConvertToEnvelopeResult>
            {
                new ConvertToEnvelopeResult(CreateTransactionEnvelope("1", "A", false), subscriberMapping),
                new ConvertToEnvelopeResult(CreateTransactionEnvelope("2", "A", false), subscriberMapping),
                new ConvertToEnvelopeResult(CreateTransactionEnvelope("3", "A", true), subscriberMapping),
                new ConvertToEnvelopeResult(CreateTransactionEnvelope("4", "A", false), subscriberMapping),
                new ConvertToEnvelopeResult(CreateTransactionEnvelope("5", "A", false), subscriberMapping),
            };

            await messageManager.ProcessMessageGroupAsync(messageGroup, "A");

            var messageStorage = serviceProvider.GetRequiredService<Tests.Common.Models.TempStorage<TransactionInfo>>();

            var transactionsInGroupA = messageStorage.FifoMessages["A"];

            // Since message ID = "3" failed, all messages after it are skipped from processing
            Assert.Equal(2, transactionsInGroupA.Count);
            Assert.Equal("1", transactionsInGroupA[0].Id);
            Assert.Equal("2", transactionsInGroupA[1].Id);
        }

        [Fact]
        public async Task DefaultMessageManager_RethrowsInvalidMessageHandlerSignatureException()
        {
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = new Mock<IHandlerInvoker>();
            mockHandlerInvoker
                .Setup(x => x.InvokeAsync(It.IsAny<MessageEnvelope>(), It.IsAny<SubscriberMapping>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidMessageHandlerSignatureException("Test exception"));

            var manager = new DefaultMessageManager(
                mockSQSMessageCommunication.Object,
                mockHandlerInvoker.Object,
                new NullLogger<DefaultMessageManager>(),
                new MessageManagerConfiguration(),
                TimeProvider.System);

            var messageEnvelope = new MessageEnvelope<ChatMessage> { Id = "1" };
            var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

            await Assert.ThrowsAsync<InvalidMessageHandlerSignatureException>(() =>
                manager.ProcessMessageAsync(messageEnvelope, subscriberMapping));

            // Verify that the handler was invoked
            mockHandlerInvoker.Verify(x => x.InvokeAsync(
                It.Is<MessageEnvelope>(m => m == messageEnvelope),
                It.Is<SubscriberMapping>(s => s == subscriberMapping),
                It.IsAny<CancellationToken>()),
                Times.Once);

            mockSQSMessageCommunication.Verify(x => x.DeleteMessagesAsync(It.IsAny<IEnumerable<MessageEnvelope>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockSQSMessageCommunication.Verify(x => x.ReportMessageFailureAsync(It.IsAny<MessageEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);

            Assert.Equal(0, manager.ActiveMessageCount);
        }

        [Fact]
        public async Task ProcessMessageGroupAsync_WhenMessageFails_RemovesSkippedMessagesFromInflightMetadata()
        {
            // Arrange
            var mockSQSMessageCommunication = CreateMockSQSMessageCommunication();
            var mockHandlerInvoker = CreateMockHandlerInvoker(MessageProcessStatus.Failed());

            var manager = new DefaultMessageManager(
                mockSQSMessageCommunication.Object,
                mockHandlerInvoker.Object,
                new NullLogger<DefaultMessageManager>(),
                new MessageManagerConfiguration(),
                TimeProvider.System);

            // Create a message group with 2 messages
            var message1 = new MessageEnvelope<ChatMessage>
            {
                Id = "1",
                SQSMetadata = new SQSMetadata()
                {
                    MessageID ="1",
                    MessageGroupId = "group1",
                    ReceiptHandle =  "receipt1"
                },
            };

            var message2 = new MessageEnvelope<ChatMessage>
            {
                Id = "2",
                SQSMetadata = new SQSMetadata()
                {
                    MessageID ="2",
                    MessageGroupId = "group1",
                    ReceiptHandle =  "receipt2"
                },
            };

            var messageGroup = new List<ConvertToEnvelopeResult>
            {
                new(message1, SubscriberMapping.Create<ChatMessageHandler, ChatMessage>()),
                new(message2, SubscriberMapping.Create<ChatMessageHandler, ChatMessage>()),
            };

            // Act
            await manager.ProcessMessageGroupAsync(messageGroup, "group`", CancellationToken.None);

            // Access _inFlightMessageMetadata through reflection
            var inFlightMessageMetadataField = typeof(DefaultMessageManager)
                .GetField("_inFlightMessageMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(inFlightMessageMetadataField);
            var inFlightMessageMetadata = inFlightMessageMetadataField.GetValue(manager) as ConcurrentDictionary<MessageEnvelope, InFlightMetadata>;

            // Assert
            Assert.NotNull(inFlightMessageMetadata);
            Assert.False(inFlightMessageMetadata.ContainsKey(message2),
                "Skipped message should not remain in _inFlightMessageMetadata after group failure");

            // Verify message failure was reported for skipped message
            mockSQSMessageCommunication.Verify(x =>
                x.ReportMessageFailureAsync(message2, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private MessageEnvelope<TransactionInfo> CreateTransactionEnvelope(string id, string userId, bool shouldFail)
        {
            return new MessageEnvelope<TransactionInfo>
            {
                Id = id,
                Message = new TransactionInfo { TransactionId = id, UserId = userId, ShouldFail = shouldFail }
            };
        }

        /// <summary>
        /// Mocks a message poller with the given visibility refresh interval
        /// </summary>
        /// <returns>Mock poller</returns>
        private Mock<ISQSMessageCommunication> CreateMockSQSMessageCommunication()
        {
            var mockSQSMessageCommunication = new Mock<ISQSMessageCommunication>();

            mockSQSMessageCommunication.Setup(x => x.ExtendMessageVisibilityTimeoutAsync(
                    It.IsAny<IEnumerable<MessageEnvelope>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return mockSQSMessageCommunication;
        }

        /// <summary>
        /// Mocks a handler invoker that will always return the given status with an optional delay
        /// </summary>
        /// <param name="messageProcessStatus">Message status the handler should always return</param>
        /// <param name="messageHandlingDelay">Optional delay during handling, leave null to return immediately</param>
        /// <returns>Mock handler invoker</returns>
        private Mock<IHandlerInvoker> CreateMockHandlerInvoker(MessageProcessStatus messageProcessStatus, TimeSpan? messageHandlingDelay = null)
        {
            var mockHandlerInvoker = new Mock<IHandlerInvoker>();

            // Moq doesn't support passing in null or TimeSpan.Zero (the default value) for the delay, so branch
            if (messageHandlingDelay != null)
            {
                mockHandlerInvoker.Setup(x => x.InvokeAsync(
                    It.IsAny<MessageEnvelope>(),
                    It.IsAny<SubscriberMapping>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(messageProcessStatus, (TimeSpan)messageHandlingDelay);
            }
            else // return the status immediately
            {
                mockHandlerInvoker.Setup(x => x.InvokeAsync(
                    It.IsAny<MessageEnvelope>(),
                    It.IsAny<SubscriberMapping>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(messageProcessStatus);
            }

            return mockHandlerInvoker;
        }
    }

    /// <summary>
    /// Extension methods for mocked <see cref="IMessagePoller"/> and <see cref="IHandlerInvoker"/> objects to be able to verify more concisely above
    /// </summary>
    public static class MockSQSMessagePollerExtensions
    {
        /// <summary>
        /// Verifies that <see cref="IMessagePoller.DeleteMessagesAsync"/> was called with a specified message a specified number of times
        /// </summary>
        public static void VerifyReportMessageFailureAsync(this Mock<ISQSMessageCommunication> mockSQSMessageCommunication, MessageEnvelope expectedMessage, Times times)
        {
            mockSQSMessageCommunication.Verify(sqsMessageCommunication => sqsMessageCommunication.ReportMessageFailureAsync(
                    It.Is<MessageEnvelope>(x => x == expectedMessage),
                    It.IsAny<CancellationToken>()),
                times);
        }

        /// <summary>
        /// Verifies that <see cref="IMessagePoller.DeleteMessagesAsync"/> was called with a specified message a specified number of times
        /// </summary>
        public static void VerifyDeleteMessagesAsyncWasCalledWith(this Mock<ISQSMessageCommunication> mockSQSMessageCommunication, MessageEnvelope expectedMessage, Times times)
        {
            mockSQSMessageCommunication.Verify(sqsMessageCommunication => sqsMessageCommunication.DeleteMessagesAsync(
                    It.Is<IEnumerable<MessageEnvelope>>(x => x.Count() == 1 && x.First() == expectedMessage),
                    It.IsAny<CancellationToken>()),
                times);
        }

        /// <summary>
        /// Verifies that <see cref="IHandlerInvoker.InvokeAsync"/> was called with a specified message and mapping a specified number of times
        /// </summary>
        public static void VerifyInvokeAsyncWasCalledWith(this Mock<IHandlerInvoker> mockHandlerInvoker, MessageEnvelope expectedMessage, SubscriberMapping expectedSubscriberMapping, Times times)
        {
            mockHandlerInvoker.Verify(x => x.InvokeAsync(
                    It.Is<MessageEnvelope>(actualEnvelope => actualEnvelope == expectedMessage),
                    It.Is<SubscriberMapping>(actualMapping => actualMapping == expectedSubscriberMapping),
                    It.IsAny<CancellationToken>()),
                times);
        }

        /// <summary>
        /// Verifies that <see cref="IMessagePoller.ExtendMessageVisibilityTimeoutAsync"/> was called with specified message(s) a specified number of times
        /// </summary>
        public static void VerifyExtendMessageVisibilityTimeoutAsync(this Mock<ISQSMessageCommunication> mockSQSMessageCommunication, IEnumerable<MessageEnvelope> messages, Times times)
        {
            mockSQSMessageCommunication.Verify(sqsMessageCommunication => sqsMessageCommunication.ExtendMessageVisibilityTimeoutAsync(
                    It.Is<IEnumerable<MessageEnvelope>>(x => x.ToHashSet().SetEquals(messages.ToHashSet())), // use HashSets becuase the order may differ
                    It.IsAny<CancellationToken>()),
                times);
        }

        /// <summary>
        /// Verifies that <see cref="IMessagePoller.ExtendMessageVisibilityTimeoutAsync"/> was called with an arbitrary non-empty set of messages a specified number of times
        /// </summary>
        public static void VerifyAnyExtendMessageVisibilityTimeoutAsync(this Mock<ISQSMessageCommunication> mockSQSMessageCommunication, Times times)
        {
            mockSQSMessageCommunication.Verify(sqsMessageCommunication => sqsMessageCommunication.ExtendMessageVisibilityTimeoutAsync(
                    It.Is<IEnumerable<MessageEnvelope>>(enumerable => enumerable.Count() > 0),
                    It.IsAny<CancellationToken>()),
                times);
        }
    }
}
