# Signs every .exe/.dll under -Path (a file or a folder) that has no Authenticode signature yet, then verifies them.
# Used by build.ps1 for the final EXE and by the csproj target SignFilesToBundle for the files inside the single-file
# bundle: native libraries are extracted to %TEMP% at run time, and Smart App Control blocks unsigned ones.
param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [string] $SignTool,
    [Parameter(Mandatory)] [string] $Dlib,
    [Parameter(Mandatory)] [string] $Metadata
)
$ErrorActionPreference = 'Stop'

$files = if (Test-Path $Path -PathType Container) {
    Get-ChildItem -Recurse -File $Path -Include *.dll, *.exe
} else {
    Get-Item $Path
}
$unsigned = @($files | Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -eq 'NotSigned' } | ForEach-Object { $_.FullName })
if ($unsigned.Count -eq 0) {
    Write-Host "== Nothing to sign in $Path"
    return
}

Write-Host "== Signing $($unsigned.Count) file(s):"
$unsigned | ForEach-Object { Write-Host "   $_" }
& $SignTool sign /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $Dlib /dmdf $Metadata @unsigned
if ($LASTEXITCODE -ne 0) { throw "signing failed" }

foreach ($file in $unsigned) {
    $sig = Get-AuthenticodeSignature $file
    if ($sig.Status -ne 'Valid') { throw "signature of $file is not valid: $($sig.Status) $($sig.StatusMessage)" }
}
Write-Host "== Signed by $((Get-AuthenticodeSignature $unsigned[0]).SignerCertificate.Subject)"
