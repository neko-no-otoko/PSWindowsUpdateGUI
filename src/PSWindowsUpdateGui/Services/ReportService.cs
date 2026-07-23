using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Cli;

namespace PSWindowsUpdateGui.Services;

[DataContract]
internal sealed class ReportConfiguration
{
    [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; } = 1;
    [DataMember(Name = "smtpHost", Order = 2)] public string SmtpHost { get; set; } = string.Empty;
    [DataMember(Name = "smtpPort", Order = 3)] public int SmtpPort { get; set; } = 587;
    [DataMember(Name = "enableSsl", Order = 4)] public bool EnableSsl { get; set; } = true;
    [DataMember(Name = "from", Order = 5)] public string From { get; set; } = string.Empty;
    [DataMember(Name = "to", Order = 6)] public string To { get; set; } = string.Empty;
    [DataMember(Name = "credentialUser", Order = 7)] public string CredentialUser { get; set; } = string.Empty;
}

internal sealed class ReportService
{
    private const string CredentialTarget = "PSWindowsUpdateGUI/SMTP";
    private readonly string _configurationPath;

    public ReportService()
    {
        var settings = new PortableSettingsService();
        _configurationPath = Path.Combine(settings.DataDirectory, "report.json");
    }

    public async Task<int> ExecuteCliAsync(CliArguments arguments)
    {
        var verb = arguments.Positionals.Count > 1 ? arguments.Positionals[1].ToLowerInvariant() : "status";
        if (verb == "status") { RenderStatus(Load()); return 0; }
        if (verb == "configure") return Configure(arguments);
        if (verb == "test" || verb == "send")
        {
            if (!arguments.Has("yes")) throw new InvalidOperationException("Sending mail noninteractively requires --yes.");
            var body = verb == "test" ? "PSWindowsUpdateGUI SMTP configuration test." : ReadBody(arguments.Get("body-file"));
            var subject = arguments.Get("subject", verb == "test" ? "PSWindowsUpdateGUI SMTP test" : "PSWindowsUpdateGUI report");
            await SendAsync(subject, body).ConfigureAwait(false);
            Console.WriteLine("Report sent.");
            return 0;
        }
        throw new FormatException("report supports status, configure, test, or send.");
    }

    private int Configure(CliArguments arguments)
    {
        if (arguments.Has("ssl") && arguments.Has("no-ssl")) throw new FormatException("Use either --ssl or --no-ssl, not both.");
        var configuration = new ReportConfiguration
        {
            SmtpHost = arguments.Get("smtp-host"),
            SmtpPort = arguments.GetInt("smtp-port", 587),
            EnableSsl = !arguments.Has("no-ssl"),
            From = arguments.Get("from"),
            To = arguments.Get("to"),
            CredentialUser = arguments.Get("credential-user")
        };
        Validate(configuration);
        if (!arguments.Has("yes")) throw new InvalidOperationException("Saving report configuration noninteractively requires --yes.");
        if (!string.IsNullOrWhiteSpace(configuration.CredentialUser))
        {
            if (Console.IsInputRedirected) throw new InvalidOperationException("SMTP password entry requires an interactive console and is never accepted as an argument.");
            Console.Error.Write("SMTP password (stored in Windows Credential Manager): ");
            using var password = ReadPassword();
            CredentialStore.Write(CredentialTarget, configuration.CredentialUser, password);
            Console.Error.WriteLine();
        }
        Save(configuration);
        Console.WriteLine("Report configuration saved. No password was written to the portable configuration.");
        return 0;
    }

    private async Task SendAsync(string subject, string body)
    {
        var configuration = Load();
        Validate(configuration);
        using var message = new MailMessage(configuration.From, configuration.To, subject, body);
        using var client = new SmtpClient(configuration.SmtpHost, configuration.SmtpPort) { EnableSsl = configuration.EnableSsl };
        if (!string.IsNullOrWhiteSpace(configuration.CredentialUser))
        {
            var credential = CredentialStore.Read(CredentialTarget) ?? throw new InvalidOperationException("The SMTP credential is not present in Windows Credential Manager.");
            client.UseDefaultCredentials = false;
            client.Credentials = credential;
        }
        else client.UseDefaultCredentials = true;
        await client.SendMailAsync(message).ConfigureAwait(false);
    }

    private ReportConfiguration Load()
    {
        if (!File.Exists(_configurationPath)) return new ReportConfiguration();
        using var stream = File.OpenRead(_configurationPath);
        return (ReportConfiguration?)new DataContractJsonSerializer(typeof(ReportConfiguration)).ReadObject(stream) ?? new ReportConfiguration();
    }

    private void Save(ReportConfiguration configuration)
    {
        var temporary = _configurationPath + ".tmp";
        using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(ReportConfiguration)).WriteObject(stream, configuration);
        if (File.Exists(_configurationPath)) File.Replace(temporary, _configurationPath, null); else File.Move(temporary, _configurationPath);
    }

    private static void Validate(ReportConfiguration value)
    {
        if (string.IsNullOrWhiteSpace(value.SmtpHost) || value.SmtpHost.Length > 253 || value.SmtpHost.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new FormatException("A valid single-line SMTP host is required.");
        if (value.SmtpPort < 1 || value.SmtpPort > 65535) throw new FormatException("SMTP port must be from 1 through 65,535.");
        _ = new MailAddress(value.From);
        _ = new MailAddress(value.To);
    }

    private static string ReadBody(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FormatException("report send requires --body-file so report content is not exposed in process arguments.");
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length > 10 * 1024 * 1024) throw new IOException("The report body file must exist and be at most 10 MiB.");
        return File.ReadAllText(fullPath);
    }

    private static void RenderStatus(ReportConfiguration configuration) =>
        Console.WriteLine($"SMTP={configuration.SmtpHost}:{configuration.SmtpPort} SSL={configuration.EnableSsl} From={configuration.From} To={configuration.To} Credential={(string.IsNullOrWhiteSpace(configuration.CredentialUser) ? "Windows default" : configuration.CredentialUser)}");

    private static SecureString ReadPassword()
    {
        var value = new SecureString();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.RemoveAt(value.Length - 1); continue; }
            if (!char.IsControl(key.KeyChar)) value.AppendChar(key.KeyChar);
        }
        value.MakeReadOnly();
        return value;
    }
}

internal static class CredentialStore
{
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    public static void Write(string target, string userName, SecureString password)
    {
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                UserName = userName,
                CredentialBlob = pointer,
                CredentialBlobSize = password.Length * 2,
                Persist = PersistLocalMachine
            };
            if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
    }

    public static NetworkCredential? Read(string target)
    {
        if (!CredRead(target, GenericCredential, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var password = credential.CredentialBlob == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2) ?? string.Empty;
            return new NetworkCredential(credential.UserName, password);
        }
        finally { CredFree(pointer); }
    }
}
