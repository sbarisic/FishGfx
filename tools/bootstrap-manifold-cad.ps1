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
		if ($LASTEXITCODE -ne 0)
		{
			throw "Failed to clone vcpkg."
		}
	}

	& (Join-Path $vcpkgRoot "bootstrap-vcpkg.bat") -disableMetrics
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to bootstrap vcpkg."
	}
}

$manifest = Get-Content (Join-Path $nativeRoot "vcpkg.json") -Raw | ConvertFrom-Json
$baseline = $manifest.'builtin-baseline'

git -C $vcpkgRoot cat-file -e "$baseline^{commit}" 2>$null

if ($LASTEXITCODE -ne 0)
{
	git -C $vcpkgRoot fetch origin $baseline --depth 1

	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to fetch the pinned vcpkg baseline $baseline."
	}
}

Push-Location $nativeRoot

try
{
	cmake --preset windows-x64
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to configure the native CAD kernel."
	}

	cmake --build --preset ("windows-x64-" + $Configuration.ToLowerInvariant()) --parallel
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to build the native CAD kernel."
	}

	ctest --preset ("windows-x64-" + $Configuration.ToLowerInvariant())
	if ($LASTEXITCODE -ne 0)
	{
		throw "Native CAD kernel tests failed."
	}
}
finally
{
	Pop-Location
}

dotnet test (Join-Path $repository "FishGfx.ManifoldCad.Tests\FishGfx.ManifoldCad.Tests.csproj") `
	-c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0)
{
	throw "Managed CAD tests failed."
}

dotnet build (Join-Path $repository "FishGfx.Modern.sln") -c $Configuration -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0)
{
	throw "Managed solution build failed."
}
