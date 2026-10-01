<#
  Build a release zip that needs no .NET SDK: the prebuilt module DLL next to "Start Here.cmd" and the scripts.
  setup.ps1 uses the prebuilt DLL when it finds it in the zip's root folder.

  usage:  powershell -ExecutionPolicy Bypass -File scripts\package.ps1   ->  dist\vrcft-steam-frame-<version>.zip
#>
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Find-DotnetSdk10
if (-not $dotnet) { throw ".NET 10 SDK not found" }
$vrcft = Find-VrcftDir
if (-not $vrcft) { throw "VRCFaceTracking not found (its DLLs are needed to build)" }

& $dotnet build "$root\module" -c Release --nologo -v q "-p:VrcftDir=$vrcft"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$ver = ([xml](Get-Content "$root\module\SteamFrameVRCFTModule.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "vrcft-steam-frame-$ver"
$stage = Join-Path $root "dist\$name"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$stage\scripts", "$stage\tools" | Out-Null
Copy-Item "$root\module\bin\Release\net10.0\$script:ModuleDll" $stage
Copy-Item "$root\Start Here.cmd", "$root\README.md", "$root\LICENSE" $stage
Copy-Item "$root\scripts\common.ps1", "$root\scripts\setup.ps1", "$root\scripts\doctor.ps1", "$root\scripts\headset-setup.sh",
          "$root\scripts\headset-install.sh", "$root\scripts\headset-run.sh", "$root\scripts\frameeyeosc.service" "$stage\scripts"
Copy-Item "$root\tools\tune.py", "$root\tools\overlay.py" "$stage\tools"
$zip = Join-Path $root "dist\$name.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "created $zip"
