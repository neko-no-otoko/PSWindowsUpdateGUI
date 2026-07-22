# Troubleshooting

## Startup integrity failure

Redownload the EXE and verify its release checksum. Security software that modifies or
quarantines embedded content can also trigger this failure. Do not bypass verification.

## Remote scan fails

Confirm DNS, `Test-WSMan <host>`, Kerberos/HTTPS trust, administrator authorization,
and that the current Windows identity is accepted by the target. The GUI deliberately
does not repair remoting configuration.

## Remote install does not run

Check the target Task Scheduler, the temporary module directory, `Get-WUJob`, Windows
PowerShell execution policy, and `%TEMP%\PSWindowsUpdate.log` on the target. Keep the
temporary module until the scheduled job and any reboot recursion finish.

## AllSigned or Restricted environment

The GUI imports the verified signed DLL directly instead of executing the module
wrapper script. Native remote tasks can still be subject to the target's execution
policy; diagnose this before changing organizational policy.

## Portable state warning

Move the EXE to a writable folder if settings/log persistence is desired. The GUI will
not silently write to another profile directory.

## Upstream behavior

Reproduce suspected module issues in an elevated Windows PowerShell 5.1 session using
PSWindowsUpdate 2.2.1.5, then consult the upstream issue tracker.
