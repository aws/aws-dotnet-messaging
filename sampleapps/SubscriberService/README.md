# AWS Message Processing Framework Subscriber Service Sample

This sample application demonstrates how to use the AWS Message Processing Framework for .NET to process messages from SQS queues with configurable polling and message handling.

## Overview

This sample demonstrates:
- Configuring and using SQS message pollers
- Implementing typed message handlers
- Adding subscriber middleware to the processing pipeline
- Configuration-based setup using .NET Aspire
- Configurable backoff policies for message processing
- Proper message handling patterns

## Prerequisites

- .NET 8.0 or later
- Follow the setup instructions in the AppHost README to ensure all AWS resources and .NET Aspire components are properly configured


## Project Structure
```
SubscriberService/
├── MessageHandlers/
│   └── ChatMessageHandler.cs    # Message handler implementation
├── Middleware/
│   └── SampleMiddleware.cs      # Sample middleware implementation
├── Models/
│   └── ChatMessage.cs          # Message type definition
├── Program.cs                  # Application entry point
└── appsettings.json           # Application configuration
```

## Middleware

This sample includes `SampleMiddleware` to demonstrate the subscriber middleware feature. Middleware implements the `IHandlerMiddleware` interface and is registered using `builder.AddHandlerMiddleware<SampleMiddleware>()` in `Program.cs`. Middleware executes in registration order, wrapping around the message handler, and is useful for cross-cutting concerns such as logging, metrics, or message enrichment. See the main [README](../../README.md#subscriber-middleware) for more details.

## Testing

You can test the service using one of these two methods:

### 1. Using PublisherAPI (Recommended)

1. Ensure the Aspire AppHost is running
2. Open the PublisherAPI Swagger UI
3. Navigate to the ChatMessage endpoint
4. Send a test message using the Swagger interface


### 2. Manual Testing via AWS CLI
```
$messageBody = '{"type":"chatMessage","id":"123","source":"test","specversion":"1.0","time":"2024-01-01T00:00:00Z","data":{"messageDescription":"Test message"}}'

aws sqs send-message --queue-url YOUR_QUEUE_URL --message-body $messageBody
```

## Configuration Options
```
{
    "MaxNumberOfConcurrentMessages": 10,    // Maximum number of messages to process concurrently
    "VisibilityTimeout": 20,               // Time (in seconds) that a message is invisible after being received
    "WaitTimeSeconds": 20,                 // Long polling duration
    "VisibilityTimeoutExtensionHeartbeatInterval": 1,  // How often to extend visibility timeout
    "VisibilityTimeoutExtensionThreshold": 5   // When to extend visibility timeout
}
```
### Backoff Policy Options
```
{
    "BackoffPolicy": "CappedExponential",  // Available options: Linear, Exponential, CappedExponential
    "CappedExponentialBackoffOptions": {
        "CapBackoffTime": 60               // Maximum backoff time in seconds
    }
}
```
