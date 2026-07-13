using NetworkRoutesConflictResolver.Parsing;

namespace NetworkRoutesConflictResolver.Tests;

public sealed class ParserTests
{
    private const string Json = """
        [
            { "hostname": "1.0.0.0/9", "ip": "" },
            { "hostname": "10.0.0.0/8", "ip": "" },
            { "hostname": "not-a-cidr", "ip": "" }
        ]
        """;

    private const string Plain = """
        # personal split-tunnel list
        1.0.0.0/9
        10.0.0.0/8   // trailing comment
        192.168.1.5
        garbage-line
        """;

    [Fact]
    public void Json_parser_reads_entries_and_collects_invalid()
    {
        var result = new JsonIpListParser().Parse(Json);
        Assert.Equal(["1.0.0.0/9", "10.0.0.0/8"], result.Entries.Select(e => e.Hostname));
        Assert.Equal(["not-a-cidr"], result.Invalid);
    }

    [Fact]
    public void Plain_parser_reads_cidrs_skips_comments_and_bare_ip_is_slash32()
    {
        var result = new PlainTextIpListParser().Parse(Plain);
        Assert.Equal(
            ["1.0.0.0/9", "10.0.0.0/8", "192.168.1.5"],
            result.Entries.Select(e => e.Hostname));
        Assert.Equal("192.168.1.5/32", result.Entries[2].Cidr.ToString());
        Assert.Equal(["garbage-line"], result.Invalid);
    }

    [Fact]
    public void CanParse_distinguishes_formats()
    {
        Assert.True(new JsonIpListParser().CanParse(Json));
        Assert.False(new JsonIpListParser().CanParse(Plain));
        Assert.True(new PlainTextIpListParser().CanParse(Plain));
        Assert.False(new PlainTextIpListParser().CanParse(Json));
    }

    [Fact]
    public void Registry_resolves_by_id()
    {
        var registry = new IpListParserRegistry();
        Assert.IsType<JsonIpListParser>(registry.Resolve("json", Plain));
        Assert.IsType<PlainTextIpListParser>(registry.Resolve("plain", Json));
    }

    [Fact]
    public void Registry_auto_detects_by_content()
    {
        var registry = new IpListParserRegistry();
        Assert.IsType<JsonIpListParser>(registry.Resolve("auto", Json));
        Assert.IsType<PlainTextIpListParser>(registry.Resolve(null, Plain));
    }

    [Fact]
    public void Registry_unknown_format_throws_with_available_list()
    {
        var registry = new IpListParserRegistry();
        var ex = Assert.Throws<ArgumentException>(() => registry.Resolve("yaml", Json));
        Assert.Contains("json", ex.Message);
        Assert.Contains("plain", ex.Message);
    }

    [Fact]
    public void Registry_available_formats_include_auto_first()
    {
        var registry = new IpListParserRegistry();
        Assert.Equal(["auto", "json", "plain"], registry.AvailableFormats);
    }
}
