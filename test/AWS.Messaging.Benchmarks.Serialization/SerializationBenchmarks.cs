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
    private IEnvelopeSerializer _standardSerializerWithJsonContext;
    private IEnvelopeSerializer _jsonWriterSerializerWithJsonContext;
    private IEnvelopeSerializer _jsonWriterSerializerWithJsonContextUnsafe;
    private MessageEnvelope<AddressInfoListEnvelope> _envelope;
    private Mock<IDateTimeHandler> _mockDateTimeHandler;

    [Params(1, 10, 100, 1000)]
    public int ItemCount;

    [GlobalSetup]
    public void Setup()
    {
        _mockDateTimeHandler = new Mock<IDateTimeHandler>();
        var testDate = new DateTime(2023, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        _mockDateTimeHandler.Setup(x => x.GetUtcNow()).Returns(testDate);

        CreateStandardSerializer();
        CreateStandardSerializerWithJsonContext();
        CreateJsonWriterSerializerWithJsonContext();
        CreateJsonWriterSerializerWithJsonContextUnsafe();
        CreateJsonWriterSerializer();

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

    private void CreateStandardSerializer()
    {
        var _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
        });

        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), _mockDateTimeHandler.Object));

        _standardSerializer = _serviceCollection.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();
    }

    private void CreateStandardSerializerWithJsonContext()
    {
        var _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(new AddressInfoListEnvelopeSerializerContext(), builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
        });
        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), _mockDateTimeHandler.Object));
        _standardSerializerWithJsonContext = _serviceCollection.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();
    }

    private void CreateJsonWriterSerializerWithJsonContext()
    {
        var _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(new AddressInfoListEnvelopeSerializerContext(), builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
            builder.EnableExperimentalFeatures();
        });
        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), _mockDateTimeHandler.Object));
        _jsonWriterSerializerWithJsonContext = _serviceCollection.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();
    }

    private void CreateJsonWriterSerializerWithJsonContextUnsafe()
    {
        var _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(new AddressInfoListEnvelopeSerializerContext(), builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
            builder.EnableExperimentalFeatures();
            builder.ConfigureSerializationOptions(options =>
            {
                options.CleanRentedBuffers = false; // Disable cleaning rented buffers for performance
            });
        });
        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), _mockDateTimeHandler.Object));
        _jsonWriterSerializerWithJsonContextUnsafe = _serviceCollection.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();
    }

    private void CreateJsonWriterSerializer()
    {
        var _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<AddressInfoListEnvelope>("sqsQueueUrl", "addressInfoList");
            builder.AddMessageHandler<AddressInfoListHandler, AddressInfoListEnvelope>("addressInfoList");
            builder.AddMessageSource("/aws/messaging");
            builder.EnableExperimentalFeatures();
        });
        _serviceCollection.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), _mockDateTimeHandler.Object));
        _jsonWriterSerializer = _serviceCollection.BuildServiceProvider().GetRequiredService<IEnvelopeSerializer>();
    }

    [Benchmark(Baseline = true)]
    public async Task<string> StandardSerializer()
    {
        return await _standardSerializer.SerializeAsync(_envelope);
    }

    [Benchmark]
    public async Task<string> StandardSerializerWithJsonContext()
    {
        return await _standardSerializerWithJsonContext.SerializeAsync(_envelope);
    }

    [Benchmark]
    public async Task<string> JsonWriterSerializer()
    {
        return await _jsonWriterSerializer.SerializeAsync(_envelope);
    }

    [Benchmark]
    public async Task<string> JsonWriterSerializerWithJsonContext()
    {
        return await _jsonWriterSerializerWithJsonContext.SerializeAsync(_envelope);
    }

    [Benchmark]
    public async Task<string> JsonWriterSerializerWithJsonContextUnsafe()
    {
        return await _jsonWriterSerializerWithJsonContextUnsafe.SerializeAsync(_envelope);
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
