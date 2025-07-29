# OpenTelemetry plugin for AWS Message Processing Framework for .NET
[![nuget](https://img.shields.io/nuget/v/AWS.Messaging.Telemetry.OpenTelemetry.svg) ![downloads](https://img.shields.io/nuget/dt/AWS.Messaging.Telemetry.OpenTelemetry.svg)](https://www.nuget.org/packages/AWS.Messaging.Telemetry.OpenTelemetry/)

This package is an [Instrumentation
Library](https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/glossary.md#instrumentation-library), which instruments the [AWS Message Processing Framework for .NET](https://github.com/awslabs/aws-dotnet-messaging) to collect traces about 
messages that are sent and received.

## Configuration

### 1. Install Packages

Add a reference to [`AWS.Messaging.Telemetry.OpenTelemetry`](https://www.nuget.org/packages/AWS.Messaging.Telemetry.OpenTelemetry). In this example, we're going to configure OpenTelemetry on our `IServiceCollection`, so also add a reference to [`OpenTelemetry.Extensions.Hosting`](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting). This is not required if starting and stopping tracing via `CreateTracerProviderBuilder`. 

You may also add a reference to one or more [exporters](https://opentelemetry.io/docs/instrumentation/net/exporters/) to visualize your telemetry data.

```shell
dotnet add package AWS.Messaging.Telemetry.OpenTelemetry
dotnet add package OpenTelemetry.Extensions.Hosting
```

### 2. Enable Instrumentation
In the `Startup` class add a call to `AddOpenTelemetry` to configure OpenTelemetry. On the `TracerProviderBuilder`, call `AddAWSMessagingInstrumentation` to begin capturing traces for the AWS Message Processing Framework for .NET.

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPoller("https://sqs.us-west-2.amazonaws.com/012345678910/MPF");
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("chatMessage");
        });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("myApplication"))
            .WithTracing(tracing => tracing
                .AddAWSMessagingInstrumentation()
                .AddConsoleExporter());
    }
}
```

### AWS X-Ray Linked Spans Configuration
1. If you are using OTEL with X-Ray, make sure you configure your OTEL setup to use OTLP for Cloudwatch. This is needed in order to view linked spans in the AWS X-Ray console. https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/CloudWatch-OTLPSimplesetup.html 

# Useful Links
* [AWS Message Processing Framework for .NET Design Document](../../docs/docs/design/message-processing-framework-design.md)
* [Sample Applications](https://github.com/awslabs/aws-dotnet-messaging/tree/main/sampleapps) - contains sample applications of a publisher service, long-running subscriber service, Lambda function handlers, and using Polly to override the framework's built-in backoff logic.
* [Developer Guide](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/msg-proc-fw.html)
* [API Reference](https://awslabs.github.io/aws-dotnet-messaging/)
* [Introducing the AWS Message Processing Framework for .NET (Preview) Blog Post](https://aws.amazon.com/blogs/developer/introducing-the-aws-message-processing-framework-for-net-preview/) - walks through creating simple applications to send and receive SQS messages.

# Security

See [CONTRIBUTING](CONTRIBUTING.md#security-issue-notifications) for more information.

# License

This project is licensed under the Apache-2.0 License.
