<#
SCRIPT-METADATA:
  Id: clean-dotnet-artifacts
  Name: Clean .NET build artifacts
  Category: Maintenance
  Description: Recursively finds project folders (containing a .csproj) under the given root and deletes their bin/ and obj/ subfolders.
  Parameters:
    - Name: RootPath
      Type: String
      Required: true
      DisplayName: Root folder
      HelpText: Starting folder to scan for .csproj files.
END-SCRIPT-METADATA
#>
param(
  [Parameter(Mandatory=$true)][string]$RootPath
)

if(!(Test-Path -Path $RootPath -PathType Container)){
  Write-Error "RootPath not found: $RootPath"; exit 1
}

$ErrorActionPreference = 'Stop'

try{
  $projDirs = Get-ChildItem -Path $RootPath -Recurse -File -Filter *.csproj -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty DirectoryName -Unique
  if(-not $projDirs){
    Write-Output "No .csproj directories found under $RootPath"
    exit 0
  }
  $totalBin = 0; $totalObj = 0; $errors = 0
  foreach($dir in $projDirs){
    $bin = Join-Path $dir 'bin'
    $obj = Join-Path $dir 'obj'
    if(Test-Path $bin){
      try{ Remove-Item -LiteralPath $bin -Recurse -Force -ErrorAction Stop; $totalBin++ ; Write-Output "Deleted: $bin" }
      catch { $errors++; Write-Warning "Failed deleting $bin: $($_.Exception.Message)" }
    }
    if(Test-Path $obj){
      try{ Remove-Item -LiteralPath $obj -Recurse -Force -ErrorAction Stop; $totalObj++ ; Write-Output "Deleted: $obj" }
      catch { $errors++; Write-Warning "Failed deleting $obj: $($_.Exception.Message)" }
    }
  }
  Write-Output "Summary: deleted bin=$totalBin, obj=$totalObj, errors=$errors"
  exit 0
}
catch{
  Write-Error $_.Exception.Message; exit 1
}
