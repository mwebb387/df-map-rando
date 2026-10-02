using DarkForces.Core.Verification;

namespace DarkForces.Core.Tests;

public class RoundTripTests
{
    [Theory]
    [MemberData(nameof(TestData.DemoGobNames), MemberType = typeof(TestData))]
    public void Every_supported_file_in_demo_gob_round_trips(string gobName)
    {
        var results = RoundTrip.CheckGob(gobName, TestData.DemoGobBytes(gobName)).ToList();
        Assert.All(results, r => Assert.True(r.Status == RoundTripStatus.Ok, r.ToString()));
        Assert.Contains(results, r => r.File.EndsWith(".LEV"));
    }

    [Fact]
    public void Detects_dropped_data()
    {
        // The GOL model has no place for EXTRA:, so the writer drops it and the token check must notice.
        var result = RoundTrip.CheckFile("x.GOL", "x.GOL", "GOL 1.0\r\nGOAL: 0 ITEM: 5 EXTRA: 1\r\n"u8.ToArray());
        Assert.Equal(RoundTripStatus.Mismatch, result.Status);
    }

    [Fact]
    public void Reports_parse_errors()
    {
        var result = RoundTrip.CheckFile("x.LEV", "x.LEV", "LEV 2.1\r\nBOGUS 1\r\n"u8.ToArray());
        Assert.Equal(RoundTripStatus.Error, result.Status);
    }
}
