// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests;

public class MessageSerializerTests
{
    private readonly Mock<ILogger<MessageSerializer>> _logger;

    public MessageSerializerTests()
    {
        _logger = new Mock<ILogger<MessageSerializer>>();
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Serialize(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        // ARRANGE
        IMessageSerializer serializer = new MessageSerializer(new NullLogger<MessageSerializer>(), new MessageConfiguration(), messageJsonSerializerContextFactory);
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
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

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

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Trace),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == $"Serialized the message object to a raw string with a content length of {jsonString.Length}."),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Serialize_DataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

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

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Trace),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, @type) =>
                    @object.ToString() ==
                    "Serialized the message object as the following raw string:\n{\"FirstName\":\"Bob\",\"LastName\":\"Stone\",\"Age\":30,\"Gender\":\"Male\",\"Address\":{\"Unit\":12,\"Street\":\"Prince St\",\"ZipCode\":\"00001\"}}"),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, new NullMessageJsonSerializerContextContainer());

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
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

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
        IMessageSerializer serializer = new MessageSerializer(new NullLogger<MessageSerializer>(), new MessageConfiguration(), messageJsonSerializerContextFactory);
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
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

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

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Trace),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == "Deserializing the following message into type 'AWS.Messaging.UnitTests.Models.PersonInfo'"),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize_DataMessageLogging_NoError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration{ LogMessageContent = true };
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

        var jsonString =
            @"{""FirstName"":""Bob""}";

        serializer.Deserialize<PersonInfo>(jsonString);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Trace),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == "Deserializing the following message into type 'AWS.Messaging.UnitTests.Models.PersonInfo':\n" +
                    @"{""FirstName"":""Bob""}"),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [ClassData(typeof(JsonSerializerContextClassData))]
    public void Deserialize_NoDataMessageLogging_WithError(IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory)
    {
        var messageConfiguration = new MessageConfiguration();
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

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
        IMessageSerializer serializer = new MessageSerializer(_logger.Object, messageConfiguration, messageJsonSerializerContextFactory);

        var jsonString = "{'FirstName':'Bob'}";

        var exception = Assert.Throws<FailedToDeserializeApplicationMessageException>(() => serializer.Deserialize<PersonInfo>(jsonString));

        Assert.Equal("Failed to deserialize application message into an instance of AWS.Messaging.UnitTests.Models.PersonInfo.", exception.Message);
        Assert.NotNull(exception.InnerException);
    }
}

[JsonSerializable(typeof(PersonInfo))]
[JsonSerializable(typeof(MessageSerializerTests.UnsupportedType))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
public partial class UnitTestsSerializerContext : JsonSerializerContext
{
}

public class JsonSerializerContextClassData : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object[] { new NullMessageJsonSerializerContextContainer() };
        yield return new object[] { new DefaultMessageJsonSerializerContextContainer(UnitTestsSerializerContext.Default) };
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
