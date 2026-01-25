# Bdaya.Abp.BackgroundJobs.PubSub

Google Cloud Pub/Sub integration for ABP Framework's background jobs.

## Overview

This package provides an implementation of ABP's `IBackgroundJobManager` using Google Cloud Pub/Sub as the job queue. It follows the same patterns as the official ABP RabbitMQ background jobs integration.

## Installation

### Using ABP CLI

```bash
abp add-package Bdaya.Abp.BackgroundJobs.PubSub
```

### Manual Installation

1. Install the NuGet package:
```bash
dotnet add package Bdaya.Abp.BackgroundJobs.PubSub
```

2. Add the module dependency to your ABP module:
```csharp
[DependsOn(typeof(AbpBackgroundJobsPubSubModule))]
public class YourModule : AbpModule
{
    // ...
}
```

## Configuration

### appsettings.json

```json
{
  "PubSub": {
    "Connections": {
      "Default": {
        "ProjectId": "your-gcp-project-id",
        "CredentialsPath": "/path/to/service-account.json"
      }
    },
    "BackgroundJobs": {
      "DefaultTopicPrefix": "AbpBackgroundJobs",
      "DefaultSubscriptionPrefix": "AbpBackgroundJobs",
      "PrefetchCount": 1,
      "AckDeadlineSeconds": 60,
      "AutoCreateTopics": true,
      "AutoCreateSubscriptions": true
    }
  }
}
```

### Using Local Emulator

For local development, use the Pub/Sub emulator:

```json
{
  "PubSub": {
    "Connections": {
      "Default": {
        "ProjectId": "test-project",
        "EmulatorHost": "localhost:8085"
      }
    }
  }
}
```

### Configuration Options

#### Connection Options (`AbpPubSubOptions`)

| Property | Description |
|----------|-------------|
| `Connections` | Dictionary of named connection configurations |
| `Default` | Shortcut to access `Connections["Default"]` |

#### Connection Configuration (`PubSubConnectionConfiguration`)

| Property | Description |
|----------|-------------|
| `ProjectId` | Google Cloud Project ID (required) |
| `CredentialsPath` | Path to service account JSON file (optional, uses ADC if not set) |
| `EmulatorHost` | Pub/Sub emulator host for local development (e.g., `localhost:8085`) |

#### Background Job Options (`AbpPubSubBackgroundJobOptions`)

| Property | Default | Description |
|----------|---------|-------------|
| `ConnectionName` | `null` | Named connection to use (uses "Default" if not set) |
| `DefaultTopicPrefix` | `AbpBackgroundJobs` | Prefix for job topic names |
| `DefaultSubscriptionPrefix` | `AbpBackgroundJobs` | Prefix for job subscription names |
| `DefaultDelayedTopicPrefix` | `AbpBackgroundJobs.Delayed` | Prefix for delayed job topic names |
| `PrefetchCount` | `1` | Maximum concurrent handlers (flow control) |
| `AckDeadlineSeconds` | `60` | Message acknowledgment deadline |
| `MessageRetentionDays` | `7` | Message retention duration in days |
| `MaxDeliveryAttempts` | `5` | Max delivery attempts before dead letter |
| `AutoCreateTopics` | `true` | Auto-create topics if they don't exist |
| `AutoCreateSubscriptions` | `true` | Auto-create subscriptions if they don't exist |
| `DeadLetterTopicSuffix` | `DeadLetter` | Suffix for dead letter topics |

#### Job Queue Configuration (`JobQueueConfiguration`)

Configure specific jobs with custom settings:

```csharp
Configure<AbpPubSubBackgroundJobOptions>(options =>
{
    options.JobQueues[typeof(EmailSendingArgs)] = new JobQueueConfiguration(
        jobArgsType: typeof(EmailSendingArgs),
        topicName: "my-app.email-jobs",
        subscriptionName: "my-app.email-jobs-sub",
        connectionName: "Default",
        ackDeadlineSeconds: 120,
        prefetchCount: 5
    );
});
```

## Usage

### Define a Job

```csharp
public class EmailSendingArgs
{
    public string EmailAddress { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
}

public class EmailSendingJob : AsyncBackgroundJob<EmailSendingArgs>, ITransientDependency
{
    private readonly IEmailSender _emailSender;

    public EmailSendingJob(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public override async Task ExecuteAsync(EmailSendingArgs args)
    {
        await _emailSender.SendAsync(args.EmailAddress, args.Subject, args.Body);
    }
}
```

### Enqueue a Job

```csharp
public class MyService
{
    private readonly IBackgroundJobManager _backgroundJobManager;

    public MyService(IBackgroundJobManager backgroundJobManager)
    {
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task DoSomethingAsync()
    {
        // Enqueue immediately
        await _backgroundJobManager.EnqueueAsync(new EmailSendingArgs
        {
            EmailAddress = "user@example.com",
            Subject = "Hello",
            Body = "World"
        });

        // Enqueue with delay
        await _backgroundJobManager.EnqueueAsync(
            new EmailSendingArgs
            {
                EmailAddress = "user@example.com",
                Subject = "Delayed Hello",
                Body = "This will be sent in 5 minutes"
            },
            delay: TimeSpan.FromMinutes(5));
    }
}
```

### Start Job Processing

In your application startup, start processing for each job type:

```csharp
public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
{
    var jobManager = context.ServiceProvider.GetRequiredService<IPubSubBackgroundJobManager>();
    
    // Start processing specific job types
    if (jobManager is PubSubBackgroundJobManager pubSubManager)
    {
        await pubSubManager.StartProcessingAsync<EmailSendingArgs>();
        await pubSubManager.StartProcessingAsync<ReportGenerationArgs>();
    }
}
```

### Using Job Name Attribute

```csharp
[BackgroundJobName("EmailSending")]
public class EmailSendingArgs
{
    public string EmailAddress { get; set; }
    // ...
}
```

This will create a topic named `AbpBackgroundJobs.EmailSending` instead of using the full type name.

## Local Development with Docker

Use the included `docker-compose.yml` to run a local Pub/Sub emulator:

```bash
docker-compose up pubsub-emulator
```

Then configure your application to use the emulator:

```json
{
  "PubSub": {
    "Connections": {
      "Default": {
        "ProjectId": "test-project",
        "EmulatorHost": "localhost:8085"
      }
    }
  }
}
```

## Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Application   │────▶│   Pub/Sub Topic │────▶│  Subscription   │
│                 │     │ (Job Queue)     │     │                 │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
                                                         ▼
                                               ┌─────────────────┐
                                               │  Job Processor  │
                                               │  (Worker)       │
                                               └─────────────────┘
```

- Each job type gets its own topic and subscription
- Jobs are processed in FIFO order (within a single subscription)
- Failed jobs are retried or moved to dead letter topic
- Delayed jobs are supported via message attributes

## License

MIT
