<#
SCRIPT-METADATA:
  Id: purge-temp-extensions
  Name: Purge files by extension under temp
  Category: Maintenance
  Description: Deletes files with the specified extensions under a folder rooted in C:\\Temp. Reports folder size before and after, plus C: drive space.
  Parameters:
    - Name: TargetPath
      Type: String
      Required: true
      DisplayName: Target folder
      HelpText: Folder to purge; must start with C:\\Temp
    - Name: Extensions
      Type: String
      Required: true
      DisplayName: Extensions (comma-separated)
      HelpText: One or more extensions to delete, e.g. tmp,log,bak
END-SCRIPT-METADATA
#>
param(
  [Parameter(Mandatory=$true)][string]$TargetPath,
  [Parameter(Mandatory=$true)][string]$Extensions
)

if(-not ($TargetPath -like 'C:\\Temp*')){ Write-Error 'Refusing to operate outside C:\\Temp'; exit 1 }
if(!(Test-Path -Path $TargetPath -PathType Container)){
  Write-Error "TargetPath not found: $TargetPath"; exit 1
}

function Get-FolderBytes([string]$Path){
  (Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
}

try{
  $before = Get-FolderBytes -Path $TargetPath
  $exts = $Extensions.Split(',') | ForEach-Object { $_.Trim().TrimStart('.') } | Where-Object { $_ }
  if($exts.Count -eq 0){ Write-Error 'No extensions specified'; exit 1 }

  $deleted = 0; $errors = 0
  foreach($ext in $exts){
    Get-ChildItem -LiteralPath $TargetPath -Recurse -File -Filter "*.${ext}" -ErrorAction SilentlyContinue | ForEach-Object {
      try{ Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop; $deleted++ }
      catch { $errors++; Write-Warning "Failed deleting $($_.FullName): $($_.Exception.Message)" }
    }
  }
  $after = Get-FolderBytes -Path $TargetPath
  $drive = Get-PSDrive -Name 'C' -ErrorAction SilentlyContinue
  $line = if($null -ne $drive){
    "C: Total={0:N0} GB Free={1:N0} GB Used={2:N0} GB" -f (($drive.Used+$drive.Free)/1GB), ($drive.Free/1GB), ($drive.Used/1GB)
  }
  Write-Output ("Deleted files: {0}, Size before: {1:N0} KB, after: {2:N0} KB" -f $deleted, [math]::Round($before/1kb), [math]::Round($after/1kb))
  if($line){ Write-Output $line }
  exit 0
}
catch{
  Write-Error $_.Exception.Message; exit 1
}
