# Builds out/StillHere.exe (single file with the .NET runtime inside) and optionally signs it with
# Azure Artifact Signing (Trusted Signing). Signing is enabled by these environment variables:
#   ARTIFACT_SIGNING_ENDPOINT   endpoint of the account's region, e.g. https://weu.codesigning.azure.net
#   ARTIFACT_SIGNING_ACCOUNT    signing account name
#   ARTIFACT_SIGNING_PROFILE    certificate profile name
#   AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET   app registration with the role
#                               "Artifact Signing Certificate Profile Signer"
# Without ARTIFACT_SIGNING_ACCOUNT nothing is signed (local build).
param(
    [Parameter(Mandatory)] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."
$project = Join-Path $root 'src\StillHere\StillHere.csproj'
$out = Join-Path $root 'out'
$publish = Join-Path $out 'publish\win-x64'
$target = Join-Path $out 'StillHere.exe' # no version in the name: a new download replaces the old one

function Setting([string] $name) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value) -or $value -like '$(*)') { return $null }  # an undefined pipeline variable arrives as "$(Name)"
    return $value
}

$signArgs = @()
if (Setting 'ARTIFACT_SIGNING_ACCOUNT') {
    foreach ($name in 'ARTIFACT_SIGNING_ENDPOINT', 'ARTIFACT_SIGNING_PROFILE', 'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_SECRET') {
        if (-not (Setting $name)) { throw "ARTIFACT_SIGNING_ACCOUNT is set, but $name is missing" }
    }
    Write-Host "== Downloading the signing tools (SignTool + Azure Artifact Signing plugin from NuGet)"
    $tools = Join-Path $out 'signing-tools'
    New-Item -ItemType Directory -Force $tools | Out-Null
    $nuget = Join-Path $tools 'nuget.exe'
    if (-not (Test-Path $nuget)) { Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget }
    foreach ($package in 'Microsoft.Windows.SDK.BuildTools', 'Microsoft.ArtifactSigning.Client') {
        & $nuget install $package -ExcludeVersion -NonInteractive -Verbosity quiet -OutputDirectory $tools
        if ($LASTEXITCODE -ne 0) { throw "nuget install $package failed" }
    }
    $signTool = Get-ChildItem -Recurse "$tools\Microsoft.Windows.SDK.BuildTools" -Filter signtool.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    $dlib = Get-ChildItem -Recurse "$tools\Microsoft.ArtifactSigning.Client" -Filter Azure.CodeSigning.Dlib.dll | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $signTool -or -not $dlib) { throw "signtool.exe or Azure.CodeSigning.Dlib.dll (x64) not found" }
    $metadata = Join-Path $tools 'metadata.json'
    @{
        Endpoint = Setting 'ARTIFACT_SIGNING_ENDPOINT'
        CodeSigningAccountName = Setting 'ARTIFACT_SIGNING_ACCOUNT'
        CertificateProfileName = Setting 'ARTIFACT_SIGNING_PROFILE'
        ExcludeCredentials = @('ManagedIdentityCredential', 'WorkloadIdentityCredential', 'SharedTokenCacheCredential', 'VisualStudioCredential',
            'VisualStudioCodeCredential', 'AzureCliCredential', 'AzurePowerShellCredential', 'AzureDeveloperCliCredential', 'InteractiveBrowserCredential')
    } | ConvertTo-Json | ForEach-Object { [IO.File]::WriteAllText($metadata, $_) } # no BOM - the plugin does not read it

    $signScript = Join-Path $PSScriptRoot 'sign-files.ps1'
    $signParams = @{ SignTool = $signTool.FullName; Dlib = $dlib.FullName; Metadata = $metadata }
    # The csproj target SignFilesToBundle signs the unsigned DLLs before they go into the single-file bundle.
    $signArgs = @("-p:SignFilesScript=$signScript", "-p:SignToolExe=$($signTool.FullName)", "-p:SignDlib=$($dlib.FullName)", "-p:SignMetadata=$metadata")
} else {
    Write-Host "== ARTIFACT_SIGNING_ACCOUNT not set – nothing is signed"
}

Write-Host "== Publishing Still Here $Version (win-x64, self-contained single file)"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force $out | Out-Null
dotnet publish $project -c Release -r win-x64 -o $publish -nologo "-p:Version=$Version" @signArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Copy-Item (Join-Path $publish 'StillHere.exe') $target -Force

if ($signArgs.Count -gt 0) {
    & $signScript -Path $target @signParams
}

$hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Encoding ascii "$target.sha256" "$hash  $(Split-Path -Leaf $target)"
Write-Host "== Done: $target ($([math]::Round((Get-Item $target).Length / 1MB)) MB, sha256 $hash)"
