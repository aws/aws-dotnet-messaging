// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public class EnvelopeDeserializerTests
{
    private readonly IServiceCollection _serviceCollection;
    private readonly DateTimeOffset _testdate = new DateTime(year: 2000, month: 12, day: 5, hour: 10, minute: 30, second: 55, DateTimeKind.Utc);

    public EnvelopeDeserializerTests()
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
    public async Task ConvertToEnvelope_NoOuterEnvelope_In_SQSMessageBody()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "66659d05-e4ff-462f-81c4-09e560e66a5c",
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
        var sqsMessage = new Message
        {
            Body = await envelopeSerializer.SerializeAsync(messageEnvelope),
            ReceiptHandle = "receipt-handle",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
            Attributes = new Dictionary<string, string>()
        };
        sqsMessage.MessageAttributes.Add("attr1", new MessageAttributeValue { DataType = "String", StringValue = "val1" });
        sqsMessage.Attributes.Add("MessageGroupId", "group-123");
        sqsMessage.Attributes.Add("MessageDeduplicationId", "dedup-123");

        // ACT
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);

        // ASSERT
        var envelope = (MessageEnvelope<AddressInfo>)result.Envelope;
        Assert.NotNull(envelope);
        Assert.Equal(_testdate, envelope.TimeStamp);
        Assert.Equal("1.0", envelope.Version);
        Assert.Equal("/aws/messaging", envelope.Source?.ToString());
        Assert.Equal("addressInfo", envelope.MessageTypeIdentifier);

        var addressInfo = envelope.Message;
        Assert.Equal("Prince St", addressInfo?.Street);
        Assert.Equal(123, addressInfo?.Unit);
        Assert.Equal("00001", addressInfo?.ZipCode);

        var subscribeMapping = result.Mapping;
        Assert.NotNull(subscribeMapping);
        Assert.Equal("addressInfo", subscribeMapping.MessageTypeIdentifier);
        Assert.Equal(typeof(AddressInfo), subscribeMapping.MessageType);
        Assert.Equal(typeof(AddressInfoHandler), subscribeMapping.HandlerType);

        var sqsMetadata = envelope.SQSMetadata!;
        Assert.Equal("receipt-handle", sqsMetadata.ReceiptHandle);
        Assert.Equal("group-123", sqsMetadata.MessageGroupId);
        Assert.Equal("dedup-123", sqsMetadata.MessageDeduplicationId);
        Assert.Equal("String", sqsMetadata.MessageAttributes!["attr1"].DataType);
        Assert.Equal("val1", sqsMetadata.MessageAttributes["attr1"].StringValue);
    }

    [Fact]
    public async Task ConvertToEnvelope_With_SNSOuterEnvelope_In_SQSMessageBody()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        var innerMessageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "66659d05-e4ff-462f-81c4-09e560e66a5c",
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

        var outerMessageEnvelope = new Dictionary<string, object>
        {
            { "Type", "Notification" },
            { "MessageId", "abcd-123" },
            { "TopicArn", "arn:aws:sns:us-east-2:111122223333:ExampleTopic1" },
            { "Subject", "TestSubject" },
            { "Timestamp", _testdate },
            { "SignatureVersion", "1" },
            { "Signature", "abcdef33242" },
            { "SigningCertURL", "https://sns.us-east-2.amazonaws.com/SimpleNotificationService-010a507c1833636cd94bdb98bd93083a.pem" },
            { "UnsubscribeURL", "https://www.click-here.com" },
            { "Message", await envelopeSerializer.SerializeAsync(innerMessageEnvelope) },
            { "MessageAttributes", new Dictionary<string, Amazon.SimpleNotificationService.Model.MessageAttributeValue>
            {
                { "attr1", new Amazon.SimpleNotificationService.Model.MessageAttributeValue{ DataType = "String", StringValue = "val1"} },
                { "attr2", new Amazon.SimpleNotificationService.Model.MessageAttributeValue{ DataType = "Number", StringValue = "3"} }
            } }
        };

        var sqsMessage = new Message
        {
            Body = JsonSerializer.Serialize(outerMessageEnvelope),
        };

        // ACT
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);

        // ASSERT
        var envelope = (MessageEnvelope<AddressInfo>)result.Envelope;
        Assert.NotNull(envelope);
        Assert.Equal(_testdate, envelope.TimeStamp);
        Assert.Equal("1.0", envelope.Version);
        Assert.Equal("/aws/messaging", envelope.Source?.ToString());
        Assert.Equal("addressInfo", envelope.MessageTypeIdentifier);

        var addressInfo = envelope.Message;
        Assert.Equal("Prince St", addressInfo?.Street);
        Assert.Equal(123, addressInfo?.Unit);
        Assert.Equal("00001", addressInfo?.ZipCode);

        var subscribeMapping = result.Mapping;
        Assert.NotNull(subscribeMapping);
        Assert.Equal("addressInfo", subscribeMapping.MessageTypeIdentifier);
        Assert.Equal(typeof(AddressInfo), subscribeMapping.MessageType);
        Assert.Equal(typeof(AddressInfoHandler), subscribeMapping.HandlerType);

        var snsMetadata = envelope.SNSMetadata!;
        Assert.Equal("arn:aws:sns:us-east-2:111122223333:ExampleTopic1", snsMetadata.TopicArn);
        Assert.Equal("https://www.click-here.com", snsMetadata.UnsubscribeURL);
        Assert.Equal("abcd-123", snsMetadata.MessageId);
        Assert.Equal("TestSubject", snsMetadata.Subject);
        Assert.Equal(_testdate, snsMetadata.Timestamp);
        Assert.Equal("String", snsMetadata.MessageAttributes!["attr1"].DataType);
        Assert.Equal("val1", snsMetadata.MessageAttributes["attr1"].StringValue);
        Assert.Equal("Number", snsMetadata.MessageAttributes["attr2"].DataType);
        Assert.Equal("3", snsMetadata.MessageAttributes["attr2"].StringValue);
    }

    [Fact]
    public async Task ConvertToEnvelope_With_EventBridgeOuterEnvelope_In_SQSMessageBody()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        var innerMessageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "66659d05-e4ff-462f-81c4-09e560e66a5c",
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

        var outerMessageEnvelope = new Dictionary<string, object>
        {
            { "version", "0" },
            { "id", "abcd-123" },
            { "source", "some-source" },
            { "detail-type", "address" },
            { "time", _testdate },
            { "account", "123456789123" },
            { "region", "us-west-2" },
            { "resources", new List<string>{ "arn1", "arn2" } },
            { "detail", innerMessageEnvelope }, // The "detail" property is set as a JSON object and not a string.
        };

        var sqsMessage = new Message
        {
            Body = JsonSerializer.Serialize(outerMessageEnvelope)
        };

        // ACT
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);

        // ASSERT
        var envelope = (MessageEnvelope<AddressInfo>)result.Envelope;
        Assert.NotNull(envelope);
        Assert.Equal(_testdate, envelope.TimeStamp);
        Assert.Equal("1.0", envelope.Version);
        Assert.Equal("/aws/messaging", envelope.Source?.ToString());
        Assert.Equal("addressInfo", envelope.MessageTypeIdentifier);

        var addressInfo = envelope.Message;
        Assert.Equal("Prince St", addressInfo?.Street);
        Assert.Equal(123, addressInfo?.Unit);
        Assert.Equal("00001", addressInfo?.ZipCode);

        var subscribeMapping = result.Mapping;
        Assert.NotNull(subscribeMapping);
        Assert.Equal("addressInfo", subscribeMapping.MessageTypeIdentifier);
        Assert.Equal(typeof(AddressInfo), subscribeMapping.MessageType);
        Assert.Equal(typeof(AddressInfoHandler), subscribeMapping.HandlerType);

        var eventBridgeMetadata = envelope.EventBridgeMetadata!;
        Assert.Equal("abcd-123", eventBridgeMetadata.EventId);
        Assert.Equal("some-source", eventBridgeMetadata.Source);
        Assert.Equal("address", eventBridgeMetadata.DetailType);
        Assert.Equal(_testdate, eventBridgeMetadata.Time);
        Assert.Equal("123456789123", eventBridgeMetadata.AWSAccount);
        Assert.Equal("us-west-2", eventBridgeMetadata.AWSRegion);
        Assert.Equal(new List<string> { "arn1", "arn2" }, eventBridgeMetadata.Resources);
    }

    [Fact]
    public async Task ConvertToEnvelope_MissingSubscriberMapping_ThrowsException()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageEnvelope = new MessageEnvelope<ChatMessage>
        {
            Id = "66659d05-e4ff-462f-81c4-09e560e66a5c",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "chatmessage",
            TimeStamp = _testdate,
            Message = new ChatMessage
            {
                MessageDescription = "This is a test message"
            }

        };
        var sqsMessage = new Message
        {
            Body = await envelopeSerializer.SerializeAsync(messageEnvelope),
            ReceiptHandle = "receipt-handle"
        };

        // ACT and ASSERT
        // This throws an exception because no subscriber is configured against the ChatMessage type.
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        await Assert.ThrowsAsync<FailedToCreateMessageEnvelopeException>(async () => await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConvertToEnvelopeAsync_DataMessageLogging_WithError(bool dataMessageLogging)
    {
        // ARRANGE
        var logger = new Mock<ILogger<EnvelopeDeserializer>>();
        var messageConfiguration = new MessageConfiguration { LogMessageContent = dataMessageLogging };
        var messageSerializer = new Mock<IMessageSerializer>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var classifier = new MessageTypeClassifier(new IWrapperReader[] { new SNSWrapperReader(), new EventBridgeWrapperReader() });
        var sqsWrapperReader = new SQSWrapperReader();
        var envelopeDeserializer = new EnvelopeDeserializer(
            logger.Object,
            messageConfiguration,
            messageSerializer.Object,
            classifier,
            sqsWrapperReader);

        // Create an SQS message with invalid JSON that will cause Utf8JsonReader to fail
        var sqsMessage = new Message
        {
            Body = "invalid json {",
            ReceiptHandle = "receipt-handle"
        };

        // ACT & ASSERT
        var exception = await Assert.ThrowsAsync<FailedToCreateMessageEnvelopeException>(
            async () => await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage));

        Assert.Equal("Failed to create MessageEnvelope", exception.Message);

        if (dataMessageLogging)
        {
            Assert.NotNull(exception.InnerException);
            Assert.IsAssignableFrom<JsonException>(exception.InnerException); // JsonReaderException is not directly usable so just verify that its a generic json exception for now.
        }
        else
        {
            Assert.Null(exception.InnerException);
        }

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
    public async Task ConvertToEnvelope_WithMetadata_PreservesOnlyExpectedMetadataProperties()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        // Create a JSON string with both standard envelope properties and custom metadata
        var testData = new
        {
            id = "test-id-123",
            source = "/aws/messaging",
            specversion = "1.0",
            type = "addressInfo",
            time = "2000-12-05T10:30:55+00:00",
            data = new AddressInfo
            {
                Unit = 123,
                Street = "Prince St",
                ZipCode = "00001"
            },
            customString = "test-value",
            customNumber = 42,
            customBoolean = true,
            customObject = new { nestedKey = "nestedValue" }
        };

        var json = JsonSerializer.Serialize(testData);

        var sqsMessage = new Message
        {
            Body = json
        };

        // ACT
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);
        var deserializedEnvelope = (MessageEnvelope<AddressInfo>)result.Envelope;

        // ASSERT
        Assert.NotNull(deserializedEnvelope);
        Assert.NotNull(deserializedEnvelope.Metadata);

        // Verify standard envelope properties
        Assert.Equal("test-id-123", deserializedEnvelope.Id);
        Assert.Equal("/aws/messaging", deserializedEnvelope.Source.ToString());
        Assert.Equal("1.0", deserializedEnvelope.Version);
        Assert.Equal("addressInfo", deserializedEnvelope.MessageTypeIdentifier);

        // Define expected metadata properties
        var expectedMetadataKeys = new HashSet<string>
        {
            "customString",
            "customNumber",
            "customBoolean",
            "customObject"
        };

        // Verify metadata contains exactly the expected keys
        Assert.Equal(expectedMetadataKeys, deserializedEnvelope.Metadata.Keys.ToHashSet());

        // Verify each metadata property has the correct value
        Assert.Equal("test-value", deserializedEnvelope.Metadata["customString"].GetString());
        Assert.Equal(42, deserializedEnvelope.Metadata["customNumber"].GetInt32());
        Assert.True(deserializedEnvelope.Metadata["customBoolean"].GetBoolean());
        Assert.Equal("nestedValue", deserializedEnvelope.Metadata["customObject"].GetProperty("nestedKey").GetString());

        // Verify standard envelope properties are not in metadata
        Assert.False(deserializedEnvelope.Metadata.ContainsKey("id"));
        Assert.False(deserializedEnvelope.Metadata.ContainsKey("source"));
        Assert.False(deserializedEnvelope.Metadata.ContainsKey("specversion"));
        Assert.False(deserializedEnvelope.Metadata.ContainsKey("type"));
        Assert.False(deserializedEnvelope.Metadata.ContainsKey("time"));
        Assert.False(deserializedEnvelope.Metadata.ContainsKey("data"));

        // Verify message content
        Assert.NotNull(deserializedEnvelope.Message);
        Assert.Equal("Prince St", deserializedEnvelope.Message.Street);
        Assert.Equal(123, deserializedEnvelope.Message.Unit);
        Assert.Equal("00001", deserializedEnvelope.Message.ZipCode);
    }

    [Fact]
    public async Task ConvertToEnvelope_NullSubscriberMapping_ThrowsException()
    {
        // ARRANGE
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();
        var messageEnvelope = new MessageEnvelope<AddressInfo>
        {
            Id = "66659d05-e4ff-462f-81c4-09e560e66a5c",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "unknownMessageType", // Using an unknown message type
            TimeStamp = _testdate,
            Message = new AddressInfo
            {
                Street = "Prince St",
                Unit = 123,
                ZipCode = "00001"
            }
        };

        var sqsMessage = new Message
        {
            Body = await envelopeSerializer.SerializeAsync(messageEnvelope),
            ReceiptHandle = "receipt-handle"
        };

        // ACT & ASSERT
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var exception = await Assert.ThrowsAsync<FailedToCreateMessageEnvelopeException>(
            async () => await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage)
        );

        // Verify the exception message
        Assert.Equal("Failed to create MessageEnvelope", exception.Message);

        // Verify the inner exception type and message
        Assert.IsType<InvalidDataException>(exception.InnerException);
        var innerException = exception.InnerException as InvalidDataException;
        Assert.NotNull(innerException);
        Assert.Contains("'unknownMessageType' is not a valid subscriber mapping.", innerException.Message);
        Assert.Contains("Available mappings:", innerException.Message);
        Assert.Contains("addressInfo", innerException.Message);
    }

    [Fact]
    public async Task ConvertToEnvelope_WithNonJsonContentType()
    {
        // ARRANGE
        var logger = new Mock<ILogger<EnvelopeSerializer>>();
        var deserializerLogger = new Mock<ILogger<EnvelopeDeserializer>>();
        var services = new ServiceCollection();
        services.AddAWSMessageBus(builder =>
        {
            builder.AddMessageHandler<PlainTextHandler, string>("plaintext");
        });
        var serviceProvider = services.BuildServiceProvider();
        var messageConfiguration = serviceProvider.GetRequiredService<IMessageConfiguration>();
        var messageSerializer = new Mock<IMessageSerializer>();
        var dateTimeHandler = new Mock<IDateTimeHandler>();
        var messageIdGenerator = new Mock<IMessageIdGenerator>();
        var messageSourceHandler = new Mock<IMessageSourceHandler>();
        var envelopeSerializer = new EnvelopeSerializer(logger.Object, messageConfiguration, messageSerializer.Object, dateTimeHandler.Object, messageIdGenerator.Object, messageSourceHandler.Object, serviceProvider);
        var envelopeDeserializer = new EnvelopeDeserializer(deserializerLogger.Object, messageConfiguration, messageSerializer.Object, serviceProvider.GetRequiredService<IMessageTypeClassifier>(), serviceProvider.GetRequiredService<ISQSWrapperReader>());
        var plainTextContent = "Hello, this is plain text content";
        var messageEnvelope = new MessageEnvelope<string>
        {
            Id = "id-123",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "plaintext",
            TimeStamp = _testdate,
            Message = plainTextContent
        };

        var serializedContent = JsonSerializer.Serialize(messageEnvelope.Message);
        var messageSerializeResults = new MessageSerializerResults(serializedContent, "text/plain");

        // Mock the serializer to return a specific string
        messageSerializer
            .Setup(x => x.Serialize(It.IsAny<object>()))
            .Returns(messageSerializeResults);

        messageSerializer
            .Setup(x => x.Deserialize(It.IsAny<string>(), typeof(string)))
            .Returns("Hello, this is plain text content");

        var sqsMessage = new Message
        {
            Body = await envelopeSerializer.SerializeAsync(messageEnvelope),
            ReceiptHandle = "receipt-handle"
        };

        // ACT
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);

        // ASSERT
        var envelope = (MessageEnvelope<string>)result.Envelope;
        Assert.NotNull(envelope);
        Assert.Equal(_testdate, envelope.TimeStamp);
        Assert.Equal("1.0", envelope.Version);
        Assert.Equal("/aws/messaging", envelope.Source?.ToString());
        Assert.Equal("plaintext", envelope.MessageTypeIdentifier);
        Assert.Equal("text/plain", envelope.DataContentType);
        Assert.Equal(plainTextContent, envelope.Message);
    }

    [Fact]
    public async Task ConvertToEnvelope_WithCustomJsonContentType()
    {
        // ARRANGE
        var mockMessageSerializer = new Mock<IMessageSerializer>();
        _serviceCollection.RemoveAll<IMessageSerializer>();
        _serviceCollection.AddSingleton(mockMessageSerializer.Object);

        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var envelopeSerializer = serviceProvider.GetRequiredService<IEnvelopeSerializer>();

        var message = new AddressInfo
        {
            Street = "Prince St",
            Unit = 123,
            ZipCode = "00001"
        };

        // Mock serializer behavior
        var serializedMessage = JsonSerializer.Serialize(message);
        mockMessageSerializer
            .Setup(x => x.Serialize(It.IsAny<object>()))
            .Returns(new MessageSerializerResults(serializedMessage, "application/ld+json"));

        mockMessageSerializer
            .Setup(x => x.Deserialize(It.IsAny<string>(), typeof(AddressInfo)))
            .Returns(message);

        // Create the envelope
        var envelope = new MessageEnvelope<AddressInfo>
        {
            Id = "test-id-123",
            Source = new Uri("/aws/messaging", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfo",
            TimeStamp = _testdate,
            Message = message
        };

        // Serialize the envelope to SQS message
        var sqsMessage = new Message
        {
            Body = await envelopeSerializer.SerializeAsync(envelope),
            ReceiptHandle = "receipt-handle"
        };

        // ACT
        var envelopeDeserializer = serviceProvider.GetRequiredService<IEnvelopeDeserializer>();
        var result = await envelopeDeserializer.ConvertToEnvelopeAsync(sqsMessage);

        // ASSERT
        var deserializedEnvelope = (MessageEnvelope<AddressInfo>)result.Envelope;
        Assert.NotNull(deserializedEnvelope);

        // Verify the content type was preserved
        Assert.Equal("application/ld+json", deserializedEnvelope.DataContentType);

        // Verify the message was correctly deserialized
        Assert.NotNull(deserializedEnvelope.Message);
        Assert.Equal("Prince St", deserializedEnvelope.Message.Street);
        Assert.Equal(123, deserializedEnvelope.Message.Unit);
        Assert.Equal("00001", deserializedEnvelope.Message.ZipCode);

        // Verify other envelope properties
        Assert.Equal("test-id-123", deserializedEnvelope.Id);
        Assert.Equal("/aws/messaging", deserializedEnvelope.Source?.ToString());
        Assert.Equal("1.0", deserializedEnvelope.Version);
        Assert.Equal("addressInfo", deserializedEnvelope.MessageTypeIdentifier);
        Assert.Equal(_testdate, deserializedEnvelope.TimeStamp);

        // Verify the serializer was called with correct parameters
        mockMessageSerializer.Verify(
            x => x.Serialize(It.IsAny<object>()),
            Times.Once);

        mockMessageSerializer.Verify(
            x => x.Deserialize(It.IsAny<string>(), typeof(AddressInfo)),
            Times.Once);
    }
}
