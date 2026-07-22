[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Executable,
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$exe = Get-Item -LiteralPath $Executable
$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$repository = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'neko-no-otoko/PSWindowsUpdateGUI' }
$namespace = 'https://github.com/' + $repository + '/releases/tag/v' + $Version + '/sbom/' + [guid]::NewGuid().ToString('N')
$document = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "PSWindowsUpdateGUI-$Version"
    documentNamespace = $namespace
    creationInfo = [ordered]@{
        created = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        creators = @('Tool: PSWindowsUpdateGUI-New-Sbom.ps1')
    }
    packages = @(
        [ordered]@{
            name = 'PSWindowsUpdateGUI'
            SPDXID = 'SPDXRef-Package-GUI'
            versionInfo = $Version
            downloadLocation = 'NOASSERTION'
            filesAnalyzed = $true
            licenseConcluded = 'MIT'
            licenseDeclared = 'MIT'
            checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $hash })
        }
    )
    files = @([ordered]@{
        fileName = $exe.Name
        SPDXID = 'SPDXRef-File-Executable'
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $hash })
        licenseConcluded = 'MIT'
    })
    relationships = @(
        [ordered]@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = 'SPDXRef-Package-GUI' },
        [ordered]@{ spdxElementId = 'SPDXRef-Package-GUI'; relationshipType = 'CONTAINS'; relatedSpdxElement = 'SPDXRef-File-Executable' }
    )
}

$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
