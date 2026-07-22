param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$vcpkgRoot = Join-Path $repository ".tools\vcpkg"
$nativeRoot = Join-Path $repository "FishGfx.CadKernel.Native"

if (-not (Test-Path (Join-Path $vcpkgRoot "vcpkg.exe")))
{
	New-Item -ItemType Directory -Path (Split-Path -Parent $vcpkgRoot) -Force | Out-Null

	if (-not (Test-Path (Join-Path $vcpkgRoot ".git")))
	{
		git clone --depth 1 https://github.com/microsoft/vcpkg.git $vcpkgRoot
	}

	& (Join-Path $vcpkgRoot "bootstrap-vcpkg.bat") -disableMetrics
}

Push-Location $nativeRoot

try
{
	cmake --preset windows-x64
	cmake --build --preset ("windows-x64-" + $Configuration.ToLowerInvariant()) --parallel
	ctest --preset ("windows-x64-" + $Configuration.ToLowerInvariant())
}
finally
{
	Pop-Location
}

dotnet test (Join-Path $repository "FishGfx.ManifoldCad.Tests\FishGfx.ManifoldCad.Tests.csproj") `
	-c $Configuration -p:Platform=x64
dotnet build (Join-Path $repository "FishGfx.Modern.sln") -c $Configuration -p:Platform=x64 --no-restore
