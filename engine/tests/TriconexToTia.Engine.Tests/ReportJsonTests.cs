using System.Text;
using System.Text.Json;
using TriconexToTia.Core.Results;
using TriconexToTia.Status;
using Xunit;

namespace TriconexToTia.Engine.Tests;

/// <summary>
/// Verifies the engine emits <c>report.json</c> and that the file is honest evidence
/// the shared status engine can consume (missing roundtrip = not verified).
/// </summary>
public sealed class ReportJsonTests : IDisposable
{
    private const string Tc6 = "http://www.plcopen.org/xml/tc6.xsd";
    private const string Ts = "http://www.triconex.com/controller/03/2008";

    private readonly string _root;
    private readonly string _output;
    private readonly string _status;

    public ReportJsonTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "reportjson-" + Guid.NewGuid().ToString("N"));
        _output = Path.Combine(_root, "output");
        _status = Path.Combine(_root, "status");
        Directory.CreateDirectory(_output);
        Directory.CreateDirectory(_status);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Sample() => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <project xmlns="{Tc6}" xmlns:ts="{Ts}">
          <header><content name="name">MainConfiguration</content></header>
          <types>
            <dataTypes />
            <pous>
              <pou name="MainProgram" pouType="program">
                <body>
                  <ST><![CDATA[FUNCTION_BLOCK MainProgram VAR Start : BOOL; END_VAR BEGIN Start := TRUE; END_FUNCTION_BLOCK]]></ST>
                </body>
              </pou>
            </pous>
          </types>
          <instances>
            <configurations>
              <configuration name="MainConfiguration">
                <resource name="Res1">
                  <task name="MainTask" interval="100" />
                  <globalVars>
                    <variable name="AIXXX" address="MIW.0009">
                      <type><derived name="DINT" /></type>
                      <ts:TAGNAME>
                        <ts:POINT_ASSIGNMENT PointType="MEMORY" AliasType="READWRITE_ALIASED" PhysicalAddress="MIW.0009" />
                      </ts:TAGNAME>
                    </variable>
                    <variable name="AIXXX2" address="MIW.0009">
                      <type><derived name="DINT" /></type>
                      <ts:TAGNAME>
                        <ts:POINT_ASSIGNMENT PointType="MEMORY" AliasType="READWRITE_ALIASED" PhysicalAddress="MIW.0009" />
                      </ts:TAGNAME>
                    </variable>
                  </globalVars>
                </resource>
              </configuration>
            </configurations>
            <ts:CONTROLLER name="TRICON_EP" majorMinorVersion="11.2">
              <ts:COMMUNICATION><ts:UDP_PORT>1502</ts:UDP_PORT></ts:COMMUNICATION>
            </ts:CONTROLLER>
          </instances>
        </project>
        """;

    private ConversionResult Run() => new ConversionEngine().Run(
        new MemoryStream(Encoding.UTF8.GetBytes(Sample())),
        new ConverterOptions { TiaVersion = 16, PlcCpu = "6ES7 515-2AM01-0AB0/V2.0" },
        "sample.xml");

    [Fact]
    public void Engine_emits_report_json_with_expected_shape()
    {
        var result = Run();

        Assert.True(result.Success);
        Assert.NotNull(result.ReportJson);

        using var doc = JsonDocument.Parse(result.ReportJson!);
        var root = doc.RootElement;

        Assert.Equal("V16", root.GetProperty("tia_target").GetString());
        Assert.Equal("6ES7 515-2AM01-0AB0/V2.0", root.GetProperty("cpu_order_number").GetString());

        var counts = root.GetProperty("counts");
        Assert.True(counts.GetProperty("pous_total").GetInt32() >= 1);
        Assert.Equal(0, counts.GetProperty("fatals").GetInt32());

        var issues = root.GetProperty("known_issues").EnumerateArray().ToList();
        Assert.Contains(issues, i => i.GetProperty("id").GetString() == "INIT_WARNINGS");
        Assert.Contains(issues, i => i.GetProperty("id").GetString() == "TYPE_CONFLICT");

        var collision = issues.Single(i => i.GetProperty("id").GetString() == "ADDRESS_COLLISION");
        Assert.Equal(2, collision.GetProperty("tags_affected").GetInt32());
        Assert.Equal(1, collision.GetProperty("shared_address_count").GetInt32());
        Assert.Equal("OPEN", collision.GetProperty("status").GetString());
    }

    [Fact]
    public void Report_json_drives_status_engine_to_unverified_until_roundtrip_exists()
    {
        var result = Run();
        File.WriteAllText(Path.Combine(_output, "report.json"), result.ReportJson!);

        var status = new StatusEngine().Compute(_output, _status);

        Assert.Equal(EvidenceStatus.NotStarted, status.BuildStatus);
        Assert.Equal(EvidenceStatus.Unverified, status.ConversionStatus);
        Assert.True(status.OpenKnownIssues >= 1);

        // A roundtrip that verifies the same block lifts it to VERIFIED IN TIA.
        File.WriteAllText(Path.Combine(_output, "roundtrip.json"),
            """{ "objects": [ { "kind": "Block", "name": "MainProgram", "imported": true, "compiled": true, "messages": [] } ] }""");
        var verified = new StatusEngine().Compute(_output, _status);
        Assert.Equal(EvidenceStatus.VerifiedInTia, verified.ConversionStatus);
    }
}