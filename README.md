# AWS Message Processing Framework for .NET
[![nuget](https://img.shields.io/nuget/v/AWS.Messaging.svg) ![downloads](https://img.shields.io/nuget/dt/AWS.Messaging.svg)](https://www.nuget.org/packages/AWS.Messaging/)
[![build status](https://img.shields.io/github/actions/workflow/status/awslabs/aws-dotnet-messaging/aws-ci.yml?branch=dev)](https://github.com/awslabs/aws-dotnet-messaging/actions/workflows/aws-ci.yml)

The **AWS Message Processing Framework for .NET** is an AWS-native framework that simplifies the development of .NET message processing applications that use AWS services, such as Amazon Simple Queue Service (SQS), Amazon Simple Notification Service (SNS), and Amazon EventBridge. The framework reduces the amount of boiler-plate code developers need to write, allowing you to focus on your business logic when publishing and consuming messages.
* For publishers, the framework serializes the message from a .NET object to a [CloudEvents](https://cloudevents.io/)-compatible message, and then wraps that in the service-specific AWS message. It then publishes the message to the configured SQS queue, SNS topic, or EventBridge event bus. 
* For consumers, the framework deserializes the message to its .NET object and routes it to the appropriate business logic. The framework also keeps track of the message visibility while it is being processed (to avoid processing a message more than once), and deletes the message from the queue when completed. The framework supports consuming messages in both long-running polling processes and in AWS Lambda functions.

# Getting started

Add the `AWS.Messaging` NuGet package to your project:
```
dotnet add package AWS.Messaging
```

The framework integrates with .NET's [dependency injection (DI) service container](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection). You can configure the framework during your application's startup by calling `AddAWSMessageBus` to add it to the DI container.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the AWS Message Processing Framework for .NET
builder.Services.AddAWSMessageBus(builder =>
{
    // Register that you'll publish messages of type ChatMessage to an existing queue
    builder.AddSQSPublisher<ChatMessage>("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd");
});
```

The framework supports publishing one or more message types, processing one or more message types, or doing both in the same application.

# Publishing Messages

The following code shows a configuration for an application that is publishing different message types to different AWS services.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the AWS Message Processing Framework for .NET
builder.Services.AddAWSMessageBus(builder =>
{
    // Register that you'll publish messages of type ChatMessage to an existing queue
    builder.AddSQSPublisher<ChatMessage>("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd");

    // Register that you'll publish messages of type OrderInfo to an existing SNS topic
    builder.AddSNSPublisher<OrderInfo>("arn:aws:sns:us-west-2:012345678910:MyAppProd");

    // Register that you'll publish messages of type FoodItem to an existing EventBridge bus
    builder.AddEventBridgePublisher<FoodItem>("arn:aws:events:us-west-2:012345678910:event-bus/default");
});
```

Once you have registered the framework during startup, inject the generic `IMessagePublisher` into your code. Call its `PublishAsync` method to publish any of the message types that were configured above. The generic publisher will determine the destination to route the message to based on its type.

In the following example, an ASP.NET MVC controller receives both `ChatMessage` messages and `OrderInfo` events from users, and then publishes them to SQS and SNS respectively. Both message types can be published using the generic publisher that was configured above.

```csharp
[ApiController]
[Route("[controller]")]
public class PublisherController : ControllerBase
{
    private readonly IMessagePublisher _messagePublisher;

    public PublisherController(IMessagePublisher messagePublisher)
    {
        _messagePublisher = messagePublisher;
    }

    [HttpPost("chatmessage", Name = "Chat Message")]
    public async Task<IActionResult> PublishChatMessage([FromBody] ChatMessage message)
    {
        // Perform business and validation logic on the ChatMessage here
        if (message == null)
        {
            return BadRequest("A chat message was not submitted. Unable to forward to the message queue.");
        }
        if (string.IsNullOrEmpty(message.MessageDescription))
        {
            return BadRequest("The MessageDescription cannot be null or empty.");
        }

        // Publish the ChatMessage to SQS, using the generic publisher
        await _messagePublisher.PublishAsync(message);

        return Ok();
    }

    [HttpPost("order", Name = "Order")]
    public async Task<IActionResult> PublishOrder([FromBody] OrderInfo message)
    {
        if (message == null)
        {
            return BadRequest("An order was not submitted.");
        }

        // Publish the OrderInfo to SNS, using the generic publisher
        await _messagePublisher.PublishAsync(message);

        return Ok();
    }
}
```
## Service-specific publishers

The example shown above uses the generic `IMessagePublisher`, which can publish to any supported AWS service based on the configured message type. The framework also provides *service-specific publishers* for SQS, SNS and EventBridge. These specific publishers expose options that only apply to that service, and can be injected using the types `ISQSPublisher`, `ISNSPublisher` and `IEventBridgePublisher`. 

For example, when publishing messages to an SQS FIFO queue, you must set the appropriate [message group ID](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/FIFO-key-terms.html). The following code shows the `ChatMessage` example again, but now using an `ISQSPublisher` to set SQS-specific options.
```csharp
public class PublisherController : ControllerBase
{
    private readonly ISQSPublisher _sqsPublisher;

    public PublisherController(ISQSPublisher sqsPublisher)
    {
        _sqsPublisher = sqsPublisher;
    }

    [HttpPost("chatmessage", Name = "Chat Message")]
    public async Task<IActionResult> PublishChatMessage([FromBody] ChatMessage message)
    {
        // Perform business and validation logic on the ChatMessage here
        if (message == null)
        {
            return BadRequest("A chat message was not submitted. Unable to forward to the message queue.");
        }
        if (string.IsNullOrEmpty(message.MessageDescription))
        {
            return BadRequest("The MessageDescription cannot be null or empty.");
        }

        // Send the ChatMessage to SQS using the injected ISQSPublisher, with SQS-specific options
        await _sqsPublisher.SendAsync(message, new SQSOptions
        {
            DelaySeconds = <delay-in-seconds>,
            MessageAttributes = <message-attributes>,
            MessageDeduplicationId = <message-deduplication-id>,
            MessageGroupId = <message-group-id>
        });

        return Ok();
    }
}
```

The same can be done for SNS and EventBridge, using `ISNSPublisher` and `IEventBridgePublisher` respectively.
```csharp
await _snsPublisher.PublishAsync(message, new SNSOptions
{
    Subject = <subject>,
    MessageAttributes = <message-attributes>,
    MessageDeduplicationId = <message-deduplication-id>,
    MessageGroupId = <message-group-id>
});
```
```csharp
await _eventBridgePublisher.PublishAsync(message, new EventBridgeOptions
{
    DetailType = <detail-type>,
    Resources = <resources>,
    Source = <source>,
    Time = <time>,
    TraceHeader = <trace-header>
});
```

## Batch publishing to SQS

The `ISQSPublisher` also supports sending messages in batches using the SQS [`SendMessageBatch`](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_SendMessageBatch.html) API. This can significantly increase throughput — from 300 messages per second with individual sends to up to 3,000 messages per second with batching.

The `SendBatchAsync` method accepts a collection of messages and automatically chunks them into groups of 10 (the SQS maximum per batch request).

### Simple batch — no per-message options
For standard (non-FIFO) queues where no per-message options are needed, you can pass the messages as a simple collection:

```csharp
var sqsPublisher = serviceProvider.GetRequiredService<ISQSPublisher>();

var messages = new List<ChatMessage>
{
    new ChatMessage { MessageDescription = "Hello" },
    new ChatMessage { MessageDescription = "World" },
    new ChatMessage { MessageDescription = "Batch!" }
};

// Send all messages in a batch
var response = await sqsPublisher.SendBatchAsync(messages);

// Check the results
Console.WriteLine($"Successful: {response.Successful.Count}");
Console.WriteLine($"Failed: {response.Failed.Count}");
```

### Per-message options using `SQSBatchEntry`
When each message in the batch needs its own options (e.g., different `MessageGroupId` values for FIFO queues), use `SQSBatchEntry<T>` to pair each message with its own `SQSMessageOptions`:

```csharp
var entries = new List<SQSBatchEntry<ChatMessage>>
{
    new SQSBatchEntry<ChatMessage>(
        new ChatMessage { MessageDescription = "User A's message" },
        new SQSMessageOptions { MessageGroupId = "userA" }),
    new SQSBatchEntry<ChatMessage>(
        new ChatMessage { MessageDescription = "User B's message" },
        new SQSMessageOptions { MessageGroupId = "userB" }),
};

// Send with per-message options
var response = await sqsPublisher.SendBatchAsync(entries);
```

You can also override the queue URL or SQS client for the entire batch using `SQSBatchOptions`:

```csharp
var response = await sqsPublisher.SendBatchAsync(entries, new SQSBatchOptions
{
    QueueUrl = "https://sqs.us-west-2.amazonaws.com/012345678910/AnotherQueue"
});
```

### Handling the batch response
`SendBatchAsync` returns a `SQSSendBatchResponse` that contains:
* `Successful` — a list of `SQSSendBatchResponseEntry` with the `Id` and `MessageId` (SQS-assigned ID) for each successfully sent message.
* `Failed` — a list of `SQSSendBatchResponseFailedEntry` with `Id`, `Code`, `Message`, and `SenderFault` for each message that failed.

The `Id` on each response entry corresponds to the `MessageEnvelope.Id` that the framework generated for that message, so you can correlate successes and failures back to your original inputs.

```csharp
var response = await sqsPublisher.SendBatchAsync(messages);

foreach (var success in response.Successful)
{
    Console.WriteLine($"Sent message {success.Id} with SQS MessageId: {success.MessageId}");
}

foreach (var failure in response.Failed)
{
    Console.WriteLine($"Failed to send {failure.Id}: [{failure.Code}] {failure.Message}");
}
```

> **Note:** Messages are automatically chunked into groups of 10. If you send 25 messages, the framework will make 3 SQS `SendMessageBatch` API calls (10 + 10 + 5) and aggregate the results into a single `SQSSendBatchResponse`.

### Error handling and partial results
If an `AmazonSQSException` is thrown during a batch send (e.g., on the second chunk after the first chunk already succeeded), the framework throws a `FailedToPublishBatchException`. This exception includes a `PartialResponse` property containing any results from chunks that were sent before the failure occurred, so you can determine what was already published and avoid duplicate retries.

```csharp
try
{
    var response = await sqsPublisher.SendBatchAsync(messages);
}
catch (FailedToPublishBatchException ex)
{
    // Some messages from earlier chunks may have been sent successfully
    var alreadySent = ex.PartialResponse?.Successful ?? new List<SQSSendBatchResponseEntry>();
    Console.WriteLine($"{alreadySent.Count} message(s) were sent before the failure.");
    Console.WriteLine($"Error: {ex.InnerException?.Message}");
}
```

# Consuming Messages

To consume messages, implement a message handler using the `IMessageHandler` interface for each message type you wish to process. The mapping between message types and message handlers is configured in the project startup.

```csharp
await Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Register the AWS Message Processing Framework for .NET
        services.AddAWSMessageBus(builder =>
        {
            // Register an SQS Queue that the framework will poll for messages
            builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd");

            // Register all IMessageHandler implementations with the message type they should process. 
            // Here messages that match our ChatMessage .NET type will be handled by our ChatMessageHandler
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });
    })
    .Build()
    .RunAsync();
```

The following code shows a sample message handler for a `ChatMessage` message. 

```csharp
public class ChatMessageHandler : IMessageHandler<ChatMessage>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<ChatMessage> messageEnvelope, CancellationToken token = default)
    {
        // Add business and validation logic here
        if (messageEnvelope == null)
        {
            return Task.FromResult(MessageProcessStatus.Failed());
        }

        if (messageEnvelope.Message == null)
        {
            return Task.FromResult(MessageProcessStatus.Failed());
        }

        ChatMessage message = messageEnvelope.Message;

        Console.WriteLine($"Message Description: {message.MessageDescription}");

        // Return success so the framework will delete the message from the queue
        return Task.FromResult(MessageProcessStatus.Success());
    }
}
```

The outer `MessageEnvelope` contains metadata used by the framework. Its `message` property is the message type (in this case `ChatMessage`). 

You can return `MessageProcessStatus.Success()` to indicate that the message was processed successfully and the framework will delete the message from the SQS queue. When returning `MessageProcessStatus.Failed()` the message will remain in the queue, where it can be processed again or moved to a [dead-letter queue](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-dead-letter-queues.html) if configured.

## Subscriber Middleware

You can add middleware to the subscriber message processing pipeline. Middleware executes in the order it is registered, wrapping around the message handler. This is useful for cross-cutting concerns such as logging, metrics, message enrichment, or custom retry logic.

To create middleware, implement the `IHandlerMiddleware` interface:

```csharp
public class LoggingMiddleware : IHandlerMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task<MessageProcessStatus> InvokeAsync<T>(
        MessageEnvelope<T> messageEnvelope,
        RequestDelegate next,
        CancellationToken token = default)
    {
        _logger.LogInformation("Processing message {MessageId}", messageEnvelope.Id);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await next();

        stopwatch.Stop();
        _logger.LogInformation("Processed message {MessageId} in {Elapsed}ms", messageEnvelope.Id, stopwatch.ElapsedMilliseconds);

        return result;
    }
}
```

Register middleware during startup using `AddHandlerMiddleware`. Middleware is registered as a singleton by default, but you can configure its service lifetime:

```csharp
services.AddAWSMessageBus(builder =>
{
    builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd");
    builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();

    // Add middleware - executes in registration order
    builder.AddHandlerMiddleware<LoggingMiddleware>();
    builder.AddHandlerMiddleware<MetricsMiddleware>();

    // Optionally specify a different service lifetime
    builder.AddHandlerMiddleware<ScopedMiddleware>(ServiceLifetime.Scoped);
});
```

## Message Error Handling

You can register a message error handler to control what happens when an exception occurs during message processing (in the handler or middleware). The error handler can choose to retry, fail, or mark the message as successful.

Implement the `IMessageErrorHandler` interface:
```csharp
public class TransientErrorHandler : IMessageErrorHandler
{
    private readonly ILogger<TransientErrorHandler> _logger;

    public TransientErrorHandler(ILogger<TransientErrorHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<MessageErrorHandlerResponse> OnHandleError<T>(
        MessageEnvelope<T> messageEnvelope,
        Exception exception,
        int attempts,
        CancellationToken token)
    {
        // Retry up to 3 times for transient errors
        if (attempts <= 3 && IsTransient(exception))
        {
            _logger.LogWarning(exception, "Transient error on attempt {Attempt} for message {MessageId}, retrying.", attempts, messageEnvelope.Id);
            return ValueTask.FromResult(MessageErrorHandlerResponse.Retry);
        }

        // Give up after max retries
        _logger.LogError(exception, "Failed to process message {MessageId} after {Attempt} attempts.", messageEnvelope.Id, attempts);
        return ValueTask.FromResult(MessageErrorHandlerResponse.Failed);
    }

    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException or HttpRequestException;
}
```

Register the error handler during startup:
```csharp
services.AddAWSMessageBus(builder =>
{
    builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd");
    builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();

    // Register an error handler for retry/failure control
    builder.AddMessageErrorHandler<TransientErrorHandler>();
});
```

The `MessageErrorHandlerResponse` enum supports three responses:
* `Retry` - Re-execute the entire processing pipeline (middleware + handler) in a new DI scope. This is useful for transient errors since scoped services like `DbContext` will be recreated.
* `Failed` - Return a failed status. The message will remain in the queue for reprocessing or dead-letter queue routing.
* `Success` - Treat the message as successfully processed. The framework will delete it from the queue. This is useful for routing poison messages to a dead-letter queue.

## Handling Messages in a Long-Running Process
You can call `AddSQSPoller` with an SQS queue URL to start a long-running [`BackgroundService`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.backgroundservice) that will continuously poll the queue and process messages.

```csharp
await Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Register the AWS Message Processing Framework for .NET
        services.AddAWSMessageBus(builder =>
        {
            // Register an SQS Queue that the framework will poll for messages
            builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd", options => 
            {
                // The maximum number of messages from this queue that the framework will process concurrently on this client
                options.MaxNumberOfConcurrentMessages = 10;

                // The duration each call to SQS will wait for new messages
                options.WaitTimeSeconds = 20; 
            });

            // Register all IMessageHandler implementations with the message type they should process
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>();
        });
    })
    .Build()
    .RunAsync();
```

### Consuming AWS service events (raw JSON payloads)
Many AWS services can deliver events to SQS (directly, via SNS, or via EventBridge rules). These payloads are typically *raw JSON* and do not include the framework's CloudEvents envelope.

To consume raw JSON payloads, configure the SQS poller for a *single message type* and disable envelope support via `MessageEnvelopeMode.NotSupported`.

#### Example: Consume S3 event notifications from SQS
If you configure an S3 bucket to send event notifications directly to an SQS queue (for example, `s3:ObjectCreated:*`), the message body contains an S3 event notification JSON document.

You can model this payload yourself, but you can also reuse the strongly-typed event classes from the AWS Lambda .NET packages.

Add the S3 events package:
```
dotnet add package Amazon.Lambda.S3Events
```

The following example uses `Amazon.Lambda.S3Events.S3Event` and configures JSON deserialization to be case-insensitive (S3 event JSON uses a mix of `Records` and camelCase properties).

```csharp
using System.Text.Json;
using AWS.Messaging;
using AWS.Messaging.Configuration;
using Amazon.Lambda.S3Events;
using Microsoft.Extensions.Hosting;

public sealed class S3EventNotificationHandler : IMessageHandler<S3Event>
{
    public Task<MessageProcessStatus> HandleAsync(
        MessageEnvelope<S3Event> messageEnvelope,
        CancellationToken token = default)
    {
        var record = messageEnvelope.Message.Records?.FirstOrDefault();
        var bucket = record?.S3?.Bucket?.Name;
        var key = record?.S3?.Object?.Key;

        Console.WriteLine($"S3 event: bucket='{bucket}', key='{key}', eventName='{record?.EventName}'");

        return Task.FromResult(MessageProcessStatus.Success());
    }
}

await Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddAWSMessageBus(builder =>
        {
            // The poller is tied to a single message type and expects raw JSON.
            // This is a good fit for a queue dedicated to a single AWS service event.
            builder.AddSQSPoller<S3Event>(
                "https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd",
                messageEnvelopeMode: MessageEnvelopeMode.NotSupported);

            // Recommended for AWS service event JSON: be lenient about casing.
            builder.ConfigureSerializationOptions(options =>
            {
                options.SystemTextJsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
            });

            builder.AddMessageHandler<S3EventNotificationHandler, S3Event>();
        });
    })
    .Build()
    .RunAsync();
```

Notes:
* Without the CloudEvents envelope, the framework cannot type-route messages by a `'type'` discriminator. Use a dedicated queue per message type (or configure multiple pollers).
* The S3-to-SQS notification format can include additional properties not modeled above; that's fine as long as your handler reads the fields it needs.
* This same approach works with other AWS Lambda .NET event packages (for example, `Amazon.Lambda.SNSEvents`, etc.) when the SQS message body is raw JSON for that event type.

### Configuring the SQS Message Poller
The SQS message poller can be configured by the `SQSMessagePollerOptions` when calling `AddSQSPoller`.
* `MaxNumberOfConcurrentMessages` - The maximum number of messages from the queue to process concurrently. The default value is `10`.
* `WaitTimeSeconds` - The duration (in seconds) for which the `ReceiveMessage` SQS call waits for a message to arrive in the queue before returning. If a message is available, the call returns sooner than `WaitTimeSeconds`. The default value is `20`.

#### Message Visibility Timeout Handling
SQS messages have a [visibility timeout](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html) period. When one consumer begins handling a given message, it remains in the queue but is hidden from other consumers to avoid processing it more than once. If the message is not handled and deleted before becoming visible again, another consumer may attempt to handle the same message.

The framework will track and attempt to extend the visibility timeout for messages that it is currently handling. You can configure this behavior on the `SQSMessagePollerOptions` when calling `AddSQSPoller`.
* `VisibilityTimeout` - The duration in seconds that received messages are hidden from subsequent retrieve requests. The default value is `30`.
* `VisibilityTimeoutExtensionThreshold` - When a message's visibility timeout is within this many seconds of expiring, the framework will extend the visibility timeout (by another `VisibilityTimeout` seconds). The default value is `5`.
* `VisibilityTimeoutExtensionHeartbeatInterval`- How often in seconds that the framework will check for messages that are within `VisibilityTimeoutExtensionThreshold` seconds of expiring, and then extend their visibility timeout. The default value is `1`.

In the following example the framework will check every 1 second for messages that are still being handled. For those messages within 5 seconds of becoming visible again, the framework will automatically extend the visibility timeout of each message by another 30 seconds.
```csharp
 builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MyAppProd", options => 
{
    options.VisibilityTimeout = 30;
    options.VisibilityTimeoutExtensionThreshold = 5;
    VisibilityTimeoutExtensionHeartbeatInterval = 1;
});
```

## Handling Messages in AWS Lambda Functions
You can use the AWS Message Processing Framework for .NET with [SQS's integration with Lambda](https://docs.aws.amazon.com/lambda/latest/dg/with-sqs.html). This is provided by the `AWS.Messaging.Lambda` package. Refer to its [README](https://github.com/awslabs/aws-dotnet-messaging/blob/main/src/AWS.Messaging.Lambda/README.md) to get started.

## SQS Poller Resiliency
The SQS Poller is resilient by design and is able to handle errors thrown by the underlying .NET SDK as well as the framework itself in two ways. We have classified a number of exceptions that may occur due to invalid configuration from the user's side as fatal exceptions. These exceptions will cause the SQS Poller background service to stop running after throwing a user-friendly error message. However, any exceptions thrown, outside of the fatal ones we have defined, will not cause the SQS Poller to error out and will remain resilient in the event that an underlying service is facing degraded performance or outages. The SQS poller leverages backoffs in an effort to retry any failed SQS requests while applying a certain time-delay between retries. 

The framework defined two interfaces, `IBackoffHandler` and `IBackoffPolicy`. The `IBackoffPolicy` is closely tied to the `BackoffHandler` which implements `IBackoffHandler`. The default implementation of `IBackoffHandler` checks the attached `IBackoffPolicy` to see if a backoff should be applied. If a backoff is to be applied, the `IBackoffPolicy` also returns the time delay between retries.

The framework support three backoff policies:
* `None`, which would disable the backoff handler. This will allow users to fully rely on the SDK’s retry logic.
* `Interval`, which would backoff on a given and configurable interval. Default value is 1 second.
* `CappedExponential`, which would perform an exponential backoff up until it reaches a certain configurable max backoff time, at which point it would switch to an interval backoff. Default value for the cap backoff time is 1 hour.

By default, without any user configuration, the SQS Poller will use the default implementation of the `IBackoffHandler` interface, coupled with a capped exponential backoff policy.

Users are free to change the backoff policy as follows:
```csharp
services.AddAWSMessageBus(builder =>
{
    builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/536721586275/MPF");

    // Optional: Configure the backoff policy used by the SQS Poller.
    builder.ConfigureBackoffPolicy(options =>
    {
        // Use 1 of the available 3 backoff policies:

        // No backoff Policy
        options.UseNoBackoff();

        // Interval backoff policy
        options.UseIntervalBackoff(x =>
        {
            x.FixedInterval = 1;
        });

        // Capped exponential backoff policy
        options.UseCappedExponentialBackoff(x =>
        {
            x.CapBackoffTime = 60;
        });
    });
});
```

Users can also implement their own backoff handler by implementing the interface `IBackoffHandler` and injecting it before the AWS Message Bus is added to the DI Container. This can be done as follows:
```csharp
services.TryAddSingleton<IBackoffHandler, CustomBackoffHandler>();

services.AddAWSMessageBus(builder =>
{
    builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MPF");
    builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("chatMessage");
});
```

As an example, you can use [Polly](https://github.com/App-vNext/Polly) to handle the retries. A sample app that achieves this could be found [here](./sampleapps/PollyIntegration/).

# Telemetry
The AWS Message Processing Framework for .NET is instrumented for OpenTelemetry to log [traces](https://opentelemetry.io/docs/concepts/signals/traces/) for each message that is published or handled by the framework. This is provided by the `AWS.Messaging.Telemetry.OpenTelemetry` package. Refer to its [README](https://github.com/awslabs/aws-dotnet-messaging/blob/main/src/AWS.Messaging.Telemetry.OpenTelemetry/README.md) to get started.

# Customization
The framework builds, sends, and handles messages in three different "layers":
1. At the outermost layer, the framework builds the AWS-native request or response specific to a service. With SQS for example, it builds [`SendMessage`](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_SendMessage.html) requests, and works with the [`Message`](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_Message.html) objects that are defined by the service.
2. Inside the SQS request and response, it sets the `MessageBody` element (or `Message` for SNS or `Detail` for EventBridge) to a [JSON-formatted CloudEvent](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/formats/json-format.md). This contains metadata set by the framework that is accessible on the `MessageEnvelope` object when handling a message.
3. At the innermost layer, the `data` attribute inside the CloudEvent JSON object contains a JSON serialization of the .NET object that was sent or received as the message.

```json
{
    "id":"b02f156b-0f02-48cf-ae54-4fbbe05cffba",
    "source":"/aws/messaging",
    "specversion":"1.0",
    "type":"Publisher.Models.ChatMessage",
    "time":"2023-11-21T16:36:02.8957126+00:00",
    "data":"<the ChatMessage object serialized as JSON>"
}
```

You can customize how the message envelope is configured and read:
* `"id"` uniquely identifies the message. By default it is set to a new GUID, but this can be overridden by implementing your own `IMessageIdGenerator` and injecting that into the DI container.
* `"type"` controls how the message is routed to handlers. By default this uses the full name of the .NET type that corresponds to the message. You can override this via the `messageTypeIdentifier` parameter when mapping the message type to the destination via `AddSQSPublisher`, `AddSNSPublisher`, or `AddEventBridgePublisher`.
* `"source"` indicates which system or server sent the message. 
     * This will be the function name if publishing from AWS Lambda, the cluster name and task ARN if on Amazon ECS, the instance ID if on Amazon EC2, otherwise a fallback value of `/aws/messaging`.
     * You can override this via `AddMessageSource` or `AddMessageSourceSuffix` on the `MessageBusBuilder`.
* `"time"` set to the current DateTime in UTC. This can be overridden by implementing your own `IDateTimeHandler` and injecting that into the DI container.
* `"data"` contains a JSON representation of the .NET object that was sent or received as the message:
    * `ConfigureSerializationOptions` on `MessageBusBuilder` allows you to configure the [`System.Text.Json.JsonSerializerOptions`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions) that will be used when serializing and deserializing the message.
    * To inject additional attributes or transform the message envelope once the framework builds it, you can implement `ISerializationCallback` and register that via `AddSerializationCallback` on `MessageBusBuilder`.

# Permissions
To use the AWS Message Processing Framework for .NET to publish a message to an _existing_ AWS SQS Queue, the following permissions are required:
```
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Sid": "Statement1",
            "Effect": "Allow",
            "Action": [
                "sqs:sendmessage",
                "sqs:sendmessagebatch"
            ],
            "Resource": [
                "arn:aws:sqs:<region>:<account>:<queue>"
            ]
        }
    ]
}
```
To use the AWS Message Processing Framework for .NET to publish a message to an _existing_ AWS SNS Topic, the following permissions are required:
```
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Sid": "Statement1",
            "Effect": "Allow",
            "Action": [
                "sns:Publish"
            ],
            "Resource": [
                "arn:aws:sns:<region>:<account>:<topic>"
            ]
        }
    ]
}
```
To use the AWS Message Processing Framework for .NET to publish a message to an _existing_ AWS EventBridge Event Bus, the following permissions are required:
```
{
	"Version": "2012-10-17",
	"Statement": [
		{
			"Sid": "Statement1",
			"Effect": "Allow",
			"Action": ["events:PutEvents"],
			"Resource": ["arn:aws:events:<region>:<account>:event-bus/<bus>"]
		}
	]
}
```
To use the AWS Message Processing Framework for .NET to poll messages from an _existing_ AWS SQS Queue, the following permissions are required:
```
{
	"Version": "2012-10-17",
	"Statement": [
		{
			"Sid": "Statement1",
			"Effect": "Allow",
			"Action": ["sqs:receivemessage", "sqs:deletemessage", "sqs:changemessagevisibility"],
			"Resource": ["arn:aws:sqs:<region>:<account>:<queue>"]
		}
	]
}
```

# Getting Help
For feature requests or issues using this framework please open an [issue in this repository](https://github.com/aws/aws-dotnet-messaging/issues).

# Contributing
We welcome community contributions and pull requests. See [CONTRIBUTING.md](./CONTRIBUTING.md) for information on how to submit code.

# Security
The AWS Message Processing Framework for .NET relies on the [AWS SDK for .NET](https://github.com/aws/aws-sdk-net) for communicating with AWS. Refer to the [security section](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/security.html) in the [AWS SDK for .NET Developer Guide](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/welcome.html) for more information. You can also find more information in [AWS Message Processing Framework for .NET Developer Guide](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/msg-proc-fw.html).

The framework does not log data messages sent by the user for security purposes. If users want to enable this functionality for debugging purposes, you need to call `EnableMessageContentLogging()` in the AWS Message Bus as follows:
```csharp
builder.Services.AddAWSMessageBus(bus =>
{
    builder.EnableMessageContentLogging();
});
```

If you discover a potential security issue, refer to the [security policy](https://github.com/awslabs/aws-dotnet-messaging/security/policy) for reporting information.

# Additional Resources
* [AWS Message Processing Framework for .NET Design Document](./docs/docs/design/message-processing-framework-design.md)
* [Sample Applications](https://github.com/awslabs/aws-dotnet-messaging/tree/main/sampleapps) - contains sample applications of a publisher service, long-running subscriber service, Lambda function handlers, and using Polly to override the framework's built-in backoff logic.
* [Developer Guide](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/msg-proc-fw.html)
* [API Reference](https://awslabs.github.io/aws-dotnet-messaging/)
* [Introducing the AWS Message Processing Framework for .NET (Preview) Blog Post](https://aws.amazon.com/blogs/developer/introducing-the-aws-message-processing-framework-for-net-preview/) - walks through creating simple applications to send and receive SQS messages.

# License

This project is licensed under the Apache-2.0 License.
