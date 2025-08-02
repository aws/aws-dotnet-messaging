using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.UnitTests.Models;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace AWS.Messaging.Benchmarks.Serialization;

[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private IEnvelopeSerializer _jsonWriterSerializer;
    private IEnvelopeSerializer _standardSerializer;
    private MessageEnvelope<AddressInfoListEnvelope> _envelope;

    [Params(1, 10, 100, 1000)]
    public int ItemCount;

    [GlobalSetup]
    public void Setup()
    {
        var mockDateTimeHandler = new Mock<IDateTimeHandler>();
        var testDate = new DateTime(2023, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        mockDateTimeHandler.Setup(x => x.GetUtcNow()).Returns(testDate);

        var _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
        });

        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), mockDateTimeHandler.Object));

        _standardSerializer = _serviceCollection.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();

        var _serviceCollectionJsonWriter = new ServiceCollection();
        _serviceCollectionJsonWriter.AddLogging();
        _serviceCollectionJsonWriter.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
            builder.EnableExperimentalFeatures();
        });

        _serviceCollectionJsonWriter.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), mockDateTimeHandler.Object));

        _jsonWriterSerializer = _serviceCollectionJsonWriter.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();

        var items = new List<AddressInfo>(ItemCount);
        for (var i = 0; i < ItemCount; i++)
        {
            items.Add(new AddressInfo
            {
                Street = $"Street {i}",
                Unit = i,
                ZipCode = $"{10000 + i}"
            });
        }

        var message = new AddressInfoListEnvelope
        {
            Items = items
        };

        _envelope = new MessageEnvelope<AddressInfoListEnvelope>
        {
            Id = "id-123",
            Source = new Uri("/backend/service", UriKind.Relative),
            Version = "1.0",
            MessageTypeIdentifier = "addressInfoList",
            TimeStamp = DateTimeOffset.UtcNow,
            Message = message
        };
    }

    [Benchmark]
    public async Task<string> JsonWriterSerialize()
    {
        return await _jsonWriterSerializer.SerializeAsync(_envelope);
    }

    [Benchmark(Baseline = true)]
    public async Task<string> StandardSerialize()
    {
        return await _standardSerializer.SerializeAsync(_envelope);
    }
}

public class AddressInfoListEnvelope
{
    public List<AddressInfo> Items { get; set; } = [];
}

public class AddressInfoListHandler : IMessageHandler<AddressInfoListEnvelope>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<AddressInfoListEnvelope> messageEnvelope, CancellationToken token = default)
        => Task.FromResult(MessageProcessStatus.Success());
}
