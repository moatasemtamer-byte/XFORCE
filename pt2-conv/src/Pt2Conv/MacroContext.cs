namespace Pt2Conv;

/// <summary>
/// The macro values a TriStation title block (or an FBD annotation) can expand.
/// The placeholders are the ones the sheet templates use: %PROJECT_NAME, %ELEMENT_NAME,
/// %DRAWING_TITLE, %SHEET_NUMBER, %COMPANY_INFO1..3, %DATE_CREATED, ...
/// </summary>
public sealed class MacroContext
{
    public string ProjectName = "";
    public string ProjectShort = "";
    public string ProjectVersion = "";
    public string Company1 = "", Company2 = "", Company3 = "";
    public string User = "";
    public string ElementName = "";
    public string DrawingTitle = "", DrawingNumber = "", DrawingSize = "", Revision = "";
    public string SheetTemplate = "", SheetTitle = "";
    public string CreatedDate = "", CreatedTime = "";
    public string ModifiedDate = "", ModifiedTime = "";
    public string PrintedDate = "", PrintedTime = "";
    public string ApprovedName = "", ApprovedTime = "";
    public string YearModified = "";
    public int SheetNumber, SheetCount;

    /// <summary>The placeholder -> value map, in replacement order.</summary>
    public Dictionary<string, string> Values()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["%PROJECT_NAME"] = ProjectName,
            ["%ELEMENT_NAME"] = ElementName,
            ["%DRAWING_TITLE"] = DrawingTitle,
            ["%DRAWING_NUMBER"] = DrawingNumber,
            ["%DRAWING_SIZE"] = DrawingSize,
            ["%REVISION"] = Revision,
            ["%SHEET_TEMPLATE"] = SheetTemplate,
            ["%SHEET_TITLE"] = SheetTitle,
            ["%SHEET_NUMBER"] = SheetNumber.ToString(),
            ["%SHEET_COUNT"] = SheetCount.ToString(),
            ["%COMPANY_INFO1"] = Company1,
            ["%COMPANY_INFO2"] = Company2,
            ["%COMPANY_INFO3"] = Company3,
            ["%CREATED_BY"] = User,
            ["%MODIFIED_BY"] = User,
            ["%PRINTED_BY"] = User,
            ["%DATE_CREATED"] = CreatedDate,
            ["%TIME_CREATED"] = CreatedTime,
            ["%DATE_MODIFIED"] = ModifiedDate,
            ["%TIME_MODIFIED"] = ModifiedTime,
            ["%DATE_PRINTED"] = PrintedDate,
            ["%TIME_PRINTED"] = PrintedTime,
            ["%APPROVED_NAME"] = ApprovedName,
            ["%APPROVED_TIME"] = ApprovedTime,
            ["%YEAR_MODIFIED"] = YearModified,
        };
    }

    /// <summary>Expands the macros in <paramref name="text"/>; unknown placeholders are dropped.</summary>
    public string Expand(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('%') < 0) return text;
        foreach (var (k, v) in Values()) text = text.Replace(k, v);
        text = System.Text.RegularExpressions.Regex.Replace(text, "%[A-Z0-9_]+", "");
        return text;
    }
}
