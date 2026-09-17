namespace Vestigium.Logging.Tests;

public sealed class PropertyBagTests
{
    [Fact]
    public void NullAndEmptyBecomeNull()
    {
        Assert.Null(VestigiumPropertyBag.Sanitize(null));
        Assert.Null(VestigiumPropertyBag.Sanitize(new Dictionary<string, string?>()));
        Assert.Null(VestigiumPropertyBag.Sanitize(new Dictionary<string, string?> { ["host"] = null }));
    }

    [Fact]
    public void UnderscoreKeysAreLegal()
    {
        var bag = VestigiumPropertyBag.Sanitize(new Dictionary<string, string?> { ["rtt_ms"] = "12", ["A"] = "1" })!;
        Assert.Equal("12", bag["rtt_ms"]);
        Assert.Equal("1", bag["A"]);
    }

    [Fact]
    public void DuplicateKeyKeepsFirst()
    {
        var bag = VestigiumPropertyBag.Sanitize(new RepeatKeys(("host", "first"), ("host", "second")))!;
        Assert.Equal("first", bag["host"]);
        Assert.Single(bag);
    }

    [Fact]
    public void DropsWhitespaceAndPunctuationKeys()
    {
        var bag = VestigiumPropertyBag.Sanitize(new Dictionary<string, string?>
        {
            ["host name"] = "x",
            ["host-name"] = "x",
            [""] = "x",
            ["_lead"] = "x",
            ["ok"] = "yes"
        })!;
        Assert.Equal(new[] { "ok" }, bag.Keys);
    }

    private sealed class RepeatKeys : IReadOnlyDictionary<string, string?>
    {
        private readonly (string Key, string? Value)[] _items;
        public RepeatKeys(params (string Key, string? Value)[] items) => _items = items;
        public IEnumerable<string> Keys => _items.Select(i => i.Key);
        public IEnumerable<string?> Values => _items.Select(i => i.Value);
        public int Count => _items.Length;
        public string? this[string key] => _items.First(i => i.Key == key).Value;
        public bool ContainsKey(string key) => _items.Any(i => i.Key == key);
        public bool TryGetValue(string key, out string? value)
        {
            var hit = _items.FirstOrDefault(i => i.Key == key);
            value = hit.Value;
            return ContainsKey(key);
        }

        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() =>
            _items.Select(i => new KeyValuePair<string, string?>(i.Key, i.Value)).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
