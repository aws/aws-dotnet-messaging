## Release 2025-07-29

### AWS.Messaging.Telemetry.OpenTelemetry (1.0.0)
* This marks the first stable release of AWS Message Processing Framework OTEL for .NET which is now generally available.

## Release 2025-07-14

### AWS.Messaging.Lambda (1.0.1)
* Don't use .Any() on List<T>
### AWS.Messaging (1.0.1)
* Support scoped IAsyncDisposable services being disposed after message is processed
* Minor threading and memory optimizations and cleanups

## Release 2025-06-03

### AWS.Messaging.Telemetry.OpenTelemetry (0.23.0)
* Updated consumer processing logic to use Links for connecting to publisher instead of parent-child relationship

## Release 2025-05-05

### AWS.Messaging (1.0.0)
* This marks the first stable release of AWS Message Processing Framework for .NET which is now generally available.
### AWS.Messaging.Lambda (1.0.0)
* This marks the first stable release of AWS Message Processing Framework for .NET which is now generally available.
### AWS.Messaging.Telemetry.OpenTelemetry (0.22.0)
* Update dependency on AWS.Messaging to version 1.0.0

## Release 2025-04-28

### AWS.Messaging.Lambda (0.21.0)
* Fix null pointer exception when message contains no SQS message attributes
* Updated the .NET SDK dependencies to the latest version GA 4.0.0
### AWS.Messaging (0.22.0)
* Updated the .NET SDK dependencies to the latest version GA 4.0.0
### AWS.Messaging.Telemetry.OpenTelemetry (0.21.0)
* Updated the .NET SDK dependencies to the latest version GA 4.0.0

## Release 2025-04-08

### AWS.Messaging.Lambda (0.10.2)
* Fix null pointer exception when message contains no SQS message attributes

## Release 2025-04-03

### AWS.Messaging (0.21.0-preview)
* Add null check when retrieved messages is null
* Update MessageSerializer to store data as actual json. Fixes #168
* Remove failed messages from inflight messages metatadata dictionary. Fixes #191

## Release 2025-04-01 #2

### AWS.Messaging (0.20.0-preview)
* Update error message for handling scenario where subscriber mapping is not valid.
* Implement a start/stop mechanism for message consumption. (ISSUE 147)
* Update .NET target to .NET 8
* Add trimming support for Native AOT compatiblity
* Updated the .NET SDK dependencies to the latest version 4.0.0-preview.11
### AWS.Messaging.Lambda (0.20.0-preview)
* Update .NET target to .NET 8
* Add trimming support for Native AOT compatiblity
* Updated the .NET SDK dependencies to the latest version 4.0.0-preview.11
### AWS.Messaging.Telemetry.OpenTelemetry (0.20.0-preview)
* Update .NET target to .NET 8
* Add trimming support for Native AOT compatiblity
* Updated the .NET SDK dependencies to the latest version 4.0.0-preview.11

## Release 2025-03-12

### AWS.Messaging (0.9.5)
* Update error message for handling scenario where subscriber mapping is not valid.

## Release 2025-02-20

### AWS.Messaging (0.9.4)
* Fix issue with fifo when a message is failed to process the later messages are not retried
* Update User-Agent string
* Avoid logging error during shutdown
### AWS.Messaging.Lambda (0.10.1)
* Update AWS.Messaging dependency
* Update project dependencies
### AWS.Messaging.Telemetry.OpenTelemetry (0.9.2)
* Update project dependencies

## Release 2025-01-13

### AWS.Messaging (0.9.3)
* Removes references to SNS in the EventBridge publisher documentation
* Update logic to make DefaultMessageManager fail on InvalidMessageHandlerSignatureException

## Release 2024-08-15

### AWS.Messaging (0.9.2)
* Add support for AddAWSMessageBus being invoked multiple times against the same ServiceCollection. This allows different modules to register their own handlers rather than requiring a centralized registration.

## Release 2024-08-02

### AWS.Messaging.Lambda (0.10.0)
* Add default visibility timeout for failed partial batch response.

## Release 2024-04-22

### AWS.Messaging (0.9.1)
* Adding the response to the publish operation
* Update User-Agent string
### AWS.Messaging.Lambda (0.9.1)
* Update project dependencies
### AWS.Messaging.Telemetry.OpenTelemetry (0.9.1)
* Update project dependencies

## Release 2024-03-26

### AWS.Messaging (0.9.0)
* **AWS.Messaging** is now in _**Developer Preview**_
### AWS.Messaging.Lambda (0.9.0)
* **AWS.Messaging.Lambda** is now in _**Developer Preview**_
### AWS.Messaging.Telemetry.OpenTelemetry (0.9.0)
* **AWS.Messaging.Telemetry.OpenTelemetry** is now in _**Developer Preview**_

## Release 2024-03-20

### AWS.Messaging (0.3.0-beta)
* Added back-off logic to the SQS Poller that can perform Exponential, Interval or disable back-offs entirely. The SQS Poller will now back-off before attempting to reach SQS in case of an exception.
* Added support for SourceLink
### AWS.Messaging.Lambda (0.1.1-beta)
* Added support for SourceLink
### AWS.Messaging.Telemetry.OpenTelemetry (0.1.1-beta)
* Added support for SourceLink

## Release 2024-03-08
### AWS.Messaging (0.2.0-beta)
* BREAKING CHANGE: Message content is no longer included by default in logs or exceptions. Call `EnableDataMessageLogging` during setup to re-enable.
* BREAKING CHANGE: Replaced `IsSQSExceptionFatal` with `IsExceptionFatal` to allow classifying a broader range of exceptions. Expanded the default list of fatal exceptions.
* BREAKING CHANGE: Renamed `PublishAsync` to `SendAsync on the SQS-specific publisher, and create separate interface definitions to clarify "publishing" vs. "sending" depending on the destination service.
* Allow overriding the destination and AWS service client on the service-specific publishers. This allows you to set the destination and credentials on a per-message basis, which may be useful for multi-tenant applications.
* Improved validation on ECS task metadata when deriving the default value for the message source on ECS.
* Improved documentation and examples around `IMessagePublisher`

## Release 2023-12-08
### AWS.Messaging (0.1.0-beta)
* Initial _**beta**_ release.
### AWS.Messaging.Lambda (0.1.0-beta)
* Initial _**beta**_ release.
### AWS.Messaging.Telemetry.OpenTelemetry (0.1.0-beta)
* Initial _**beta**_ release.