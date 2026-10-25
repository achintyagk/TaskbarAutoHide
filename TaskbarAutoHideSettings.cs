using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("Taskbar Auto-Hide Settings")]
[assembly: AssemblyProduct("Taskbar Auto-Hide")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Achintya Giridhar Kini (https://achintya.design/)")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SettingsForm());
    }
}

class TaskConfig
{
    public int ResumeDelay = 10;
    public bool RunAtSignIn = false;
    public int SignInDelay = 15;
    public string ExePath;
}

static class TaskManager
{
    public const string TaskName = "TaskbarAutoHide";
    const string Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    const int TASK_CREATE_OR_UPDATE = 6;
    const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

    static dynamic RootFolder()
    {
        Type t = Type.GetTypeFromProgID("Schedule.Service");
        dynamic service = Activator.CreateInstance(t);
        service.Connect();
        return service.GetFolder("\\");
    }

    public static TaskConfig Load()
    {
        string xml;
        try
        {
            xml = RootFolder().GetTask(TaskName).Xml;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (COMException ex)
        {
            if ((uint)ex.ErrorCode == 0x80070002) return null;
            throw;
        }

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("t", Ns);

        var cfg = new TaskConfig();

        XmlNode resumeDelay = doc.SelectSingleNode("//t:EventTrigger/t:Delay", nsm);
        if (resumeDelay != null) cfg.ResumeDelay = ToSeconds(resumeDelay.InnerText, 10);

        XmlNode logon = doc.SelectSingleNode("//t:LogonTrigger", nsm);
        cfg.RunAtSignIn = logon != null;
        if (logon != null)
        {
            XmlNode logonDelay = logon.SelectSingleNode("t:Delay", nsm);
            if (logonDelay != null) cfg.SignInDelay = ToSeconds(logonDelay.InnerText, 15);
        }

        XmlNode cmd = doc.SelectSingleNode("//t:Exec/t:Command", nsm);
        if (cmd != null) cfg.ExePath = cmd.InnerText.Trim().Trim('"');

        return cfg;
    }

    public static void Save(TaskConfig cfg)
    {
        RootFolder().RegisterTask(TaskName, BuildXml(cfg), TASK_CREATE_OR_UPDATE,
            null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
    }

    public static void Delete()
    {
        RootFolder().DeleteTask(TaskName, 0);
    }

    static int ToSeconds(string isoDuration, int fallback)
    {
        try { return (int)XmlConvert.ToTimeSpan(isoDuration).TotalSeconds; }
        catch { return fallback; }
    }

    static string BuildXml(TaskConfig cfg)
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string exe = SecurityElement.Escape(cfg.ExePath);
        string subscription = SecurityElement.Escape(
            "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">" +
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]" +
            "</Select></Query></QueryList>");

        string logonTrigger = "";
        if (cfg.RunAtSignIn)
        {
            logonTrigger =
                "    <LogonTrigger>\n" +
                "      <Enabled>true</Enabled>\n" +
                "      <UserId>" + user + "</UserId>\n" +
                "      <Delay>PT" + cfg.SignInDelay + "S</Delay>\n" +
                "    </LogonTrigger>\n";
        }

        return
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"" + Ns + "\">\n" +
            "  <RegistrationInfo>\n" +
            "    <Description>Re-applies taskbar auto-hide after waking from sleep or hibernation. Managed by TaskbarAutoHideSettings.exe.</Description>\n" +
            "  </RegistrationInfo>\n" +
            "  <Triggers>\n" +
            "    <EventTrigger>\n" +
            "      <Enabled>true</Enabled>\n" +
            "      <Subscription>" + subscription + "</Subscription>\n" +
            "      <Delay>PT" + cfg.ResumeDelay + "S</Delay>\n" +
            "    </EventTrigger>\n" +
            logonTrigger +
            "  </Triggers>\n" +
            "  <Principals>\n" +
            "    <Principal id=\"Author\">\n" +
            "      <UserId>" + user + "</UserId>\n" +
            "      <LogonType>InteractiveToken</LogonType>\n" +
            "      <RunLevel>LeastPrivilege</RunLevel>\n" +
            "    </Principal>\n" +
            "  </Principals>\n" +
            "  <Settings>\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
            "    <AllowHardTerminate>true</AllowHardTerminate>\n" +
            "    <StartWhenAvailable>false</StartWhenAvailable>\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\n" +
            "    <Enabled>true</Enabled>\n" +
            "    <Hidden>false</Hidden>\n" +
            "    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>\n" +
            "  </Settings>\n" +
            "  <Actions Context=\"Author\">\n" +
            "    <Exec>\n" +
            "      <Command>\"" + exe + "\"</Command>\n" +
            "    </Exec>\n" +
            "  </Actions>\n" +
            "</Task>\n";
    }
}

class SettingsForm : Form
{
    Label statusLabel, resumeValue, signInValue, pathLabel;
    TrackBar resumeSlider, signInSlider;
    CheckBox signInCheck;
    Button saveButton, removeButton, runButton, closeButton;
    readonly string fixExePath;

    public SettingsForm()
    {
        fixExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TaskbarAutoHide.exe");

        SuspendLayout();
        Text = "Taskbar Auto-Hide Settings";
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        // AppIcon.ico is only embedded when build.bat finds icon.ico.
        Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.ico");
        if (iconStream != null)
        {
            try { Icon = new System.Drawing.Icon(iconStream); }
            catch { }
        }

        var main = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill
        };

        main.Controls.Add(CreateHeader());

        statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 0, 3, 10)
        };
        main.Controls.Add(statusLabel);

        var wakeGroup = MakeGroup("When the PC wakes from sleep or hibernation");
        var wakeLayout = MakeGroupLayout();
        resumeValue = new Label { AutoSize = true };
        wakeLayout.Controls.Add(MakeDelayRow("Wait before running:", resumeValue));
        resumeSlider = MakeSlider();
        resumeSlider.ValueChanged += delegate { resumeValue.Text = DelayText(resumeSlider.Value); };
        wakeLayout.Controls.Add(resumeSlider);
        wakeGroup.Controls.Add(wakeLayout);
        main.Controls.Add(wakeGroup);

        var signInGroup = MakeGroup("When I sign in");
        var signInLayout = MakeGroupLayout();
        signInCheck = new CheckBox
        {
            Text = "Also run when I sign in (covers restarts and shutdowns)",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 6)
        };
        signInLayout.Controls.Add(signInCheck);
        signInValue = new Label { AutoSize = true };
        signInLayout.Controls.Add(MakeDelayRow("Wait before running:", signInValue));
        signInSlider = MakeSlider();
        signInSlider.ValueChanged += delegate { signInValue.Text = DelayText(signInSlider.Value); };
        signInLayout.Controls.Add(signInSlider);
        signInCheck.CheckedChanged += delegate { signInSlider.Enabled = signInCheck.Checked; };
        signInGroup.Controls.Add(signInLayout);
        main.Controls.Add(signInGroup);

        pathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 8)
        };
        main.Controls.Add(pathLabel);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        closeButton = MakeButton("Close");
        saveButton = MakeButton("Create task");
        removeButton = MakeButton("Remove task");
        runButton = MakeButton("Run fix now");
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(removeButton);
        buttons.Controls.Add(runButton);
        main.Controls.Add(buttons);

        closeButton.Click += delegate { Close(); };
        saveButton.Click += OnSave;
        removeButton.Click += OnRemove;
        runButton.Click += OnRunNow;
        AcceptButton = saveButton;
        CancelButton = closeButton;

        Controls.Add(main);
        ResumeLayout(true);

        RefreshState();
    }

    const string AppTitle = "Taskbar Auto-Hide";
    const string AppTagline = "Keeps auto-hide working after sleep and hibernation";
    const int HeaderIconSize = 64;

    static Control CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(3, 0, 3, 12)
        };

        Bitmap iconImage = RenderHeaderIcon();
        if (iconImage != null)
        {
            header.Controls.Add(new PictureBox
            {
                Image = iconImage,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Width = HeaderIconSize,
                Height = HeaderIconSize,
                Margin = new Padding(0, 0, 14, 0)
            }, 0, 0);
        }

        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left
        };
        text.Controls.Add(new Label
        {
            Text = AppTitle,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = Color.FromArgb(0x1A, 0x1A, 0x1A),
            Margin = new Padding(0)
        });
        text.Controls.Add(new Label
        {
            Text = AppTagline,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.75F),
            ForeColor = Color.FromArgb(0x55, 0x55, 0x55),
            Margin = new Padding(2, 2, 0, 0)
        });
        header.Controls.Add(text, iconImage != null ? 1 : 0, 0);

        return header;
    }

    // Renders the icon at display's physical size
    static Bitmap RenderHeaderIcon()
    {
        Image source = LoadLargestIconFrame();
        if (source == null) return null;

        try
        {
            float scale;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                scale = screen.DpiX / 96f;

            int px = Math.Max(1, (int)Math.Round(HeaderIconSize * scale));
            var rendered = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(rendered))
            using (var attributes = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(0, 0, px, px),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return rendered;
        }
        catch
        {
            return null;
        }
        finally
        {
            source.Dispose();
        }
    }

    // System.Drawing.Icon can't load PNG-compressed frames, so the 256 px frame is directly read.
    static Image LoadLargestIconFrame()
    {
        Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.ico");
        if (stream == null) return null;

        try
        {
            byte[] data;
            using (stream)
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                data = ms.ToArray();
            }

            int count = BitConverter.ToUInt16(data, 4);
            int best = -1, bestSize = 0;
            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                int size = data[entry] == 0 ? 256 : data[entry];
                if (size > bestSize) { bestSize = size; best = entry; }
            }
            if (best < 0) return null;

            int length = BitConverter.ToInt32(data, best + 8);
            int offset = BitConverter.ToInt32(data, best + 12);

            bool isPng = data[offset] == 0x89 && data[offset + 1] == 0x50 &&
                         data[offset + 2] == 0x4E && data[offset + 3] == 0x47;
            if (isPng)
            {
                using (var frame = new MemoryStream(data, offset, length))
                using (Image png = Image.FromStream(frame))
                    return new Bitmap(png);
            }

            using (var whole = new MemoryStream(data))
            using (var icon = new System.Drawing.Icon(whole, bestSize, bestSize))
                return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    static GroupBox MakeGroup(string title)
    {
        return new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(3, 3, 3, 8),
            Dock = DockStyle.Fill
        };
    }

    static TableLayoutPanel MakeGroupLayout()
    {
        return new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill
        };
    }

    static FlowLayoutPanel MakeDelayRow(string caption, Label valueLabel)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        row.Controls.Add(new Label { Text = caption, AutoSize = true });
        valueLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        row.Controls.Add(valueLabel);
        return row;
    }

    // Slider steps 1..12 map to 5..60 seconds.
    static TrackBar MakeSlider()
    {
        return new TrackBar
        {
            Minimum = 1,
            Maximum = 12,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            Value = 2,
            Width = 420
        };
    }

    static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(90, 28),
            Padding = new Padding(6, 0, 6, 0)
        };
    }

    static string DelayText(int steps)
    {
        return " " + (steps * 5) + " seconds";
    }

    static int SecondsToSteps(int seconds)
    {
        int steps = (int)Math.Round(seconds / 5.0);
        return Math.Max(1, Math.Min(12, steps));
    }

    void RefreshState()
    {
        TaskConfig cfg;
        try
        {
            cfg = TaskManager.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't read Task Scheduler:\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            cfg = null;
        }

        bool fixExists = File.Exists(fixExePath);
        bool taskExists = cfg != null;
        TaskConfig shown = cfg ?? new TaskConfig();

        statusLabel.Text = taskExists
            ? "Status: Active. The fix runs automatically."
            : "Status: Not set up. The fix won't run automatically.";
        statusLabel.ForeColor = taskExists ? Color.ForestGreen : SystemColors.ControlText;
        saveButton.Text = taskExists ? "Save changes" : "Create task";
        removeButton.Enabled = taskExists;
        resumeSlider.Value = SecondsToSteps(shown.ResumeDelay);
        signInCheck.Checked = shown.RunAtSignIn;
        signInSlider.Value = SecondsToSteps(shown.SignInDelay);

        // ValueChanged doesn't fire if the value is unchanged, so set the labels directly.
        resumeValue.Text = DelayText(resumeSlider.Value);
        signInValue.Text = DelayText(signInSlider.Value);
        signInSlider.Enabled = signInCheck.Checked;

        if (!fixExists)
        {
            pathLabel.Text = "TaskbarAutoHide.exe was not found next to this program. " +
                             "Keep both .exe files in the same folder.";
            pathLabel.ForeColor = Color.Firebrick;
            saveButton.Enabled = false;
            runButton.Enabled = false;
        }
        else if (cfg != null && !string.IsNullOrEmpty(cfg.ExePath) &&
                 !string.Equals(SafeFullPath(cfg.ExePath), SafeFullPath(fixExePath),
                                StringComparison.OrdinalIgnoreCase))
        {
            pathLabel.Text = "The existing task points to:\n" + cfg.ExePath +
                             "\n\nClick Save changes to point it to this copy instead:\n" + fixExePath;
            pathLabel.ForeColor = Color.DarkOrange;
            saveButton.Enabled = true;
            runButton.Enabled = true;
        }
        else
        {
            pathLabel.Text = "Fix program: " + fixExePath;
            pathLabel.ForeColor = SystemColors.GrayText;
            saveButton.Enabled = true;
            runButton.Enabled = true;
        }
    }

    static string SafeFullPath(string p)
    {
        try { return Path.GetFullPath(p); }
        catch { return p; }
    }

    void OnSave(object sender, EventArgs e)
    {
        var cfg = new TaskConfig
        {
            ResumeDelay = resumeSlider.Value * 5,
            RunAtSignIn = signInCheck.Checked,
            SignInDelay = signInSlider.Value * 5,
            ExePath = fixExePath
        };

        try
        {
            TaskManager.Save(cfg);
            RefreshState();
            MessageBox.Show(this, "Saved. Taskbar auto-hide will be re-applied automatically.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't save the scheduled task:\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void OnRemove(object sender, EventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Remove the scheduled task? The fix will no longer run automatically.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        try
        {
            TaskManager.Delete();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't remove the scheduled task:\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshState();
    }

    void OnRunNow(object sender, EventArgs e)
    {
        try
        {
            Process.Start(fixExePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't start TaskbarAutoHide.exe:\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
