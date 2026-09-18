namespace Vestigium.Logging.Tests;

public sealed class EventIdRangeTests
{
    [Fact]
    public void ConstantsSplitCatalogOpsAndCustom()
    {
        Assert.Equal(4999, VestigiumEventCatalog.ReservedMax);
        Assert.Equal(5000, VestigiumEventCatalog.OpsMin);
        Assert.Equal(9999, VestigiumEventCatalog.OpsMax);
        Assert.Equal(10000, VestigiumEventCatalog.CustomMin);
        Assert.True(VestigiumEventCatalog.IsOpsId(5000));
        Assert.True(VestigiumEventCatalog.IsOpsId(9999));
        Assert.False(VestigiumEventCatalog.IsOpsId(4999));
        Assert.False(VestigiumEventCatalog.IsOpsId(10000));
        Assert.True(VestigiumEventCatalog.IsCustomId(10000));
        Assert.False(VestigiumEventCatalog.IsCustomId(9999));
    }

    [Fact]
    public void EmbeddedEngineOpsIdIsAccepted()
    {
        var row = new VestigiumEventDefinition(
            5000, "Engine.Start", "Vestigium.Logging.Engine.Start",
            "Engine", "Lifetime", "Information", "Engine", true);
        var catalog = new VestigiumEventCatalog(
            VestigiumEventCatalog.SeedGeneral().Append(row), allowCustom: false);
        Assert.True(catalog.TryGetById(5000, out var loaded));
        Assert.Equal("Engine", loaded.Kind);
    }

    [Fact]
    public void EmbeddedNonEngineOpsIdThrows()
    {
        var row = new VestigiumEventDefinition(
            5000, "Nope", "App.Nope", "System", "Core", "Error", "Exception", true);
        Assert.Throws<InvalidOperationException>(() =>
            new VestigiumEventCatalog(VestigiumEventCatalog.SeedGeneral().Append(row), allowCustom: false));
    }

    [Fact]
    public void OperationsLogIsEnabledByDefault()
    {
        var options = new VestigiumLoggerOptions();
        Assert.True(options.OperationsLogEnabled);
        options.OperationsLogEnabled = false;
        Assert.False(options.OperationsLogEnabled);
    }
}
