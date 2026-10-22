namespace Pt2Conv;

/// <summary>
/// The data a TriStation report template binds to: a single header row (Table2) shared by
/// every report, plus one row set (Table1) per logical report. Field names mirror the
/// <c>Fields.[X]</c> keys the templates reference, so the renderer can evaluate an
/// expression by a plain dictionary lookup.
/// </summary>
public sealed class ReportData
{
    /// <summary>Header fields (Table2): PRJNAME, PLATFORM, TS_VER, SAVEDDATE, DWLDDATE,
    /// DWNLD_VER, CURNT_VER, NODE_NAME, NODE_NUM, DESCRIP, USER_NAME, OS_VER.</summary>
    public Dictionary<string, string> Header = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Detail rows (Table1) for the POU Variable report.</summary>
    public List<Dictionary<string, string>> PouReport = new();
    /// <summary>Rows for the Instance Variable Connections report.</summary>
    public List<Dictionary<string, string>> InstVarReport = new();
    /// <summary>Rows for the Program Variable Listing report.</summary>
    public List<Dictionary<string, string>> ProgramVariableListing = new();
    /// <summary>Rows for the Tagnames Complete Listing report.</summary>
    public List<Dictionary<string, string>> TagnameCompleteListing = new();
    /// <summary>Rows for the POU Cross-Reference report.</summary>
    public List<Dictionary<string, string>> PouXRefReport = new();
    /// <summary>Rows for the Shared Libraries report.</summary>
    public List<Dictionary<string, string>> SharedLibReport = new();
    /// <summary>Rows for the PIGeneralParameters sub-report (ITEM / VALUE).</summary>
    public List<Dictionary<string, string>> GeneralParameters = new();
    /// <summary>Rows for the ProgramExecutionList sub-report (SEQUENCE / PROGRAM / DESCRIPTION).</summary>
    public List<Dictionary<string, string>> ProgramExecutionList = new();
    /// <summary>Rows for the SequenceOfEvents sub-report (SOEBLKNUM / SOEBLKTTL / SOEBLKTYP / SOEBUFSIZ).</summary>
    public List<Dictionary<string, string>> SequenceOfEvents = new();
    /// <summary>Rows for the SOE cross-tab sub-report: per tag, the SOE block number(s).</summary>
    public List<(string Tagname, string BlockNum)> SoeAssignments = new();

    /// <summary>Name of the report to render (matches a template's Report Name), for the
    /// sub-report "General Parameters" title and friends.</summary>
    public string ProjectName = "";
}
