// JSON exporter — emits a structured, machine-readable view of the analysis
// (one object per page, each with its rungs).  Uses System.Text.Json only, so
// no extra NuGet dependency.  Handy for feeding the equations into scripts,
// diffing two revisions of a program, or re-importing elsewhere.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitReader.Core;

public static class JsonExporter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(string pdfName, string dialect, IReadOnlyList<PageLadder> pages,
                                XrefIndex? xref = null)
    {
        var doc = new
        {
            source = pdfName,
            dialect,
            page_count = pages.Count,
            rung_count = pages.Sum(p => p.Rungs.Count),
            cross_reference = xref is null ? null : BuildXrefDto(xref),
            pages = pages.Select(p => new
            {
                page = p.PageNumber,
                rung_count = p.Rungs.Count,
                rungs = p.Rungs.Select(r =>
                {
                    var addr = r.Output?.Address ?? "";
                    var label = p.AddressLabels.TryGetValue(addr, out var l)
                        ? l : r.Output?.Label ?? "";
                    return new
                    {
                        number = r.Number,
                        kind = KindString(r.Output?.Kind),
                        output = addr,
                        label = string.IsNullOrEmpty(label) ? null : label,
                        boolean_equation = r.BooleanEquation,
                        function_equation = r.FunctionEquation,
                        minimized = string.IsNullOrEmpty(r.Minimized) ? null : r.Minimized,
                        comment = string.IsNullOrEmpty(r.Comment) ? null : r.Comment,
                        statements = r.Statements.Count > 0 ? r.Statements : null,
                        note = r.Note,
                        inputs = r.Inputs.Count > 0
                            ? r.Inputs.Select(i => new
                            {
                                kind = i.Kind.ToString(),
                                address = i.Address,
                                label = string.IsNullOrEmpty(i.Label) ? null : i.Label,
                            }).ToArray()
                            : null,
                    };
                }).ToArray(),
            }).ToArray(),
        };
        return JsonSerializer.Serialize(doc, Opts);
    }

    public static void Write(string pdfName, string dialect,
        IReadOnlyList<PageLadder> pages, string path, XrefIndex? xref = null) =>
        File.WriteAllText(path, ToJson(pdfName, dialect, pages, xref), new UTF8Encoding(false));

    private static object BuildXrefDto(XrefIndex idx)
    {
        string[] Addrs(IEnumerable<AddressXref> xs) =>
            xs.Select(e => e.Address).OrderBy(a => a).ToArray();
        return new
        {
            distinct_addresses = idx.ByAddress.Count,
            coil_outputs = idx.AllCoils.Count(),
            field_inputs = Addrs(idx.FieldInputs),
            field_outputs = Addrs(idx.FieldOutputs),
            state_variables = Addrs(idx.StateVariables),
            stuck_latches = Addrs(idx.StuckLatches),
            write_only_unlatches = Addrs(idx.WriteOnlyUnlatches),
            dangling_reads = Addrs(idx.DanglingReads),
            orphaned_writes = Addrs(idx.OrphanedWrites),
            multiple_writers = Addrs(idx.MultipleWriters),
        };
    }

    private static string KindString(ElementKind? k) => k switch
    {
        ElementKind.Coil => "COIL",
        ElementKind.Latch => "LATCH",
        ElementKind.Unlatch => "UNLATCH",
        ElementKind.Timer => "TIMER",
        ElementKind.LetAssign => "LET",
        ElementKind.ClippedCoil => "CLIPPED",
        _ => "?",
    };
}
