using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CodexProfileSwitcherPublic {
    public static class Theme {
        public static Color Background = Color.FromArgb(18,24,35), Field = Color.FromArgb(32,42,58), Ink = Color.FromArgb(229,235,247), Muted = Color.FromArgb(165,179,200), Accent = Color.FromArgb(88,105,225);
        public static Label Label(string text) { return new Label { Text=text, AutoSize=true, ForeColor=Ink, Dock=DockStyle.Fill, Margin=new Padding(0,5,0,7) }; }
        public static Button Button(string text, Action click, bool primary=false) {
            Button b = new Button { Text=text, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Padding=new Padding(12,7,12,7), Margin=new Padding(0,0,10,8), BackColor=primary?Accent:Field, ForeColor=Color.White, FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(65,82,108); b.Click += delegate { click(); }; return b;
        }
        public static TextBox Input(bool password=false) { return new TextBox { Dock=DockStyle.Fill, BackColor=Field, ForeColor=Ink, BorderStyle=BorderStyle.FixedSingle, Margin=new Padding(0,5,0,7), UseSystemPasswordChar=password }; }
        public static FlowLayoutPanel Actions(params Control[] controls) {
            FlowLayoutPanel flow = new FlowLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Dock=DockStyle.Fill, Margin=new Padding(0,5,0,3), WrapContents=true };
            flow.Controls.AddRange(controls); return flow;
        }
    }
    public class MainForm : Form {
        readonly TextBox path=Theme.Input(), url=Theme.Input(), model=Theme.Input(), key=Theme.Input(true), env=Theme.Input();
        readonly ComboBox auth=new ComboBox(), reasoning=new ComboBox();
        readonly Label status=Theme.Label(""), keyHint=Theme.Label("");
        readonly Button routera, openai;
        string loadedPath;
        public MainForm(string testFile=null) {
            SuspendLayout();
            Text="Profile Switcher Public"; Icon=Icon.ExtractAssociatedIcon(typeof(Engine).Assembly.Location);
            Font=new Font("Segoe UI",10); BackColor=Theme.Background; ForeColor=Theme.Ink;
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(830,800); MinimumSize=new Size(700,640); StartPosition=FormStartPosition.CenterScreen;
            Panel scroll=new Panel { Dock=DockStyle.Fill, AutoScroll=true, Padding=new Padding(24) }; Controls.Add(scroll);
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, ColumnCount=1, Margin=Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); scroll.Controls.Add(layout);
            Action<Control> row = delegate(Control c) { int i=layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(c,0,i); };
            Label title=Theme.Label("Codex Profile Switcher"); title.Font=new Font("Segoe UI",23,FontStyle.Bold); row(title);
            Label subtitle=Theme.Label("Public edition  |  Your files, your account, your settings."); subtitle.ForeColor=Theme.Muted; row(subtitle);
            row(Theme.Label("CONFIGURATION FILE"));
            TableLayoutPanel picker=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=2, Margin=Padding.Empty };
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            picker.Controls.Add(path,0,0); Button browse=Theme.Button("Browse...", delegate { Browse(); }); browse.Margin=new Padding(12,0,0,5); picker.Controls.Add(browse,1,0); row(picker);
            row(Theme.Actions(Theme.Button("Load path", delegate { Guard(delegate { LoadSelected(true); }); }), Theme.Button("New config...", delegate { NewConfig(); }), Theme.Button("Open / edit...", delegate { EditConfig(); }), Theme.Button("Open folder", delegate { Guard(delegate { Engine.CheckFile(path.Text); OpenFolder(Path.GetDirectoryName(Engine.Canonical(path.Text))); }); }), Theme.Button("Detect path", delegate { path.Text=Engine.DefaultConfig; Guard(delegate { LoadSelected(true); }); })));
            status.ForeColor=Color.FromArgb(121,211,181); row(status);
            Label scope=Theme.Label("Only clients using this file are affected. Choosing or copying a file does not change where Codex looks for its settings."); scope.ForeColor=Theme.Muted; row(scope);
            row(Theme.Label("ROUTERA PROFILE"));
            TableLayoutPanel fields=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, ColumnCount=2, Margin=new Padding(0,4,0,3) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,175)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            auth.DropDownStyle=ComboBoxStyle.DropDownList; auth.Items.AddRange(new object[] { "Environment variable", "Encrypted key" }); auth.Dock=DockStyle.Fill; auth.Margin=new Padding(0,5,0,7);
            reasoning.DropDownStyle=ComboBoxStyle.DropDownList; reasoning.Items.AddRange(new object[] { "Provider default", "low", "medium", "high", "xhigh", "max", "ultra" }); reasoning.Dock=DockStyle.Fill; reasoning.Margin=new Padding(0,5,0,7);
            AddField(fields,"API base URL",url); AddField(fields,"Model ID",model); AddField(fields,"Reasoning",reasoning); AddField(fields,"Authentication",auth); AddField(fields,"Variable name",env); AddField(fields,"API key",key); row(fields);
            keyHint.ForeColor=Theme.Muted; row(keyHint);
            routera=Theme.Button("Use Routera", delegate { Switch(true); },true); openai=Theme.Button("Use OpenAI account", delegate { Switch(false); }); row(Theme.Actions(routera,openai));
            Label restart=Theme.Label("Close affected Codex clients before editing or switching. Reopen them and start a new task afterward."); restart.ForeColor=Theme.Muted; row(restart);
            row(Theme.Actions(Theme.Button("Open backups",delegate { Guard(delegate { EnsureLoaded(); OpenFolder(Path.Combine(Engine.StateDir(loadedPath),"backups")); }); }), Theme.Button("Open private data",delegate { Guard(delegate { OpenFolder(Engine.DataRoot); }); }), Theme.Button("Help",delegate { ShowHelp(); })));
            auth.SelectedIndexChanged += delegate { UpdateAuth(); }; env.TextChanged += delegate { UpdateAuth(); };
            path.TextChanged += delegate { routera.Enabled=false; openai.Enabled=false; status.Text="Load the selected path to continue."; };
            path.KeyDown += delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; Guard(delegate { LoadSelected(true); }); } };
            path.Text=testFile ?? Engine.RememberedConfig(); url.Text="https://api.routera.one/v1"; env.Text="ROUTERA_API_KEY"; auth.SelectedIndex=0; reasoning.SelectedIndex=0;
            try { LoadSelected(false); } catch(Exception ex) { status.Text=ex.Message; }
            ResumeLayout(true);
        }
        static void AddField(TableLayoutPanel table,string label,Control input) {
            int r=table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l=Theme.Label(label); l.Margin=new Padding(0,7,15,7); table.Controls.Add(l,0,r); table.Controls.Add(input,1,r);
        }
        void Guard(Action action) { try { action(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not complete the action",MessageBoxButtons.OK,MessageBoxIcon.Information); } }
        void EnsureLoaded() { if(loadedPath==null || !Engine.Canonical(path.Text).Equals(loadedPath,StringComparison.OrdinalIgnoreCase)) throw new Exception("Load the selected path first."); Engine.CheckFile(loadedPath); }
        void LoadSelected(bool remember) {
            string selected=Engine.Canonical(path.Text); Engine.CheckFile(selected); Saved s=Engine.Load(selected); string current=Engine.Current(selected);
            path.Text=selected; loadedPath=selected; url.Text=s.Url; model.Text=s.Model; env.Text=s.EnvironmentVariable; key.Clear();
            auth.SelectedIndex=s.Mode=="environment"?0:1; reasoning.SelectedItem=String.IsNullOrEmpty(s.Reasoning)?"Provider default":s.Reasoning;
            if(reasoning.SelectedIndex<0) reasoning.SelectedIndex=0;
            status.Text="Current provider: " + current; routera.Enabled=true; openai.Enabled=true; UpdateAuth(); if(remember) Engine.Remember(selected);
        }
        void UpdateAuth() {
            bool encrypted=auth.SelectedIndex==1; key.Enabled=encrypted; env.Enabled=!encrypted;
            if(encrypted) keyHint.Text="Enter your own key, or leave blank to keep a previously encrypted key. It is stored only for this Windows user.";
            else {
                bool present=false; try { present=!String.IsNullOrWhiteSpace(env.Text) && Engine.HasEnvironmentKey(env.Text); } catch { }
                keyHint.Text=present?"Variable found on this computer. Its value is never copied into the app folder. Restart clients if the variable was recently changed.":"Set this variable on your computer, or choose Encrypted key. Enter a model ID supported by your provider.";
            }
        }
        void Browse() { using(OpenFileDialog d=new OpenFileDialog { Title="Choose a Codex configuration file", Filter="TOML configurations (*.toml)|*.toml", CheckFileExists=true }) { if(d.ShowDialog(this)==DialogResult.OK) { path.Text=d.FileName; Guard(delegate { LoadSelected(true); }); } } }
        void NewConfig() { Guard(delegate { using(SaveFileDialog d=new SaveFileDialog { Title="Create a new configuration", Filter="TOML configuration (*.toml)|*.toml", FileName="config.toml", DefaultExt="toml", AddExtension=true, OverwritePrompt=true }) { if(d.ShowDialog(this)!=DialogResult.OK)return; Engine.CreateConfig(d.FileName); path.Text=d.FileName; LoadSelected(true); MessageBox.Show(this,"Configuration created. Codex must be configured to use this location separately. See Help for details.","Configuration created"); } }); }
        void EditConfig() { Guard(delegate { EnsureLoaded(); using(ConfigEditor editor=new ConfigEditor(loadedPath)) { editor.ShowDialog(this); path.Text=editor.ResultPath; LoadSelected(true); } }); }
        void Switch(bool toRoutera) { Guard(delegate {
            EnsureLoaded(); Engine.Apply(loadedPath,toRoutera,url.Text,model.Text,key.Text,auth.SelectedIndex==1?"encrypted":"environment",env.Text,reasoning.SelectedIndex<=0?"":reasoning.Text);
            LoadSelected(true); MessageBox.Show(this,"Profile saved to the selected file. Reopen the Codex clients that use this file and start a new task.","Profile saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }); }
        static void OpenFolder(string folder) { if(!Directory.Exists(folder)) throw new Exception("This folder is created after the first save or profile switch."); Process.Start(new ProcessStartInfo(folder) { UseShellExecute=true }); }
        void ShowHelp() {
            MessageBox.Show(this,"Choose the config.toml actually used by your Codex installation. The usual location is .codex in your Windows user folder, or the folder specified by CODEX_HOME.\n\nBrowse selects an existing file. Open / edit provides Save and Save As. Save As creates a copy and selects it; it does not move the original or change Codex's configuration location.\n\nIf you choose another folder, configure the clients to use that CODEX_HOME separately. Arbitrary TOML filenames can be edited, but they are not automatically loaded by Codex.\n\nKeys, backups and preferences stay in your Windows Local AppData folder under CodexProfileSwitcherPublic. Share the original download ZIP, not that private data folder.\n\nThe app makes no network requests and never changes account sign-in files.","Using the public edition",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
    public class ConfigEditor : Form {
        readonly TextBox text=new TextBox(); readonly Label location=Theme.Label("");
        byte[] expected; bool dirty; readonly string newline;
        public string ResultPath { get; private set; }
        public ConfigEditor(string file) {
            Engine.CheckFile(file); ResultPath=Engine.Canonical(file); expected=File.ReadAllBytes(ResultPath);
            string original=new UTF8Encoding(false,true).GetString(expected).TrimStart('\uFEFF');
            newline=original.Contains("\r\n")?"\r\n":"\n";
            Text="Edit configuration"; Icon=Icon.ExtractAssociatedIcon(typeof(Engine).Assembly.Location); Font=new Font("Segoe UI",10); BackColor=Theme.Background; ForeColor=Theme.Ink;
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(880,640); MinimumSize=new Size(620,450); StartPosition=FormStartPosition.CenterParent; KeyPreview=true;
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=4, Padding=new Padding(16) }; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(layout);
            location.Text=ResultPath; location.AutoEllipsis=true; layout.Controls.Add(location,0,0);
            Label hint=Theme.Label("Save backs up this file. Save As creates a copy at a new location. Basic structure is checked; Codex validates all settings when it loads the file."); hint.ForeColor=Theme.Muted; layout.Controls.Add(hint,0,1);
            text.Multiline=true; text.AcceptsTab=true; text.AcceptsReturn=true; text.WordWrap=false; text.ScrollBars=ScrollBars.Both; text.MaxLength=8*1024*1024; text.Font=new Font("Consolas",10); text.Dock=DockStyle.Fill; text.BackColor=Theme.Field; text.ForeColor=Theme.Ink; text.Text=original.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n"); layout.Controls.Add(text,0,2);
            layout.Controls.Add(Theme.Actions(Theme.Button("Save",delegate { Save(false); },true),Theme.Button("Save As...",delegate { Save(true); }),Theme.Button("Close",delegate { Close(); })),0,3);
            text.TextChanged+=delegate { dirty=true; Text="Edit configuration *"; };
            KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Control && e.KeyCode==Keys.S) { e.SuppressKeyPress=true; Save(e.Shift); } };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                if(!dirty)return;
                DialogResult answer=MessageBox.Show(this,"Save your changes before closing?","Unsaved changes",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
                if(answer==DialogResult.Cancel || (answer==DialogResult.Yes && !Save(false)))e.Cancel=true;
            };
        }
        bool Save(bool copy) {
            try {
                string target=ResultPath;
                string content=text.Text.Replace("\r\n","\n").Replace("\r","\n").Replace("\n",newline);
                if(copy) { using(SaveFileDialog d=new SaveFileDialog { Title="Save a new configuration copy", Filter="TOML configuration (*.toml)|*.toml", FileName="config.toml", InitialDirectory=Path.GetDirectoryName(ResultPath), DefaultExt="toml", AddExtension=true, OverwritePrompt=true }) { if(d.ShowDialog(this)!=DialogResult.OK)return false; target=d.FileName; } Engine.SaveCopy(ResultPath,expected,content,target); }
                else Engine.SaveText(ResultPath,expected,content);
                ResultPath=Engine.Canonical(target); expected=File.ReadAllBytes(ResultPath); location.Text=ResultPath; dirty=false; Text="Edit configuration"; return true;
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not save",MessageBoxButtons.OK,MessageBoxIcon.Information); return false; }
        }
    }
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            if(args.Length==2 && args[0]=="--token") {
                try { byte[] value=Encoding.UTF8.GetBytes(Engine.ReadKey(args[1])); using(Stream output=Console.OpenStandardOutput()) { output.Write(value,0,value.Length); } return 0; } catch { return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); return 0; } catch(Exception ex) { MessageBox.Show(ex.Message,"Profile Switcher Public",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
        }
    }
}
