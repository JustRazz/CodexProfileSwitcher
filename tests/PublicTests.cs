using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Drawing;
using System.Security.AccessControl;
using System.Windows.Forms;
using CodexProfileSwitcherPublic;
class PublicTests {
    static int count;
    static void Check(bool value,string name) { if(!value)throw new Exception("FAILED: "+name); count++; Console.WriteLine("PASS: "+name); }
    static void Throws(Action action,string name) { bool failed=false; try { action(); }catch { failed=true; } Check(failed,name); }
    static byte[] Bytes(string p) { return File.ReadAllBytes(p); }
    static string Text(string p) { return File.ReadAllText(p); }
    static void Routera(string file,string key="",string mode="encrypted",string variable="PUBLIC_SWITCHER_TEST_KEY") { Engine.Apply(file,true,"https://api.routera.one/v1","example-model",key,mode,variable,"high"); }
    static void OpenAI(string file) { Engine.Apply(file,false,"","","","environment","PUBLIC_SWITCHER_TEST_KEY",""); }
    static string Hash(string file) { using(var h=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(h.ComputeHash(Bytes(file))); }
    [STAThread] static int Main(string[] args) {
        string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        typeof(Engine).GetField("StorageOverride",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,Path.Combine(root,"private-data"));
        string variable="PUBLIC_SWITCHER_TEST_KEY";
        string previous=Environment.GetEnvironmentVariable(variable);
        try {
            string folder=Path.Combine(root,"user A", ".codex"), other=Path.Combine(root,"custom folder", "configuration"); Directory.CreateDirectory(folder); Directory.CreateDirectory(other);
            string file=Path.Combine(folder,"config.toml"), destination=Path.Combine(other,"alternate.toml"), auth=Path.Combine(folder,"auth.json");
            string shared="# shared comment\r\napproval_policy = \"never\"\r\nnotify = [\r\n'notify.exe',\r\n'done'\r\n]\r\n[desktop]\r\nmessage = '''long\r\n[not-a-table]\r\nmodel = \"string content\"\r\n'''\r\n[projects.'C:\\example project']\r\ntrust_level = 'trusted'\r\n";
            File.WriteAllText(file,"model = \"account-model\"\r\nmodel_reasoning_effort = \"max\"\r\nforced_login_method = \"chatgpt\"\r\n"+shared,new UTF8Encoding(false));
            File.WriteAllText(auth,"TEST AUTH BYTES - NEVER MODIFY"); string authHash=Hash(auth);
            Engine.CheckFile(file); Check(true,"standard .codex folder accepted");
            Check(Engine.Load(file).Model=="","fresh public profile has no account-specific model");
            Routera(file,"PUBLIC-TEST-KEY-NOT-A-REAL-CREDENTIAL");
            Check(Engine.Current(file)=="Routera","Routera selected"); Check(Text(file).Contains(shared),"unrelated settings preserved"); Check(!Text(file).Contains("PUBLIC-TEST-KEY-NOT-A-REAL-CREDENTIAL"),"no plaintext key in config");
            Check(new DirectoryInfo(Engine.StateDir(file)).GetAccessControl().AreAccessRulesProtected,"private profile folder ACL is restricted");
            Saved state=Engine.Load(file); string secret=Path.Combine(Engine.StateDir(file),state.Mode);
            Check(Engine.ReadKey(secret)=="PUBLIC-TEST-KEY-NOT-A-REAL-CREDENTIAL","DPAPI key roundtrip");
            var psi=new ProcessStartInfo(Path.Combine(Engine.StateDir(file),"CredentialHelper.exe"),"--token \""+secret+"\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true };
            using(var p=Process.Start(psi)) { string result=p.StandardOutput.ReadToEnd(); Check(p.WaitForExit(10000) && p.ExitCode==0 && result=="PUBLIC-TEST-KEY-NOT-A-REAL-CREDENTIAL","credential helper returns correct key"); }
            string beforeCopy=Hash(file); Engine.SaveCopy(file,Bytes(file),Text(file),destination);
            Check(File.Exists(destination) && Hash(file)==beforeCopy,"Save As copies to custom filename without changing source");
            Check(Engine.StateDir(file)!=Engine.StateDir(destination),"config locations have isolated state");
            OpenAI(destination); Check(Text(destination).Contains("account-model") && Text(destination).Contains("max"),"copied Routera profile restores original OpenAI settings");
            Routera(destination); Check(Engine.Current(destination)=="Routera","copied encrypted credential works at new location");
            Check(Hash(file)==beforeCopy,"switching copy does not modify original file");
            OpenAI(file); Check(Text(file).Contains(shared) && Text(file).Contains("account-model"),"OpenAI roundtrip preserves original settings");
            byte[] expected=Bytes(file); string edited=Text(file)+"[features]\r\nmemories = true\r\n"; Engine.SaveText(file,expected,edited);
            Check(Text(file)==edited,"editor saves full TOML text");
            Check(Directory.GetFiles(Path.Combine(Engine.StateDir(file),"backups")).Length==3,"backups created for switches and editor saves");
            Throws(delegate { Engine.SaveText(file,expected,"model = \"stale\"\n"); },"stale editor save rejected");
            Check(Text(file)==edited,"rejected stale save leaves file unchanged");
            Throws(delegate { Engine.SaveText(file,Bytes(file),"model = \"unterminated\n"); },"basic malformed scalar rejected");
            Throws(delegate { Engine.SaveCopy(file,Bytes(file),Text(file),destination); },"Save As does not overwrite an existing config");
            Engine.Remember(destination); Check(Engine.RememberedConfig()==destination,"selected location remembered in private storage");
            Check(File.Exists(Path.Combine(Engine.DataRoot,"preferences.json")),"preferences live outside app folder");
            Throws(delegate { Engine.CheckFile(auth); },"authentication JSON cannot be selected for editing");
            string newFile=Path.Combine(other,"new.toml"); Engine.CreateConfig(newFile); Check(Engine.Current(newFile)=="OpenAI account","New config creates usable account baseline");
            Throws(delegate { Engine.CreateConfig(newFile); },"New config cannot overwrite existing files");
            Environment.SetEnvironmentVariable(variable,"PUBLIC-FAKE-ENV-KEY",EnvironmentVariableTarget.Process); Routera(newFile,"","environment",variable);
            Check(Text(newFile).Contains("env_key = \""+variable+"\"") && !Text(newFile).Contains("PUBLIC-FAKE-ENV-KEY"),"custom environment variable used without saving its value");
            OpenAI(newFile); Environment.SetEnvironmentVariable(variable,null,EnvironmentVariableTarget.Process);
            Throws(delegate { Routera(newFile,"","environment",variable); },"missing environment variable reported");
            byte[] clean=Bytes(newFile); Throws(delegate { Engine.Apply(newFile,true,"http://unsafe.example/v1","model","fake","encrypted",variable,""); },"insecure endpoint rejected"); Check(clean.SequenceEqual(Bytes(newFile)),"invalid profile leaves file unchanged");
            File.WriteAllText(newFile,"model_provider = \"another-provider\"\nmodel = \"foreign-model\"\n[model_providers.another-provider]\nbase_url = \"https://example.invalid/v1\"\n");
            Throws(delegate { Routera(newFile,"fake"); },"foreign provider requires explicit OpenAI baseline"); OpenAI(newFile);
            Check(Engine.Current(newFile)=="OpenAI account" && !Text(newFile).Contains("foreign-model"),"OpenAI action can normalize a foreign-provider setup");
            Routera(file); File.AppendAllText(file,"[projects.'C:\\later project']\ntrust_level = 'trusted'\n"); OpenAI(file);
            Check(Text(file).Contains("later project"),"project tables added while Routera active survive switch");
            Check(Hash(auth)==authHash,"account authentication file never modified");
            Check(!Directory.Exists(Path.Combine(folder,".profile-switcher")),"public edition does not use personal-edition storage");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new MainForm(file)) {
                form.ShowInTaskbar=false; form.Opacity=0; form.Show(); Application.DoEvents();
                using(var bmp=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size)); bmp.Save(Path.Combine(root,"main-preview.png")); }
                Check(form.Text=="Profile Switcher Public","public window opens with custom config"); form.Close();
            }
            using(var editor=new ConfigEditor(file)) {
                TextBox input=editor.Controls[0].Controls.OfType<TextBox>().Single();
                Check(!System.Text.RegularExpressions.Regex.IsMatch(input.Text,@"(?<!\r)\n"),"editor displays mixed file line endings consistently");
                editor.ShowInTaskbar=false; editor.Opacity=0; editor.Show(); Application.DoEvents();
                using(var bmp=new Bitmap(editor.Width,editor.Height)) { editor.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size)); bmp.Save(Path.Combine(root,"editor-preview.png")); }
                Check(editor.ResultPath==file,"editor opens selected file"); editor.Close();
            }
            File.WriteAllText(Path.Combine(root,"result.txt"),"PASS: "+count+" checks, including production ACL and atomic writes. All files and fake credentials were isolated in a test directory."); Console.WriteLine("TOTAL: "+count+" checks passed."); return 0;
        }catch(Exception ex) { File.WriteAllText(Path.Combine(root,"result.txt"),ex.ToString());Console.Error.WriteLine(ex);return 1; }
        finally { Environment.SetEnvironmentVariable(variable,previous,EnvironmentVariableTarget.Process); }
    }
}
