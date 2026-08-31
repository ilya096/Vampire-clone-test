$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot '.utmp\BossWallSelection.Validation'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$mathFile = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets\Editor\BossWallSelectionMath.cs'))
$checksFile = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'validation\BossWallSelectionChecks.cs'))
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9.0</LangVersion>
  </PropertyGroup>
  <ItemGroup><Compile Include="$mathFile" /><Compile Include="$checksFile" /></ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $outputRoot 'Validation.csproj') -Value $projectText -Encoding utf8
Set-Content -LiteralPath (Join-Path $outputRoot 'NuGet.Config') -Value '<configuration><packageSources><clear /></packageSources></configuration>' -Encoding utf8
& dotnet run --project (Join-Path $outputRoot 'Validation.csproj') --configuration Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw "Selection validation failed: $LASTEXITCODE" }
