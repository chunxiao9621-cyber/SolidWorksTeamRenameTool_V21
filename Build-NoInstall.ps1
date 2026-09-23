param()

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $root "bin\Release"
$exe = Join-Path $buildDir "SolidWorksTeamRenameTool.exe"
$compileExe = Join-Path $root "SolidWorksTeamRenameTool.build.exe"

function Find-FirstExistingFile {
    param(
        [string[]]$Candidates,
        [string]$Pattern,
        [string[]]$SearchRoots
    )

    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    foreach ($searchRoot in $SearchRoots) {
        if ($searchRoot -and (Test-Path -LiteralPath $searchRoot)) {
            $found = Get-ChildItem -LiteralPath $searchRoot -Recurse -Filter $Pattern -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($found) {
                return $found.FullName
            }
        }
    }

    return $null
}

function Require-File {
    param(
        [string]$Path,
        [string]$Name
    )

    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
        throw "Cannot find $Name. Please install SOLIDWORKS API SDK/Interop, or copy $Name into this folder."
    }

    return $Path
}

function Copy-IfDifferent {
    param(
        [string]$Source,
        [string]$Destination
    )

    $src = (Resolve-Path -LiteralPath $Source).Path
    $dst = $Destination
    if (Test-Path -LiteralPath $Destination) {
        $dst = (Resolve-Path -LiteralPath $Destination).Path
    }

    if ([string]::Equals($src, $dst, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "  Already in output: $Destination"
        return
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

$csc = Find-FirstExistingFile `
    -Candidates @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    ) `
    -Pattern "csc.exe" `
    -SearchRoots @("$env:WINDIR\Microsoft.NET")

$interopRoots = @(
    $root,
    (Join-Path $root "lib"),
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL",
    "$env:ProgramFiles\SOLIDWORKS Corp",
    "${env:ProgramFiles(x86)}\SOLIDWORKS Corp"
)

$sldworks = Find-FirstExistingFile `
    -Candidates @(
        (Join-Path $root "SolidWorks.Interop.sldworks.dll"),
        (Join-Path $root "lib\SolidWorks.Interop.sldworks.dll")
    ) `
    -Pattern "SolidWorks.Interop.sldworks.dll" `
    -SearchRoots $interopRoots

$swconst = Find-FirstExistingFile `
    -Candidates @(
        (Join-Path $root "SolidWorks.Interop.swconst.dll"),
        (Join-Path $root "lib\SolidWorks.Interop.swconst.dll")
    ) `
    -Pattern "SolidWorks.Interop.swconst.dll" `
    -SearchRoots $interopRoots

$csc = Require-File $csc "csc.exe"
$sldworks = Require-File $sldworks "SolidWorks.Interop.sldworks.dll"
$swconst = Require-File $swconst "SolidWorks.Interop.swconst.dll"

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$sources = @(
    "Properties\AssemblyInfo.cs",
    "Program.cs",
    "DialogAutoConfirmer.cs",
    "RenameTask.cs",
    "AssemblyComponentReader.cs",
    "NamingRuleConfig.cs",
    "NamingRuleV2.cs",
    "UserSettingsStore.cs",
    "RenameExecutor.cs",
    "RenameDiagnosticRunner.cs",
    "GtkControls.cs",
    "RenameForm.cs"
) | ForEach-Object { Join-Path $root $_ }

Write-Host "Using:"
Write-Host "  csc: $csc"
Write-Host "  sldworks: $sldworks"
Write-Host "  swconst: $swconst"
Write-Host ""

& $csc `
    /nologo `
    /codepage:65001 `
    /utf8output `
    /target:winexe `
    /platform:x64 `
    /nowin32manifest `
    /optimize+ `
    /debug- `
    "/out:$compileExe" `
    "/reference:System.dll" `
    "/reference:System.Core.dll" `
    "/reference:System.Data.dll" `
    "/reference:System.Drawing.dll" `
    "/reference:System.Windows.Forms.dll" `
    "/reference:System.Xml.dll" `
    "/reference:System.Xml.Linq.dll" `
    "/reference:Microsoft.CSharp.dll" `
    "/reference:Microsoft.VisualBasic.dll" `
    "/reference:$sldworks" `
    "/reference:$swconst" `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "C# compile failed."
}

Copy-Item -LiteralPath $compileExe -Destination $exe -Force
Remove-Item -LiteralPath $compileExe -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Build succeeded:"
Write-Host "  $exe"

Write-Host ""
Write-Host "Copying SOLIDWORKS interop dependencies to output folder..."
Copy-IfDifferent -Source $sldworks -Destination (Join-Path $buildDir "SolidWorks.Interop.sldworks.dll")
Copy-IfDifferent -Source $swconst -Destination (Join-Path $buildDir "SolidWorks.Interop.swconst.dll")

Write-Host ""
Write-Host "No install or registry write is required."
