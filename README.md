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

Install the NuGet package:

```bash
dotnet add package Bdaya.Abp.BackgroundJobs.PubSub
```

Add the module dependency to your ABP module:

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

| Property      | Description                                   |
| ------------- | --------------------------------------------- |
| `Connections` | Dictionary of named connection configurations |
| `Default`     | Shortcut to access `Connections["Default"]`   |

#### Connection Configuration (`PubSubConnectionConfiguration`)

| Property          | Description                                                          |
| ----------------- | -------------------------------------------------------------------- |
| `ProjectId`       | Google Cloud Project ID (required)                                   |
| `Credential`      | Pre-configured `GoogleCredential` instance (highest priority)        |
| `CredentialsJson` | JSON string with service account credentials                         |
| `CredentialsPath` | Path to service account JSON file                                    |
| `EmulatorHost`    | Pub/Sub emulator host for local development (e.g., `localhost:8085`) |

## Authentication

This package supports multiple authentication methods following [Google Cloud best practices](https://cloud.google.com/docs/authentication). Methods are evaluated in priority order - the first one configured will be used.

### Authentication Methods (Priority Order)

| Priority | Method                          | Property             | Best For                                                                |
| -------- | ------------------------------- | -------------------- | ----------------------------------------------------------------------- |
| 1        | Pre-configured Credential       | `Credential`         | Workload Identity Federation, custom auth flows                         |
| 2        | JSON String                     | `CredentialsJson`    | Secret managers (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) |
| 3        | Credentials File                | `CredentialsPath`    | Local development, legacy systems                                       |
| 4        | Application Default Credentials | *(none - automatic)* | GCE, GKE, Cloud Run, Cloud Functions                                    |
| 5        | Emulator                        | `EmulatorHost`       | Local development and testing                                           |

### ✅ Supported Authentication Methods

#### 1. Application Default Credentials (ADC) - **Recommended for GCP**

When running on Google Cloud (GCE, GKE, Cloud Run, Cloud Functions), ADC automatically uses the attached service account. No configuration needed:

```csharp
Configure<AbpPubSubOptions>(options =>
{
    options.Connections["Default"] = new PubSubConnectionConfiguration
    {
        ProjectId = "your-project-id"
        // No credentials specified - uses ADC automatically
    };
});
```

For local development with ADC:

```bash
gcloud auth application-default login
```

#### 2. Secret Manager Integration (CredentialsJson)

Load credentials from any secret manager:

```csharp
// Azure Key Vault example
var secretClient = new SecretClient(new Uri("https://your-vault.vault.azure.net/"), new DefaultAzureCredential());
var secret = await secretClient.GetSecretAsync("gcp-service-account");

Configure<AbpPubSubOptions>(options =>
{
    options.Connections["Default"] = new PubSubConnectionConfiguration
    {
        ProjectId = "your-project-id",
        CredentialsJson = secret.Value.Value  // JSON string from secret
    };
});
```

#### 3. Pre-configured GoogleCredential

Maximum flexibility for advanced scenarios:

```csharp
// Workload Identity Federation
var credential = GoogleCredential.FromFile("client-config.json")
    .CreateScoped(PublisherServiceApiClient.DefaultScopes);

Configure<AbpPubSubOptions>(options =>
{
    options.Connections["Default"] = new PubSubConnectionConfiguration
    {
        ProjectId = "your-project-id",
        Credential = credential
    };
});
```

#### 4. Service Account Key File

For legacy systems or local development:

```csharp
Configure<AbpPubSubOptions>(options =>
{
    options.Connections["Default"] = new PubSubConnectionConfiguration
    {
        ProjectId = "your-project-id",
        CredentialsPath = "/path/to/service-account.json"
    };
});
```

> ⚠️ **Security Warning**: Service account key files are long-lived credentials. Prefer ADC, Workload Identity, or secret managers in production.

#### 5. Emulator (Local Development)

```csharp
Configure<AbpPubSubOptions>(options =>
{
    options.Connections["Default"] = new PubSubConnectionConfiguration
    {
        ProjectId = "test-project",  // Any project ID works with emulator
        EmulatorHost = "localhost:8085"
    };
});
```

### ❌ Not Directly Supported (Use Credential Property)

These methods require you to create a `GoogleCredential` and pass it via the `Credential` property:

| Method                           | How to Use                                                           |
| -------------------------------- | -------------------------------------------------------------------- |
| **Workload Identity (GKE)**      | Automatic via ADC when configured on the cluster                     |
| **Workload Identity Federation** | Create credential from config file and pass to `Credential`          |
| **Impersonation**                | Use `GoogleCredential.Impersonate()` and pass to `Credential`        |
| **OAuth 2.0 User Credentials**   | Create via `GoogleWebAuthorizationBroker` and pass to `Credential`   |

Example for Workload Identity Federation:

```csharp
// workforce-config.json contains the federation configuration
var credential = GoogleCredential
    .FromFile("workforce-config.json")
    .CreateScoped(PublisherServiceApiClient.DefaultScopes);

Configure<AbpPubSubOptions>(options =>
{
    options.Connections["Default"] = new PubSubConnectionConfiguration
    {
        ProjectId = "your-project-id",
        Credential = credential
    };
});
```

### Environment-Based Configuration

Use `appsettings.json` with environment-specific overrides:

**appsettings.json** (base):

```json
{
  "PubSub": {
    "Connections": {
      "Default": {
        "ProjectId": "your-project-id"
      }
    }
  }
}
```

**appsettings.Development.json**:

```json
{
  "PubSub": {
    "Connections": {
      "Default": {
        "EmulatorHost": "localhost:8085"
      }
    }
  }
}
```

**appsettings.Production.json**:

```json
{
  "PubSub": {
    "Connections": {
      "Default": {
        "ProjectId": "prod-project-id"
      }
    }
  }
}
```

> In production on GCP, ADC is used automatically. No credentials configuration needed!

#### Background Job Options (`AbpPubSubBackgroundJobOptions`)

| Property                    | Default                     | Description                                         |
| --------------------------- | --------------------------- | --------------------------------------------------- |
| `ConnectionName`            | `null`                      | Named connection to use (uses "Default" if not set) |
| `DefaultTopicPrefix`        | `AbpBackgroundJobs`         | Prefix for job topic names                          |
| `DefaultSubscriptionPrefix` | `AbpBackgroundJobs`         | Prefix for job subscription names                   |
| `DefaultDelayedTopicPrefix` | `AbpBackgroundJobs.Delayed` | Prefix for delayed job topic names                  |
| `PrefetchCount`             | `1`                         | Maximum concurrent handlers (flow control)          |
| `AckDeadlineSeconds`        | `60`                        | Message acknowledgment deadline                     |
| `MessageRetentionDays`      | `7`                         | Message retention duration in days                  |
| `MaxDeliveryAttempts`       | `5`                         | Max delivery attempts before dead letter            |
| `AutoCreateTopics`          | `true`                      | Auto-create topics if they don't exist              |
| `AutoCreateSubscriptions`   | `true`                      | Auto-create subscriptions if they don't exist       |
| `DeadLetterTopicSuffix`     | `DeadLetter`                | Suffix for dead letter topics                       |

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

```text
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
