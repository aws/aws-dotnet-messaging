using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VSDiagnostics;

namespace AWS.Messaging.Benchmarks;
[MemoryDiagnoser]
[CPUUsageDiagnoser]
public class EnvelopeSerializerBenchmarks
{
    private IEnvelopeSerializer _envelopeSerializer = null!;
    private IEnvelopeSerializer _envelopeSerializerWithContext = null!;
    private Message _sqsMessage = null!;
    private Message _sqsMessageWithContext = null!;
    private Message _snsMessage = null!;
    private Message _eventBridgeMessage = null!;
    [GlobalSetup]
    public void Setup()
    {
        _envelopeSerializer = BuildEnvelopeSerializer(null);
        _envelopeSerializerWithContext = BuildEnvelopeSerializer(BenchmarkJsonContext.Default);
        var envelopeJson = JsonSerializer.Serialize(new { id = "bench-001", source = "/benchmark/test", specversion = "1.0", type = typeof(BenchmarkAddressInfo).FullName, time = DateTimeOffset.UtcNow, datacontenttype = "application/json", data = new BenchmarkAddressInfo { Unit = 42, Street = "Prince St", ZipCode = "00001" } });
        _sqsMessage = new Message
        {
            Body = envelopeJson,
            MessageId = "msg-1",
            ReceiptHandle = "rh-1"
        };
        _sqsMessageWithContext = new Message
        {
            Body = envelopeJson,
            MessageId = "msg-2",
            ReceiptHandle = "rh-2"
        };

        var snsWrapped = JsonSerializer.Serialize(new
        {
            Type = "Notification",
            MessageId = "sns-msg-1",
            TopicArn = "arn:aws:sns:us-east-1:123456789012:BenchTopic",
            Subject = "BenchSubject",
            Timestamp = DateTimeOffset.UtcNow,
            UnsubscribeURL = "https://example.com/unsub",
            Message = envelopeJson
        });
        _snsMessage = new Message { Body = snsWrapped, MessageId = "msg-3", ReceiptHandle = "rh-3" };

        var ebWrapped = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["version"] = "0",
            ["id"] = "eb-evt-1",
            ["source"] = "bench.source",
            ["detail-type"] = "BenchDetail",
            ["time"] = DateTimeOffset.UtcNow,
            ["account"] = "123456789012",
            ["region"] = "us-east-1",
            ["resources"] = new[] { "arn:aws:resource:1" },
            ["detail"] = JsonSerializer.Deserialize<JsonElement>(envelopeJson)
        });
        _eventBridgeMessage = new Message { Body = ebWrapped, MessageId = "msg-4", ReceiptHandle = "rh-4" };
    }

    private static IEnvelopeSerializer BuildEnvelopeSerializer(JsonSerializerContext? jsonContext)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        if (jsonContext != null)
        {
            services.AddAWSMessageBus(jsonContext, builder =>
            {
                builder.AddMessageHandler<BenchmarkAddressInfoHandler, BenchmarkAddressInfo>();
            });
        }
        else
        {
            services.AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<BenchmarkAddressInfoHandler, BenchmarkAddressInfo>();
            });
        }
        var mockDateTimeHandler = new DateTimeHandler();
        services.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), mockDateTimeHandler));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IEnvelopeSerializer>();
    }

    [Benchmark]
    public ConvertToEnvelopeResult Deserialize_Envelope()
    {
        return _envelopeSerializer.ConvertToEnvelopeAsync(_sqsMessage).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ConvertToEnvelopeResult Deserialize_Envelope_WithJsonContext()
    {
        return _envelopeSerializerWithContext.ConvertToEnvelopeAsync(_sqsMessageWithContext).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ConvertToEnvelopeResult Deserialize_SNS_Wrapped()
    {
        return _envelopeSerializer.ConvertToEnvelopeAsync(_snsMessage).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ConvertToEnvelopeResult Deserialize_EventBridge_Wrapped()
    {
        return _envelopeSerializer.ConvertToEnvelopeAsync(_eventBridgeMessage).GetAwaiter().GetResult();
    }
}

public class BenchmarkAddressInfo
{
    public int Unit { get; set; }
    public string? Street { get; set; }
    public string? ZipCode { get; set; }
}

public class BenchmarkAddressInfoHandler : IMessageHandler<BenchmarkAddressInfo>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<BenchmarkAddressInfo> messageEnvelope, CancellationToken token = default)
    {
        return Task.FromResult(MessageProcessStatus.Success());
    }
}

[JsonSerializable(typeof(BenchmarkAddressInfo))]
[JsonSourceGenerationOptions(Converters = [typeof(JsonStringEnumConverter)])]
internal partial class BenchmarkJsonContext : JsonSerializerContext
{
}
