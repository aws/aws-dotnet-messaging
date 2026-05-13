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

public enum PayloadSize { Small, Medium, Large }

[MemoryDiagnoser]
[CPUUsageDiagnoser]
public class EnvelopeSerializerBenchmarks
{
    [Params(PayloadSize.Small, PayloadSize.Medium, PayloadSize.Large)]
    public PayloadSize Payload { get; set; }

    private IEnvelopeSerializer _envelopeSerializer = null!;
    private Message _sqsMessage = null!;
    private Message _snsMessage = null!;
    private Message _eventBridgeMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _envelopeSerializer = BuildEnvelopeSerializer();

        var dataJson = Payload switch
        {
            PayloadSize.Small => JsonSerializer.Serialize(CreateSmallPayload()),
            PayloadSize.Medium => JsonSerializer.Serialize(CreateMediumPayload()),
            PayloadSize.Large => JsonSerializer.Serialize(CreateLargePayload()),
            _ => throw new ArgumentOutOfRangeException()
        };

        var messageType = Payload switch
        {
            PayloadSize.Small => typeof(SmallPayload).FullName!,
            PayloadSize.Medium => typeof(MediumPayload).FullName!,
            PayloadSize.Large => typeof(LargePayload).FullName!,
            _ => throw new ArgumentOutOfRangeException()
        };

        var envelopeJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = "bench-001",
            ["source"] = "/benchmark/test",
            ["specversion"] = "1.0",
            ["type"] = messageType,
            ["time"] = DateTimeOffset.UtcNow,
            ["datacontenttype"] = "application/json",
            ["data"] = JsonSerializer.Deserialize<JsonElement>(dataJson)
        });

        _sqsMessage = new Message { Body = envelopeJson, MessageId = "msg-1", ReceiptHandle = "rh-1" };

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

    private static IEnvelopeSerializer BuildEnvelopeSerializer()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddAWSMessageBus(builder =>
        {
            builder.AddMessageHandler<SmallPayloadHandler, SmallPayload>();
            builder.AddMessageHandler<MediumPayloadHandler, MediumPayload>();
            builder.AddMessageHandler<LargePayloadHandler, LargePayload>();
        });
        var mockDateTimeHandler = new DateTimeHandler();
        services.Replace(new ServiceDescriptor(typeof(IDateTimeHandler), mockDateTimeHandler));
        services.AddOptions<RentedBufferOptions>().Configure(options => options.CleanRentedBuffers = false);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IEnvelopeSerializer>();
    }

    [Benchmark]
    public async ValueTask<ConvertToEnvelopeResult> Deserialize_Envelope()
    {
        return await _envelopeSerializer.ConvertToEnvelopeAsync(_sqsMessage);
    }

    [Benchmark]
    public async ValueTask<ConvertToEnvelopeResult> Deserialize_SNS_Wrapped()
    {
        return await _envelopeSerializer.ConvertToEnvelopeAsync(_snsMessage);
    }

    [Benchmark]
    public async ValueTask<ConvertToEnvelopeResult> Deserialize_EventBridge_Wrapped()
    {
        return await _envelopeSerializer.ConvertToEnvelopeAsync(_eventBridgeMessage);
    }

    // --- Payload factories ---

    private static SmallPayload CreateSmallPayload() => new()
    {
        Unit = 42,
        Street = "Prince St",
        ZipCode = "00001"
    };

    private static MediumPayload CreateMediumPayload() => new()
    {
        OrderId = "ORD-20250415-001",
        CustomerId = "CUST-12345",
        Status = "Confirmed",
        TotalAmount = 249.99m,
        Currency = "USD",
        CreatedAt = DateTimeOffset.UtcNow,
        ShippingAddress = new Address
        {
            Line1 = "123 Main St",
            Line2 = "Apt 4B",
            City = "Seattle",
            State = "WA",
            ZipCode = "98101",
            Country = "US"
        },
        Items = new[]
        {
            new OrderItem { Sku = "SKU-001", Name = "Widget A", Quantity = 2, UnitPrice = 49.99m },
            new OrderItem { Sku = "SKU-002", Name = "Widget B", Quantity = 1, UnitPrice = 150.01m }
        },
        Tags = new[] { "priority", "prime", "domestic" }
    };

    private static LargePayload CreateLargePayload()
    {
        var employees = new Employee[10];
        for (int i = 0; i < employees.Length; i++)
        {
            employees[i] = new Employee
            {
                Id = $"EMP-{i:D4}",
                FirstName = $"First{i}",
                LastName = $"Last{i}",
                Email = $"employee{i}@example.com",
                Department = i % 3 == 0 ? "Engineering" : i % 3 == 1 ? "Marketing" : "Sales",
                Salary = 60000 + i * 5000,
                HireDate = DateTimeOffset.UtcNow.AddDays(-365 * (i + 1)),
                Skills = new[] { "skill-a", "skill-b", "skill-c" },
                Address = new Address
                {
                    Line1 = $"{100 + i} Corporate Blvd",
                    City = "Portland",
                    State = "OR",
                    ZipCode = $"{97200 + i}",
                    Country = "US"
                }
            };
        }
        return new LargePayload
        {
            CompanyId = "COMP-9876",
            CompanyName = "Benchmark Corp International",
            Industry = "Technology",
            Founded = 2005,
            IsPublic = true,
            Revenue = 15_000_000.50m,
            Headquarters = new Address
            {
                Line1 = "1 Corporate Plaza",
                Line2 = "Suite 100",
                City = "Portland",
                State = "OR",
                ZipCode = "97201",
                Country = "US"
            },
            Employees = employees,
            Departments = new[] { "Engineering", "Marketing", "Sales", "HR", "Finance" },
            Metadata = new Dictionary<string, string>
            {
                ["region"] = "us-west-2",
                ["tier"] = "enterprise",
                ["sla"] = "99.99"
            }
        };
    }
}

// --- Small payload (~200B JSON) ---
public class SmallPayload
{
    public int Unit { get; set; }
    public string? Street { get; set; }
    public string? ZipCode { get; set; }
}

// --- Medium payload (~1KB JSON) ---
public class MediumPayload
{
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public string? Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Address? ShippingAddress { get; set; }
    public OrderItem[]? Items { get; set; }
    public string[]? Tags { get; set; }
}

public class OrderItem
{
    public string? Sku { get; set; }
    public string? Name { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Address
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

// --- Large payload (~5KB JSON) ---
public class LargePayload
{
    public string? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string? Industry { get; set; }
    public int Founded { get; set; }
    public bool IsPublic { get; set; }
    public decimal Revenue { get; set; }
    public Address? Headquarters { get; set; }
    public Employee[]? Employees { get; set; }
    public string[]? Departments { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class Employee
{
    public string? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public decimal Salary { get; set; }
    public DateTimeOffset HireDate { get; set; }
    public string[]? Skills { get; set; }
    public Address? Address { get; set; }
}

// --- Handlers ---
public class SmallPayloadHandler : IMessageHandler<SmallPayload>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<SmallPayload> messageEnvelope, CancellationToken token = default)
        => Task.FromResult(MessageProcessStatus.Success());
}

public class MediumPayloadHandler : IMessageHandler<MediumPayload>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<MediumPayload> messageEnvelope, CancellationToken token = default)
        => Task.FromResult(MessageProcessStatus.Success());
}

public class LargePayloadHandler : IMessageHandler<LargePayload>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<LargePayload> messageEnvelope, CancellationToken token = default)
        => Task.FromResult(MessageProcessStatus.Success());
}

[JsonSerializable(typeof(SmallPayload))]
[JsonSerializable(typeof(MediumPayload))]
[JsonSerializable(typeof(LargePayload))]
[JsonSourceGenerationOptions(Converters = [typeof(JsonStringEnumConverter)])]
internal partial class BenchmarkJsonContext : JsonSerializerContext
{
}
