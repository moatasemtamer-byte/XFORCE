#Requires -Version 5.1
<#
.SYNOPSIS
  TIA V16 Openness import with dependency-aware UDT ordering
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$PackageDir,
  [string]$ProjectName='TestImport_Minimal',
  [string]$ProjectPath=(Join-Path $env:TEMP 'tia-import-test'),
  [string]$CpuOrderNumber='',
  [string]$LogFile=(Join-Path $env:TEMP ('tia-import-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'.log')),
  [string]$SummaryFile=([System.IO.Path]::ChangeExtension((Join-Path $env:TEMP ('tia-import-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))),'.json')),
  [string]$TiaInstallPath='C:\Program Files\Siemens\Automation\Portal V16',
  [string]$Version='V16'
)
$ErrorActionPreference='Stop'
function Write-Log{ param([string]$m); $ts=[DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff'); $line="[$ts] $m"; Write-Host $line; Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 }
$PublicApi=Join-Path $TiaInstallPath "PublicAPI\$Version"; $Dll=Join-Path $PublicApi 'Siemens.Engineering.dll'
if(-not(Test-Path $Dll)){ throw "TIA Openness DLL not found: $Dll" }
if(-not(Test-Path $PackageDir)){ throw "PackageDir not found: $PackageDir" }
Write-Log "=== Import-ToTia.ps1 START ==="
$StageDir=Join-Path $env:TEMP ("tia-stage-"+[Guid]::NewGuid().ToString('N').Substring(0,12)); New-Item -ItemType Directory -Path $StageDir -Force|Out-Null
Copy-Item (Join-Path $PublicApi '*.dll') $StageDir -Force
$TiaBin=Join-Path $TiaInstallPath 'Bin'; if(Test-Path $TiaBin){ Copy-Item (Join-Path $TiaBin 'Siemens.Engineering*.dll') $StageDir -Force -ErrorAction SilentlyContinue }
[System.Reflection.Assembly]::LoadFrom((Join-Path $StageDir 'Siemens.Engineering.dll'))|Out-Null
function Get-OpennessService{ param($o,[Type]$t); $m=$o.GetType().GetMethod('GetService',[System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance); if($null -eq $m){ throw "no GetService"}; return $m.MakeGenericMethod($t).Invoke($o,@()) }
function Get-PlcSoftware{ param($d); foreach($i in $d.DeviceItems){ try{ $c=Get-OpennessService $i ([Siemens.Engineering.HW.Features.SoftwareContainer]); if($c -and $c.Software){ foreach($sw in @($c.Software)){ if($sw -is [Siemens.Engineering.SW.PlcSoftware]){ return $sw }}}}catch{}}; return $null }
$tia=$null;$proj=$null;$sum=[ordered]@{Status='UNKNOWN';Imported=[ordered]@{UDTs=@();Tags=@();Blocks=@();BlocksFailed=@();Library=@();InstanceDbs=@();InstanceDbsFailed=@();Globals=@()};Compiler=[ordered]@{State=$null;ErrorCount=0;WarningCount=0;Messages=@()};Exception=$null}
try{
  $full=Join-Path $ProjectPath $ProjectName; if(-not(Test-Path $ProjectPath)){ New-Item -ItemType Directory -Path $ProjectPath -Force|Out-Null }
  Write-Log "Starting TIA..."; $tia=New-Object Siemens.Engineering.TiaPortal([Siemens.Engineering.TiaPortalMode]::WithoutUserInterface); Write-Log "TIA started"
  $pdi=[System.IO.DirectoryInfo]::new($ProjectPath); $proj=$tia.Projects.Create($pdi,$ProjectName); Write-Log "Project created"
  $plc=$null; if(-not [string]::IsNullOrWhiteSpace($CpuOrderNumber)){ $ord=$CpuOrderNumber; if($ord -notlike 'OrderNumber:*'){$ord="OrderNumber:$ord"}; $dev=$proj.Devices.CreateWithItem($ord,'PLC_1','PLC_1'); $plc=Get-PlcSoftware $dev }
  if($null -eq $plc){ $proj.Save(); $sum.Status='NO_PLC_SOFTWARE'; $sum|ConvertTo-Json -Depth 5|Out-File $SummaryFile -Encoding UTF8; return }
  # UDTs
  $udtDir=Join-Path $PackageDir 'UDTs'; if(Test-Path $udtDir){ $udtFiles=@(Get-ChildItem $udtDir -Filter '*.xml'|Sort-Object Name); $graph=@{}; $all=@{}; foreach($f in $udtFiles){ $all[[IO.Path]::GetFileNameWithoutExtension($f.Name)]=$f }; foreach($f in $udtFiles){ $name=[IO.Path]::GetFileNameWithoutExtension($f.Name); $c=[IO.File]::ReadAllText($f.FullName); $refs=@(); [regex]::Matches($c,'(UT[A-Za-z0-9_]*)')|%{ $n=$_.Groups[1].Value; if($all.ContainsKey($n)-and $n -ne $name){ $refs+=$n }}; $graph[$name]=@($refs|Select-Object -Unique) }; $vis=@{};$tmp=@{};$ord=New-Object Collections.Generic.List[string]; function Visit($n){ if($vis.ContainsKey($n)){return}; if($tmp.ContainsKey($n)){return}; $tmp[$n]=$true; foreach($d in $graph[$n]){ if($graph.ContainsKey($d)){Visit $d}}; $tmp.Remove($n); $vis[$n]=$true; $ord.Add($n)|Out-Null }; foreach($n in ($graph.Keys|Sort-Object)){ Visit $n }; foreach($n in ($all.Keys|Where-Object{ -not $vis.ContainsKey($_)}|Sort-Object)){ $ord.Add($n)|Out-Null }; foreach($n in $ord){ if($all.ContainsKey($n)){ $f=$all[$n]; Write-Log "  UDT: $($f.Name)"; $plc.TypeGroup.Types.Import((New-Object IO.FileInfo($f.FullName)),[Siemens.Engineering.ImportOptions]::Override); $sum.Imported.UDTs+=$f.Name }}}
  # Tags
  $tagDir=Join-Path $PackageDir 'Tags'; if(Test-Path $tagDir){ Get-ChildItem $tagDir -Filter '*.xml'|Sort-Object Name|%{ Write-Log "  Tags: $($_.Name)"; $plc.TagTableGroup.TagTables.Import((New-Object IO.FileInfo($_.FullName)),[Siemens.Engineering.ImportOptions]::Override); $sum.Imported.Tags+=$_.Name }}
  # Blocks, Globals and Library FBs are imported in dependency order. The generator emits
  # ImportOrder.json listing every generated source in that order (library FBs, the global data
  # block, user FBs, wrapper FBs, instance-DB XML, then the OBs). An OB cannot hold block instances,
  # so its logic lives in a wrapper FB and the OB calls it through an instance DB imported as XML.
  # Fall back to the legacy folder order when the manifest is absent.
  function Add-Imported{ param([string]$rel,[string]$fname); if($rel -like 'Library/*'){ $sum.Imported.Library+=$fname } elseif($rel -like 'Globals/*'){ $sum.Imported.Globals+=$fname } else { $sum.Imported.Blocks+=$fname } }
  function Import-SclSource{ param([string]$rel); $full=Join-Path $PackageDir ($rel -replace '/','\'); $fname=[IO.Path]::GetFileName($full); Write-Log "  Block: $rel"; try{ $ext=$plc.ExternalSourceGroup.ExternalSources.CreateFromFile([IO.Path]::GetFileNameWithoutExtension($full),$full); $ext.GenerateBlocksFromSource(); Add-Imported $rel $fname }catch{ Write-Log "  Block FAILED: $rel`n$($_.Exception.Message)"; try{ $detail = "=== $rel ===`n" + ($_.Exception | Format-List * -Force | Out-String); $inner=$_.Exception.InnerException; while($inner){ $detail += "--- inner ---`n" + ($inner | Format-List * -Force | Out-String); $inner=$inner.InnerException }; $detail | Out-File -FilePath (Join-Path $env:TEMP 'import-detail.txt') -Append -Encoding UTF8 }catch{}; $sum.Imported.BlocksFailed+=$fname } }
  function Import-BlockXml{ param([string]$rel); $full=Join-Path $PackageDir ($rel -replace '/','\'); $fname=[IO.Path]::GetFileName($full); Write-Log "  BlockXml: $rel"; try{ $plc.BlockGroup.Blocks.Import((New-Object IO.FileInfo($full)),[Siemens.Engineering.ImportOptions]::Override)|Out-Null; Add-Imported $rel $fname }catch{ Write-Log "  BlockXml FAILED: $rel`n$($_.Exception.Message)"; $sum.Imported.BlocksFailed+=$fname } }
  $orderFile=Join-Path $PackageDir 'ImportOrder.json'
  if(Test-Path $orderFile){
    $manifest=Get-Content $orderFile -Raw|ConvertFrom-Json
    foreach($e in $manifest.order){ $rel=[string]$e.path; if([string]$e.kind -eq 'xml'){ Import-BlockXml $rel } else { Import-SclSource $rel } }
  } else {
    $libDir=Join-Path $PackageDir 'Library'; if(Test-Path $libDir){ Get-ChildItem $libDir -Filter '*.scl'|Sort-Object Name|%{ Import-SclSource ('Library/'+$_.Name) } }
    $gloDir=Join-Path $PackageDir 'Globals'; if(Test-Path $gloDir){ Get-ChildItem $gloDir -Filter '*.db'|Sort-Object Name|%{ Import-SclSource ('Globals/'+$_.Name) } }
    $blkXml=Join-Path $PackageDir 'Blocks'; if(Test-Path $blkXml){ Get-ChildItem $blkXml -Filter '*.xml'|Sort-Object Name|%{ Import-BlockXml ('Blocks/'+$_.Name) } }
    $blkDir=Join-Path $PackageDir 'Blocks'; if(Test-Path $blkDir){ Get-ChildItem $blkDir -Filter '*.scl'|Sort-Object Name|%{ Import-SclSource ('Blocks/'+$_.Name) } }
  }
  if($env:TIA_DIAG_WRAPPER){
    $diagName=$env:TIA_DIAG_WRAPPER
    $diagPath=Join-Path $PackageDir ("Blocks\$diagName.scl")
    if(Test-Path $diagPath){
      Write-Log "=== DIAG bisect: $diagName ==="
      $all=Get-Content -LiteralPath $diagPath
      $bodyStart=-1; $endIdx=-1
      for($i=0;$i -lt $all.Count;$i++){ if($all[$i] -match '^BEGIN\s*$'){ $bodyStart=$i; break } }
      for($i=$all.Count-1;$i -ge 0;$i--){ if($all[$i] -match '^END_FUNCTION_BLOCK'){ $endIdx=$i; break } }
      if($bodyStart -lt 0 -or $endIdx -lt 0){ Write-Log 'DIAG: could not find BEGIN/END' } else {
        $header=New-Object System.Text.StringBuilder
        for($i=0;$i -le $bodyStart;$i++){ [void]$header.AppendLine($all[$i]) }
        $headerTxt=$header.ToString()
        $body=@(); for($i=$bodyStart+1;$i -lt $endIdx;$i++){ $body+=$all[$i] }
        Write-Log "DIAG: body lines=$($body.Count) (file $($bodyStart+2)..$endIdx)"
function Test-Prefix{ param([int]$k)
          $hdr=$headerTxt -replace ('FUNCTION_BLOCK\s+"'+[regex]::Escape($diagName)+'"'),('FUNCTION_BLOCK "DIAG_'+$k+'"')
          $sb=New-Object System.Text.StringBuilder; [void]$sb.Append($hdr)
          for($i=0;$i -lt $k;$i++){ [void]$sb.AppendLine($body[$i]) }
          [void]$sb.AppendLine('END_FUNCTION_BLOCK')
          $cand=Join-Path $StageDir ("diag_$k.scl"); Set-Content -LiteralPath $cand -Value $sb.ToString() -Encoding UTF8
          $srcName="DIAGSRC_$k"; $ext=$null
          try{ $ext=$plc.ExternalSourceGroup.ExternalSources.CreateFromFile($srcName,$cand); $ext.GenerateBlocksFromSource(); $blk=$plc.BlockGroup.Blocks.Find("DIAG_$k"); if($blk){ $blk.Delete() }; try{$ext.Delete()}catch{}; return $true }
          catch{ $script:lastErr=$_.Exception.Message; try{ $blk=$plc.BlockGroup.Blocks.Find("DIAG_$k"); if($blk){ $blk.Delete() } }catch{}; try{ if($ext){$ext.Delete()} }catch{}; return $false }
        }
        $lo=0; $hi=$body.Count
        if(Test-Prefix $hi){ Write-Log 'DIAG: full body imports clean (unexpected)' }
        else{
          if(-not (Test-Prefix 0)){ Write-Log 'DIAG: even empty body fails -> header/interface issue' }
          else{
            while($hi-$lo -gt 1){ $mid=[int](($lo+$hi)/2); if(Test-Prefix $mid){ $lo=$mid } else { $hi=$mid } }
            $fl=$bodyStart+2+$hi
            Write-Log "DIAG: first failing statement is body index $hi (file line $fl): $($body[$hi])"
            Write-Log "DIAG: last ok body index $lo (file line $($bodyStart+2+$lo)): $($body[$lo])"
            [void](Test-Prefix $hi)
            Write-Log "DIAG: error for prefix $hi -> $($script:lastErr)"
          }
        }
      }
      Write-Log '=== DIAG done ==='
    }
  }
  # Compile
  Write-Log 'Compiling...'; $comp=Get-OpennessService $plc ([Siemens.Engineering.Compiler.ICompilable]); $res=$comp.Compile(); $sum.Compiler.State=[string]$res.State; $sum.Compiler.ErrorCount=[int]$res.ErrorCount; $sum.Compiler.WarningCount=[int]$res.WarningCount
  $flat=New-Object System.Collections.ArrayList
  $walk=$null
  $walk={ param($m,[string]$chain) foreach($x in @($m)){ $here=if([string]::IsNullOrWhiteSpace([string]$x.Path)){$chain}else{$chain+'/'+([string]$x.Path)}; $desc=[string]$x.Description; if(-not [string]::IsNullOrWhiteSpace($desc)){ [void]$flat.Add([ordered]@{State=[string]$x.State;Description=$desc;Chain=$here}) }; if($x.Messages){ & $walk $x.Messages $here } } }
  & $walk $res.Messages ''
  $sum.Compiler.Messages=$flat
  # FBD roundtrip read-back: a Blocks/*.xml that declares a ProgrammingLanguage must import as that
  # language (the reason an FBD block is emitted as SimaticML instead of SCL).
  $langOk=$true;$langChecks=New-Object System.Collections.ArrayList
  foreach($n in @($sum.Imported.Blocks)){
    if(-not ($n -like '*.xml')){ continue }
    $xmlFull=Join-Path $PackageDir ('Blocks\'+([IO.Path]::GetFileName($n)))
    if(-not (Test-Path -LiteralPath $xmlFull)){ continue }
    $xml=[IO.File]::ReadAllText($xmlFull)
    $m=[regex]::Match($xml,'<ProgrammingLanguage>([^<]+)</ProgrammingLanguage>')
    if(-not $m.Success){ continue }
    $expected=$m.Groups[1].Value
    $blkName=[IO.Path]::GetFileNameWithoutExtension($xmlFull)
    $blk=$plc.BlockGroup.Blocks.Find($blkName)
    $actual=if($blk){ [string]$blk.ProgrammingLanguage } else { $null }
    if($actual -and $actual -ne $expected){ $langOk=$false }
    [void]$langChecks.Add([ordered]@{ name=$blkName; expected=$expected; actual=$actual; ok=($actual -eq $expected) })
  }
  $sum.RoundTripLanguages=$langChecks
  $proj.Save(); if($sum.Imported.BlocksFailed.Count -gt 0 -or $sum.Imported.InstanceDbsFailed.Count -gt 0){ $sum.Status='BLOCK_IMPORT_ERRORS' } elseif(-not $langOk){ $sum.Status='TIA_LANGUAGE_MISMATCH' } elseif($res.State -eq [Siemens.Engineering.Compiler.CompilerResultState]::Success){ $sum.Status='VERIFIED_IN_TIA' } else { $sum.Status='TIA_COMPILED_WITH_ISSUES' }
  # roundtrip.json: per-object evidence read by the GUI status engine (see GUI_PROMPT_IMPROVED.md).
  $rtObjects=New-Object System.Collections.ArrayList
  function Add-RoundTripObject{ param([string]$Kind,[string]$Name,[string]$File,[bool]$Imported)
    $objMsgs=@(); $hasError=$false
    foreach($m in @($flat)){ if(($m.Description -and $m.Description -like "*$Name*") -or ($m.Chain -and $m.Chain -like "*$Name*")){ $objMsgs+=[ordered]@{ state=$m.State; description=$m.Description; path=$m.Chain }; if($m.State -eq 'Error'){ $hasError=$true } } }
    $compiled=$Imported -and (-not $hasError)
    [void]$rtObjects.Add([ordered]@{ kind=$Kind; name=$Name; file=$File; imported=$Imported; compiled=$compiled; messages=$objMsgs })
  }
  foreach($n in @($sum.Imported.UDTs)){ Add-RoundTripObject 'UDT' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $true }
  foreach($n in @($sum.Imported.Tags)){ Add-RoundTripObject 'Tag' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $true }
  foreach($n in @($sum.Imported.Library)){ Add-RoundTripObject 'Block' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $true }
  foreach($n in @($sum.Imported.Globals)){ Add-RoundTripObject 'GlobalDB' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $true }
  foreach($n in @($sum.Imported.Blocks)){ Add-RoundTripObject 'Block' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $true }
  foreach($n in @($sum.Imported.InstanceDbs)){ Add-RoundTripObject 'InstanceDB' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $true }
  foreach($n in @($sum.Imported.BlocksFailed)){ Add-RoundTripObject 'Block' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $false }
  foreach($n in @($sum.Imported.InstanceDbsFailed)){ Add-RoundTripObject 'InstanceDB' ([IO.Path]::GetFileNameWithoutExtension($n)) $n $false }
  $rtProjPath=$full; $rtAp16=Join-Path $full ($ProjectName+'.ap16'); if(Test-Path $rtAp16){ $rtProjPath=$rtAp16 }
  $roundtrip=[ordered]@{ timestamp=[DateTime]::UtcNow.ToString('o'); tia_version=$Version; cpu_order_number=$CpuOrderNumber; project_path=$rtProjPath; objects=$rtObjects; compile_warnings=[int]$sum.Compiler.WarningCount }
  $roundtrip|ConvertTo-Json -Depth 8|Out-File (Join-Path $PackageDir 'roundtrip.json') -Encoding UTF8
  Write-Log "roundtrip.json written: $($rtObjects.Count) object(s)"
  $sum|ConvertTo-Json -Depth 15|Out-File $SummaryFile -Encoding UTF8; Write-Log "=== DONE: $($sum.Status) ==="
}catch{
  $sum.Status='FAILED'; $sum.Exception=$_.Exception.Message; $sum|ConvertTo-Json -Depth 5|Out-File $SummaryFile -Encoding UTF8; throw
}finally{ if($tia){ try{$tia.Dispose()}catch{} }}

