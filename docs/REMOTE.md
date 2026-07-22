# Remote administration

Remote mode manages one Windows 11 x64 DNS host at a time and only enables parameter
sets that declare `ComputerName` upstream.

## Required configuration

- Working name resolution.
- Kerberos in a domain, or WinRM HTTPS with a system-trusted server certificate.
- Administrative authorization for the current identity or a native cmdlet credential.
- SMB administrative-share access when temporary module staging is required.
- Windows PowerShell 5.1 and Task Scheduler on the target.

Select **WinRM HTTPS** when the target has a certificate-validated HTTPS listener.
Leaving it clear uses normal Windows remoting negotiation, which should resolve to
Kerberos in a domain. The connection preflight validates DNS, Windows version and
architecture, the administrator token, PowerShell, Task Scheduler, execution policy,
Windows Update service, reboot state, module version, clock, free space, and SMB copy
access. It never installs or reconfigures those prerequisites.

The application does not add TrustedHosts, disable certificate checks, enable Basic
authentication, open firewall profiles, or change remote UAC filtering.

## Temporary module

Remote download/install uses an upstream scheduled job that must import the module on
the target. If 2.2.1.5 is absent, the GUI offers to copy it to:

```text
C:\Program Files\WindowsPowerShell\Modules\PSWindowsUpdate\2.2.1.5
```

The GUI verifies the remote DLL hash and writes `.pswugui-owned`. It will not overwrite
a non-matching existing directory. Cleanup requires the exact ownership token and path.
Use **Clean temporary remote module** only after scheduled/reboot-recursion jobs finish.

## Credentials

`Get-WindowsUpdate` has no native `Credential` parameter; it therefore uses the current
Windows identity remotely. Commands such as `Invoke-WUJob` expose their own in-memory
credential prompt through the Advanced page.
