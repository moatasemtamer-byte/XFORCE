using TriconexToTia.Status;
using Xunit;

namespace TriconexToTia.Status.Tests;

/// <summary>
/// Tests for the evidence-driven status engine. Every test writes (or omits)
/// real evidence files and asserts the status is derived correctly. No test
/// relies on hard-coded status.
/// </summary>
public sealed class StatusEngineTests : IDisposable
{
    private readonly string _root;
    private readonly string _output;
    private readonly string _status;

    public StatusEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "status-tests-" + Guid.NewGuid().ToString("N"));
        _output = Path.Combine(_root, "output");
        _status = Path.Combine(_root, "status");
        Directory.CreateDirectory(_output);
        Directory.CreateDirectory(_status);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ProjectStatus Compute() => new StatusEngine().Compute(_output, _status);

    private void WriteReport(string json) => File.WriteAllText(Path.Combine(_output, "report.json"), json);
    private void WriteRoundTrip(string json) => File.WriteAllText(Path.Combine(_output, "roundtrip.json"), json);
    private void WriteTests(string xml) => File.WriteAllText(Path.Combine(_status, "tests.trx"), xml);

    private static string Trx(int total, int passed, int failed, string outcome) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary outcome="{outcome}">
            <Counters total="{total}" executed="{total}" passed="{passed}" failed="{failed}" />
          </ResultSummary>
        </TestRun>
        """;

    private const string EmptyReport = """{ "counts": { "fatals": 0 }, "converted": [], "approximated": [], "manual": [], "never_converted": [], "known_issues": [] }""";

    [Fact]
    public void Status_MissingEvidenceFile_IsNotStarted()
    {
        var status = Compute();

        Assert.Equal(EvidenceStatus.NotStarted, status.BuildStatus);
        Assert.Equal(EvidenceStatus.NotStarted, status.ConversionStatus);
        Assert.Equal(0, status.CompileWarnings);
        Assert.Equal(0, status.OpenKnownIssues);
        Assert.Null(status.EvidencePath);
        Assert.Empty(status.Items);
    }

    [Fact]
    public void Status_TestsTrxAllPass_BuildIsUnitTestOnly()
    {
        WriteTests(Trx(total: 151, passed: 151, failed: 0, outcome: "Passed"));

        var status = Compute();

        Assert.Equal(EvidenceStatus.UnitTestOnly, status.BuildStatus);
    }

    [Fact]
    public void Status_TestsTrxWithFailure_BuildIsFailed()
    {
        WriteTests(Trx(total: 151, passed: 149, failed: 2, outcome: "Failed"));

        var status = Compute();

        Assert.Equal(EvidenceStatus.Failed, status.BuildStatus);
    }

    [Fact]
    public void Status_RoundtripImportedAndCompiled_IsVerifiedInTia()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            {
              "timestamp": "2026-10-06T14:32:01Z",
              "objects": [
                { "kind": "Block", "name": "PumpCtrl", "imported": true, "compiled": true, "messages": [] }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(EvidenceStatus.VerifiedInTia, status.ConversionStatus);
        var item = Assert.Single(status.Items);
        Assert.Equal(EvidenceStatus.VerifiedInTia, item.Status);
        Assert.Equal("2026-10-06T14:32:01Z", status.EvidenceTimestamp);
    }

    [Fact]
    public void Status_RoundtripImportedNotCompiled_IsUnverified()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            {
              "objects": [
                { "kind": "Block", "name": "PumpCtrl", "imported": true, "compiled": false, "messages": [] }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(EvidenceStatus.Unverified, status.ConversionStatus);
        Assert.Equal(EvidenceStatus.Unverified, Assert.Single(status.Items).Status);
        Assert.Equal(1, status.UnverifiedCount);
    }

    [Fact]
    public void Status_ApproximateItem_CappedAtUnverified_EvenIfCompiled()
    {
        WriteReport("""
            {
              "counts": { "fatals": 0 },
              "converted": [],
              "approximated": [ { "kind": "LibraryCall", "name": "TDD_I", "reason": "TOF wrapper" } ],
              "manual": [], "never_converted": [],
              "known_issues": []
            }
            """);
        WriteRoundTrip("""
            {
              "objects": [
                { "kind": "Block", "name": "TDD_I", "imported": true, "compiled": true, "messages": [] }
              ]
            }
            """);

        var status = Compute();

        var item = Assert.Single(status.Items);
        Assert.Equal(EvidenceStatus.Unverified, item.Status);
        Assert.Equal(EvidenceStatus.Unverified, status.ConversionStatus);
    }

    [Fact]
    public void Status_CompileWarning_CountedInFooter()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            {
              "objects": [
                {
                  "kind": "Block", "name": "PumpCtrl", "imported": true, "compiled": true,
                  "messages": [
                    { "state": "Warning", "description": "Variable '#Q1' might not be initialized", "path": "PumpCtrl" }
                  ]
                }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(1, status.CompileWarnings);
        Assert.Equal(EvidenceStatus.VerifiedInTia, Assert.Single(status.Items).Status);
    }

    [Fact]
    public void Status_CompileWarning_FromTopLevelField_IsPreferred()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            { "compile_warnings": 7, "objects": [] }
            """);

        var status = Compute();

        Assert.Equal(7, status.CompileWarnings);
    }

    [Fact]
    public void Status_ObjectWithErrorState_IsFailed()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            {
              "objects": [
                {
                  "kind": "Block", "name": "PumpCtrl", "imported": true, "compiled": false,
                  "messages": [ { "state": "Error", "description": "Undefined identifier '1'" } ]
                }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(EvidenceStatus.Failed, status.ConversionStatus);
        Assert.Equal(EvidenceStatus.Failed, Assert.Single(status.Items).Status);
    }

    [Fact]
    public void Status_FailedImport_IsFailed()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            {
              "objects": [
                { "kind": "UDT", "name": "PumpData", "imported": false, "compiled": false, "messages": [] }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(EvidenceStatus.Failed, Assert.Single(status.Items).Status);
    }

    [Fact]
    public void Status_OpenKnownIssue_AppearsInFooterCount()
    {
        WriteReport("""
            {
              "counts": { "fatals": 0 },
              "converted": [], "approximated": [], "manual": [], "never_converted": [],
              "known_issues": [
                { "id": "INIT_WARNINGS", "description": "VAR init", "status": "OPEN" },
                { "id": "RESOLVED_ONE", "description": "done", "status": "RESOLVED" }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(1, status.OpenKnownIssues);
    }

    [Fact]
    public void Status_ConvertedItemNotSeenByTia_IsUnitTestOnly()
    {
        WriteReport("""
            {
              "counts": { "fatals": 0 },
              "converted": [ { "kind": "Block", "name": "SimpleST", "status": "CONVERTED" } ],
              "approximated": [], "manual": [], "never_converted": [], "known_issues": []
            }
            """);

        var status = Compute();

        Assert.Equal(EvidenceStatus.Unverified, status.ConversionStatus); // report exists, nothing verified
        Assert.Equal(1, status.UnitTestOnlyCount);
        Assert.Equal(EvidenceStatus.UnitTestOnly, Assert.Single(status.Items).Status);
    }

    [Fact]
    public void Status_PhaseWithoutRoundtripEntry_IsNotStarted()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""{ "objects": [] }""");

        var status = Compute();

        Assert.Equal(8, status.PhaseStatuses.Count);
        Assert.All(status.PhaseStatuses, s => Assert.Equal(EvidenceStatus.NotStarted, s));
    }

    [Fact]
    public void Status_PhaseAllVerified_IsVerifiedInTia()
    {
        WriteReport(EmptyReport);
        WriteRoundTrip("""
            {
              "objects": [
                { "kind": "Block", "name": "A", "imported": true, "compiled": true, "messages": [], "phase": 4 },
                { "kind": "Block", "name": "B", "imported": true, "compiled": true, "messages": [], "phase": 4 }
              ]
            }
            """);

        var status = Compute();

        Assert.Equal(EvidenceStatus.VerifiedInTia, status.PhaseStatuses[4]);
        Assert.Equal(EvidenceStatus.NotStarted, status.PhaseStatuses[0]);
        Assert.Equal(2, status.Phases[4].VerifiedCount);
    }
}