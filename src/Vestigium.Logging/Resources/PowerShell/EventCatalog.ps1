#Requires -Version 5.1
<#
  Generic Event ID catalog.

  .\EventCatalog.ps1 -Action Generate
  .\EventCatalog.ps1 -Action Get -EventId 0
  .\EventCatalog.ps1 -Action Get -FullName System.IO.FileNotFoundException
#>
[CmdletBinding()]
param(
    [ValidateSet('Generate','Get','Add','Set','Remove','List','Renumber')]
    [string]$Action = 'Generate',
    [string]$Root = 'C:\IT\EventCatalog',
    [int]$EventId,
    [string]$FullName,
    [string]$EventName,
    [string]$Namespace,
    [string]$Category,
    [string]$Subcategory,
    [string]$Description,
    [string]$Severity = 'Error',
    [string]$Kind = 'Custom',
    [string]$ShardId,
    [switch]$All,
    [switch]$Hard
)

Import-Module (Join-Path $PSScriptRoot 'EventCatalog.psm1') -Force

switch ($Action) {
    'Generate' { Initialize-EventCatalog -Root $Root }
    'Get' {
        if ($EventId)      { Get-EventCatalogEntry -Root $Root -EventId $EventId }
        elseif ($FullName) { Get-EventCatalogEntry -Root $Root -FullName $FullName }
        elseif ($EventName){ Get-EventCatalogEntry -Root $Root -EventName $EventName }
        else { Get-EventCatalog -Root $Root }
    }
    'Add' {
        Add-EventCatalogEntry -Root $Root -EventName $EventName -FullName $FullName `
            -Namespace $Namespace -Category $Category -Subcategory $Subcategory `
            -Description $Description -Severity $Severity -Kind $Kind
    }
    'Set' {
        $argsSet = @{ Root = $Root; EventId = $EventId }
        if ($EventName)   { $argsSet.EventName    = $EventName }
        if ($Category)    { $argsSet.Category     = $Category }
        if ($Subcategory) { $argsSet.Subcategory  = $Subcategory }
        if ($Description) { $argsSet.Description  = $Description }
        if ($Severity)    { $argsSet.Severity     = $Severity }
        if ($Kind)        { $argsSet.Kind         = $Kind }
        Set-EventCatalogEntry @argsSet
    }
    'Remove' { Remove-EventCatalogEntry -Root $Root -EventId $EventId -Hard:$Hard }
    'List'     { Get-EventCatalog -Root $Root -ShardId $ShardId -All:$All }
    'Renumber' { Update-EventCatalogRanges -Root $Root }
}
