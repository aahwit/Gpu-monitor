namespace GpuMonitor;

internal sealed class PreferencesDialog : Form
{
    private readonly CheckBox updateCheck = new();
    private readonly CheckBox telemetryCheck = new();

    public PreferencesDialog()
    {
        var preferences = AppPreferences.Load();
        Text = "Privacy & Updates";
        ClientSize = new Size(470, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(248, 250, 251);
        ForeColor = Color.FromArgb(24, 32, 38);

        var heading = new Label { Text = "Help Improve GPU Monitor", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true, Location = new Point(24, 22) };
        var intro = new Label { Text = "Choose how GPU Monitor communicates with KENRI services.", AutoSize = true, Location = new Point(27, 58), ForeColor = Color.FromArgb(85, 95, 102) };
        updateCheck.Text = "Check for updates automatically";
        updateCheck.Checked = preferences.AutoUpdate;
        updateCheck.AutoSize = true;
        updateCheck.Location = new Point(28, 97);
        updateCheck.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        var updateInfo = new Label { Text = "Receive a notification when a new version is available.", AutoSize = true, Location = new Point(49, 124), ForeColor = Color.FromArgb(85, 95, 102) };
        telemetryCheck.Text = "Send anonymous usage data";
        telemetryCheck.Checked = preferences.AnonymousTelemetry;
        telemetryCheck.AutoSize = true;
        telemetryCheck.Location = new Point(28, 159);
        telemetryCheck.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        var telemetryInfo = new Label
        {
            Text = "Shares a random installation ID, app version, GPU model, online status,\nand general performance metrics. No usernames, computer names, application\nlists, or file paths are collected.",
            AutoSize = true,
            Location = new Point(49, 186),
            ForeColor = Color.FromArgb(85, 95, 102)
        };
        var details = new LinkLabel { Text = "View data collection details", AutoSize = true, Location = new Point(49, 246) };
        details.LinkClicked += (_, _) => MessageBox.Show(this,
            "Anonymous usage data may include the installation ID, GPU Monitor version, GPU model, online status, and aggregate GPU performance metrics. It does not include usernames, computer names, application lists, process names, model paths, or file paths.",
            "Data Collection Details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        var save = new Button { Text = "Save", Size = new Size(92, 34), Location = new Point(350, 278), BackColor = Color.FromArgb(30, 120, 170), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        save.Click += (_, _) => { AppPreferences.Save(updateCheck.Checked, telemetryCheck.Checked); DialogResult = DialogResult.OK; Close(); };
        var cancel = new Button { Text = "Cancel", Size = new Size(92, 34), Location = new Point(248, 278), DialogResult = DialogResult.Cancel };
        AcceptButton = save;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { heading, intro, updateCheck, updateInfo, telemetryCheck, telemetryInfo, details, cancel, save });
    }
}
