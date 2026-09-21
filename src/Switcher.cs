using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexProfileSwitcherPublic {
    public class Saved {
        public string Url = "https://api.routera.one/v1";
        public string Model = "";
        public string EnvironmentVariable = "ROUTERA_API_KEY";
        public string Reasoning = "";
        public string OriginalRoots;
        public string Mode = "environment";
    }

    public static class Engine {
        internal static bool TestMode = false;
        internal static string StorageOverride = null;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string existing, string replacement, int flags);
        public const string Provider = "routera_public_switcher";
        const string Begin = "# BEGIN PUBLIC PROFILE SWITCHER";
        const string End = "# END PUBLIC PROFILE SWITCHER";
        static readonly string[] Keys = { "model", "model_provider", "model_reasoning_effort", "model_verbosity", "forced_login_method", "openai_base_url", "chatgpt_base_url", "profile" };
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static string DataRoot { get { return StorageOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexProfileSwitcherPublic"); } }
        public static string DefaultConfig {
            get {
                string p = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (String.IsNullOrWhiteSpace(p)) p = Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.User);
                if (String.IsNullOrWhiteSpace(p)) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                return Path.Combine(p, "config.toml");
            }
        }
        public static string Canonical(string path) {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new Exception("Choose an absolute path to a TOML configuration file.");
            string full = Path.GetFullPath(path.Trim());
            if (!Path.GetExtension(full).Equals(".toml", StringComparison.OrdinalIgnoreCase)) throw new Exception("Choose a .toml configuration file. Authentication files are not configuration files.");
            return full;
        }
        public static string StateDir(string home) {
            using (SHA256 hash = SHA256.Create()) {
                string id = BitConverter.ToString(hash.ComputeHash(Utf8.GetBytes(Canonical(home).ToUpperInvariant()))).Replace("-", "").ToLowerInvariant();
                return Path.Combine(DataRoot, "profiles", id);
            }
        }
        static string Config(string home) { return Canonical(home); }
        static string StateFile(string home) { return Path.Combine(StateDir(home), "settings.json"); }
        public static bool HasEnvironmentKey(string variable) {
            return !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)) || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User));
        }
        static void CheckDestination(string home) {
            string p = Canonical(home);
            for (DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(p)); d != null; d = d.Parent)
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new Exception("Linked settings folders are not supported. Choose the real configuration folder.");
            if (File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new Exception("This configuration is a link. Choose the original file.");
        }
        public static void CheckFile(string home) {
            CheckDestination(home); string p = Canonical(home);
            if (!File.Exists(p)) throw new Exception("This configuration file does not exist. Browse to a file or choose New config.");
            if (new FileInfo(p).Length > 8 * 1024 * 1024) throw new Exception("This configuration is too large to edit (maximum 8 MB).");
            foreach (string path in new [] { Config(home), StateDir(home), StateFile(home), Path.Combine(StateDir(home), "backups") })
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("A configuration or switcher path is a link. No changes were made.");
        }
        public static Saved Load(string home) {
            if (!File.Exists(StateFile(home))) return new Saved();
            Saved s = new JavaScriptSerializer().Deserialize<Saved>(File.ReadAllText(StateFile(home)));
            if (s == null) throw new Exception("Saved settings are unreadable. Restore a configuration backup before continuing.");
            return s;
        }
        static void SecureDirectory(string path) {
            for (DirectoryInfo d = new DirectoryInfo(path); d != null; d = d.Parent)
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new Exception("A storage folder is a link. Choose a real folder.");
            Directory.CreateDirectory(path);
            if (TestMode) return; // The self-test sandbox disallows ACL changes; never used by the UI.
            DirectorySecurity acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        static void Atomic(string path, byte[] bytes) {
            string temp = Path.Combine(Path.GetDirectoryName(path), "tmp-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using (FileStream f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
                if (!TestMode && File.Exists(path)) File.SetAccessControl(temp, File.GetAccessControl(path));
                if (!MoveFileEx(temp, path, 1 | 8)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static string Quote(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\""; }
        public static string RememberedConfig() {
            string p = Path.Combine(DataRoot, "preferences.json");
            try {
                if (File.Exists(p)) {
                    Dictionary<string, string> settings = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(p));
                    if (settings != null && settings.ContainsKey("lastConfig")) return Canonical(settings["lastConfig"]);
                }
            } catch { /* A missing or stale preference should not prevent browsing to a file. */ }
            return DefaultConfig;
        }
        public static void Remember(string file) {
            SecureDirectory(DataRoot);
            Atomic(Path.Combine(DataRoot, "preferences.json"), Utf8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string,string> { { "lastConfig", Canonical(file) } })));
        }
        static string Backup(string file, byte[] bytes) {
            string folder = Path.Combine(StateDir(file), "backups"); Directory.CreateDirectory(folder);
            string p = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".toml");
            Atomic(p, bytes); return p;
        }
        public static void SaveText(string file, byte[] expected, string text) {
            CheckFile(file); string ignored; ExtractRoots(text, out ignored);
            SecureDirectory(StateDir(file));
            using (FileStream gate = new FileStream(Path.Combine(StateDir(file), "switch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                byte[] current = File.ReadAllBytes(Config(file));
                if (!current.SequenceEqual(expected)) throw new Exception("The file changed outside the editor. Close and reopen the editor before saving.");
                Backup(file, current);
                if (!File.ReadAllBytes(Config(file)).SequenceEqual(current)) throw new Exception("The file changed while saving. Reload it before trying again.");
                Atomic(Config(file), Utf8.GetBytes(text));
            }
        }
        public static void CreateConfig(string file) {
            file = Canonical(file); CheckDestination(file);
            if (!Directory.Exists(Path.GetDirectoryName(file))) throw new Exception("Choose an existing destination folder.");
            byte[] content = Utf8.GetBytes("# OpenAI account configuration. Sign in through Codex if needed.\r\nmodel_provider = \"openai\"\r\nforced_login_method = \"chatgpt\"\r\n");
            using (FileStream f = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(content, 0, content.Length); f.Flush(true); }
        }
        public static void SaveCopy(string source, byte[] expected, string text, string destination) {
            CheckFile(source); destination = Canonical(destination); CheckDestination(destination);
            if (destination.Equals(Canonical(source), StringComparison.OrdinalIgnoreCase)) { SaveText(source, expected, text); return; }
            if (!File.ReadAllBytes(Config(source)).SequenceEqual(expected)) throw new Exception("The source changed outside the editor. Reload it before saving a copy.");
            if (File.Exists(destination)) throw new Exception("Save As creates a new file. Choose a new name, or open the existing destination to edit it.");
            if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new Exception("Choose an existing destination folder.");
            string roots; ExtractRoots(text, out roots);
            bool active = RootValue(roots, "model_provider") == Provider;
            Saved saved = Load(source);
            if (active && saved.OriginalRoots == null) throw new Exception("This Routera configuration has no saved OpenAI profile. Switch to OpenAI before copying it.");
            SecureDirectory(StateDir(destination));
            if (File.Exists(StateFile(source))) {
                if (saved.Mode != "environment") {
                    if (String.IsNullOrWhiteSpace(saved.Mode) || Path.GetFileName(saved.Mode) != saved.Mode) throw new Exception("Invalid saved credential reference.");
                    Atomic(Path.Combine(StateDir(destination), saved.Mode), File.ReadAllBytes(Path.Combine(StateDir(source), saved.Mode)));
                }
                Atomic(StateFile(destination), Utf8.GetBytes(new JavaScriptSerializer().Serialize(saved)));
            }
            byte[] content = Utf8.GetBytes(text);
            using (FileStream f = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(content,0,content.Length); f.Flush(true); }
        }
        public static string RemoveBlock(string text) {
            int a = text.IndexOf(Begin, StringComparison.Ordinal);
            int b = text.IndexOf(End, StringComparison.Ordinal);
            if (a < 0 && b < 0) return text;
            if (a < 0 || b < a || text.IndexOf(Begin, a + Begin.Length, StringComparison.Ordinal) >= 0 || text.IndexOf(End, b + End.Length, StringComparison.Ordinal) >= 0)
                throw new Exception("The switcher's configuration block has been edited. Restore a backup before switching.");
            // Codex may append new project tables. Keep those, but do not reinterpret values
            // still belonging to the provider table after the managed end marker.
            string tail = text.Substring(b + End.Length);
            string first = Regex.Split(tail, "\r?\n").Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && !x.StartsWith("#"));
            if (first != null && !first.StartsWith("[")) throw new Exception("A provider value was added below the switcher block. Move it into the managed provider table before switching.");
            string block = text.Substring(a, b - a);
            foreach (string line in Regex.Split(block, "\r?\n")) {
                string t = line.Trim();
                if (t.StartsWith("[") && t != "[model_providers." + Provider + "]" && t != "[model_providers." + Provider + ".auth]")
                    throw new Exception("An unrelated table was added inside the switcher block. No changes were made.");
            }
            return text.Substring(0, a) + tail.TrimStart('\r', '\n');
        }
        // Preserve the original text, including multiline arrays/strings and comments. Only
        // single-line root scalar settings in Keys are replaced; unsupported forms fail closed.
        public static string ExtractRoots(string text, out string roots) {
            StringBuilder keep = new StringBuilder(), found = new StringBuilder();
            bool inTable = false; string multi = null; int depth = 0;
            HashSet<string> seen = new HashSet<string>();
            foreach (Match m in Regex.Matches(text, @"[^\r\n]*(?:\r\n|\n|\r|$)")) {
                string line = m.Value; if (line.Length == 0) continue;
                string t = line.TrimStart();
                bool neutral = multi == null && depth == 0;
                if (neutral && t.StartsWith("[")) inTable = true;
                Match key = Regex.Match(t, "^(?:([A-Za-z0-9_-]+)|\"([A-Za-z0-9_-]+)\"|'([A-Za-z0-9_-]+)')\\s*=");
                string k = key.Success ? key.Groups.Cast<Group>().Skip(1).First(x => x.Success).Value : "";
                bool target = !inTable && neutral && Keys.Contains(k);
                if (target) {
                    if (!seen.Add(k)) throw new Exception("Duplicate profile setting: " + k);
                    string val = t.Substring(key.Length).Trim();
                    if (!Regex.IsMatch(val, "^(?:\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"|'[^'\\r\\n]*')\\s*(?:#[^\\r\\n]*)?$"))
                        throw new Exception("Unsupported value for " + k + ". Use a single-line quoted string.");
                    found.Append(line);
                } else keep.Append(line);
                // Track lexical state so a table-looking line inside a multiline value stays data.
                char quote = '\0';
                for (int i = 0; i < line.Length; i++) {
                    if (multi != null) {
                        if (i + 2 < line.Length && line.Substring(i, 3) == multi) { multi = null; i += 2; }
                        else if (multi == "\"\"\"" && line[i] == '\\') i++;
                        continue;
                    }
                    char c = line[i];
                    if (quote != '\0') { if (c == '\\' && quote == '"') i++; else if (c == quote) quote = '\0'; continue; }
                    if (c == '#') break;
                    if (c == '"' || c == '\'') {
                        if (i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c) { multi = new string(c, 3); i += 2; } else quote = c;
                    } else if (c == '[' || c == '{') depth++;
                    else if (c == ']' || c == '}') depth--;
                }
                if (quote != '\0' || depth < 0) throw new Exception("The configuration contains an incomplete value. Fix it before switching.");
            }
            if (multi != null || depth != 0) throw new Exception("The configuration contains an incomplete multiline value.");
            roots = found.ToString(); return keep.ToString();
        }
        public static string RootValue(string roots, string key) {
            Match m = Regex.Match(roots, "(?m)^\\s*(?:" + key + "|\"" + key + "\"|'" + key + "')\\s*=\\s*(?:\"([^\"\\r\\n]*)\"|'([^'\\r\\n]*)')");
            return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : "";
        }
        public static string Current(string home) {
            CheckFile(home); string roots; ExtractRoots(File.ReadAllText(Config(home)), out roots);
            string p = RootValue(roots, "model_provider");
            return p == Provider ? "Routera" : (p == "" || p == "openai" ? "OpenAI account" : p);
        }
        public static void ValidateRoutera(string url, string model) {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https" || !String.IsNullOrEmpty(u.UserInfo) || !String.IsNullOrEmpty(u.Query) || !String.IsNullOrEmpty(u.Fragment))
                throw new Exception("Enter an HTTPS API base URL without credentials, query parameters, or a fragment.");
            if (String.IsNullOrWhiteSpace(model) || model.Any(Char.IsControl) || model.Any(Char.IsWhiteSpace)) throw new Exception("Enter the Routera model ID without spaces.");
        }
        static bool StandardUrl(string value, string expected) {
            return value.Length == 0 || value.TrimEnd('/').Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        public static string ReadKey(string path) {
            return Utf8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
        }
        public static void Apply(string home, bool routera, string url, string model, string newKey, string authMode, string envName, string reasoning) {
            CheckFile(home);
            SecureDirectory(StateDir(home));
            using (FileStream gate = new FileStream(Path.Combine(StateDir(home), "switch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                byte[] before = File.ReadAllBytes(Config(home));
                string original = Utf8.GetString(before).TrimStart('\uFEFF');
                string clean = RemoveBlock(original), roots;
                clean = ExtractRoots(clean, out roots);
                Saved s = Load(home);
                bool active = RootValue(roots, "model_provider") == Provider;
                if (active && s.OriginalRoots == null) throw new Exception("The saved OpenAI profile is missing. Restore an OpenAI backup first.");
                if (!active) {
                    string provider = RootValue(roots, "model_provider");
                    bool standard = (provider == "" || provider == "openai") && StandardUrl(RootValue(roots, "openai_base_url"), "https://api.openai.com/v1") && StandardUrl(RootValue(roots, "chatgpt_base_url"), "https://chatgpt.com/backend-api") && RootValue(roots, "profile") == "";
                    if (!standard && routera) throw new Exception("This file currently uses another provider or a custom account URL. Use OpenAI account first to create a normal baseline, then configure Routera.");
                    s.OriginalRoots = standard ? roots : "model_provider = \"openai\"\n";
                }
                string next;
                string newline = original.Contains("\r\n") ? "\r\n" : "\n";
                if (routera) {
                    url = url.Trim().TrimEnd('/'); model = model.Trim(); ValidateRoutera(url, model);
                    if (Regex.IsMatch(clean, @"(?m)^\s*\[\s*model_providers\s*\.\s*['" + "\"" + @"]?routera_public_switcher\b"))
                        throw new Exception("A conflicting public switcher provider exists outside the managed block.");
                    if (authMode != "encrypted" && authMode != "environment") throw new Exception("Select an authentication method.");
                    if (!Regex.IsMatch(envName ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new Exception("Enter a valid environment variable name, such as ROUTERA_API_KEY.");
                    if (!new [] { "", "low", "medium", "high", "xhigh", "max", "ultra" }.Contains(reasoning)) throw new Exception("Choose a supported reasoning setting.");
                    string keyPath = Path.Combine(StateDir(home), "routera-key.dpapi");
                    string credentialName = null;
                    if (authMode == "encrypted" && !String.IsNullOrWhiteSpace(newKey)) {
                        newKey = newKey.Trim();
                        if (newKey.Any(Char.IsWhiteSpace) || newKey.Any(Char.IsControl)) throw new Exception("The API key contains whitespace or control characters.");
                        // Versioned files preserve the last working configuration if a later write fails.
                        credentialName = "routera-key-" + Guid.NewGuid().ToString("N") + ".dpapi";
                        keyPath = Path.Combine(StateDir(home), credentialName);
                        Atomic(keyPath, ProtectedData.Protect(Utf8.GetBytes(newKey), null, DataProtectionScope.CurrentUser));
                        s.Mode = credentialName;
                    } else if (authMode == "encrypted" && s.Mode != "environment" && !String.IsNullOrEmpty(s.Mode)) {
                        if (Path.GetFileName(s.Mode) != s.Mode) throw new Exception("The saved credential reference is invalid.");
                        keyPath = Path.Combine(StateDir(home), s.Mode); ReadKey(keyPath);
                    } else if (authMode == "environment") {
                        if (!HasEnvironmentKey(envName)) throw new Exception("The selected environment variable is not set. Set it for your Windows user, or choose Encrypted key and enter your own key.");
                        s.Mode = "environment";
                    } else throw new Exception("Enter a key to save with Windows encryption.");
                    s.EnvironmentVariable = envName;
                    string block = Begin + newline + "[model_providers." + Provider + "]" + newline +
                        "name = \"Routera\"" + newline + "base_url = " + Quote(url) + newline + "wire_api = \"responses\"" + newline + "supports_websockets = false" + newline;
                    if (s.Mode == "environment") block += "env_key = " + Quote(envName) + newline;
                    else {
                        string helper = Path.Combine(StateDir(home), "CredentialHelper.exe");
                        byte[] binary = File.ReadAllBytes(typeof(Engine).Assembly.Location);
                        if (!File.Exists(helper) || !File.ReadAllBytes(helper).SequenceEqual(binary)) Atomic(helper, binary);
                        block += newline + "[model_providers." + Provider + ".auth]" + newline + "command = " + Quote(helper) + newline +
                            "args = [\"--token\", " + Quote(keyPath) + "]" + newline + "timeout_ms = 5000" + newline + "refresh_interval_ms = 0" + newline;
                    }
                    block += End + newline;
                    next = "model_provider = \"" + Provider + "\"" + newline + "model = " + Quote(model) + newline + (reasoning.Length > 0 ? "model_reasoning_effort = " + Quote(reasoning) + newline : "") + clean.TrimEnd('\r', '\n') + newline + block;
                    s.Url = url; s.Model = model; s.Reasoning = reasoning;
                } else {
                    string baseRoots = s.OriginalRoots ?? roots;
                    baseRoots = Regex.Replace(baseRoots, "(?m)^\\s*(?:forced_login_method|\"forced_login_method\"|'forced_login_method')\\s*=[^\\r\\n]*(?:\\r?\\n|$)", "");
                    next = "forced_login_method = \"chatgpt\"" + newline + baseRoots.TrimEnd('\r', '\n') + newline + clean.TrimEnd('\r', '\n') + newline;
                }
                // Validate the generated structure before any config replacement.
                string checkRoots; ExtractRoots(next, out checkRoots);
                Backup(home, before);
                Atomic(StateFile(home), Utf8.GetBytes(new JavaScriptSerializer().Serialize(s)));
                if (!File.ReadAllBytes(Config(home)).SequenceEqual(before)) throw new Exception("Codex changed the configuration while switching. Close Codex and try again.");
                Atomic(Config(home), Utf8.GetBytes(next));
            }
        }
    }

}
