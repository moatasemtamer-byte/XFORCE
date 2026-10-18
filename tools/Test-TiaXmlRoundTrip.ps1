#Requires -Version 5.1
<#
.SYNOPSIS
    Round-trip validation harness for REAL Siemens TIA Portal XML exports.

.DESCRIPTION
    Demonstrates, on real data rather than on claims:

        TIA export -> parse -> internal model -> reconstruct -> compare

    Two object families are covered:

      * SW.Types.PlcStruct        (PLC data type / UDT, project-object export)
      * SW.Tags.PlcTagTable       (PLC tag table)

    The internal model is a plain PowerShell object graph. The reconstruction step
    re-emits the TIA-shaped XML from that model alone, and the comparison step then
    re-parses the reconstruction and diffs it field by field against the model that
    produced it. Anything that cannot survive that path is reported as information
    lost, which is the whole point of the exercise.

.NOTES
    Two DIFFERENT TIA UDT XML shapes exist and this harness handles the project-object
    one used by a workspace export:

      project-object export   SW.Types.PlcStruct
                               AttributeList/Interface holds real child elements
                               UDT name is AttributeList/Name

      library type export     SW.Types.PlcTypeLibraryTypeVersion
                               the interface arrives as an escaped XML string

    Confusing the two produces a parse failure that looks like data corruption but is
    only a shape mismatch.
#>
[CmdletBinding()]
param(
    [string]$Con100Root = 'C:\Users\Moatasem\Downloads\New folder (3)\PS2001-pal-software-master\CON100',
    [string]$OutputDir = 'C:\XforceMsw\goldens\v16\roundtrip',
    [string[]]$AdditionalTagDir = @()
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$ifaceNs = 'http://www.siemens.com/automation/Openness/SW/Interface/v4'

function Read-Utf8 {
    param([string]$Path)
    # Read bytes and decode explicitly, stripping the BOM. Get-Content would guess an
    # ANSI code page and silently mangle the non-ASCII characters Siemens uses in
    # comments (the files are valid UTF-8 and do contain real Unicode, e.g. U+2580).
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    return $text
}

function ChildText {
    param($Parent, [string]$Name)
    if ($null -eq $Parent) { return $null }
    $n = $Parent.SelectSingleNode("*[local-name()='$Name']")
    if ($null -eq $n) { return $null }
    return $n.InnerText
}

function Esc {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    return [System.Security.SecurityElement]::Escape($Text)
}

# --------------------------------------------------------------------- UDT model

function Split-Datatype {
    <#
        A member datatype is one of:
            Bool
            "UT01000_St_SysRevision"          nested UDT, wrapped in escaped quotes
            String[7]                          array of an elementary type
            "UT01000_St_SysRevision"[0..9]     array of a nested UDT
    #>
    param([string]$Datatype)

    $raw    = $Datatype
    $bounds = @()
    $base   = $Datatype.Trim()

    # Peel array suffixes from the outside in.
    while ($true) {
        $m = [regex]::Match($base, '^(.*?)\[([^\]]*)\]$')
        if (-not $m.Success) { break }
        $bounds = , $m.Groups[2].Value + $bounds
        $base   = $m.Groups[1].Value.Trim()
    }
    $isUdt = $false
    if ($base.StartsWith('"') -and $base.EndsWith('"')) {
        $isUdt = $true
        $base  = $base.Substring(1, $base.Length - 2)
    }
    return [pscustomobject]@{ Raw = $raw; Base = $base; IsUdt = $isUdt; Bounds = $bounds }
}

function Read-Member {
    param($Member)

    $attr = $Member.SelectSingleNode("*[local-name()='AttributeList']")
    $flags = [ordered]@{}
    if ($null -ne $attr) {
        foreach ($b in @($attr.SelectNodes("*[local-name()='BooleanAttribute']"))) {
            $flags[$b.GetAttribute('Name')] = [pscustomobject]@{
                Value         = $b.InnerText
                SystemDefined = $b.GetAttribute('SystemDefined')
            }
        }
    }

    # Member comments live inside the Interface and carry the culture as an attribute.
    $comments = @()
    foreach ($t in @($Member.SelectNodes("*[local-name()='Comment']/*[local-name()='MultiLanguageText']"))) {
        $comments += , ([pscustomobject]@{ Lang = $t.GetAttribute('Lang'); Text = $t.InnerText })
    }

    $nested = @()
    $sectionsNode = $Member.SelectSingleNode("*[local-name()='Sections']")
    if ($null -ne $sectionsNode) {
        foreach ($sec in @($sectionsNode.SelectNodes("*[local-name()='Section']"))) {
            $nested += , (Read-Section $sec)
        }
    }

    return [pscustomobject]@{
        Name       = $Member.GetAttribute('Name')
        DataType   = Split-Datatype $Member.GetAttribute('Datatype')
        StartValue = ChildText $Member 'StartValue'
        Comments   = $comments
        Flags      = $flags
        Nested     = $nested
    }
}

function Read-Section {
    param($Section)
    $members = @()
    foreach ($m in @($Section.SelectNodes("*[local-name()='Member']"))) { $members += , (Read-Member $m) }
    return [pscustomobject]@{ SectionName = $Section.GetAttribute('Name'); Members = $members }
}

# Object-level multilingual text, e.g. the UDT's own Comment and Title.
function Read-ObjectTexts {
    param($Node)
    $out = @()
    foreach ($t in @($Node.SelectNodes("*[local-name()='ObjectList']/*[local-name()='MultilingualText']"))) {
        $items = @()
        foreach ($i in @($t.SelectNodes("*[local-name()='ObjectList']/*[local-name()='MultilingualTextItem']"))) {
            $al = $i.SelectSingleNode("*[local-name()='AttributeList']")
            $items += , ([pscustomobject]@{ Culture = ChildText $al 'Culture'; Text = ChildText $al 'Text' })
        }
        $out += , ([pscustomobject]@{ Kind = $t.GetAttribute('CompositionName'); Items = $items })
    }
    return $out
}

function Read-UdtModel {
    param([string]$Path)

    [xml]$doc = Read-Utf8 $Path
    $struct = $doc.Document.SelectSingleNode("*[local-name()='SW.Types.PlcStruct']")
    if ($null -eq $struct) { throw "no SW.Types.PlcStruct in $Path" }

    $al    = $struct.SelectSingleNode("*[local-name()='AttributeList']")
    $iface = $al.SelectSingleNode("*[local-name()='Interface']")

    $sections = @()
    foreach ($sec in @($iface.SelectNodes("*[local-name()='Sections']/*[local-name()='Section']"))) {
        $sections += , (Read-Section $sec)
    }

    return [pscustomobject]@{
        SourceFile         = $Path
        Engineering        = $doc.Document.SelectSingleNode("*[local-name()='Engineering']").GetAttribute('version')
        Name               = ChildText $al 'Name'
        IsFailsafeCompliant = ChildText $al 'IsFailsafeCompliant'
        Sections           = $sections
        ObjectTexts        = (Read-ObjectTexts $struct)
    }
}

# ------------------------------------------------------------ UDT reconstruction

function Emit-Member {
    param($Member, [int]$Indent)

    $pad = ' ' * $Indent
    $dt  = $Member.DataType
    $bounds = ''
    foreach ($b in $dt.Bounds) { $bounds += "[$b]" }
    $rendered = if ($dt.IsUdt) { '&quot;' + $dt.Base + '&quot;' } else { $dt.Base }
    $dtText   = $rendered + $bounds

    $hasFlags  = $Member.Flags.Count -gt 0
    $hasNested = $Member.Nested.Count -gt 0
    if (-not $hasFlags -and -not $hasNested -and $null -eq $Member.StartValue -and $Member.Comments.Count -eq 0) {
        return "$pad<Member Name=`"$($Member.Name)`" Datatype=`"$dtText`" />`r`n"
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("$pad<Member Name=`"$($Member.Name)`" Datatype=`"$dtText`">")
    if ($hasFlags) {
        [void]$sb.Append('<AttributeList>')
        foreach ($k in $Member.Flags.Keys) {
            $f = $Member.Flags[$k]
            [void]$sb.Append("<BooleanAttribute Name=`"$k`" SystemDefined=`"$($f.SystemDefined)`">$($f.Value)</BooleanAttribute>")
        }
        [void]$sb.Append('</AttributeList>')
    }
    foreach ($c in $Member.Comments) {
        [void]$sb.Append("<Comment><MultiLanguageText Lang=`"$($c.Lang)`">$(Esc $c.Text)</MultiLanguageText></Comment>")
    }
    if ($null -ne $Member.StartValue) { [void]$sb.Append("<StartValue>$(Esc $Member.StartValue)</StartValue>") }
    foreach ($n in $Member.Nested) {
        [void]$sb.Append('<Sections>')
        [void]$sb.Append((Emit-Section $n ($Indent + 2)))
        [void]$sb.Append('</Sections>')
    }
    [void]$sb.AppendLine('</Member>')
    return $sb.ToString()
}

function Emit-Section {
    param($Section, [int]$Indent)
    $pad = ' ' * $Indent
    $sb  = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("$pad<Section Name=`"$($Section.SectionName)`">")
    foreach ($m in $Section.Members) { [void]$sb.Append((Emit-Member $m ($Indent + 2))) }
    [void]$sb.AppendLine("$pad</Section>")
    return $sb.ToString()
}

function ConvertTo-UdtXml {
    param($Model)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine('<Document>')
    [void]$sb.AppendLine("  <Engineering version=`"$($Model.Engineering)`" />")
    [void]$sb.AppendLine('  <SW.Types.PlcStruct ID="0">')
    [void]$sb.AppendLine('    <AttributeList>')
    [void]$sb.AppendLine('      <Interface>')
    [void]$sb.AppendLine("        <Sections xmlns=`"$ifaceNs`">")
    foreach ($s in $Model.Sections) { [void]$sb.Append((Emit-Section $s 10)) }
    [void]$sb.AppendLine('        </Sections>')
    [void]$sb.AppendLine('      </Interface>')
    [void]$sb.AppendLine("      <IsFailsafeCompliant>$($Model.IsFailsafeCompliant)</IsFailsafeCompliant>")
    [void]$sb.AppendLine("      <Name>$(Esc $Model.Name)</Name>")
    [void]$sb.AppendLine('    </AttributeList>')

    $id = 1
    foreach ($ot in $Model.ObjectTexts) {
        [void]$sb.AppendLine('    <ObjectList>')
        [void]$sb.AppendLine("      <MultilingualText ID=`"$id`" CompositionName=`"$($ot.Kind)`">")
        [void]$sb.AppendLine('        <ObjectList>')
        $iid = 0
        foreach ($it in $ot.Items) {
            [void]$sb.AppendLine("          <MultilingualTextItem ID=`"$($id + 1 + $iid)`" CompositionName=`"Items`">")
            [void]$sb.AppendLine('            <AttributeList>')
            [void]$sb.AppendLine("              <Culture>$(Esc $it.Culture)</Culture>")
            [void]$sb.AppendLine("              <Text>$(Esc $it.Text)</Text>")
            [void]$sb.AppendLine('            </AttributeList>')
            [void]$sb.AppendLine('          </MultilingualTextItem>')
            $iid++
        }
        [void]$sb.AppendLine('        </ObjectList>')
        [void]$sb.AppendLine('      </MultilingualText>')
        [void]$sb.AppendLine('    </ObjectList>')
        $id += 2 + $ot.Items.Count
    }

    [void]$sb.AppendLine('  </SW.Types.PlcStruct>')
    [void]$sb.AppendLine('</Document>')
    return $sb.ToString()
}

# ---------------------------------------------------------- tag table round trip

function Read-TagTableModel {
    param([string]$Path)

    [xml]$doc = Read-Utf8 $Path
    $table = $doc.Document.SelectSingleNode("*[local-name()='SW.Tags.PlcTagTable']")
    if ($null -eq $table) { throw "no SW.Tags.PlcTagTable in $Path" }

    $tags = @()
    foreach ($t in @($table.SelectNodes("*[local-name()='ObjectList']/*[local-name()='SW.Tags.PlcTag']"))) {
        $a   = $t.SelectSingleNode("*[local-name()='AttributeList']")
        $cmt = @()
        foreach ($i in @($t.SelectNodes(".//*[local-name()='MultilingualTextItem']"))) {
            $al = $i.SelectSingleNode("*[local-name()='AttributeList']")
            $cmt += , ([pscustomobject]@{ Lang = ChildText $al 'Culture'; Text = ChildText $al 'Text' })
        }
        $tags += , ([pscustomobject]@{
            Name               = ChildText $a 'Name'
            DataTypeName       = ChildText $a 'DataTypeName'
            LogicalAddress     = ChildText $a 'LogicalAddress'
            ExternalAccessible = ChildText $a 'ExternalAccessible'
            ExternalVisible    = ChildText $a 'ExternalVisible'
            ExternalWritable   = ChildText $a 'ExternalWritable'
            Comments           = $cmt
        })
    }

    return [pscustomobject]@{
        SourceFile  = $Path
        Engineering = $doc.Document.SelectSingleNode("*[local-name()='Engineering']").GetAttribute('version')
        Name        = ChildText ($table.SelectSingleNode("*[local-name()='AttributeList']")) 'Name'
        Tags        = $tags
    }
}

function ConvertTo-TagTableXml {
    param($Model)

    $id = 1
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine('<Document>')
    [void]$sb.AppendLine("  <Engineering version=`"$($Model.Engineering)`" />")
    [void]$sb.AppendLine('  <SW.Tags.PlcTagTable ID="0">')
    [void]$sb.AppendLine('    <AttributeList>')
    [void]$sb.AppendLine("      <Name>$(Esc $Model.Name)</Name>")
    [void]$sb.AppendLine('    </AttributeList>')
    [void]$sb.AppendLine('    <ObjectList>')
    foreach ($t in $Model.Tags) {
        [void]$sb.AppendLine("      <SW.Tags.PlcTag ID=`"$id`" CompositionName=`"Tags`">")
        [void]$sb.AppendLine('        <AttributeList>')
        [void]$sb.AppendLine("          <DataTypeName>$(Esc $t.DataTypeName)</DataTypeName>")
        [void]$sb.AppendLine("          <ExternalAccessible>$($t.ExternalAccessible)</ExternalAccessible>")
        [void]$sb.AppendLine("          <ExternalVisible>$($t.ExternalVisible)</ExternalVisible>")
        [void]$sb.AppendLine("          <ExternalWritable>$($t.ExternalWritable)</ExternalWritable>")
        if ($t.LogicalAddress) { [void]$sb.AppendLine("          <LogicalAddress>$(Esc $t.LogicalAddress)</LogicalAddress>") }
        [void]$sb.AppendLine("          <Name>$(Esc $t.Name)</Name>")
        [void]$sb.AppendLine('        </AttributeList>')
        if ($t.Comments.Count -gt 0) {
            [void]$sb.AppendLine('        <ObjectList>')
            [void]$sb.AppendLine("          <MultilingualText ID=`"$($id * 100 + 1)`" CompositionName=`"Comment`">")
            [void]$sb.AppendLine('            <ObjectList>')
            $iid = 0
            foreach ($c in $t.Comments) {
                [void]$sb.AppendLine("              <MultilingualTextItem ID=`"$($id * 100 + 2 + $iid)`" CompositionName=`"Items`">")
                [void]$sb.AppendLine('                <AttributeList>')
                [void]$sb.AppendLine("                  <Culture>$(Esc $c.Lang)</Culture>")
                [void]$sb.AppendLine("                  <Text>$(Esc $c.Text)</Text>")
                [void]$sb.AppendLine('                </AttributeList>')
                [void]$sb.AppendLine('              </MultilingualTextItem>')
                $iid++
            }
            [void]$sb.AppendLine('            </ObjectList>')
            [void]$sb.AppendLine('          </MultilingualText>')
            [void]$sb.AppendLine('        </ObjectList>')
        }
        [void]$sb.AppendLine('      </SW.Tags.PlcTag>')
        $id++
    }
    [void]$sb.AppendLine('    </ObjectList>')
    [void]$sb.AppendLine('  </SW.Tags.PlcTagTable>')
    [void]$sb.AppendLine('</Document>')
    return $sb.ToString()
}

# ----------------------------------------------------------------- comparison

function Compare-Udt {
    param($A, $B, [ref]$Diff)

    if ($A.Name -ne $B.Name) { $Diff.Value += "name '$($A.Name)' != '$($B.Name)'" }
    if ($A.Engineering -ne $B.Engineering) { $Diff.Value += "engineering differs" }
    if ($A.IsFailsafeCompliant -ne $B.IsFailsafeCompliant) { $Diff.Value += "IsFailsafeCompliant '$($A.IsFailsafeCompliant)' != '$($B.IsFailsafeCompliant)'" }

    $at = ($A.ObjectTexts | ForEach-Object { "$($_.Kind)=" + (($_.Items | ForEach-Object { "$($_.Culture):$($_.Text)" }) -join ',') }) -join '|'
    $bt = ($B.ObjectTexts | ForEach-Object { "$($_.Kind)=" + (($_.Items | ForEach-Object { "$($_.Culture):$($_.Text)" }) -join ',') }) -join '|'
    if ($at -ne $bt) { $Diff.Value += 'object-level Comment/Title differs' }

    if ($A.Sections.Count -ne $B.Sections.Count) { $Diff.Value += "section count $($A.Sections.Count) != $($B.Sections.Count)"; return }
    for ($i = 0; $i -lt $A.Sections.Count; $i++) {
        $x = $A.Sections[$i]; $y = $B.Sections[$i]
        if ($x.SectionName -ne $y.SectionName) { $Diff.Value += "section[$i] name differs" }
        if ($x.Members.Count -ne $y.Members.Count) { $Diff.Value += "section[$i] member count $($x.Members.Count) != $($y.Members.Count)"; continue }
        for ($j = 0; $j -lt $x.Members.Count; $j++) { Compare-Member $x.Members[$j] $y.Members[$j] "s$i.m$j" $Diff }
    }
}

function Compare-Member {
    param($A, $B, [string]$Path, [ref]$Diff)

    if ($A.Name -ne $B.Name) { $Diff.Value += "$Path name '$($A.Name)' != '$($B.Name)'" }
    if ($A.DataType.Raw -ne $B.DataType.Raw) { $Diff.Value += "$Path '$($A.Name)' datatype '$($A.DataType.Raw)' != '$($B.DataType.Raw)'" }
    if ([string]$A.StartValue -ne [string]$B.StartValue) { $Diff.Value += "$Path '$($A.Name)' start '$($A.StartValue)' != '$($B.StartValue)'" }

    $ac = ($A.Comments | ForEach-Object { "$($_.Lang)=$($_.Text)" }) -join '|'
    $bc = ($B.Comments | ForEach-Object { "$($_.Lang)=$($_.Text)" }) -join '|'
    if ($ac -ne $bc) { $Diff.Value += "$Path '$($A.Name)' comment differs" }

    foreach ($k in @($A.Flags.Keys)) {
        if (-not $B.Flags.Contains($k)) { $Diff.Value += "$Path '$($A.Name)' flag $k lost" }
        elseif ($A.Flags[$k].Value -ne $B.Flags[$k].Value) { $Diff.Value += "$Path '$($A.Name)' flag $k value differs" }
    }

    if ($A.Nested.Count -ne $B.Nested.Count) { $Diff.Value += "$Path '$($A.Name)' nested count $($A.Nested.Count) != $($B.Nested.Count)"; return }
    for ($k = 0; $k -lt $A.Nested.Count; $k++) {
        $na = $A.Nested[$k]; $nb = $B.Nested[$k]
        if ($na.Members.Count -ne $nb.Members.Count) { $Diff.Value += "$Path '$($A.Name)' nested[$k] member count differs"; continue }
        for ($m = 0; $m -lt $na.Members.Count; $m++) { Compare-Member $na.Members[$m] $nb.Members[$m] "$Path/$($A.Name)[$k].$m" $Diff }
    }
}

function Compare-TagTable {
    param($A, $B, [ref]$Diff)

    if ($A.Name -ne $B.Name) { $Diff.Value += "table name '$($A.Name)' != '$($B.Name)'" }
    if ($A.Tags.Count -ne $B.Tags.Count) { $Diff.Value += "tag count $($A.Tags.Count) != $($B.Tags.Count)"; return }
    for ($i = 0; $i -lt $A.Tags.Count; $i++) {
        $x = $A.Tags[$i]; $y = $B.Tags[$i]
        if ($x.Name -ne $y.Name) { $Diff.Value += "tag[$i] name '$($x.Name)' != '$($y.Name)'" }
        if ($x.DataTypeName -ne $y.DataTypeName) { $Diff.Value += "tag '$($x.Name)' type differs" }
        if ($x.LogicalAddress -ne $y.LogicalAddress) { $Diff.Value += "tag '$($x.Name)' address '$($x.LogicalAddress)' != '$($y.LogicalAddress)'" }
        foreach ($f in 'ExternalAccessible', 'ExternalVisible', 'ExternalWritable') {
            if ([string]$x.$f -ne [string]$y.$f) { $Diff.Value += "tag '$($x.Name)' $f differs" }
        }
        $xc = ($x.Comments | ForEach-Object { "$($_.Lang)=$($_.Text)" }) -join '|'
        $yc = ($y.Comments | ForEach-Object { "$($_.Lang)=$($_.Text)" }) -join '|'
        if ($xc -ne $yc) { $Diff.Value += "tag '$($x.Name)' comment differs" }
    }
}

# ------------------------------------------------------------------------- main

if (-not (Test-Path -LiteralPath $Con100Root)) { throw "CON100 root not found: $Con100Root" }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$reconDir = Join-Path $OutputDir 'reconstructed'
New-Item -ItemType Directory -Path $reconDir -Force | Out-Null

function Measure-Member {
    param($M, [ref]$Ref, [ref]$Arr, [ref]$Start, [ref]$Cmt)
    if ($M.DataType.IsUdt) { $Ref.Value++ }
    if ($M.DataType.Bounds.Count -gt 0) { $Arr.Value++ }
    if ($null -ne $M.StartValue) { $Start.Value++ }
    if ($M.Comments.Count -gt 0) { $Cmt.Value++ }
    foreach ($n in $M.Nested) { foreach ($mm in $n.Members) { Measure-Member $mm $Ref $Arr $Start $Cmt } }
}

function CountOf {
    param($model)
    $n = 0
    foreach ($s in $model.Sections) { $n += $s.Members.Count }
    return $n
}

Write-Host '=== UDT round trip (SW.Types.PlcStruct) ===' -ForegroundColor Cyan
$udtFiles = Get-ChildItem -LiteralPath (Join-Path $Con100Root 'PLC data types') -Recurse -File -Filter '*.xml' | Sort-Object Name
$udtPass = 0; $udtFail = 0
$udtDiffs = New-Object System.Collections.Generic.List[string]
$mTot = 0; $refTot = 0; $arrTot = 0; $startTot = 0; $cmtTot = 0; $nestedTot = 0

foreach ($f in $udtFiles) {
    try {
        $model = Read-UdtModel $f.FullName
        [System.IO.File]::WriteAllText((Join-Path $reconDir $f.Name), (ConvertTo-UdtXml $model), $utf8)

        $again = Read-UdtModel (Join-Path $reconDir $f.Name)
        $diff = @()
        Compare-Udt $model $again ([ref]$diff)

        if ($diff.Count -eq 0) {
            $udtPass++
            Write-Host ("  PASS  {0,-34} {1,-28} members={2}" -f $f.Name, $model.Name, $(CountOf $model)) -ForegroundColor Green
        } else {
            $udtFail++
            Write-Host ("  FAIL  {0,-34} {1} difference(s)" -f $f.Name, $diff.Count) -ForegroundColor Red
            foreach ($d in ($diff | Select-Object -First 6)) { Write-Host "          $d" -ForegroundColor Red }
            $udtDiffs.Add("$($f.Name): " + ($diff -join '; '))
        }

        foreach ($s in $model.Sections) {
            foreach ($m in $s.Members) {
                $mTot++
                if ($m.Nested.Count -gt 0) { $nestedTot++ }
                Measure-Member $m ([ref]$refTot) ([ref]$arrTot) ([ref]$startTot) ([ref]$cmtTot)
            }
        }
    } catch {
        $udtFail++
        Write-Host ("  FAIL  {0,-34} {1}" -f $f.Name, $_.Exception.Message) -ForegroundColor Red
        $udtDiffs.Add("$($f.Name): $($_.Exception.Message)")
    }
}

Write-Host ''
Write-Host '=== Tag table round trip (SW.Tags.PlcTagTable) ===' -ForegroundColor Cyan
$tagFiles = @()
foreach ($x in (Get-ChildItem -LiteralPath (Join-Path $Con100Root 'PLC tags') -Recurse -File -Filter '*.xml' | Sort-Object Name)) {
    $tagFiles += , [pscustomobject]@{ Path = $x.FullName; Name = $x.Name }
}
foreach ($d in $AdditionalTagDir) {
    foreach ($x in (Get-ChildItem -LiteralPath $d -Recurse -File -Filter '*.xml' | Sort-Object FullName)) {
        $rel = $x.FullName.Substring($d.Length).TrimStart('\')
        $safe = ($rel -replace '[\\/:*?"<>|]', '_')
        $tagFiles += , [pscustomobject]@{ Path = $x.FullName; Name = $safe }
    }
}
$tagPass = 0; $tagFail = 0; $tagCount = 0; $tagDiffs = New-Object System.Collections.Generic.List[string]
$tagCommented = 0

foreach ($f in $tagFiles) {
    try {
        $model = Read-TagTableModel $f.Path
        [System.IO.File]::WriteAllText((Join-Path $reconDir $f.Name), (ConvertTo-TagTableXml $model), $utf8)

        $again = Read-TagTableModel (Join-Path $reconDir $f.Name)
        $diff = @()
        Compare-TagTable $model $again ([ref]$diff)

        $tagCount += $model.Tags.Count
        $tagCommented += (@($model.Tags | Where-Object { $_.Comments.Count -gt 0 })).Count

        if ($diff.Count -eq 0) {
            $tagPass++
            Write-Host ("  PASS  {0,-26} table={1,-18} tags={2}" -f $f.Name, $model.Name, $model.Tags.Count) -ForegroundColor Green
        } else {
            $tagFail++
            Write-Host ("  FAIL  {0,-26} {1} difference(s)" -f $f.Name, $diff.Count) -ForegroundColor Red
            foreach ($d in ($diff | Select-Object -First 6)) { Write-Host "          $d" -ForegroundColor Red }
            $tagDiffs.Add("$($f.Name): " + ($diff -join '; '))
        }
    } catch {
        $tagFail++
        Write-Host ("  FAIL  {0,-26} {1}" -f $f.Name, $_.Exception.Message) -ForegroundColor Red
        $tagDiffs.Add("$($f.Name): $($_.Exception.Message)")
    }
}

Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
Write-Host "  UDT files       : $($udtFiles.Count)  PASS=$udtPass  FAIL=$udtFail"
Write-Host "  UDT top members : $mTot   with nested struct = $nestedTot"
Write-Host "  UDT nested refs : $refTot   arrays = $arrTot   start values = $startTot   commented members = $cmtTot"
Write-Host "  Tag tables      : $($tagFiles.Count)  PASS=$tagPass  FAIL=$tagFail   tags = $tagCount   with comments = $tagCommented"

$summary = @()
$summary += 'TIA XML ROUND-TRIP VALIDATION'
$summary += "run      : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$summary += "source   : $Con100Root"
$summary += "pipeline : TIA XML -> model -> reconstructed TIA XML -> reparse -> field diff"
$summary += ''
$summary += "UDT  SW.Types.PlcStruct       files=$($udtFiles.Count) pass=$udtPass fail=$udtFail"
$summary += "TAG  SW.Tags.PlcTagTable      files=$($tagFiles.Count) pass=$tagPass fail=$tagFail tags=$tagCount"
$summary += ''
$summary += "UDT content measured: top-level members=$mTot nested-struct members=$nestedTot nested-UDT refs=$refTot arrays=$arrTot start-values=$startTot commented=$cmtTot"
if ($udtDiffs.Count) { $summary += ''; $summary += 'UDT differences:'; $udtDiffs | ForEach-Object { $summary += "  $_" } }
if ($tagDiffs.Count) { $summary += ''; $summary += 'Tag table differences:'; $tagDiffs | ForEach-Object { $summary += "  $_" } }
[System.IO.File]::WriteAllLines((Join-Path $OutputDir 'roundtrip-report.txt'), $summary, $utf8)
Write-Host ''
Write-Host "  report: $(Join-Path $OutputDir 'roundtrip-report.txt')"
