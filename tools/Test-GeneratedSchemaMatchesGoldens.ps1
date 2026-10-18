<#
.SYNOPSIS
    Compares the shape of generated UDT and tag-table XML against the real TIA V16 exports.

.DESCRIPTION
    The real exports under goldens\v16 are the only ground truth available for the schema, so this
    check asserts that the generated documents use the same element names, the same child order and
    the same namespaces as those files. It deliberately does not compare values: a tag name or a
    start value legitimately differs between a converted project and an unrelated Siemens project.

    Passing this check means the generated XML is shaped like a real export. It is not evidence that
    TIA Portal will import it; only an actual Openness import can show that.

.PARAMETER RealUdtRoot
    Directory holding real exported UDT files. Defaults to the CON100 export, which is the only
    supplied dataset that contains data types.

.PARAMETER RealTagRoot
    Directory holding real exported tag tables. Defaults to goldens\v16.

.PARAMETER GeneratedRoot
    Directory holding a generated package.

.EXAMPLE
    pwsh -File Test-GeneratedSchemaMatchesGoldens.ps1 -GeneratedRoot C:\temp\pkg
#>
[CmdletBinding()]
param(
    [string] $RealUdtRoot,
    [string] $RealTagRoot = 'C:\XforceMsw\goldens\v16',
    [Parameter(Mandatory)]
    [string] $GeneratedRoot
)

if (-not $RealUdtRoot) {
    $RealUdtRoot = 'C:\Users\Moatasem\Downloads\New folder (3)\PS2001-pal-software-master\CON100\PLC data types'
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Namespace {
    param([xml] $Document)

    # Namespace declarations that appear anywhere in the document, since a nested element may declare
    # one that its ancestors do not. Collected as prefix=uri pairs in sorted order.
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $queue = New-Object System.Collections.Generic.Queue[System.Xml.XmlNode]
    $queue.Enqueue($Document.DocumentElement)

    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        foreach ($attribute in $node.Attributes) {
            if ($attribute.Prefix -eq 'xmlns' -or $attribute.Name -eq 'xmlns') {
                [void] $seen.Add(('{0}={1}' -f $attribute.Prefix, $attribute.Value))
            }
        }
        foreach ($child in $node.ChildNodes) {
            if ($child -is [System.Xml.XmlElement]) { $queue.Enqueue($child) }
        }
    }

    @($seen | Sort-Object)
}

function Get-ElementOrder {
    param([System.Xml.XmlNode] $Node)

    # Returned as one joined string so a caller never has to distinguish a scalar from an array,
    # which Set-StrictMode otherwise turns into a missing .Count error.
    (@($Node.ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] } | ForEach-Object { $_.Name }) -join ' ; ')
}

function Get-SchemaShape {
    param([string] $Path)

    $document = New-Object System.Xml.XmlDocument
    $document.Load($Path)

    $members = @($document.SelectNodes("//*[local-name()='Member']"))
    $tags = @($document.SelectNodes("//*[local-name()='PlcTag']"))

    [pscustomobject]@{
        Path = $Path
        Encoding = $document.FirstChild.Encoding
        RootNamespaces = Get-Namespace -Document $document
        RootChildren = Get-ElementOrder -Node $document.DocumentElement

        # The body element, e.g. SW.Types.PlcStruct or SW.Tags.PlcTagTable.
        BodyName = @($document.DocumentElement.ChildNodes |
            Where-Object { $_ -is [System.Xml.XmlElement] -and $_.Name -notin 'Engineering', 'DocumentInfo' } |
            ForEach-Object { $_.Name })[0]

        AttributeListChildren = if ($document.SelectSingleNode("//*[local-name()='AttributeList']")) {
            Get-ElementOrder -Node $document.SelectSingleNode("//*[local-name()='AttributeList']")
        } else { @() }

        MemberCount = $members.Count
        TagCount = $tags.Count

        # Distinct child sequences a Member can carry, with counts. Real files use a small set.
        # Distinct child sequences a Member can carry, as an ordered set, so the generated file need
        # only use signatures the real files also use, not the same ones with the same frequency.
        MemberSignatures = @($members | ForEach-Object { Get-ElementOrder -Node $_ } |
            Sort-Object -Unique)

        # Same idea for the AttributeList of each tag row.
        TagAttributeLists = @($tags | ForEach-Object {
            $attributeList = $_.SelectSingleNode("*[local-name()='AttributeList']")
            if ($attributeList) { Get-ElementOrder -Node $attributeList } else { '(none)' }
        } | Sort-Object -Unique)

        SectionName = if ($document.SelectSingleNode("//*[local-name()='Section']")) {
            $document.SelectSingleNode("//*[local-name()='Section']").Name
        } else { '(none)' }
    }
}

Write-Host "Real UDTs    : $RealUdtRoot"
Write-Host "Real tags    : $RealTagRoot"
Write-Host "Generated    : $GeneratedRoot"
Write-Host ''

if (-not (Test-Path -LiteralPath $RealUdtRoot)) { throw "Real UDT root '$RealUdtRoot' does not exist." }
if (-not (Test-Path -LiteralPath $RealTagRoot)) { throw "Real tag root '$RealTagRoot' does not exist." }
if (-not (Test-Path -LiteralPath $GeneratedRoot)) { throw "Generated root '$GeneratedRoot' does not exist." }

$realUdt = @(Get-ChildItem -LiteralPath $RealUdtRoot -Recurse -File -Filter '*.xml')
$generatedUdt = @(Get-ChildItem -LiteralPath $GeneratedRoot -Recurse -File -Filter '*.xml' |
    Where-Object { $_.FullName -match '[\\/]UDTs[\\/]' })

# Tag tables only: block exports also live under the goldens tree.
$realTag = @(Get-ChildItem -LiteralPath $RealTagRoot -Recurse -File -Filter '*.xml' |
    Where-Object { $_.FullName -match 'tagtables|PLC tags' })
$generatedTag = @(Get-ChildItem -LiteralPath $GeneratedRoot -Recurse -File -Filter '*.xml' |
    Where-Object { $_.FullName -match '[\\/]Tags[\\/]' })

Write-Host ("Real UDT files     : {0}" -f $realUdt.Count)
Write-Host ("Generated UDT files: {0}" -f $generatedUdt.Count)
Write-Host ("Real tag tables    : {0}" -f $realTag.Count)
Write-Host ("Generated tag tbls : {0}" -f $generatedTag.Count)
Write-Host ''

$failures = New-Object System.Collections.Generic.List[string]

function Compare-Group {
    param(
        [string] $Label,
        [object[]] $RealFiles,
        [object[]] $GeneratedFiles,
        [string[]] $Properties
    )

    if (@($RealFiles).Count -eq 0 -or @($GeneratedFiles).Count -eq 0) {
        $failures.Add("$Label : nothing to compare (real=$(@($RealFiles).Count) generated=$(@($GeneratedFiles).Count))")
        return
    }

    # Union of everything the real files do, per property.
    $realUnion = @{}
    foreach ($file in $RealFiles) {
        $shape = Get-SchemaShape -Path $file.FullName
        foreach ($property in $Properties) {
            if (-not $realUnion.ContainsKey($property)) {
                $realUnion[$property] = New-Object System.Collections.Generic.HashSet[string]
            }
            foreach ($value in (ConvertTo-ValueList -Value $shape.$property)) {
                [void] $realUnion[$property].Add($value)
            }
        }
    }

    foreach ($file in $GeneratedFiles) {
        $shape = Get-SchemaShape -Path $file.FullName
        foreach ($property in $Properties) {
            foreach ($value in (ConvertTo-ValueList -Value $shape.$property)) {
                if (-not $realUnion[$property].Contains($value)) {
                    $failures.Add("$Label : $($file.Name) has $($property) = '$value', which no real export uses")
                }
            }
        }
    }

    Write-Host ("{0}: compared {1} generated against {2} real" -f $Label, @($GeneratedFiles).Count, @($RealFiles).Count)
}

function ConvertTo-ValueList {
    param($Value)

    # A set-valued property (member signatures, namespaces) becomes several comparable values;
    # a scalar property becomes one.
    if ($null -eq $Value) { return @('(none)') }
    if ($Value -is [string]) { return @($Value) }
    return @($Value | ForEach-Object { [string] $_ })
}

$udtProperties = @(
    'RootNamespaces', 'RootChildren', 'BodyName', 'AttributeListChildren', 'SectionName', 'MemberSignatures'
)
Compare-Group -Label 'UDT' -RealFiles $realUdt -GeneratedFiles $generatedUdt -Properties $udtProperties

$tagProperties = @(
    'RootNamespaces', 'RootChildren', 'BodyName', 'AttributeListChildren', 'TagAttributeLists'
)
Compare-Group -Label 'Tag table' -RealFiles $realTag -GeneratedFiles $generatedTag -Properties $tagProperties

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "FAILED: $($failures.Count) schema difference(s)" -ForegroundColor Red
    foreach ($failure in $failures) { Write-Host "  - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host 'PASSED: generated XML uses only element names, ordering and namespaces seen in the real exports.' -ForegroundColor Green
Write-Host 'Note: this is a shape comparison, not proof that TIA Portal will import the files.'