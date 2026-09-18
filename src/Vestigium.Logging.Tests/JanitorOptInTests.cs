namespace Vestigium.Logging.Tests;

public sealed class JanitorOptInTests
{
    [Fact]
    public void JanitorFlagsDefaultOff()
    {
        var options = new VestigiumLoggerOptions();
        Assert.False(options.JanitorEnabled);
        Assert.False(options.OperationsJanitorEnabled);
    }

    [Fact]
    public void EnabledWithoutAgeThrows()
    {
        Assert.Throws<ArgumentException>(() => new VestigiumLoggerOptions { JanitorEnabled = true }.ValidateArchiveOptions());
        Assert.Throws<ArgumentException>(() => new VestigiumLoggerOptions { OperationsJanitorEnabled = true }.ValidateArchiveOptions());
    }

    [Fact]
    public void HostAndOpsJanitorAgesAreIndependent()
    {
        var options = new VestigiumLoggerOptions
        {
            JanitorEnabled = true,
            JanitorMaxAge = TimeSpan.FromDays(14),
            OperationsJanitorEnabled = true,
            OperationsJanitorMaxAge = TimeSpan.FromDays(90)
        };
        options.ValidateArchiveOptions();
        Assert.Equal(TimeSpan.FromDays(14), options.JanitorMaxAge);
        Assert.Equal(TimeSpan.FromDays(90), options.OperationsJanitorMaxAge);
    }
}
