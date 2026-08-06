// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Serialization.Parsers;
using AWS.Messaging.Services;
using AWS.Messaging.UnitTests.MessageHandlers;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests;

public class EnvelopeSerializerTests
{
    private readonly IServiceCollection _serviceCollection;
    private readonly DateTimeOffset _testdate = new DateTime(year: 2000, month: 12, day: 5, hour: 10, minute: 30, second: 55, DateTimeKind.Utc);

    public EnvelopeSerializerTests()
    {
        _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfo>("sqsQueueUrl", "addressInfo");
            builder.AddMessageHandler<AddressInfoHandler, AddressInfo>("addressInfo");
            builder.AddMessageHandler<PlainTextHandler, string>("plaintext");
            builder.AddMessageSource("/aws/messaging");
        });

        var mockDateTimeHandler = new Mock<IDateTimeHandler>();
        mockDateTimeHandler.Setup(x => x.GetUtcNow()).Returns(_testdate);
        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), mockDateTimeHandler.Object));
    }

    [Fact]
    public async Task CreateEnvelope()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var message = new AddressInfo
        {
            Street = "Prince St",
            Unit = 123,
            ZipCode = "00001"
        };

        // ACT
        var envelope = await envelopeSerializer.CreateEnvelopeAsync(message);

        // ASSERT
        Assert.NotNull(envelope);
        Assert.Equal(_testdate, envelope.TimeStamp);
        Assert.Equal("1.0", envelope.Version);
        Assert.Equal("/aws/messaging", envelope.Source?.ToString());
        Assert.Equal("addressInfo", envelope.MessageTypeIdentifier);

        var addressInfo = envelope.Message;
        Assert.Equal("Prince St", addressInfo?.Street);
        Assert.Equal(123, addressInfo?.Unit);
        Assert.Equal("00001", addressInfo?.ZipCode);
    }

    [Fact]
    public async Task CreateEnvelope_MissingPublisherMapping_ThrowsException()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        var message = new ChatMessage
        {
            MessageDescription = "This is a test message"
        };

        // ACT and ASSERT
        // This throws an exception since no publisher is configured against the ChatMessage type.
        await Assert.ThrowsAsync<FailedToCreateMessageEnvelopeException>(async () => await envelopeSerializer.CreateEnvelopeAsync(message));
    }


    [Fact]
    public async Task SerializeEnvelope()
    {
        // ARRANGE
        var message = new AddressInfo
        {
            Street = "Prince St",
            Unit = 123,
            ZipCode = "00001"
        };

        var envelope = new MessageEnvelope<AddressInfo>
        {
            Id =  "id-123",
            Source = new Uri("/backend/service", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfo",
            TimeStamp = _testdate,
            Message = message
        };

        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        // ACT
        var jsonBlob = await envelopeSerializer.SerializeAsync(envelope);

        // ASSERT
        // The \u0022 corresponds to quotation mark (")
        var expectedBlob = "{\"id\":\"id-123\",\"source\":\"/backend/service\",\"specversion\":\"1.0\",\"type\":\"addressInfo\",\"time\":\"2000-12-05T10:30:55+00:00\",\"datacontenttype\":\"application/json\",\"data\":{\"Unit\":123,\"Street\":\"Prince St\",\"ZipCode\":\"00001\"}}";
        Assert.Equal(expectedBlob, jsonBlob);
    }









    [Fact]
    public async Task SerializationCallbacks_AreCorrectlyInvoked()
    {
        // ARRANGE
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddMessageHandler<AddressInfoHandler, AddressInfo>("addressInfo");
            builder.AddSerializationCallback(new MockSerializationCallback());
        });
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "123",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfo",
            TimeStamp = _testdate,
            Message = new AddressInfo
            {
                Street = "Prince St",
                Unit = 123,
                ZipCode = "00001"
            }
        };

        // ACT - Serialize Envelope
        var serializedMessage = await envelopeSerializer.SerializeAsync(messageEnvelope);

        // ASSERT - Check expected base 64 encoded string
        var expectedserializedMessage = "eyJpZCI6IjEyMyIsInNvdXJjZSI6Ii9hd3MvbWVzc2FnaW5nIiwic3BlY3ZlcnNpb24iOiIxLjAiLCJ0eXBlIjoiYWRkcmVzc0luZm8iLCJ0aW1lIjoiMjAwMC0xMi0wNVQxMDozMDo1NSswMDowMCIsImRhdGFjb250ZW50dHlwZSI6ImFwcGxpY2F0aW9uL2pzb24iLCJkYXRhIjp7IlVuaXQiOjEyMywiU3RyZWV0IjoiUHJpbmNlIFN0IiwiWmlwQ29kZSI6IjAwMDAxIn0sIklzLURlbGl2ZXJlZCI6ZmFsc2V9";
        Assert.Equal(expectedserializedMessage, serializedMessage);

        // ACT - Convert To Envelope from base 64 Encoded Message
        var sqsMessage = new Message
        {
            Body = serializedMessage
        };

        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var conversionResult = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);

        // ASSERT
        var envelope = (MessageEnvelope<AddressInfo>)conversionResult.Envelope;
        Assert.NotNull(envelope);
        Assert.Equal("123", envelope.Id);
        Assert.Equal(_testdate, envelope.TimeStamp);
        Assert.Equal("1.0", envelope.Version);
        Assert.Equal("/aws/messaging", envelope.Source?.ToString());
        Assert.Equal("addressInfo", envelope.MessageTypeIdentifier);
        Assert.True(envelope.Metadata["Is-Delivered"].GetBoolean());

        var subscribeMapping = conversionResult.Mapping;
        Assert.NotNull(subscribeMapping);
        Assert.Equal("addressInfo", subscribeMapping.MessageTypeIdentifier);
        Assert.Equal(typeof(AddressInfo), subscribeMapping.MessageType);
        Assert.Equal(typeof(AddressInfoHandler), subscribeMapping.HandlerType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializeAsync_DataMessageLogging_NoError(bool dataMessageLogging)
    {
        var logger = new Mock<ILogger<EnvelopeSerializer>>();
        var messageConfiguration = new MessageConfiguration { LogMessageContent = dataMessageLogging };
        var messageSerializer = new Mock<IMessageSerializer>();
        var dateTimeHandler = new Mock<IDateTimeHandler>();
        var messageIdGenerator = new Mock<IMessageIdGenerator>();
        var messageSourceHandler = new Mock<IMessageSourceHandler>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var envelopeSerializer = new EnvelopeSerializer(logger.Object, messageConfiguration, messageSerializer.Object, dateTimeHandler.Object, messageIdGenerator.Object, messageSourceHandler.Object, serviceProvider);
        var messageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "123",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfo",
            TimeStamp = _testdate,
            Message = new AddressInfo
            {
                Street = "Prince St",
                Unit = 123,
                ZipCode = "00001"
            }
        };

        var serializedContent = JsonSerializer.Serialize(messageEnvelope.Message);
        var messageSerializeResults = new MessageSerializerResults(serializedContent, "application/json");


        // Mock the serializer to return a specific string
        messageSerializer
            .Setup(x => x.Serialize(It.IsAny<object>()))
            .Returns(messageSerializeResults);

        await envelopeSerializer.SerializeAsync(messageEnvelope);

        if (dataMessageLogging)
        {
            logger.Verify(log => log.Log(
                    It.Is<LogLevel>(logLevel => logLevel == LogLevel.Trace),
                    It.Is<EventId>(eventId => eventId.Id == 0),
                    It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == "Serialized the MessageEnvelope object as the following raw string:\n{\"id\":\"123\",\"source\":\"/aws/messaging\",\"specversion\":\"1.0\",\"type\":\"addressInfo\",\"time\":\"2000-12-05T10:30:55+00:00\",\"datacontenttype\":\"application/json\",\"data\":{\"Unit\":123,\"Street\":\"Prince St\",\"ZipCode\":\"00001\"}}"),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        else
        {
            logger.Verify(log => log.Log(
                    It.Is<LogLevel>(logLevel => logLevel == LogLevel.Trace),
                    It.Is<EventId>(eventId => eventId.Id == 0),
                    It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == "Serialized the MessageEnvelope object to a raw string"),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializeAsync_DataMessageLogging_WithError(bool dataMessageLogging)
    {
        // ARRANGE
        var logger = new Mock<ILogger<EnvelopeSerializer>>();
        var services = new ServiceCollection();
        services.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfo>("sqsQueueUrl", "addressInfo");
        });
        var serviceProvider = services.BuildServiceProvider();
        var messageConfiguration = serviceProvider.GetRequiredService<IMessageConfiguration>();
        messageConfiguration.LogMessageContent = dataMessageLogging;

        var messageSerializer = new Mock<IMessageSerializer>();
        var dateTimeHandler = new Mock<IDateTimeHandler>();
        var messageIdGenerator = new Mock<IMessageIdGenerator>();
        var messageSourceHandler = new Mock<IMessageSourceHandler>();
        var envelopeSerializer = new EnvelopeSerializer(
            logger.Object,
            messageConfiguration,
            messageSerializer.Object,
            dateTimeHandler.Object,
            messageIdGenerator.Object,
            messageSourceHandler.Object,
            serviceProvider);

        var messageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "123",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfo",
            TimeStamp = _testdate,
            Message = new AddressInfo
            {
                Street = "Prince St",
                Unit = 123,
                ZipCode = "00001"
            }
        };

        // Setup the serializer to throw when trying to serialize the message
        messageSerializer.Setup(x => x.Serialize(It.IsAny<object>()))
            .Throws(new JsonException("Test exception"));

        // ACT & ASSERT
        var exception = await Assert.ThrowsAsync<FailedToSerializeMessageEnvelopeException>(
            async () => await envelopeSerializer.SerializeAsync(messageEnvelope));

        Assert.Equal("Failed to serialize the MessageEnvelope into a raw string", exception.Message);

        if (dataMessageLogging)
        {
            Assert.NotNull(exception.InnerException);
            Assert.IsType<JsonException>(exception.InnerException);
            Assert.Equal("Test exception", exception.InnerException.Message);
        }
        else
        {
            Assert.Null(exception.InnerException);
        }

        // Verify logging behavior
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TypedSerializationCallback_ExtractsSubjectWithZeroCasting()
    {
        // ARRANGE — Register a typed ISerializationCallback<AddressInfo> via DI.
        // The callback has direct typed access to the message with no casting required.
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfo>("sqsQueueUrl", "addressInfo");
            builder.AddMessageHandler<AddressInfoHandler, AddressInfo>("addressInfo");
            builder.AddSerializationCallback<AddressInfoSubjectCallback, AddressInfo>();
        });
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "123",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfo",
            TimeStamp = _testdate,
            Message = new AddressInfo
            {
                Street = "Prince St",
                Unit = 123,
                ZipCode = "00001"
            }
        };

        // ACT
        var serializedMessage = await envelopeSerializer.SerializeAsync(messageEnvelope);

        // ASSERT - Verify the serialized output contains the "subject" key extracted from the message payload
        var jsonDoc = JsonDocument.Parse(serializedMessage);
        Assert.True(jsonDoc.RootElement.TryGetProperty("subject", out var subjectElement));
        Assert.Equal("00001", subjectElement.GetString());
    }

    [Fact]
    public async Task TypedSerializationCallback_NotInvokedForNonMatchingType()
    {
        // ARRANGE — Register a typed callback for AddressInfo, but serialize a ChatMessage.
        // The callback should NOT be invoked because the type doesn't match.
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<ChatMessage>("sqsQueueUrl", "chatMessage");
            builder.AddSerializationCallback<AddressInfoSubjectCallback, AddressInfo>();
        });
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageEnvelope = new MessageEnvelope<ChatMessage>
        {
            Id = "456",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "chatMessage",
            TimeStamp = _testdate,
            Message = new ChatMessage
            {
                MessageDescription = "Hello"
            }
        };

        // ACT
        var serializedMessage = await envelopeSerializer.SerializeAsync(messageEnvelope);

        // ASSERT - Verify the "subject" key is NOT present since the callback is for AddressInfo, not ChatMessage
        var jsonDoc = JsonDocument.Parse(serializedMessage);
        Assert.False(jsonDoc.RootElement.TryGetProperty("subject", out _));
    }





}

public class MockSerializationCallback : ISerializationCallback
{
    public ValueTask PreSerializationAsync(MessageEnvelope messageEnvelope)
    {
        messageEnvelope.Metadata["Is-Delivered"] = JsonSerializer.SerializeToElement(false);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> PostSerializationAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var encodedString = Convert.ToBase64String(bytes);
        return new ValueTask<string>(encodedString);
    }

    public ValueTask<string> PreDeserializationAsync(string message)
    {
        var bytes = Convert.FromBase64String(message);
        var decodedString = Encoding.UTF8.GetString(bytes);
        return new ValueTask<string>(decodedString);
    }

    public ValueTask PostDeserializationAsync(MessageEnvelope messageEnvelope)
    {
        messageEnvelope.Metadata["Is-Delivered"] = JsonSerializer.SerializeToElement(true);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A type-specific serialization callback that implements <see cref="ISerializationCallback{T}"/>
/// for <see cref="AddressInfo"/>. It extracts the ZipCode as a "subject" CloudEvents extension attribute.
/// No casting is required — the callback receives direct typed access to the message.
/// This demonstrates the use case from GitHub discussion #317.
/// </summary>
public class AddressInfoSubjectCallback : ISerializationCallback<AddressInfo>
{
    public ValueTask PreSerializationAsync(MessageEnvelope<AddressInfo> messageEnvelope)
    {
        // Zero casting — direct typed access to the message payload
        messageEnvelope.Metadata["subject"] = JsonSerializer.SerializeToElement(messageEnvelope.Message.ZipCode);
        return ValueTask.CompletedTask;
    }
}
