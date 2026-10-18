[CmdletBinding()]
param(
  [string]$InputDir = 'C:\XforceMsw\goldens\v16\ladlabs',
  [string]$Report   = 'C:\XforceMsw\goldens\v16\ladlabs\lad-reconstruction.txt'
)

$ErrorActionPreference = 'Stop'

$SINK_KIND = @{ 'Coil' = 'ASSIGN'; 'SCoil' = 'SET'; 'RCoil' = 'RESET' }

$OUTPUT_PINS = @{
  'Contact' = @('out'); 'Coil' = @('out'); 'SCoil' = @('out'); 'RCoil' = @('out')
  'PContact' = @('out'); 'RContact' = @('out'); 'NContact' = @('out')
  'TON' = @('Q', 'ET'); 'TP' = @('Q', 'ET'); 'TONR' = @('Q', 'ET')
  'Div' = @('out'); 'Mul' = @('out'); 'Add' = @('out'); 'Sub' = @('out')
  'O' = @('out'); 'And' = @('out')
  'Gt' = @('out'); 'Ge' = @('out'); 'Lt' = @('out'); 'Le' = @('out')
  'Eq' = @('out'); 'Ne' = @('out'); 'Gt0' = @('out'); 'Le0' = @('out')
  'CTUD' = @('Q', 'QU', 'QD', 'CV'); 'CTU' = @('Q', 'CV'); 'CTD' = @('QD', 'CV')
  'Move' = @('out'); 'MoveNeg' = @('out')
}

function Get-Local {
  param($Node, [string]$Name)
  if ($null -eq $Node) { return '' }
  $n = $Node.SelectSingleNode("*[local-name()='$Name']")
  if ($null -eq $n) { return '' }
  return ($n.InnerText -replace '\s+', ' ').Trim()
}

function Get-AccessText {
  param($Acc)
  if ($null -eq $Acc) { return '?' }
  $scope = $Acc.GetAttribute('Scope')

  if ($scope -eq 'LiteralConstant' -or $scope -eq 'TypedConstant') {
    $cv = $Acc.SelectSingleNode("*[local-name()='Constant']/*[local-name()='ConstantValue']")
    if ($null -ne $cv) {
      $v = ($cv.InnerText -replace '\s+', ' ').Trim()
      if ($v) { return $v }
    }
  }

  $names = @()
  $sym = $Acc.SelectSingleNode("*[local-name()='Symbol']")
  if ($null -ne $sym) {
    foreach ($c in $sym.SelectNodes("*[local-name()='Component']")) { $names += $c.GetAttribute('Name') }
  }
  if ($names.Count -eq 0) {
    foreach ($c in $Acc.SelectNodes("*[local-name()='Component']")) { $names += $c.GetAttribute('Name') }
  }
  if ($names.Count -eq 0) { return '?' }
  return ($names -join '.')
}

function Read-FlgNet {
  param([xml]$Doc)

  $flg = $Doc.Document.SelectSingleNode("//*[local-name()='FlgNet']")
  if ($null -eq $flg) { return $null }

  $access = @{}
  foreach ($a in $flg.SelectNodes("*[local-name()='Parts']/*[local-name()='Access']")) {
    $access[[int]$a.GetAttribute('UId')] = Get-AccessText $a
  }

  $parts = @{}
  foreach ($p in $flg.SelectNodes("*[local-name()='Parts']/*[local-name()='Part']")) {
    $neg = @()
    foreach ($n in $p.SelectNodes("*[local-name()='Negated']")) { $neg += $n.GetAttribute('Name') }
    $inst = ''
    $iNode = $p.SelectSingleNode("*[local-name()='Instance']")
    if ($null -ne $iNode) { $inst = Get-AccessText $iNode }
    $parts[[int]$p.GetAttribute('UId')] = [pscustomobject]@{
      Uid = [int]$p.GetAttribute('UId'); Name = $p.GetAttribute('Name')
      Negated = $neg; Instance = $inst; Node = $p
    }
  }

  $wires = @()
  foreach ($w in $flg.SelectNodes("*[local-name()='Wires']/*[local-name()='Wire']")) {
    $o = [pscustomobject]@{
      Rail = ($null -ne $w.SelectSingleNode("*[local-name()='Powerrail']"))
      Idents = @(); Pins = @(); Opens = @()
    }
    foreach ($n in $w.ChildNodes) {
      if ($n -isnot [System.Xml.XmlElement]) { continue }
      switch ($n.LocalName) {
        'IdentCon' { $o.Idents += [int]$n.GetAttribute('UId') }
        'OpenCon'  { $o.Opens  += [int]$n.GetAttribute('UId') }
        'NameCon'  { $o.Pins   += ('{0}|{1}' -f $n.GetAttribute('UId'), $n.GetAttribute('Name')) }
      }
    }
    $wires += ,$o
  }

  return [pscustomobject]@{ Access = $access; Parts = $parts; Wires = $wires }
}

function Get-PartOperand {
  param($Net, $Part)
  $key = '{0}|operand' -f $Part.Uid
  foreach ($w in $Net.Wires) {
    if ($w.Pins -contains $key) {
      foreach ($i in $w.Idents) { if ($Net.Access.ContainsKey($i)) { return $Net.Access[$i] } }
      return '<unconnected>'
    }
  }
  return '?'
}

function Get-PartPinText {
  param($Net, $Part, [string]$Pin)
  $key = '{0}|{1}' -f $Part.Uid, $Pin
  foreach ($w in $Net.Wires) {
    if ($w.Pins -contains $key) {
      foreach ($i in $w.Idents) { if ($Net.Access.ContainsKey($i)) { return $Net.Access[$i] } }
      return '<unconnected>'
    }
  }
  return $null
}

function Resolve-Nets {
  param($Net)

  $pinNet = @{}
  $netRail = @{}
  $netIdents = @{}
  $drv = @{}
  $n = 0

  foreach ($w in $Net.Wires) {
    $n++
    if ($w.Rail) { $netRail[$n] = $true }
    foreach ($p in $w.Pins) { $pinNet[$p] = $n }
    if ($w.Idents.Count -gt 0) { $netIdents[$n] = $w.Idents }
  }

  $partPins = @{}
  foreach ($kp in $pinNet.Keys) {
    $sp = $kp.Split('|')
    $u = [int]$sp[0]
    if (-not $partPins.ContainsKey($u)) { $partPins[$u] = @() }
    $partPins[$u] += $sp[1]
  }

  $dangling = @()
  foreach ($entry in $Net.Parts.GetEnumerator()) {
    $p = $entry.Value
    if ($null -eq $p) { continue }
    if ([string]::IsNullOrEmpty($p.Name) -or -not $OUTPUT_PINS.ContainsKey($p.Name)) {
      $dangling += "part $($p.Name) #$($entry.Key) has no output-pin table; not evaluated"
      continue
    }
    foreach ($pin in $OUTPUT_PINS[$p.Name]) {
      $kp = '{0}|{1}' -f $p.Uid, $pin
      if (-not $pinNet.ContainsKey($kp)) { $dangling += "part $($p.Name) #$($entry.Key) pin '$pin' is not wired"; continue }
      $net = $pinNet[$kp]
      if ($null -eq $net) { $dangling += "part $($p.Name) #$($entry.Key) pin '$pin' resolved to no net"; continue }
      if (-not $drv.ContainsKey($net)) { $drv[$net] = @() }
      $drv[$net] += $kp
    }
  }

  return [pscustomobject]@{ PinNet = $pinNet; Rail = $netRail; Idents = $netIdents; Driven = $drv; Dangling = $dangling }
}

function Get-NetExpr {
  param([int]$NetId, [int]$Depth = 0)

  if ($script:Memo.ContainsKey($NetId)) { return $script:Memo[$NetId] }
  if ($Depth -gt 40) { return '<depth>' }
  if ($script:Active.Contains($NetId)) { return '<cycle>' }

  $script:Active.Add($NetId)
  try {
    $pieces = @()

    if ($script:Res.Rail.ContainsKey($NetId)) { $pieces += 'TRUE' }

    if ($script:Res.Driven.ContainsKey($NetId)) {
      foreach ($kp in $script:Res.Driven[$NetId]) {
        $sp = $kp.Split('|')
        $uid = [int]$sp[0]
        $pin = $sp[1]
        $p = $script:Net.Parts[$uid]

        if ($SINK_KIND.ContainsKey($p.Name)) { continue }

        $opnd = Get-PartOperand $script:Net $p
        $term = if ($p.Negated -contains 'operand') { "!$opnd" } else { $opnd }

        $outs = $OUTPUT_PINS[$p.Name]
        if ($p.Name -eq 'PContact' -or $p.Name -eq 'RContact') {
          $bit = Get-PartPinText $script:Net $p 'bit'
          $pre = Get-PartPinText $script:Net $p 'pre'
          $dir = if ($p.Name -eq 'PContact') { 'edge_up' } else { 'edge_down' }
          $pieces += "$dir($term)"
          $script:Notes["$($p.Name) #$uid : operand=$term bit=$(if ($bit) { $bit } else { '<none>' }) pre=$(if ($pre) { $pre } else { '<unwired>' })"] = $true
          continue
        }

        if ($p.Name -eq 'Contact' -or $p.Name -eq 'NContact') {
          $inKey = "$uid|in"
          if ($script:Res.PinNet.ContainsKey($inKey)) {
            $src = Get-NetExpr $script:Res.PinNet[$inKey] ($Depth + 1)
            $pieces += "($src & $term)"
          } else { $pieces += $term }
          continue
        }

        if ($p.Name -eq 'TON' -or $p.Name -eq 'TP' -or $p.Name -eq 'TONR') {
          $inKey = "$uid|IN"
          $inExpr = if ($script:Res.PinNet.ContainsKey($inKey)) { Get-NetExpr $script:Res.PinNet[$inKey] ($Depth + 1) } else { 'TRUE' }
          $pt = Get-PartPinText $script:Net $p 'PT'
          if (-not $pt) { $pt = '?' }
          $inst = if ($p.Instance) { $p.Instance } else { "$($uid)_inst" }
          $fn = $p.Name.ToUpper()
          $script:Notes["$fn $inst : IN=$inExpr PT=$pt"] = $true
          if ($pin -eq 'ET') { $pieces += "ET($inst)" } else { $pieces += "$fn($inst, PT=$pt)" }
          continue
        }

        if ($p.Name -eq 'CTUD' -or $p.Name -eq 'CTU' -or $p.Name -eq 'CTD') {
          $inst = if ($p.Instance) { $p.Instance } else { "$($uid)_inst" }
          $script:Notes["COUNTER $inst ($($p.Name))"] = $true
          $pieces += "$($p.Name)($inst)"
          continue
        }

        $ins = @()
        foreach ($ip in @('in1', 'in2', 'CU', 'CD', 'R', 'PV')) {
          $k = "$uid|$ip"
          if ($script:Res.PinNet.ContainsKey($k)) { $ins += (Get-NetExpr $script:Res.PinNet[$k] ($Depth + 1)) }
        }
        if ($ins.Count -eq 0) { $ins += $term }

        $sym = switch ($p.Name) {
          'Div' { 'div' } 'Mul' { 'mul' } 'Add' { 'add' } 'Sub' { 'sub' }
          'O'   { 'or' }  'And' { 'and' }
          'Gt'  { '>' }   'Ge'  { '>=' } 'Lt' { '<' } 'Le' { '<=' } 'Eq' { '=' } 'Ne' { '<>' }
          'Gt0' { '>' }   'Le0' { '<=' }
          default { $p.Name }
        }
        $comb = if ($ins.Count -gt 1) { ', ' } else { '' }
        $pieces += "$sym($(($ins) -join $comb))"
      }
    }

    if ($pieces.Count -eq 0 -and $script:Res.Idents.ContainsKey($NetId)) {
      $ts = @()
      foreach ($i in $script:Res.Idents[$NetId]) {
        if ($script:Net.Access.ContainsKey($i)) { $ts += $script:Net.Access[$i] }
      }
      if ($ts.Count -gt 0) { $pieces += ($ts -join ' | ') }
    }

    $expr = if ($pieces.Count -eq 0) { '?' } else { ($pieces | Select-Object -Unique) -join ' | ' }
    $script:Memo[$NetId] = $expr
    return $expr
  } finally {
    $script:Active.Remove($NetId)
  }
}

$out = New-Object System.Collections.Generic.List[string]
$stats = [ordered]@{ Blocks = 0; Graphical = 0; DataBlocks = 0; Contacts = 0; Coils = 0; Timers = 0; Counters = 0 }

$files = Get-ChildItem -LiteralPath $InputDir -Recurse -Filter '*.xml' -File |
         Where-Object { $_.FullName -match '\\export\\blocks\\' } | Sort-Object FullName

foreach ($f in $files) {
  $stats.Blocks++
  $rel = $f.FullName.Substring($InputDir.Length + 1)
  [xml]$d = Get-Content -LiteralPath $f.FullName -Raw

  $blockNode = $d.Document.SelectSingleNode("//*[starts-with(local-name(),'SW.Blocks.') and not(local-name()='SW.Blocks.CompileUnit')]")
  $langNode = $d.Document.SelectSingleNode("//*[local-name()='SW.Blocks.CompileUnit']/*[local-name()='AttributeList']/*[local-name()='ProgrammingLanguage']")
  $lang = if ($langNode) { $langNode.InnerText.Trim() } else { 'n/a' }

  $out.Add('')
  $out.Add(('=' * 78))
  $out.Add("BLOCK  $rel")
  $out.Add("TYPE   $($blockNode.LocalName)    LANGUAGE  $lang")

  $net = Read-FlgNet $d
  if ($null -eq $net) {
    $stats.DataBlocks++
    $mem = $d.Document.SelectNodes("//*[local-name()='Sections']/*[local-name()='Section']/*[local-name()='Member']")
    $out.Add("STRUCTURE  no FlgNet. Static members: $($mem.Count)")
    foreach ($m in $mem) {
      $mn = $m.GetAttribute('Name')
      $dt = $m.GetAttribute('Datatype')
      $rem = $m.GetAttribute('Remanence')
      $ic = $m.SelectSingleNode("*[local-name()='StartValue']//*[local-name()='Value']")
      $iv = if ($ic) { (Get-Local $ic '').Trim() } else { '' }
      $ln = $m.GetAttribute('LogicalAddress')
      $addr = if ($ln) { "  @$ln" } else { '' }
      $ret = if ($rem -eq 'Retain') { ' (retain)' } else { '' }
      $out.Add("   $mn : $dt$addr$ret$(if($iv){' = ' + $iv})")
    }
    continue
  }

  $stats.Graphical++
  $script:Net = $net
  $script:Res = Resolve-Nets $net
  $script:Memo = @{}
  $script:Active = New-Object 'System.Collections.Generic.HashSet[int]'
  $script:Emitted = @{}
  $script:Notes = @{}

  $census = ($net.Parts.Values | Group-Object Name | Sort-Object Count -Descending | ForEach-Object { "$($_.Count)x$($_.Name)" }) -join '  '
  $out.Add("GRAPH  $($net.Parts.Count) parts, $($net.Wires.Count) wires, $($net.Access.Count) accesses")
  $out.Add("ELEMENTS  $census")

  foreach ($p in $net.Parts.Values) {
    switch ($p.Name) {
      'Contact' { $stats.Contacts++ }
      { $_ -in @('Coil', 'SCoil', 'RCoil') } { $stats.Coils++ }
      { $_ -in @('TON', 'TP', 'TONR') } { $stats.Timers++ }
      { $_ -in @('CTUD', 'CTU', 'CTD') } { $stats.Counters++ }
    }
  }

  foreach ($n in ($script:Res.PinNet.Values | Sort-Object -Unique)) { $null = Get-NetExpr $n }

  $out.Add('')
  $out.Add('  LOGIC (solved from the rung graph)')

  $assigned = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($nid in ($script:Res.Idents.Keys | Sort-Object)) {
    if (-not $script:Res.Driven.ContainsKey($nid)) { continue }
    if (-not $script:Memo.ContainsKey($nid)) { continue }
    foreach ($i in $script:Res.Idents[$nid]) {
      $t = if ($script:Net.Access.ContainsKey($i)) { $script:Net.Access[$i] } else { '?' }
      if ($t -eq '?' -or $t -eq '<unconnected>') { continue }
      if ($t -match '^\d+$') { continue }
      if ($t -match '^(T#|DINT|INT|BOOL|WORD|DWORD|REAL|LREAL|LTIME|TIME|LWORD|LBYTE|CHAR|SINT|USINT|UDINT|ULINT)') { continue }
      if ($assigned.Add($t)) { $out.Add("     $t := $($script:Memo[$nid])") }
    }
  }

  foreach ($entry in $script:Net.Parts.GetEnumerator()) {
    $p = $entry.Value
    if ($null -eq $p) { continue }
    if (-not $SINK_KIND.ContainsKey($p.Name)) { continue }
    $k = "$($p.Uid)|in"
    $cond = '?'
    if ($script:Res.PinNet.ContainsKey($k)) {
      $nid = $script:Res.PinNet[$k]
      if ($script:Memo.ContainsKey($nid)) { $cond = $script:Memo[$nid] }
    }
    $op = Get-PartOperand $script:Net $p
    $out.Add("     $($SINK_KIND[$p.Name]) $op  when  $cond")
  }

  if ($script:Notes.Count -gt 0) {
    foreach ($n in ($script:Notes.Keys | Sort-Object)) { $out.Add("     [$n]") }
  }
  if ($script:Res.Dangling.Count -gt 0) {
    foreach ($danglingNote in ($script:Res.Dangling | Select-Object -Unique)) { $out.Add("     (note) $danglingNote") }
  }
}

$out.Add('')
$out.Add(('=' * 78))
$out.Add("SUMMARY  blocks=$($stats.Blocks)  graphicalLAD=$($stats.Graphical)  dataOnly=$($stats.DataBlocks)  contacts=$($stats.Contacts)  coils=$($stats.Coils)  timers=$($stats.Timers)  counters=$($stats.Counters)")

$out | Set-Content -LiteralPath $Report -Encoding UTF8
Write-Output "Wrote $Report"
Write-Output "blocks=$($stats.Blocks) graphicalLAD=$($stats.Graphical) dataOnly=$($stats.DataBlocks) contacts=$($stats.Contacts) coils=$($stats.Coils) timers=$($stats.Timers) counters=$($stats.Counters)"
