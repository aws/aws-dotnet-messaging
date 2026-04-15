using System.Text.Json;
using System.Text.Json.Serialization;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VSDiagnostics;

namespace AWS.Messaging.Benchmarks;
[CPUUsageDiagnoser]
public class MessageSerializerBenchmarks
{
    private MessageSerializer _serializer = null!;
    private MessageSerializer _serializerWithContext = null!;
    private SampleMessage _message = null!;
    private string _serializedJson = null!;
    [GlobalSetup]
    public void Setup()
    {
        _message = new SampleMessage
        {
            FirstName = "Bob",
            LastName = "Stone",
            Age = 30,
            Email = "bob.stone@example.com",
            IsActive = true,
            Address = new SampleAddress
            {
                Unit = 12,
                Street = "Prince St",
                ZipCode = "00001",
                City = "New York",
                State = "NY"
            },
            Tags = new[]
            {
                "customer",
                "premium",
                "active"
            }
        };
        var nullContext = new NullMessageJsonSerializerContextContainer();
        _serializer = new MessageSerializer(new NullLogger<MessageSerializer>(), new MessageConfiguration(), nullContext);
        var contextContainer = new DefaultMessageJsonSerializerContextContainer(SampleJsonContext.Default);
        _serializerWithContext = new MessageSerializer(new NullLogger<MessageSerializer>(), new MessageConfiguration(), contextContainer);
        _serializedJson = ((IMessageSerializer)_serializer).Serialize(_message).Data;
    }

    [Benchmark]
    public MessageSerializerResults Serialize()
    {
        return ((IMessageSerializer)_serializer).Serialize(_message);
    }

    [Benchmark]
    public MessageSerializerResults Serialize_WithJsonContext()
    {
        return ((IMessageSerializer)_serializerWithContext).Serialize(_message);
    }

    [Benchmark]
    public object Deserialize()
    {
        return ((IMessageSerializer)_serializer).Deserialize(_serializedJson, typeof(SampleMessage));
    }

    [Benchmark]
    public object Deserialize_WithJsonContext()
    {
        return ((IMessageSerializer)_serializerWithContext).Deserialize(_serializedJson, typeof(SampleMessage));
    }

    [Benchmark]
    public void SerializeToBuffer()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);
        ((IMessageSerializerUtf8JsonWriter)_serializer).SerializeToBuffer(writer, _message);
    }

    [Benchmark]
    public void SerializeToBuffer_WithJsonContext()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);
        ((IMessageSerializerUtf8JsonWriter)_serializerWithContext).SerializeToBuffer(writer, _message);
    }
}

public class SampleMessage
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int Age { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public SampleAddress? Address { get; set; }
    public string[]? Tags { get; set; }
}

public class SampleAddress
{
    public int Unit { get; set; }
    public string? Street { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
}

[JsonSerializable(typeof(SampleMessage))]
[JsonSourceGenerationOptions(Converters = [typeof(JsonStringEnumConverter)])]
internal partial class SampleJsonContext : JsonSerializerContext
{
}
