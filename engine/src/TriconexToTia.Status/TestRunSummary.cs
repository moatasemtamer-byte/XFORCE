using System.Xml.Linq;

namespace TriconexToTia.Status;

/// <summary>
/// Minimal reader for a Visual Studio TRX file (<c>dotnet test --logger trx</c>).
/// Namespace-agnostic: matches elements by local name.
/// </summary>
public sealed class TestRunSummary
{
    public int Total { get; init; }
    public int Executed { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string? Outcome { get; init; }

    public bool AllPassed => Total > 0 && Failed == 0 &&
        !string.Equals(Outcome, "Failed", StringComparison.OrdinalIgnoreCase);

    public static TestRunSummary? TryReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var doc = XDocument.Load(path);
            var summary = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ResultSummary");
            if (summary is null) return null;

            var counters = summary.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Counters");

            int Attr(string name)
            {
                var value = counters?.Attribute(name)?.Value;
                return int.TryParse(value, out var n) ? n : 0;
            }

            return new TestRunSummary
            {
                Total = Attr("total"),
                Executed = Attr("executed"),
                Passed = Attr("passed"),
                Failed = Attr("failed"),
                Skipped = Attr("skipped"),
                Outcome = summary.Attribute("outcome")?.Value
            };
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return null;
        }
    }
}