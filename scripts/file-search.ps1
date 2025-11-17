<#
SCRIPT-METADATA:
  Id: file-search
  Name: File search
  Category: Files
  Description: Search for files matching a pattern recursively. Prints full path, size in KB and last modified date.
  Parameters:
    - Name: Folder
      Type: String
      Required: true
      DisplayName: Folder path
      HelpText: Folder to search within
    - Name: Pattern
      Type: String
      Required: true
      DisplayName: File name or wildcard pattern
      HelpText: Example: *.log or report*.csv
END-SCRIPT-METADATA
#>
param(
  [Parameter(Mandatory=$true)][string]$Folder,
  [Parameter(Mandatory=$true)][string]$Pattern
)
if(!(Test-Path -Path $Folder -PathType Container)){
  Write-Error "Folder does not exist: $Folder"; exit 1
}
$items = Get-ChildItem -Path $Folder -File -Recurse -Filter $Pattern -ErrorAction SilentlyContinue
if(-not $items){
  # Fallback for complex wildcard expressions
  $items = Get-ChildItem -Path $Folder -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -like $Pattern }
}
$items = $items | Sort-Object FullName
foreach($i in $items){
  $kb = [math]::Round([double]$i.Length/1kb,2)
  $dt = $i.LastWriteTime
  Write-Output ("{0}`t{1} KB`t{2:o}" -f $i.FullName,$kb,$dt)
}
