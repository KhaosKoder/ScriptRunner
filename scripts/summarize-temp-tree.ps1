<#
SCRIPT-METADATA:
  Id: summarize-temp-tree
  Name: Summarize temp folder structure
  Category: Maintenance
  Description: Builds an ASCII tree for folders under C:\\Temp, showing per-folder size and a summary of files by extension. Also outputs C: drive total, free and available space.
  Parameters:
    - Name: RootPath
      Type: String
      Required: false
      Default: "C:\\Temp"
      DisplayName: Temp root
      HelpText: Root folder to analyze; defaults to C:\\Temp.
END-SCRIPT-METADATA
#>
param(
  [string]$RootPath = 'C:\\Temp'
)

if(!(Test-Path -Path $RootPath -PathType Container)){
  Write-Error "RootPath not found: $RootPath"; exit 1
}

function Get-FolderSize([string]$Path){
  $len = 0
  Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $len += $_.Length }
  return $len
}

function Get-ExtSummary([string]$Path){
  $group = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
    Group-Object { $_.Extension.ToLowerInvariant() } | ForEach-Object {
      [PSCustomObject]@{ Ext = $_.Name; Count = $_.Count; Size = ($_.Group | Measure-Object -Property Length -Sum).Sum }
    }
  return $group | Sort-Object Ext
}

function Write-Tree($Path, $Prefix){
  $dirs = Get-ChildItem -LiteralPath $Path -Directory -ErrorAction SilentlyContinue | Sort-Object Name
  for($i=0; $i -lt $dirs.Count; $i++){
    $d = $dirs[$i]
    $last = ($i -eq $dirs.Count - 1)
    $branch = if($last){ '??' } else { '??' }
    $size = Get-FolderSize -Path $d.FullName
    Write-Output ("{0}{1} {2} ({3:N0} KB)" -f $Prefix,$branch,$d.Name, [math]::Round($size/1kb))
    Write-Tree -Path $d.FullName -Prefix ($Prefix + (if($last){ '  ' } else { '? ' }))
  }
}

Write-Output ("Folder tree for {0}" -f $RootPath)
Write-Tree -Path $RootPath -Prefix ''

Write-Output ''
Write-Output 'Extension summary:'
$sum = Get-ExtSummary -Path $RootPath
foreach($s in $sum){ Write-Output ("{0,-8} {1,8} files {2,12:N0} KB" -f ($s.Ext -replace '^\.?','.'), $s.Count, [math]::Round($s.Size/1kb)) }

Write-Output ''
$drive = Get-PSDrive -Name 'C' -ErrorAction SilentlyContinue
if($null -ne $drive){
  Write-Output ("C: Total={0:N0} GB Free={1:N0} GB Used={2:N0} GB" -f ($drive.Used+$drive.Free)/1GB, $drive.Free/1GB, $drive.Used/1GB)
}
exit 0
