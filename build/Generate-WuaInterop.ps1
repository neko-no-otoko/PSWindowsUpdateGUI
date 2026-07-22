[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

if (-not ('PSWindowsUpdateGui.Build.TypeLibLoader' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PSWindowsUpdateGui.Build
{
    public static class TypeLibLoader
    {
        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void LoadTypeLibEx(
            string file,
            int registrationKind,
            [MarshalAs(UnmanagedType.Interface)] out object typeLibrary);
    }

    public sealed class ImporterNotifySink : ITypeLibImporterNotifySink
    {
        public void ReportEvent(ImporterEventKind eventKind, int eventCode, string eventMessage) { }
        public Assembly ResolveRef(object typeLibrary) { return null; }
    }
}
'@
}

$typeLibrary = $null
[PSWindowsUpdateGui.Build.TypeLibLoader]::LoadTypeLibEx(
    (Join-Path $env:SystemRoot 'System32\wuapi.dll'),
    2,
    [ref] $typeLibrary)

$previousDirectory = [Environment]::CurrentDirectory
try {
    [Environment]::CurrentDirectory = $outputDirectory
    $converter = [System.Runtime.InteropServices.TypeLibConverter]::new()
    $assembly = $converter.ConvertTypeLibToAssembly(
        $typeLibrary,
        (Split-Path -Leaf $resolvedOutput),
        [System.Runtime.InteropServices.TypeLibImporterFlags]::SafeArrayAsSystemArray,
        [PSWindowsUpdateGui.Build.ImporterNotifySink]::new(),
        $null,
        $null,
        'WUApiLib',
        $null)
    $assembly.Save((Split-Path -Leaf $resolvedOutput))
}
finally {
    [Environment]::CurrentDirectory = $previousDirectory
    if ($null -ne $typeLibrary -and [Runtime.InteropServices.Marshal]::IsComObject($typeLibrary)) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($typeLibrary)
    }
}

if (-not (Test-Path -LiteralPath $resolvedOutput)) {
    throw "WUAPI interop generation did not produce $resolvedOutput"
}
