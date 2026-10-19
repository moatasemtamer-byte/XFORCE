using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Pt2Conv.Gui;

public sealed class MainForm : Form
{
    private enum PathMode { OpenFile, SaveFile, Folder }

    private const string Pt2Filter = "TriStation container (*.pt2;*.pt2x)|*.pt2;*.pt2x|All files (*.*)|*.*";
    private const string Pt2xFilter = "TriStation container (*.pt2x)|*.pt2x|All files (*.*)|*.*";
    private const string XmlFilter = "PLCopen XML (*.xml)|*.xml|All files (*.*)|*.*";

    private readonly TextBox _log = new();
    private readonly List<Control> _runButtons = new();
    private readonly ToolStripStatusLabel _status = new() { Text = "Ready" };
    private readonly StringBuilder _capture = new();
    private bool _busy;

    public MainForm()
    {
        Text = "pt2conv - TriStation XML <-> .pt2x";
        Width = 980;
        Height = 760;
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 460,
            Panel1MinSize = 220,
            Panel2MinSize = 140,
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabFwd = new TabPage { Text = "XML -> .pt2x  (convert)", Padding = new Padding(0) };
        tabFwd.Controls.Add(BuildConvertPanel());
        var tabRev = new TabPage { Text = ".pt2x -> XML  (decompile)", Padding = new Padding(0) };
        tabRev.Controls.Add(BuildDecompilePanel());
        tabs.TabPages.Add(tabFwd);
        tabs.TabPages.Add(tabRev);

        split.Panel1.Controls.Add(tabs);
        split.Panel2.Controls.Add(BuildOutputPanel());

        var strip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false };
        strip.Items.Add(_status);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(split, 0, 0);
        root.Controls.Add(strip, 0, 1);
        Controls.Add(root);

        AppendLine("pt2conv - TriStation XML <-> .pt2x converter ready.");
    }

    // ---------------------------------------------------------------- layout helpers

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 8, 3, 3),
    };

    private static Label NewDescription(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(820, 0),
        Anchor = AnchorStyles.Left,
        ForeColor = Color.FromArgb(70, 70, 70),
        Margin = new Padding(3, 3, 3, 12),
    };

    private static void AddDescription(TableLayoutPanel tlp, ref int row, string text)
    {
        var lbl = NewDescription(text);
        tlp.Controls.Add(lbl, 0, row);
        tlp.SetColumnSpan(lbl, 3);
        row++;
    }

    private TextBox AddPathRow(TableLayoutPanel tlp, ref int row, string label, string filter, PathMode mode)
    {
        var tb = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) };
        var btn = new Button
        {
            Text = mode == PathMode.Folder ? "Folder..." : "Browse...",
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 3, 3, 1),
        };
        btn.Click += (_, _) =>
        {
            if (mode == PathMode.Folder)
            {
                using var d = new FolderBrowserDialog();
                if (!string.IsNullOrWhiteSpace(tb.Text) && Directory.Exists(tb.Text)) d.SelectedPath = tb.Text;
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
            }
            else if (mode == PathMode.SaveFile)
            {
                using var d = new SaveFileDialog { Filter = filter, OverwritePrompt = true };
                if (!string.IsNullOrWhiteSpace(tb.Text)) d.FileName = tb.Text;
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
            }
            else
            {
                using var d = new OpenFileDialog { Filter = filter, CheckFileExists = true };
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
            }
        };
        tlp.Controls.Add(NewLabel(label), 0, row);
        tlp.Controls.Add(tb, 1, row);
        tlp.Controls.Add(btn, 2, row);
        row++;
        return tb;
    }

    private static TextBox AddTextRow(TableLayoutPanel tlp, ref int row, string label, string defaultValue = "")
    {
        var tb = new TextBox { Dock = DockStyle.Fill, Text = defaultValue, Margin = new Padding(3, 5, 3, 3) };
        tlp.Controls.Add(NewLabel(label), 0, row);
        tlp.Controls.Add(tb, 1, row);
        tlp.SetColumnSpan(tb, 2);
        row++;
        return tb;
    }

    private void AddRunRow(TableLayoutPanel tlp, ref int row, string text, Func<string[]?> factory, bool prominent = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = prominent ? new Size(170, 36) : Size.Empty,
            Padding = new Padding(18, 0, 18, 0),
            Font = prominent ? new Font("Segoe UI", 10f, FontStyle.Bold) : Font,
            BackColor = prominent ? Color.FromArgb(0, 120, 215) : SystemColors.Control,
            ForeColor = prominent ? Color.White : SystemColors.ControlText,
            FlatStyle = prominent ? FlatStyle.Flat : FlatStyle.Standard,
            UseVisualStyleBackColor = !prominent,
            Margin = new Padding(3, 14, 3, 3),
        };
        if (prominent)
        {
            b.FlatAppearance.BorderSize = 0;
            b.MouseEnter += (_, _) => b.BackColor = Color.FromArgb(0, 98, 175);
            b.MouseLeave += (_, _) => b.BackColor = Color.FromArgb(0, 120, 215);
        }
        b.Click += (_, _) =>
        {
            string[]? a;
            try { a = factory(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Cannot convert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (a is null) return;
            RunCommand(b.Text, a);
        };
        _runButtons.Add(b);
        if (prominent)
        {
            var host = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
            };
            host.Controls.Add(b);
            tlp.Controls.Add(host, 0, row);
            tlp.SetColumnSpan(host, 3);
        }
        else
        {
            tlp.Controls.Add(b, 0, row);
            tlp.SetColumnSpan(b, 3);
        }
        row++;
    }

    private static string Req(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Please provide: " + field);
        return value.Trim();
    }

    /// <summary>Best-effort default output path: the source with its extension swapped for .xml.
    /// Returns "" when the text cannot yet be resolved to a path (e.g. still being typed).</summary>
    private static string DefaultXmlPath(string source)
    {
        try { return Path.ChangeExtension(Path.GetFullPath(source.Trim()), ".xml"); }
        catch { return ""; }
    }

    // ---------------------------------------------------------------- Convert (primary)

    private Control BuildConvertPanel()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(16),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        page.Controls.Add(tlp);

        int row = 0;
        var title = new Label
        {
            Text = "Convert XML -> .pt2x",
            AutoSize = true,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 6),
        };
        tlp.Controls.Add(title, 0, row);
        tlp.SetColumnSpan(title, 3);
        row++;

        AddDescription(tlp, ref row,
            "PRIMARY TASK - build a TriStation .pt2x from a PLCopen XML export. " +
            "The XML holds the POU source and the tag definitions. With no template selected this uses the built-in generic " +
            "base project, so it works for any XML. Optionally select a template .pt2x to preserve that project's exact " +
            "libraries and hardware configuration.");

        var xml = AddPathRow(tlp, ref row, "Source XML (input)", XmlFilter, PathMode.OpenFile);
        var outFolder = AddPathRow(tlp, ref row, "Output folder (created)", "", PathMode.Folder);
        var fileName = AddTextRow(tlp, ref row, "File name (opt)", "");

        var hint = NewDescription("File name: leave blank to use the XML's name with a .pt2x extension.");
        tlp.Controls.Add(hint, 0, row);
        tlp.SetColumnSpan(hint, 3);
        row++;


        var refDir = AddPathRow(tlp, ref row, "Template (optional)",
            "TriStation container (*.pt2x;*.pt2)|*.pt2x;*.pt2|All files (*.*)|*.*", PathMode.OpenFile);
        refDir.Text = "";
        var tmplHint = NewDescription("Leave BLANK to use the built-in generic base project (recommended - works for any XML, " +
            "no .pt2x required). Optionally supply the project's original .pt2x (auto-extracted) or an extracted template folder " +
            "to preserve that project's exact libraries/configuration.");
        tlp.Controls.Add(tmplHint, 0, row);
        tlp.SetColumnSpan(tmplHint, 3);
        row++;
        var filter = AddTextRow(tlp, ref row, "POU filter (opt)", "");

        var regenTags = new CheckBox
        {
            Text = "Regenerate tag database (Tagnames.csv) from the XML",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(3, 8, 3, 3),
        };
        tlp.Controls.Add(regenTags, 0, row);
        tlp.SetColumnSpan(regenTags, 3);
        row++;

        AddRunRow(tlp, ref row, "Convert to .pt2x", () =>
        {
            string xmlPath = Req(xml.Text, "source XML");
            string outDir = Req(outFolder.Text, "output folder");
            ValidatePlcopenXml(xmlPath);
            string name = fileName.Text.Trim();
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(xmlPath) + ".pt2x";
            if (!name.EndsWith(".pt2x", StringComparison.OrdinalIgnoreCase)) name += ".pt2x";
            string outPath = Path.Combine(outDir, name);

            var args = new List<string> { "buildxml", xmlPath, outPath };
            string rd = ResolveTemplate(refDir.Text.Trim());
            if (rd.Length == 0)
                AppendLine("No template given - using the built-in generic base project.");
            string fl = filter.Text.Trim();
            if (rd.Length > 0) args.Add(rd);
            if (fl.Length > 0) { if (args.Count == 3) args.Add(""); args.Add(fl); }
            if (regenTags.Checked || rd.Length == 0) { if (args.Count == 3) args.Add(""); args.Add("--tags"); }
            return args.ToArray();
        }, prominent: true);
        return page;
    }

    // ---------------------------------------------------------------- Decompile (reverse)

    private Control BuildDecompilePanel()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(16),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        page.Controls.Add(tlp);

        int row = 0;
        var title = new Label
        {
            Text = "Decompile .pt2x -> XML",
            AutoSize = true,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 6),
        };
        tlp.Controls.Add(title, 0, row);
        tlp.SetColumnSpan(title, 3);
        row++;

        AddDescription(tlp, ref row,
            "Read a TriStation .pt2/.pt2x container and write it back out as PLCopen XML. " +
            "POU logic comes back as <FBD> graphs (blocks, wires, comments) or <ST> source, and the full tag " +
            "table is emitted with types, addresses and descriptions. " +
            "This runs entirely inside the tool - no TriStation installation and no licence are needed. " +
            "Pick your .pt2x and press Decompile: the XML is written next to it, named after it. " +
            "Change 'Save XML as' only if you want it somewhere else.");

        var pt2 = AddPathRow(tlp, ref row, "Source .pt2x (input)", Pt2xFilter, PathMode.OpenFile);
        var xmlOut = AddPathRow(tlp, ref row, "Save XML as (output)", XmlFilter, PathMode.SaveFile);

        // The output defaults to <source folder>\<source name>.xml as soon as a source is
        // chosen, so nobody has to hunt for a save location. Browsing or typing in the
        // output box takes over and stops the default from overwriting it.
        bool syncing = false;
        bool outTouched = false;
        xmlOut.TextChanged += (_, _) => { if (!syncing) outTouched = true; };
        pt2.TextChanged += (_, _) =>
        {
            if (outTouched || string.IsNullOrWhiteSpace(pt2.Text)) return;
            string guess = DefaultXmlPath(pt2.Text);
            if (guess.Length == 0) return;
            try { syncing = true; xmlOut.Text = guess; }
            finally { syncing = false; }
        };

        AddRunRow(tlp, ref row, "Decompile .pt2x to XML", () =>
        {
            string src = Req(pt2.Text, "source .pt2x");
            string dst = Req(xmlOut.Text, "output XML path");
            if (!File.Exists(src))
                throw new InvalidOperationException("File not found:\n" + src);
            if (!dst.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) dst += ".xml";
            return new[] { "frompt2", src, dst };
        }, prominent: true);
        return page;
    }

    /// <summary>
    /// Accepts either an already-extracted template folder, or the project's original
    /// .pt2x (which is extracted once into a temp cache and reused). Returns "" when blank.
    /// </summary>
    private string ResolveTemplate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            // Try to find automatically from the currently selected XML? Not directly here.
            // The caller supplies rd after picking XML? Let the caller handle auto-find.
            return "";
        }
        if (Directory.Exists(path)) return path;
        if (!File.Exists(path))
            throw new InvalidOperationException("Template not found:\n" + path);
        if (!path.EndsWith(".pt2x", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".pt2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Template must be a .pt2x/.pt2 file or an extracted folder:\n" + path);

        string root = Path.Combine(Path.GetTempPath(), "pt2conv-gui", "templates");
        Directory.CreateDirectory(root);
        string key = Path.GetFileNameWithoutExtension(path) + "_" + ShortHash(Path.GetFullPath(path));
        string dest = Path.Combine(root, key);
        if (!Directory.Exists(Path.Combine(dest, "Current")))
        {
            SetStatus("Extracting template " + Path.GetFileName(path) + " ...", Color.DarkOrange);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            int rc = global::Pt2Conv.Program.Main(new[] { "extract", path, dest });
            if (rc != 0 || !Directory.Exists(Path.Combine(dest, "Current")))
                throw new InvalidOperationException("Failed to extract template .pt2x:\n" + path);
        }
        AppendLine("Template: extracted " + path + " -> " + dest);
        return dest;
    }

    private static string ShortHash(string s)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        var h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(h).Substring(0, 8).ToLowerInvariant();
    }
    private static void ValidatePlcopenXml(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length == 0)
            throw new InvalidOperationException("The PLCopen XML file is missing or empty:\n" + path);

        using var fs = File.OpenRead(path);

        int head = (int)Math.Min(4096, fi.Length);
        var hb = new byte[head];
        int hr = 0;
        while (hr < head) { int n = fs.Read(hb, hr, head - hr); if (n <= 0) break; hr += n; }
        string headText = Encoding.UTF8.GetString(hb, 0, hr);

        if (headText.Contains("SW.Blocks", StringComparison.OrdinalIgnoreCase) ||
            headText.Contains("SW.Tags", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(headText, @"<\s*Document\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException(
                "This looks like a Siemens TIA Portal Openness export (SW.Blocks / SW.Tags / <Document>).\n\n" +
                "This tool converts TriStation PLCopen XML (root element <project>, tag blocks <ts:TAGNAME>) only.");

        // NOTE: no requirement that the document ends with </project>. A truncated
        // or fragment export is accepted here and handed to the converter as-is.
    }

    // ---------------------------------------------------------------- output panel

    private Control BuildOutputPanel()
    {
        var grp = new GroupBox { Text = "Output", Dock = DockStyle.Fill, Padding = new Padding(8) };

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var clear = new Button { Text = "Clear", Width = 80 };
        clear.Click += (_, _) => _log.Clear();
        var copy = new Button { Text = "Copy", Width = 80 };
        copy.Click += (_, _) => { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); };
        var save = new Button { Text = "Save log...", Width = 90 };
        save.Click += (_, _) => SaveLog();
        top.Controls.Add(clear);
        top.Controls.Add(copy);
        top.Controls.Add(save);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Both;
        _log.WordWrap = false;
        _log.BackColor = Color.FromArgb(30, 30, 30);
        _log.ForeColor = Color.Gainsboro;
        _log.Font = new Font("Consolas", 9f);
        _log.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(_log, 0, 1);

        grp.Controls.Add(layout);
        return grp;
    }

    private void SaveLog()
    {
        if (_log.TextLength == 0) return;
        using var d = new SaveFileDialog { Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*" };
        if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllText(d.FileName, _log.Text);
    }

    // ---------------------------------------------------------------- execution

    private async void RunCommand(string title, string[] args)
    {
        if (_busy) return;
        _busy = true;
        SetBusy(true);
        _capture.Clear();
        SetStatus("Running: " + title + " ...", Color.DarkOrange);

        AppendLine("");
        AppendLine("==== " + title + " ====");
        AppendLine("> pt2conv " + string.Join(" ", args.Select(Quote)));

        var writer = new ActionTextWriter(Append);
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        Console.SetOut(writer);
        Console.SetError(writer);

        int code;
        try
        {
            code = await Task.Run(() => global::Pt2Conv.Program.Main(args));
        }
        catch (Exception ex)
        {
            AppendLine("EXCEPTION: " + ex);
            code = -1;
        }
        finally
        {
            writer.Flush();
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }

        AppendLine($"[exit code {code}]");
        _busy = false;
        SetBusy(false);

        if (code == 0)
        {
            SetStatus("Done - success (exit 0)", Color.SeaGreen);
        }
        else
        {
            string reason = ExtractError() ?? $"command failed (exit {code})";
            SetStatus("Failed - " + reason, Color.Firebrick);
            MessageBox.Show(this, "The operation failed.\n\n" + reason +
                "\n\nSee the Output panel below for full details.", "Conversion failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string? ExtractError()
    {
        string[] lines = _capture.ToString().Replace("\r\n", "\n").Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("EXCEPTION:", StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private void SetBusy(bool busy)
    {
        foreach (var b in _runButtons) b.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void Append(string s)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action<string>(Append), s); } catch { }
            return;
        }
        _capture.Append(s);
        _log.AppendText(s);
    }

    private void AppendLine(string s) => Append(s + Environment.NewLine);

    private static string Quote(string s) => s.Any(char.IsWhiteSpace) ? "\"" + s + "\"" : s;

    private static string[] ReadPouNames(string xmlPath)
    {
        var doc = XmlRepair.Load(xmlPath);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "pou")
            .Select(e => (string?)e.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToArray();
    }
}

internal sealed class ActionTextWriter : TextWriter
{
    private readonly Action<string> _write;
    public ActionTextWriter(Action<string> write) => _write = write;
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value) => _write(value.ToString());
    public override void Write(string? value) { if (value is not null) _write(value); }
    public override void Write(char[] buffer, int index, int count) => _write(new string(buffer, index, count));
}
