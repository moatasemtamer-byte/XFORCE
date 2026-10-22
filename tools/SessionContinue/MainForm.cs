namespace SessionContinue;

/// <summary>
/// Single-window picker: choose a project folder, pick a session, press Continue.
/// The clone appears at the top of OpenCode's session list with full history.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _projectBox = new();
    private readonly ListView _list = new();
    private readonly TextBox _titleBox = new();
    private readonly Button _continueButton = new();
    private readonly Button _refreshButton = new();
    private readonly Label _statusLabel = new();

    private OcCli _cli = new();
    private List<SessionRow> _rows = new();
    private bool _busy;

    public MainForm()
    {
        Text = "OpenCode — Continue Session";
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);

        var header = new Label
        {
            Text = "Pick the session to continue. It is cloned with its full history as \"Name 2\" " +
                   "(then 3, …) and appears at the top of the OpenCode session list.",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 8, 8, 0),
            ForeColor = SystemColors.GrayText,
        };

        var projectPanel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 8, 2) };
        var projectLabel = new Label { Text = "Project folder:", AutoSize = true, Left = 8, Top = 8 };
        _projectBox.Dock = DockStyle.Fill;
        _projectBox.Text = LoadLastProject();
        _refreshButton.Text = "Refresh";
        _refreshButton.Width = 90;
        _refreshButton.Dock = DockStyle.Right;
        _refreshButton.Click += async (_, _) => await LoadSessionsAsync();
        var browse = new Button { Text = "…", Width = 34, Dock = DockStyle.Right };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) _projectBox.Text = dialog.SelectedPath;
        };
        projectPanel.Controls.Add(_projectBox);
        projectPanel.Controls.Add(browse);
        projectPanel.Controls.Add(_refreshButton);
        projectPanel.Controls.Add(projectLabel);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Columns.Add("Session title", 420);
        _list.Columns.Add("Last updated", 150);
        _list.Columns.Add("Session id", 220);
        _list.SelectedIndexChanged += (_, _) => UpdateTitlePreview();
        _list.DoubleClick += async (_, _) => await ContinueAsync();

        var titlePanel = new Panel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(8) };
        var titleLabel = new Label { Text = "New session title:", AutoSize = true, Left = 8, Top = 10 };
        _titleBox.Left = 150;
        _titleBox.Top = 7;
        _titleBox.Width = 420;
        _continueButton.Text = "Continue as new session →";
        _continueButton.Left = 590;
        _continueButton.Top = 5;
        _continueButton.Width = 220;
        _continueButton.Height = 28;
        _continueButton.Click += async (_, _) => await ContinueAsync();
        _statusLabel.Left = 150;
        _statusLabel.Top = 42;
        _statusLabel.Width = 660;
        _statusLabel.AutoSize = false;
        _statusLabel.ForeColor = SystemColors.GrayText;
        titlePanel.Controls.Add(titleLabel);
        titlePanel.Controls.Add(_titleBox);
        titlePanel.Controls.Add(_continueButton);
        titlePanel.Controls.Add(_statusLabel);

        Controls.Add(_list);
        Controls.Add(titlePanel);
        Controls.Add(projectPanel);
        Controls.Add(header);

        Load += async (_, _) => await LoadSessionsAsync();
    }

    private string SelectedProject => _projectBox.Text.Trim().TrimEnd('\\');

    private static string LoadLastProject()
    {
        try
        {
            var file = Path.Combine(CloneService.ExportDirectory, "..", "last-project.txt");
            if (File.Exists(file)) return File.ReadAllText(file).Trim();
        }
        catch { }
        return "C:\\XforceMsw";
    }

    private void SaveLastProject()
    {
        try
        {
            Directory.CreateDirectory(CloneService.ExportDirectory);
            var file = Path.Combine(Path.GetDirectoryName(CloneService.ExportDirectory)!, "last-project.txt");
            File.WriteAllText(file, SelectedProject);
        }
        catch { }
    }

    private async Task LoadSessionsAsync()
    {
        if (_busy) return;
        if (SelectedProject.Length == 0 || !Directory.Exists(SelectedProject))
        {
            _statusLabel.Text = "Choose an existing project folder first.";
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            _statusLabel.Text = "Listing sessions…";
            _cli.Dispose();
            _cli = new OcCli();
            _rows = await Task.Run(() => new CloneService(_cli).ListAsync(SelectedProject));

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var row in _rows)
            {
                var item = new ListViewItem(new[]
                {
                    row.Title,
                    row.Updated > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(row.Updated).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "",
                    row.Id,
                })
                { Tag = row };
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            SaveLastProject();
            _statusLabel.Text = _rows.Count + " session(s)" +
                                (_cli.ServerUrl is null ? "" : "  ·  (private opencode serve on " + _cli.ServerUrl + ")");
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "List failed: " + FirstLine(ex.Message);
            MessageBox.Show(this, ex.Message, "Could not list sessions", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private void UpdateTitlePreview()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not SessionRow row) return;
        _titleBox.Text = CloneService.NextTitle(row.Title, _rows.Select(r => r.Title));
    }

    private async Task ContinueAsync()
    {
        if (_busy) return;
        if (_list.SelectedItems.Count == 0)
        {
            _statusLabel.Text = "Select a session first.";
            return;
        }

        var row = (SessionRow)_list.SelectedItems[0].Tag!;
        var newTitle = _titleBox.Text.Trim();

        var confirm = MessageBox.Show(
            this,
            "Continue \"" + row.Title + "\" as a new session titled:\n\n    " + newTitle + "\n\n" +
            "The full history is carried over. Continue?",
            "Continue session",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _busy = true;
        SetBusy(true);
        try
        {
            var service = new CloneService(_cli);
            var result = await Task.Run(() => service.CloneAsync(
                SelectedProject,
                row,
                newTitle,
                _rows.Select(r => r.Title).ToList(),
                status => BeginInvoke(() => _statusLabel.Text = status + "…")));

            _statusLabel.Text = "Done — \"" + result.NewTitle + "\" is ready.";
            MessageBox.Show(
                this,
                "Created \"" + result.NewTitle + "\".\n\n" +
                "Open it from the OpenCode session list (it is at the top).\n\n" +
                "Backup export:\n" + result.ExportPath,
                "Session continued",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed: " + FirstLine(ex.Message);
            MessageBox.Show(this, ex.Message, "Could not continue session", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _continueButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _list.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 120 ? line.Substring(0, 120) + "…" : line;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cli.Dispose();
        base.OnFormClosed(e);
    }
}
