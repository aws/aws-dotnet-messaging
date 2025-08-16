// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests;

public class MessageSerializerUtf8JsonWriterTests
{
    private readonly FakeLogger<MessageSerializerUtf8JsonWriter> _logger;

    public MessageSerializerUtf8JsonWriterTests()
    {
        _logger = new FakeLogger<MessageSerializerUtf8JsonWriter>();
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Serialize(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        // ARRANGE
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(new NullLogger<MessageSerializerUtf8JsonWriter>(), new MessageConfiguration(), messageJsonSerializerContextFactory);
        var person = new PersonInfo
        {
            FirstName= "Bob",
            LastName = "Stone",
            Age= 30,
            Gender = Gender.Male,
            Address= new AddressInfo
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001"
            }
        };

        // ACT
        var result = serializer.Serialize(person);

        // ASSERT
        var expectedString = "{\"FirstName\":\"Bob\",\"LastName\":\"Stone\",\"Age\":30,\"Gender\":\"Male\",\"Address\":{\"Unit\":12,\"Street\":\"Prince St\",\"ZipCode\":\"00001\"}}";
        Assert.Equal(expectedString, result.Data);
        Assert.Equal("application/json", result.ContentType);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Serialize_NoDataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration();
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var person = new PersonInfo
        {
            FirstName= "Bob",
            LastName = "Stone",
            Age= 30,
            Gender = Gender.Male,
            Address= new AddressInfo
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001"
            }
        };

        var jsonString = serializer.Serialize(person).Data;

        Assert.Equal(1, _logger.Collector.Count);

        var lastRecord = _logger.LatestRecord;
        Assert.Equal(0, lastRecord.Id.Id);
        Assert.Equal(LogLevel.Trace, lastRecord.Level);
        Assert.Equal("Serialized the message object to a raw string with a content length of " + jsonString.Length + ".", lastRecord.Message);
        Assert.Null(lastRecord.Exception);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Serialize_DataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var person = new PersonInfo
        {
            FirstName= "Bob",
            LastName = "Stone",
            Age= 30,
            Gender = Gender.Male,
            Address= new AddressInfo
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001"
            }
        };

        serializer.Serialize(person);

        Assert.Equal(1, _logger.Collector.Count);
        var lastRecord = _logger.LatestRecord;
        Assert.Equal(0, lastRecord.Id.Id);
        Assert.Equal(LogLevel.Trace, lastRecord.Level);
        Assert.Equal("Serialized the message object as the following raw string:\n{\"FirstName\":\"Bob\",\"LastName\":\"Stone\",\"Age\":30,\"Gender\":\"Male\",\"Address\":{\"Unit\":12,\"Street\":\"Prince St\",\"ZipCode\":\"00001\"}}", lastRecord.Message);
        Assert.Null(lastRecord.Exception);
    }

    public class UnsupportedType
    {
        public string? Name { get; set; }
        public UnsupportedType? Type { get; set; }
    }

    [Fact]
    public void Serialize_NoDataMessageLogging_WithError()
    {
        var messageConfiguration = new MessageConfiguration();

        // This test doesn't use the JsonSerializationContext version because System.Text.Json
        // doesn't detect circular references like the reflection version.
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, new NullMessageJsonSerializerContextContainer());

        // Creating an object with circular dependency to force an exception in the JsonSerializer.Serialize method.
        var unsupportedType1 = new UnsupportedType { Name = "type1" };
        var unsupportedType2 = new UnsupportedType { Name = "type2" };
        unsupportedType1.Type = unsupportedType2;
        unsupportedType2.Type = unsupportedType1;

        var exception = Assert.Throws<FailedToSerializeApplicationMessageException>(() => serializer.Serialize(unsupportedType1));

        Assert.Equal("Failed to serialize application message into a string", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Serialize_DataMessageLogging_WithError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        // Creating an object with circular dependency to force an exception in the JsonSerializer.Serialize method.
        var unsupportedType1 = new UnsupportedType { Name = "type1" };
        var unsupportedType2 = new UnsupportedType { Name = "type2" };
        unsupportedType1.Type = unsupportedType2;
        unsupportedType2.Type = unsupportedType1;

        var exception = Assert.Throws<FailedToSerializeApplicationMessageException>(() => serializer.Serialize(unsupportedType1));

        Assert.Equal("Failed to serialize application message into a string", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        // ARRANGE
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(new NullLogger<MessageSerializerUtf8JsonWriter>(), new MessageConfiguration(), messageJsonSerializerContextFactory);
        var jsonString =
            @"{
                   ""FirstName"":""Bob"",
                   ""LastName"":""Stone"",
                   ""Age"":30,
                   ""Gender"":""Male"",
                   ""Address"":{
                      ""Unit"":12,
                      ""Street"":""Prince St"",
                      ""ZipCode"":""00001""
                   }
                }";

        // ACT
        var message = serializer.Deserialize<PersonInfo>(jsonString);

        // ASSERT
        Assert.Equal("Bob", message.FirstName);
        Assert.Equal("Stone", message.LastName);
        Assert.Equal(30, message.Age);
        Assert.Equal(Gender.Male, message.Gender);
        Assert.Equal(12, message.Address?.Unit);
        Assert.Equal("Prince St", message.Address?.Street);
        Assert.Equal("00001", message.Address?.ZipCode);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize_NoDataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration();
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var jsonString =
            @"{
                   ""FirstName"":""Bob"",
                   ""LastName"":""Stone"",
                   ""Age"":30,
                   ""Gender"":""Male"",
                   ""Address"":{
                      ""Unit"":12,
                      ""Street"":""Prince St"",
                      ""ZipCode"":""00001""
                   }
                }";

        serializer.Deserialize<PersonInfo>(jsonString);

        Assert.Equal(1, _logger.Collector.Count);
        var lastRecord = _logger.LatestRecord;
        Assert.Equal(0, lastRecord.Id.Id);
        Assert.Equal(LogLevel.Trace, lastRecord.Level);
        Assert.Equal("Deserializing the following message into type 'AWS.Messaging.UnitTests.Models.PersonInfo'", lastRecord.Message);
        Assert.Null(lastRecord.Exception);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize_DataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var jsonString = "{\"FirstName\":\"Bob\"}";

        serializer.Deserialize<PersonInfo>(jsonString);

        Assert.Equal(1, _logger.Collector.Count);
        var lastRecord = _logger.LatestRecord;
        Assert.Equal(0, lastRecord.Id.Id);
        Assert.Equal(LogLevel.Trace, lastRecord.Level);
        Assert.Equal("Deserializing the following message into type 'AWS.Messaging.UnitTests.Models.PersonInfo':\n{\"FirstName\":\"Bob\"}", lastRecord.Message);
        Assert.Null(lastRecord.Exception);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize_NoDataMessageLogging_WithError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration();
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var jsonString = "{'FirstName':'Bob'}";

        var exception = Assert.Throws<FailedToDeserializeApplicationMessageException>(() => serializer.Deserialize<PersonInfo>(jsonString));

        Assert.Equal("Failed to deserialize application message into an instance of AWS.Messaging.UnitTests.Models.PersonInfo.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize_DataMessageLogging_WithError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializer serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var jsonString = "{'FirstName':'Bob'}";

        var exception = Assert.Throws<FailedToDeserializeApplicationMessageException>(() => serializer.Deserialize<PersonInfo>(jsonString));

        Assert.Equal("Failed to deserialize application message into an instance of AWS.Messaging.UnitTests.Models.PersonInfo.", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    // New tests for SerializeToBuffer

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void SerializeToBuffer_WritesExpectedJson(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        // ARRANGE
        IMessageSerializerUtf8JsonWriter serializer = new MessageSerializerUtf8JsonWriter(new NullLogger<MessageSerializerUtf8JsonWriter>(), new MessageConfiguration(), messageJsonSerializerContextFactory);
        var person = new PersonInfo
        {
            FirstName= "Bob",
            LastName = "Stone",
            Age= 30,
            Gender = Gender.Male,
            Address= new AddressInfo
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001"
            }
        };

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

        // ACT
        serializer.SerializeToBuffer(writer, person);
        writer.Flush();

        var jsonString = Encoding.UTF8.GetString(buffer.WrittenSpan);

        // ASSERT
        var expectedString = "{\"FirstName\":\"Bob\",\"LastName\":\"Stone\",\"Age\":30,\"Gender\":\"Male\",\"Address\":{\"Unit\":12,\"Street\":\"Prince St\",\"ZipCode\":\"00001\"}}";
        Assert.Equal(expectedString, jsonString);
        Assert.Equal("application/json", serializer.ContentType);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void SerializeToBuffer_NoDataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration();
        IMessageSerializerUtf8JsonWriter serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var person = new PersonInfo
        {
            FirstName= "Bob",
            LastName = "Stone",
            Age= 30,
            Gender = Gender.Male,
            Address= new AddressInfo
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001"
            }
        };

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

        serializer.SerializeToBuffer(writer, person);
        writer.Flush();

        var jsonString = Encoding.UTF8.GetString(buffer.WrittenSpan);
        var contentLength = Encoding.UTF8.GetByteCount(jsonString);

        Assert.Equal(1, _logger.Collector.Count);
        var lastRecord = _logger.LatestRecord;
        Assert.Equal(0, lastRecord.Id.Id);
        Assert.Equal(LogLevel.Trace, lastRecord.Level);
        Assert.Equal($"Serialized the message object to a raw string with a content length of {contentLength}.", lastRecord.Message);
        Assert.Null(lastRecord.Exception);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void SerializeToBuffer_DataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializerUtf8JsonWriter serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        var person = new PersonInfo
        {
            FirstName= "Bob",
            LastName = "Stone",
            Age= 30,
            Gender = Gender.Male,
            Address= new AddressInfo
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001"
            }
        };

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

        serializer.SerializeToBuffer(writer, person);
        writer.Flush();

        var jsonString = Encoding.UTF8.GetString(buffer.WrittenSpan);
        var contentLength = Encoding.UTF8.GetByteCount(jsonString);

        Assert.Equal(1, _logger.Collector.Count);
        var lastRecord = _logger.LatestRecord;
        Assert.Equal(0, lastRecord.Id.Id);
        Assert.Equal(LogLevel.Trace, lastRecord.Level);
        Assert.Equal($"Serialized the message object to a raw string with a content length of {contentLength}.", lastRecord.Message);
        Assert.Null(lastRecord.Exception);
    }

    [Fact]
    public void SerializeToBuffer_NoDataMessageLogging_WithError()
    {
        var messageConfiguration = new MessageConfiguration();
        IMessageSerializerUtf8JsonWriter serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, new NullMessageJsonSerializerContextContainer());

        // Creating an object with circular dependency to force an exception in the JsonSerializer.Serialize method.
        var unsupportedType1 = new UnsupportedType { Name = "type1" };
        var unsupportedType2 = new UnsupportedType { Name = "type2" };
        unsupportedType1.Type = unsupportedType2;
        unsupportedType2.Type = unsupportedType1;

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

        var exception = Assert.Throws<FailedToSerializeApplicationMessageException>(() => serializer.SerializeToBuffer(writer, unsupportedType1));

        Assert.Equal("Failed to serialize application message into a string", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void SerializeToBuffer_DataMessageLogging_WithError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializerUtf8JsonWriter serializer = new MessageSerializerUtf8JsonWriter(_logger, messageConfiguration, messageJsonSerializerContextFactory);

        // Creating an object with circular dependency to force an exception in the JsonSerializer.Serialize method.
        var unsupportedType1 = new UnsupportedType { Name = "type1" };
        var unsupportedType2 = new UnsupportedType { Name = "type2" };
        unsupportedType1.Type = unsupportedType2;
        unsupportedType2.Type = unsupportedType1;

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

        var exception = Assert.Throws<FailedToSerializeApplicationMessageException>(() => serializer.SerializeToBuffer(writer, unsupportedType1));

        Assert.Equal("Failed to serialize application message into a string", exception.Message);
        Assert.NotNull(exception.InnerException);
    }
}
