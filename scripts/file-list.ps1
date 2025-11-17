<#
SCRIPT-METADATA:
  Id: file-list
  Name: File listing
  Category: Files
  Description: List all files in the specified folder, optionally recursively. For each file prints full path and size in KB. Sorted alphabetically.
  Parameters:
    - Name: Folder
      Type: String
      Required: true
      DisplayName: Folder path
      HelpText: Root folder to list
    - Name: Recursive
      Type: Bool
      Required: false
      Default: "false"
      DisplayName: Include subfolders
END-SCRIPT-METADATA
#>
param(
  [Parameter(Mandatory=$true)][string]$Folder,
  [bool]$Recursive=$false
)
if(!(Test-Path -Path $Folder -PathType Container)){
  Write-Error "Folder does not exist: $Folder"; exit 1
}
$items = Get-ChildItem -Path $Folder -File -Recurse:$Recursive -ErrorAction Stop | Sort-Object FullName
foreach($i in $items){
  $kb = [math]::Round([double]$i.Length/1kb,2)
  Write-Output ("{0}`t{1} KB" -f $i.FullName,$kb)
}
