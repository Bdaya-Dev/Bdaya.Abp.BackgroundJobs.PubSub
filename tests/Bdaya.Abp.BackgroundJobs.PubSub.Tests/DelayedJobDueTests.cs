using Shouldly;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// invora-backend#312, second defect — the delayed "is it due yet?" decision must compare
/// INSTANTS, not wall-clock readings in whatever timezone the host happens to be in.
///
/// <para>These are deterministic and host-independent by construction: <c>now</c> is injected
/// rather than read from the machine clock, and the inputs carry explicit offsets. That is
/// deliberate. The bug being guarded against was invisible on a UTC host — which is every CI
/// container — and only appeared when the process ran somewhere else, so a test that reads the
/// local clock would reproduce the same blind spot it is meant to close.</para>
/// </summary>
public class DelayedJobDueTests
{
    private static bool Due(string? attribute, string nowIso) =>
        PubSubBackgroundJobManager.IsDelayedJobDue(
            attribute,
            DateTimeOffset.Parse(nowIso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));

    [Fact]
    public void A_Time_In_The_Past_Is_Due()
    {
        Due("2026-01-01T00:00:00.0000000Z", "2026-01-01T00:00:01Z").ShouldBeTrue();
    }

    [Fact]
    public void A_Time_In_The_Future_Is_Not_Due()
    {
        Due("2026-01-01T00:01:00.0000000Z", "2026-01-01T00:00:00Z").ShouldBeFalse();
    }

    [Fact]
    public void The_Exact_Scheduled_Instant_Is_Due()
    {
        // Boundary: `>=`, not `>`. A job scheduled for exactly now must run now, otherwise it
        // waits a whole extra redelivery backoff for no reason.
        Due("2026-01-01T00:00:00.0000000Z", "2026-01-01T00:00:00Z").ShouldBeTrue();
    }

    /// <summary>
    /// The regression this class exists for. <c>03:00+03:00</c> and <c>00:00Z</c> are the SAME
    /// instant; a comparison that reads the offset-bearing value as a local wall-clock time gets
    /// this wrong by exactly the offset.
    /// </summary>
    [Fact]
    public void An_Offset_Bearing_Timestamp_Is_Compared_As_An_Instant_Not_A_Wall_Clock_Reading()
    {
        // Same instant as 2026-01-01T00:00:00Z, so one second later it is due.
        Due("2026-01-01T03:00:00.0000000+03:00", "2026-01-01T00:00:01Z").ShouldBeTrue();

        // ...and one second earlier it is not.
        Due("2026-01-01T03:00:00.0000000+03:00", "2025-12-31T23:59:59Z").ShouldBeFalse();

        // A negative offset behaves symmetrically.
        Due("2025-12-31T19:00:00.0000000-05:00", "2026-01-01T00:00:01Z").ShouldBeTrue();
    }

    /// <summary>
    /// Round-trips the exact string <c>EnqueueDelayedAsync</c> writes
    /// (<c>DateTime.UtcNow.Add(delay).ToString("O")</c>) so the producer and consumer of this
    /// attribute are pinned to one another rather than to two independent assumptions.
    /// </summary>
    [Fact]
    public void RoundTrips_The_Attribute_Format_The_Publisher_Actually_Writes()
    {
        var scheduled = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var attribute = scheduled.ToString("O");

        PubSubBackgroundJobManager
            .IsDelayedJobDue(attribute, new DateTimeOffset(scheduled).AddSeconds(-1))
            .ShouldBeFalse("one second before the scheduled instant the job is not due.");

        PubSubBackgroundJobManager
            .IsDelayedJobDue(attribute, new DateTimeOffset(scheduled).AddSeconds(1))
            .ShouldBeTrue("one second after the scheduled instant the job is due.");
    }

    /// <summary>
    /// An OFFSET-LESS timestamp is read as UTC, not as the host's local wall clock.
    ///
    /// <para>This is the one input shape that still carried the host-dependence after the first
    /// pass at this fix, and it is invisible on a UTC host. Measured on a UTC+2 host before the
    /// change: <c>"2026-01-01T00:00:00.0000000"</c> parsed to <c>2026-01-01T00:00:00+02:00</c>,
    /// i.e. <b>22:00 UTC the previous day</b> — two hours out, and a different instant on every
    /// machine. Caught by CodeRabbit on the PR and confirmed by direct measurement.</para>
    /// </summary>
    [Fact]
    public void An_Offset_Less_Timestamp_Is_Read_As_Utc_Not_As_Host_Local_Time()
    {
        // Read as 00:00 UTC, so one second later it is due...
        Due("2026-01-01T00:00:00.0000000", "2026-01-01T00:00:01Z").ShouldBeTrue();

        // ...and one second earlier it is not. Under a host-local reading on any host EAST of
        // UTC the first of these flips, and on any host WEST of UTC the second does.
        Due("2026-01-01T00:00:00.0000000", "2025-12-31T23:59:59Z").ShouldBeFalse();
    }

    /// <summary>
    /// A missing or unreadable attribute must FAIL OPEN (run it) rather than closed (never run
    /// it). Failing closed would strand the job silently forever, which is the same class of
    /// defect #312 is about; failing open runs it early, which is recoverable.
    ///
    /// <para>Early is not free, though, and the fall-through must not be silent about it — the
    /// delay is LOST, which on a self-re-enqueueing job collapses its backoff ladder.
    /// <c>ProcessJobMessageAsync</c> therefore logs a warning naming the message, job type and
    /// offending value before running it. That log is what makes "recoverable" true rather than
    /// aspirational; <see cref="TryParseScheduledTime_DistinguishesUnparseableFromNotYetDue"/>
    /// pins the branch condition it depends on.</para>
    ///
    /// <para>Note this is also why an offset-less value is INTERPRETED (above) rather than
    /// rejected: rejecting it would land here and discard the delay altogether.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-timestamp")]
    public void An_Absent_Or_Unparseable_Attribute_Is_Treated_As_Due(string? attribute)
    {
        Due(attribute, "2026-01-01T00:00:00Z").ShouldBeTrue();
    }

    /// <summary>
    /// The branch condition the "we lost this job's delay" warning hangs off.
    ///
    /// <para><see cref="PubSubBackgroundJobManager.IsDelayedJobDue"/> collapses "unparseable" and
    /// "due now" to the same <c>true</c>, which is right for the decision but useless for
    /// diagnosis — a caller that only has the predicate CANNOT tell a corrupted attribute from a
    /// job that legitimately came due, and so cannot warn about the first without crying wolf on
    /// the second. <c>TryParseScheduledTime</c> keeps them apart, which is what lets
    /// <c>ProcessJobMessageAsync</c> log only in the case that actually lost information.</para>
    /// </summary>
    [Fact]
    public void TryParseScheduledTime_DistinguishesUnparseableFromNotYetDue()
    {
        PubSubBackgroundJobManager
            .TryParseScheduledTime("2026-01-01T00:00:00.0000000Z", out var parsed)
            .ShouldBeTrue("a well-formed instant must parse");
        parsed.ShouldBe(DateTimeOffset.Parse("2026-01-01T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));

        foreach (var bad in new[] { null, "", "   ", "not-a-timestamp", "2026-13-45T99:99:99Z" })
        {
            PubSubBackgroundJobManager.TryParseScheduledTime(bad, out _)
                .ShouldBeFalse($"'{bad}' is not a readable instant and must be reported as such, " +
                    "not silently treated as 'due now'");

            // ...and the predicate still runs it, so the two are genuinely different answers to
            // different questions rather than a distinction without a difference.
            Due(bad, "2026-01-01T00:00:00Z").ShouldBeTrue();
        }
    }
}
