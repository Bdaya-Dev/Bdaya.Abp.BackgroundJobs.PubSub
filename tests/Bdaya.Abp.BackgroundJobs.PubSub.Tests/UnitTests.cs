using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// Unit tests for PubSub background jobs that don't require the emulator.
/// </summary>
public class UnitTests
{
    [Fact]
    public void Serializer_Should_Serialize_And_Deserialize_JobArgs()
    {
        // Arrange
        var serializer = new PubSubJobSerializer();
        var args = new TestJobArgs
        {
            Message = "Hello, World!",
            Value = 42,
            Timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var serialized = serializer.Serialize(args);
        var deserialized = serializer.Deserialize<TestJobArgs>(serialized);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.Message.ShouldBe(args.Message);
        deserialized.Value.ShouldBe(args.Value);
        deserialized.Timestamp.ShouldBe(args.Timestamp);
    }

    [Fact]
    public void Serializer_Should_Handle_Null_Values()
    {
        // Arrange
        var serializer = new PubSubJobSerializer();
        var args = new TestJobArgs
        {
            Message = null!,
            Value = 0,
            Timestamp = default
        };

        // Act
        var serialized = serializer.Serialize(args);
        var deserialized = serializer.Deserialize<TestJobArgs>(serialized);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.Message.ShouldBeNull();
        deserialized.Value.ShouldBe(0);
    }

    [Fact]
    public void Options_Should_Have_Default_Values()
    {
        // Arrange
        var options = new AbpPubSubBackgroundJobOptions();

        // Assert
        options.DefaultTopicPrefix.ShouldBe("AbpBackgroundJobs");
        options.DefaultSubscriptionPrefix.ShouldBe("AbpBackgroundJobs");
        options.DefaultDelayedTopicPrefix.ShouldBe("AbpBackgroundJobs.Delayed");
        options.PrefetchCount.ShouldBe(1);
        options.AckDeadlineSeconds.ShouldBe(60);
        options.MessageRetentionDays.ShouldBe(7);
        options.MaxDeliveryAttempts.ShouldBe(5);
        options.AutoCreateTopics.ShouldBeTrue();
        options.AutoCreateSubscriptions.ShouldBeTrue();
        options.DeadLetterTopicSuffix.ShouldBe("DeadLetter");
    }

    [Fact]
    public void Options_Should_Create_Default_Job_Queue_Configuration()
    {
        // Arrange
        var options = new AbpPubSubBackgroundJobOptions();

        // Act
        var queueConfig = options.GetOrCreateJobQueue<TestJobArgs>();

        // Assert
        queueConfig.ShouldNotBeNull();
        queueConfig.JobArgsType.ShouldBe(typeof(TestJobArgs));
        queueConfig.TopicName.ShouldContain("TestJobArgs");
        queueConfig.SubscriptionName.ShouldContain("TestJobArgs");
        queueConfig.ConnectionName.ShouldBe("Default");
        queueConfig.AckDeadlineSeconds.ShouldBe(60);
    }

    [Fact]
    public void Options_Should_Use_BackgroundJobName_Attribute()
    {
        // Arrange
        var options = new AbpPubSubBackgroundJobOptions();

        // Act
        var queueConfig = options.GetOrCreateJobQueue<CustomNamedJobArgs>();

        // Assert
        queueConfig.ShouldNotBeNull();
        queueConfig.TopicName.ShouldContain("CustomNamedJob");
        queueConfig.SubscriptionName.ShouldContain("CustomNamedJob");
    }

    [Fact]
    public void Options_Should_Return_Same_Configuration_For_Same_Type()
    {
        // Arrange
        var options = new AbpPubSubBackgroundJobOptions();

        // Act
        var config1 = options.GetOrCreateJobQueue<TestJobArgs>();
        var config2 = options.GetOrCreateJobQueue<TestJobArgs>();

        // Assert
        config1.ShouldBeSameAs(config2);
    }

    [Fact]
    public void Options_Should_Allow_Custom_Job_Queue_Configuration()
    {
        // Arrange
        var options = new AbpPubSubBackgroundJobOptions();
        var customConfig = new JobQueueConfiguration(
            typeof(TestJobArgs),
            topicName: "custom-topic",
            subscriptionName: "custom-subscription",
            connectionName: "CustomConnection",
            ackDeadlineSeconds: 120,
            prefetchCount: 10
        );

        // Act
        options.JobQueues[typeof(TestJobArgs)] = customConfig;
        var retrievedConfig = options.GetOrCreateJobQueue<TestJobArgs>();

        // Assert
        retrievedConfig.ShouldBeSameAs(customConfig);
        retrievedConfig.TopicName.ShouldBe("custom-topic");
        retrievedConfig.SubscriptionName.ShouldBe("custom-subscription");
        retrievedConfig.ConnectionName.ShouldBe("CustomConnection");
        retrievedConfig.AckDeadlineSeconds.ShouldBe(120);
        retrievedConfig.PrefetchCount.ShouldBe(10);
    }

    [Fact]
    public void ConnectionConfiguration_Should_Have_Default_Values()
    {
        // Arrange
        var config = new PubSubConnectionConfiguration();

        // Assert
        config.ProjectId.ShouldBeNull();
        config.CredentialsPath.ShouldBeNull();
        config.EmulatorHost.ShouldBeNull();
    }

    [Fact]
    public void PubSubOptions_Should_Have_Default_Connection()
    {
        // Arrange
        var options = new AbpPubSubOptions();

        // Assert
        options.Connections.ShouldContainKey("Default");
        options.Default.ShouldNotBeNull();
    }

    [Fact]
    public void PubSubOptions_Should_Allow_Multiple_Connections()
    {
        // Arrange
        var options = new AbpPubSubOptions();

        // Act
        options.Connections["Secondary"] = new PubSubConnectionConfiguration
        {
            ProjectId = "secondary-project",
            EmulatorHost = "localhost:8086"
        };

        // Assert
        options.Connections.Count.ShouldBe(2);
        options.Connections["Secondary"].ProjectId.ShouldBe("secondary-project");
    }

    [Fact]
    public void JobQueueConfiguration_Should_Set_All_Properties()
    {
        // Arrange & Act
        var config = new JobQueueConfiguration(
            jobArgsType: typeof(TestJobArgs),
            topicName: "test-topic",
            subscriptionName: "test-subscription",
            delayedTopicName: "test-delayed-topic",
            delayedSubscriptionName: "test-delayed-subscription",
            connectionName: "TestConnection",
            ackDeadlineSeconds: 90,
            messageRetentionDuration: TimeSpan.FromDays(14),
            maxDeliveryAttempts: 10,
            prefetchCount: 5
        );

        // Assert
        config.JobArgsType.ShouldBe(typeof(TestJobArgs));
        config.TopicName.ShouldBe("test-topic");
        config.SubscriptionName.ShouldBe("test-subscription");
        config.DelayedTopicName.ShouldBe("test-delayed-topic");
        config.DelayedSubscriptionName.ShouldBe("test-delayed-subscription");
        config.ConnectionName.ShouldBe("TestConnection");
        config.AckDeadlineSeconds.ShouldBe(90);
        config.MessageRetentionDuration.ShouldBe(TimeSpan.FromDays(14));
        config.MaxDeliveryAttempts.ShouldBe(10);
        config.PrefetchCount.ShouldBe(5);
    }

    [Fact]
    public void BackgroundJobNameAttribute_Should_Store_Name()
    {
        // Arrange
        var attribute = new BackgroundJobNameAttribute("TestJobName");

        // Assert
        attribute.Name.ShouldBe("TestJobName");
    }
}
