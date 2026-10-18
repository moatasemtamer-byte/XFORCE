// Global cross-reference index — the deterministic "analysis" layer.
//
// After the parser produces PageLadders, this walks every rung and maps each
// PLC address to where it is *written* (output coil) and *read* (input
// contact).  From that it derives quality findings:
//   * dangling read   — read somewhere but never written
//   * orphaned write   — written but never read
//   * multiple writers — a COIL bit driven by two different rungs
//   * state variables  — LATCH / UNLATCH bits (machine memory)
//   * stuck latch      — LATCH with no UNLATCH/COIL reset path
//   * write-only unlatch — UNLATCH with no LATCH/COIL set path
//
// Everything here is derived purely from the parser output — no LLM, fully
// deterministic and unit-testable.

using System.Text;
using System.Text.RegularExpressions;

namespace CircuitReader.Core;

/// <summary>The PLC memory-area direction of an address.  Physical field I/O is
/// expected to be one-sided in the ladder (an input is only read, an output is
/// only driven), so it must not be reported as a dangling read / orphaned write.
/// Only classified for dialects with a known I/O addressing model (TI505);
/// everything else stays <see cref="Unknown"/> and is analyzed as before.</summary>
public enum AddressClass { Unknown, Input, Output, Internal }

public sealed record AddressUse(int Page, int Rung, string Kind, string Label = "");

public sealed class AddressXref
{
    public string Address { get; }
    public string Label { get; set; }
    public List<AddressUse> Writers { get; } = new();
    public List<AddressUse> Readers { get; } = new();

    /// <summary>PLC memory-area direction (field input / output / internal).</summary>
    public AddressClass Class { get; set; } = AddressClass.Unknown;

    public AddressXref(string address, string label = "")
    {
        Address = address;
        Label = label;
    }

    // A field input (X/WX) is only ever read in the ladder and a field output
    // (Y/WY) is only ever driven, so neither is an anomaly — exclude them.
    public bool IsDanglingRead => Readers.Count > 0 && Writers.Count == 0
                                  && Class != AddressClass.Input;
    public bool IsOrphanedWrite => Writers.Count > 0 && Readers.Count == 0
                                   && Class != AddressClass.Output;
    public bool IsStateVariable => Writers.Any(w => w.Kind is "LATCH" or "UNLATCH");

    public bool HasMultipleWriters
    {
        get
        {
            if (Writers.Count(w => w.Kind == "COIL") > 1) return true;
            int latches = Writers.Count(w => w.Kind == "LATCH");
            int unlatches = Writers.Count(w => w.Kind == "UNLATCH");
            return latches > 1 || unlatches > 1;
        }
    }
}

public sealed class XrefIndex
{
    public Dictionary<string, AddressXref> ByAddress { get; } = new();

    /// <summary>Dialect id (e.g. "ti505") driving address-direction classification.</summary>
    public string Dialect { get; }

    public XrefIndex(string dialect = "") => Dialect = dialect;

    private static readonly Regex IdentRe =
        new(@"[A-Za-z_$@%][\w$@%\-:.]*", RegexOptions.Compiled);
    private static readonly Regex PureNumberRe =
        new(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.Compiled);

    // TI505 / TI565 memory model: X = discrete input image, WX = analog/word
    // input, Y = discrete output image, WY = analog/word output.  These are
    // field I/O and are one-sided in the ladder by design.
    private static readonly Regex Ti505InputRe =
        new(@"^W?X\d+$", RegexOptions.Compiled);
    private static readonly Regex Ti505OutputRe =
        new(@"^W?Y\d+$", RegexOptions.Compiled);

    private AddressClass ClassOf(string address) => Dialect switch
    {
        "ti505" when Ti505InputRe.IsMatch(address) => AddressClass.Input,
        "ti505" when Ti505OutputRe.IsMatch(address) => AddressClass.Output,
        "ti505" => AddressClass.Internal,
        _ => AddressClass.Unknown,
    };

    private AddressXref Entry(string address, string label)
    {
        if (!ByAddress.TryGetValue(address, out var e))
        {
            e = new AddressXref(address, label) { Class = ClassOf(address) };
            ByAddress[address] = e;
        }
        if (label.Length > 0 && e.Label.Length == 0) e.Label = label;
        return e;
    }

    public void AddWriter(string address, int page, int rung, string kind, string label = "")
    {
        var e = Entry(address, label);
        e.Writers.Add(new AddressUse(page, rung, kind, label));
        if (label.Length > 0) e.Label = label;   // writer labels are canonical
    }

    public void AddReader(string address, int page, int rung, string kind, string label = "")
    {
        var e = Entry(address, label);
        e.Readers.Add(new AddressUse(page, rung, kind, label));
    }

    // --- queries ------------------------------------------------------------

    public IEnumerable<AddressXref> DanglingReads => ByAddress.Values.Where(e => e.IsDanglingRead);
    public IEnumerable<AddressXref> OrphanedWrites => ByAddress.Values.Where(e => e.IsOrphanedWrite);
    public IEnumerable<AddressXref> MultipleWriters => ByAddress.Values.Where(e => e.HasMultipleWriters);
    public IEnumerable<AddressXref> StateVariables => ByAddress.Values.Where(e => e.IsStateVariable);
    public IEnumerable<AddressXref> AllCoils => ByAddress.Values.Where(e => e.Writers.Count > 0);

    /// <summary>Physical field inputs (X/WX) — read in-ladder, driven from the field.</summary>
    public IEnumerable<AddressXref> FieldInputs => ByAddress.Values.Where(e => e.Class == AddressClass.Input);
    /// <summary>Physical field outputs (Y/WY) — driven in-ladder, wired to the field.</summary>
    public IEnumerable<AddressXref> FieldOutputs => ByAddress.Values.Where(e => e.Class == AddressClass.Output);

    public IEnumerable<AddressXref> StuckLatches => ByAddress.Values.Where(e =>
    {
        var kinds = e.Writers.Select(w => w.Kind).ToHashSet();
        return kinds.Contains("LATCH") && !kinds.Contains("UNLATCH") && !kinds.Contains("COIL");
    });

    public IEnumerable<AddressXref> WriteOnlyUnlatches => ByAddress.Values.Where(e =>
    {
        var kinds = e.Writers.Select(w => w.Kind).ToHashSet();
        return kinds.Contains("UNLATCH") && !kinds.Contains("LATCH") && !kinds.Contains("COIL");
    });

    // --- builder ------------------------------------------------------------

    public static XrefIndex Build(IReadOnlyList<PageLadder> pages, string dialect = "")
    {
        var idx = new XrefIndex(dialect);
        foreach (var page in pages)
        {
            foreach (var rung in page.Rungs)
            {
                if (rung.Output is { } o && !string.IsNullOrEmpty(o.Address))
                {
                    var label = page.AddressLabels.TryGetValue(o.Address, out var l) ? l : o.Label;
                    idx.AddWriter(o.Address, page.PageNumber, rung.Number, WriterKind(o.Kind), label ?? "");
                }
                foreach (var inp in rung.Inputs)
                {
                    foreach (var addr in ReaderAddresses(inp))
                    {
                        var label = page.AddressLabels.TryGetValue(addr, out var l) ? l : "";
                        idx.AddReader(addr, page.PageNumber, rung.Number, ReaderKind(inp.Kind), label);
                    }
                }
            }
        }
        return idx;
    }

    private static string WriterKind(ElementKind k) => k switch
    {
        ElementKind.Latch => "LATCH",
        ElementKind.Unlatch => "UNLATCH",
        _ => "COIL",
    };

    private static string ReaderKind(ElementKind k) => k switch
    {
        ElementKind.NormallyClosed => "NC",
        ElementKind.Compare => "COMPARE",
        _ => "NO",
    };

    /// <summary>The address(es) a contact reads.  Plain contacts read their own
    /// address; a compare contact (whose address is a compound like
    /// "osi5300&lt;=shutdn") reads each non-numeric operand.</summary>
    private static IEnumerable<string> ReaderAddresses(Element inp)
    {
        if (inp.Kind != ElementKind.Compare)
        {
            if (!string.IsNullOrEmpty(inp.Address)) yield return inp.Address;
            yield break;
        }
        foreach (Match m in IdentRe.Matches(inp.Address))
        {
            var tok = m.Value;
            if (!PureNumberRe.IsMatch(tok)) yield return tok;
        }
    }
}
