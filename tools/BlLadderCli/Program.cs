using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BL;
using BL.SafetyData;
using OfficeOpenXml;

namespace BlLadderCli
{
    /// <summary>
    /// Headless host for the UnityLadderConverter business layer (BL.dll).
    ///
    /// Usage:
    ///   BlLadderCli [--dpt &lt;file.DPT&gt;] --lpt &lt;file.LPT&gt; --out &lt;file.xml&gt;
    ///               [--template &lt;library.xml&gt;] [--template-selection D|B] [--report &lt;file.txt&gt;]
    ///   BlLadderCli [--dpt &lt;file.DPT&gt;] --xlsx &lt;file.xlsx&gt; --out &lt;file.xml&gt; [same options]
    ///
    /// The DPT is optional: it builds the tag dictionary and adds tag/hardware annotations,
    /// but the logic converts without it (operand types are inferred from the equations).
    /// Two program inputs are accepted, matching the two
    /// paths the GUI supports:
    ///   --lpt  a TRISTATION printout (ASCII ladder art) parsed by BL's LPT reader;
    ///   --xlsx a workbook with a "Function" sheet (col A = logic equation, col B = network #)
    ///          parsed by BL's Excel "Siemens" reader.
    /// Either way the result is a TriStation PLCopen XML identical to what the GUI writes
    /// (FB_&lt;name&gt;\&lt;name&gt;_FB.XML).
    /// </summary>
    internal static class Program
    {
        private const string Usage =
            "BlLadderCli [--dpt <file.DPT>] (--lpt <file.LPT> | --xlsx <file.xlsx>) --out <file.xml> " +
            "[--template <library.xml>] [--template-selection D|B] [--report <file.txt>]";

        [STAThread]
        private static int Main(string[] args)
        {
            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException exception)
            {
                return Fail(2, exception.Message + Environment.NewLine + Usage);
            }

            var log = new List<string>();
            Logger.LogEvent += (sender, message, kind) =>
            {
                foreach (var line in message ?? Array.Empty<string>())
                {
                    log.Add(kind + ": " + line);
                }
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(options.Dpt) && !File.Exists(options.Dpt)) return Fail(2, "DPT file not found: " + options.Dpt);
                if (!File.Exists(options.Input)) return Fail(2, "Program file not found: " + options.Input);
                if (string.IsNullOrWhiteSpace(options.Out)) return Fail(2, "Missing --out.");

                // BL unconditionally dumps a per-network logic listing to this hard-coded
                // path inside the conversion, so the folder has to exist first.
                TryCreateDirectory(@"C:\WORK");

                // Pick the template set (D = standard, B = alternate) before anything reads
                // the blank PT2 data, then refresh the geometry derived from it.
                var selection = options.TemplateSelection;
                XMLOperations_LadderLogic.TemplateSelction = selection;
                ProgramOperations_Safety.TemplateSelction = selection;
                XMLOperations_LadderLogic.UpdateSetting();
                ProgramOperations_Safety.UpdateSetting();

                // Pre-load the template library so BL never shows its "open template file"
                // dialog (which would hang a headless host). If none is given we fall back to
                // the blank PT2 shipped next to this executable.
                var templatePath = options.Template;
                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    var blankName = selection == "B" ? "PT2B.xml" : "PT2D.xml";
                    templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlankData", blankName);
                }

                if (!File.Exists(templatePath))
                {
                    return Fail(2, "Template library not found: " + templatePath);
                }

                var templateXml = FileWorks.LoadXMLFile(templatePath);
                if (templateXml == null)
                {
                    return Fail(2, "Template library is not a valid XML file: " + templatePath);
                }

                SetStatic(typeof(TXTProgramOperations_Safety), "TemplateXMLFile", templateXml);
                SetStatic(typeof(TXTProgramOperations_Safety), "TemplateFile",
                    XMLOperations_safety.DeserializeSafetyProgram(templateXml));
                log.Add("template: " + templatePath);

                // 1) DPT -> tag dictionary.  The DPT is the TriStation tag database. It is
                //    OPTIONAL: the Siemens/xlsx conversion path never consults it (its gettype
                //    infers types from the literal itself), and boolean ladder needs no types at
                //    all.  When it is supplied it is still loaded so tag annotations are emitted;
                //    when it is absent an empty dictionary is installed so BL's non-null guard
                //    passes and the logic still converts.
                if (!string.IsNullOrWhiteSpace(options.Dpt))
                {
                    new TXTManager(options.Dpt, FileWorks.LADDER + "DPT");
                    if (LadderTextOperations_safety.MyDB == null)
                    {
                        return Fail(1, "The DPT did not yield a tag dictionary.");
                    }
                    log.Add("DPT loaded, dictionary rows: " + LadderTextOperations_safety.MyDB.Rows.Count);
                }
                else
                {
                    LadderTextOperations_safety.MyDB = CreateEmptyDictionary();
                    log.Add("No DPT supplied: logic-only run (tag types inferred from the equations).");
                }

                // 2) Program -> XML. Call the converter directly instead of going through
                //    TXTManager so the Triconex-registry / PT2-import side effects are skipped.
                //    The LPT path feeds BL's ladder-art parser; the XLSX path feeds the Excel
                //    "Siemens" reader (a sheet whose name contains "Function" with the logic
                //    equation in column A and the network number in column B).
                string producedXml = string.Empty;
                var ladder = new LadderTextOperations_safety();
                List<program> programs = null;
                if (!string.IsNullOrWhiteSpace(options.Xlsx))
                {
                    var allRows = ReadSiemensRows(options.Xlsx, log);
                    if (allRows.Count == 0)
                    {
                        WriteReport(options.Report, log);
                        return Fail(1, "The workbook produced no usable logic rows (no \"Function\" sheet, or every row failed the logic check).");
                    }

                    // BL is a boolean ladder converter. Arithmetic (TV1 - TV2, k * 1.02, a / b)
                    // and structured text (IF ... THEN { ... }) can never be placed on a boolean
                    // rung, and BL's attempt to do so trips the empty null-guard it should be using
                    // to flag them (ProgramOperations_Safety.ReadblockinputLogic). Split those out
                    // up front so the surrounding boolean networks still convert; they are reported
                    // as manual work.
                    var database = new List<string>();
                    var skipped = new List<string>();
                    foreach (var row in allRows)
                    {
                        if (IsManualWork(row))
                        {
                            skipped.Add(row);
                            log.Add("Network skipped (needs manual work): " + NetworkLabel(row));
                        }
                        else
                        {
                            database.Add(row);
                        }
                    }
                    log.Add("boolean-convertible rows: " + database.Count + ", manual-work rows: " + skipped.Count);
                    if (database.Count == 0) return Fail(1, "Every network needs manual work; nothing could be converted.");

                    // Anything the classifier misses still surfaces as a crash, so the same
                    // drop-and-retry net stays in place as a backstop.
                    var maxAttempts = database.Count + 5;
                    for (var attempt = 0; ; attempt++)
                    {
                        var rows = database.Where(row => !skipped.Contains(row)).ToList();
                        if (rows.Count == 0) return Fail(1, "Every network failed to convert.");

                        var window = log.Count;
                        producedXml = string.Empty;
                        try
                        {
                            programs = ladder.DeserializeSafetyProgramSeimens(rows.ToArray(), ref producedXml, options.Xlsx);
                            break;
                        }
                        catch (Exception exception)
                        {
                            var offender = FindUnconvertibleNetwork(log, window, rows);
                            // Drop the aborted attempt's chatter so the report stays readable.
                            if (log.Count > window) log.RemoveRange(window, log.Count - window);

                            if (offender == null || attempt >= maxAttempts)
                            {
                                log.Add("FATAL: " + exception);
                                WriteReport(options.Report, log);
                                return Fail(1, exception.Message);
                            }

                            skipped.Add(offender);
                            log.Add("Network skipped (needs manual work): " + NetworkLabel(offender));
                        }
                    }

                    if (skipped.Count > 0)
                    {
                        log.Add("Networks skipped: " + skipped.Count + " (manual work required)");
                    }
                }
                else
                {
                    var lines = File.ReadAllLines(options.Lpt, Encoding.UTF8);
                    programs = ladder.DeserializeSafetyProgram(lines, ref producedXml, options.Lpt);
                }
                log.Add("programs converted: " + (programs?.Count ?? 0));

                if (string.IsNullOrWhiteSpace(producedXml))
                {
                    producedXml = ExpectedOutputPath(options.Input);
                    log.Add("converter did not report an output path; using " + producedXml);
                }

                if (!File.Exists(producedXml))
                {
                    return Fail(1, "Conversion produced no XML at " + producedXml);
                }

                var outDir = Path.GetDirectoryName(Path.GetFullPath(options.Out));
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                File.Copy(producedXml, options.Out, true);

                var reportText = string.Join(Environment.NewLine, log);
                if (!string.IsNullOrWhiteSpace(options.Report))
                {
                    File.WriteAllText(options.Report, reportText);
                }

                // Echo the classification to stdout too, so callers that only capture console
                // output (e.g. the engine) still see what converted and what needs manual work.
                foreach (var line in log)
                {
                    if (line.StartsWith("boolean-convertible rows", StringComparison.Ordinal) ||
                        line.StartsWith("Network skipped", StringComparison.Ordinal) ||
                        line.StartsWith("Networks skipped", StringComparison.Ordinal) ||
                        line.StartsWith("programs converted", StringComparison.Ordinal))
                    {
                        Console.WriteLine(line);
                    }
                }

                Console.WriteLine("OK: " + options.Out);
                return 0;
            }
            catch (Exception exception)
            {
                log.Add("FATAL: " + exception);
                var reportText = string.Join(Environment.NewLine, log);
                if (!string.IsNullOrWhiteSpace(options.Report))
                {
                    try { File.WriteAllText(options.Report, reportText); } catch { /* best effort */ }
                }
                return Fail(1, exception.Message);
            }
        }

        private static string ExpectedOutputPath(string lptPath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(lptPath)) ?? ".";
            var name = Path.GetFileNameWithoutExtension(lptPath);
            return Path.Combine(directory, "FB_" + name, name + "_FB.XML");
        }

        // Mirrors TXTManager.ReadSiemensParser: every sheet whose name contains "Function",
        // data starting at row 2, one "colA@colB@colC@colD" record per valid row.  colA is the
        // full logic equation, colB the network number, colC the page and colD the contact list.
        private static List<string> ReadSiemensRows(string path, List<string> log)
        {
            var root = new List<string>();
            using (var excel = FileWorks.OpenExcelPackage(path))
            {
                if (excel == null)
                {
                    log.Add("excel: could not open " + path);
                    return root;
                }

                log.Add("excel sheets: " + string.Join(", ", excel.Workbook.Worksheets.Select(sh => sh.Name)));
                foreach (var sheet in excel.Workbook.Worksheets.Where(sh => sh.Name.Contains("Function")))
                {
                    var endRow = sheet.Dimension?.End.Row ?? 0;
                    var added = 0;
                    for (var row = 2; row <= endRow; row++)
                    {
                        if (IsValidLogic(sheet.Cells[row, 1].Text, sheet.Cells[row, 3].Text))
                        {
                            var equation = sheet.Cells[row, 1].Text;
                            var inputs = sheet.Cells[row, 4].Text;
                            if (string.IsNullOrWhiteSpace(inputs)) inputs = DeriveInputs(equation);
                            var logic = equation + "@" + sheet.Cells[row, 2].Text + "@" +
                                        sheet.Cells[row, 3].Text + "@" + inputs;
                            logic = logic.Trim().Replace(" (", "(").Replace(" =", "=").Replace("= ", "=");
                            root.Add(logic);
                            added++;
                        }
                    }
                    log.Add("excel sheet '" + sheet.Name + "': " + added + " logic rows");
                }
            }
            return root;
        }

        // Copied verbatim from TXTManager.IsValidLogic so the same rows qualify here.
        private static bool IsValidLogic(string text, string network)
        {
            text = text.Trim();
            if (!text.Contains("=") || text.StartsWith("=") || text.Contains(":")) return false;

            // BL's original test: exactly one "=", and either a parenthesised expression or no
            // "(" at all.  This keeps structured-text rows (IF ... THEN { a=b }) in the set so
            // they are reported as manual work.
            var eqCount = text.Split('=').Length - 1;
            var parens = text.Contains("(") && text.Contains(")");
            if (eqCount == 1 && (parens || !text.Contains("("))) return true;

            // Extended: equality (===/.../), <= / >= comparison contacts and multi-statement
            // analog rows carry extra "=" characters, so BL's test would silently drop the whole
            // network.  Accept any line whose left of the first "=" is a bare identifier so those
            // networks convert (or, when analog, surface as manual work) instead of vanishing.
            var lhs = text.Substring(0, text.IndexOf('=')).Trim();
            return lhs.Length > 0 && Regex.IsMatch(lhs, @"^[A-Za-z0-9_$.]+$");
        }

        // BL needs the contact list (column D) to match the operand tokens in the equation; the
        // pristine "Functions" export only carries columns A/B, so rebuild it: every leaf operand
        // of the right-hand side, "!" kept and "/"-separated.  A token that is immediately
        // followed by "(" is a block/operator name (AND, OR, SR_x, ...), not an operand, and is
        // dropped; numeric literals are dropped too.
        // A boolean ladder rung is built from contacts (! and the Triconex value/compare contacts
        // v/../ ^/../ </../ >/../), AND/OR/NOT and block calls. It never contains arithmetic
        // (+ - * or a spaced division), type annotations (:) or structured-text braces. Any row
        // that does cannot be represented as ladder and is reported for manual work instead of
        // being fed to BL, which would crash on it. Note the "/" is only arithmetic when spaced;
        // otherwise it delimits a Triconex contact and must be kept.
        private static bool IsManualWork(string row)
        {
            var logic = row.Split('@')[0];
            // Remove Triconex contacts first so their "/" and any negative literal (e.g.
            // </r6MAR,-1/) are not mistaken for arithmetic.
            logic = Regex.Replace(logic, "[<>v^]/[^/]*/", string.Empty);
            if (logic.IndexOf('+') >= 0 || logic.IndexOf('-') >= 0 || logic.IndexOf('*') >= 0 ||
                logic.IndexOf('{') >= 0 || logic.IndexOf('}') >= 0 || logic.IndexOf(':') >= 0)
            {
                return true;
            }
            return logic.Contains(" /") || logic.Contains("/ ");
        }

        private static string DeriveInputs(string equation)
        {
            var equals = equation.IndexOf('=');
            var rhs = equals >= 0 ? equation.Substring(equals + 1) : equation;
            rhs = System.Text.RegularExpressions.Regex.Replace(rhs, @"!\s*", "!");

            var leaves = new List<string>();
            var i = 0;
            while (i < rhs.Length)
            {
                var c = rhs[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '!' || c == '.')
                {
                    var start = i;
                    while (i < rhs.Length && (char.IsLetterOrDigit(rhs[i]) || rhs[i] == '_' || rhs[i] == '!' || rhs[i] == '.')) i++;
                    var token = rhs.Substring(start, i - start);
                    var j = i;
                    while (j < rhs.Length && rhs[j] == ' ') j++;
                    var followedByParen = j < rhs.Length && rhs[j] == '(';
                    if (!followedByParen && !IsKeyword(token) && !IsNumeric(token)) leaves.Add(token);
                }
                else
                {
                    i++;
                }
            }
            return string.Join("/", leaves);
        }

        private static bool IsKeyword(string token)
        {
            switch (token.ToUpperInvariant())
            {
                case "TRUE":
                case "FALSE":
                case "AND":
                case "OR":
                case "NOT":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsNumeric(string token)
        {
            double value;
            return double.TryParse(token, out value);
        }

        // Same columns TXTManager.DoPT2ExcelFormat builds, so anything that reads MyDB by name
        // (e.g. the LPT path's Gettype) still finds a valid, empty table instead of null.
        private static System.Data.DataTable CreateEmptyDictionary()
        {
            var table = new System.Data.DataTable();
            foreach (var column in new[]
                     {
                         "TagName", "Description", "Group1", "Group2", "ALIASTYPE", "ALIASNUM", "DATATYPE",
                         "POINTTYPE", "INITVALUE", "RETENTIVE", "CHASSIS", "SLOT", "POINT", "MINSPAN",
                         "MAXSPAN", "DECPL", "APPLICATION",
                     })
            {
                table.Columns.Add(column);
            }
            return table;
        }

        private static void TryCreateDirectory(string path)
        {
            try { Directory.CreateDirectory(path); } catch { /* best effort */ }
        }

        // BL exposes TemplateFile as a public static field and TemplateXMLFile as a property
        // with an internal setter, so the second one has to go through reflection.
        private static void SetStatic(Type type, string member, object value)
        {
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                property.GetSetMethod(true)?.Invoke(null, new[] { value });
                return;
            }

            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
            {
                field.SetValue(null, value);
                return;
            }

            throw new InvalidOperationException("Cannot set " + type.Name + "." + member);
        }

        private static int Fail(int code, string message)
        {
            Console.Error.WriteLine(message);
            return code;
        }

        private static void WriteReport(string path, List<string> log)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { File.WriteAllText(path, string.Join(Environment.NewLine, log)); } catch { /* best effort */ }
        }

        // BL logs "Network # <n> :Begin to import <coil> into PT2" immediately before it builds
        // each coil, so when a run crashes the last such line names the row that tripped it.
        private static string FindUnconvertibleNetwork(List<string> log, int fromIndex, List<string> rows)
        {
            string coilName = null;
            for (var i = fromIndex; i < log.Count; i++)
            {
                const string marker = ":Begin to import ";
                var at = log[i].IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) continue;
                var rest = log[i].Substring(at + marker.Length);
                var end = rest.IndexOf(" into PT2", StringComparison.Ordinal);
                if (end < 0) continue;
                coilName = rest.Substring(0, end);
            }

            if (coilName == null) return null;
            foreach (var row in rows)
            {
                if (string.Equals(CoilName(row), coilName, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        private static string NetworkLabel(string row)
        {
            var parts = row.Split('@');
            var network = parts.Length > 1 ? parts[1].Trim() : "?";
            return "Network #" + network + " (coil " + CoilName(row) + ")";
        }

        private static string CoilName(string row)
        {
            var logic = row.Split('@')[0];
            var equals = logic.IndexOf('=');
            var name = equals >= 0 ? logic.Substring(0, equals) : logic;
            // BL strips spaces from the equation before taking the coil name, so match that.
            return new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        private sealed class Options
        {
            public string Dpt { get; private set; }
            public string Lpt { get; private set; }
            public string Xlsx { get; private set; }
            public string Out { get; private set; }
            public string Template { get; private set; }
            public string Report { get; private set; }
            public string TemplateSelection { get; private set; } = "D";

            /// <summary>The program input actually chosen: the LPT when given, else the XLSX.</summary>
            public string Input => string.IsNullOrWhiteSpace(Lpt) ? Xlsx : Lpt;

            public static Options Parse(string[] args)
            {
                var result = new Options();
                for (var i = 0; i < args.Length; i++)
                {
                    var key = args[i];
                    string Next()
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException("Missing value for " + key);
                        return args[++i];
                    }

                    switch (key)
                    {
                        case "--dpt": result.Dpt = Next(); break;
                        case "--lpt": result.Lpt = Next(); break;
                        case "--xlsx": result.Xlsx = Next(); break;
                        case "--out": result.Out = Next(); break;
                        case "--template": result.Template = Next(); break;
                        case "--report": result.Report = Next(); break;
                        case "--template-selection": result.TemplateSelection = Next().ToUpperInvariant(); break;
                        case "-h":
                        case "--help": throw new ArgumentException(Usage);
                        default: throw new ArgumentException("Unknown argument: " + key);
                    }
                }

                if (result.TemplateSelection != "B") result.TemplateSelection = "D";
                if (string.IsNullOrWhiteSpace(result.Lpt) && string.IsNullOrWhiteSpace(result.Xlsx))
                {
                    throw new ArgumentException("Either --lpt or --xlsx is required.");
                }
                if (!string.IsNullOrWhiteSpace(result.Lpt) && !string.IsNullOrWhiteSpace(result.Xlsx))
                {
                    throw new ArgumentException("Give only one of --lpt or --xlsx.");
                }

                return result;
            }
        }
    }
}
