Add-Type -AssemblyName UIAutomationClient, System.Drawing
$src = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class SH {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing

$root=[System.Windows.Automation.AutomationElement]::RootElement
$c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'FastDelete')
$w=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$c)
[SH]::SetForegroundWindow($w.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500

function All-Buttons {
  $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)))
}

function Capture-Window($hwnd, $out) {
  $rc = New-Object SH+RECT
  [void][SH]::GetWindowRect($hwnd, [ref]$rc)
  $scale = [SH]::GetDpiForWindow($hwnd) / 96.0
  $bw = [int](($rc.Right - $rc.Left) * $scale); $bh = [int](($rc.Bottom - $rc.Top) * $scale)
  $bmp = New-Object System.Drawing.Bitmap($bw, $bh)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [void][SH]::PrintWindow($hwnd, $hdc, 3)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save($out)
  return @{ left=$rc.Left; top=$rc.Top }
}

function Rect-Of($el) {
  $r = $el.Current.BoundingRectangle
  return @{ x=[double]$r.X; y=[double]$r.Y; w=[double]$r.Width; h=[double]$r.Height }
}

$out = @{}

# ---- Step 1: highlight "Delete a folder..." (empty name, x 600-800, y<200) ----
$folderBtn = $null
foreach($b in All-Buttons){
  if($b.Current.Name.Length -eq 0){ $r=$b.Current.BoundingRectangle; if($r.X -gt 600 -and $r.X -lt 820 -and $r.Y -lt 200){ $folderBtn=$b } }
}
if(-not $folderBtn){ Write-Output 'NO FOLDER BTN'; exit 1 }
$out.step1 = Rect-Of $folderBtn
$out.origin = Capture-Window $w.Current.NativeWindowHandle 'C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\step1-raw.png'
Write-Output ('step1 rect: ' + ($out.step1 | ConvertTo-Json -Compress))

# ---- Step 2: Select All, highlight the red button (empty name, width>100, x 1300-1540) ----
$sa = $null
foreach($b in All-Buttons){ if($b.Current.Name -eq 'Select All'){ $sa=$b } }
if(-not $sa){ Write-Output 'NO SELECT ALL'; exit 1 }
$sa.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 700
$redBtn = $null
foreach($b in All-Buttons){
  if($b.Current.Name.Length -eq 0){ $r=$b.Current.BoundingRectangle; if($r.Width -gt 100 -and $r.X -gt 1300 -and $r.X -lt 1545){ $redBtn=$b } }
}
if(-not $redBtn){ Write-Output 'NO RED BTN'; exit 1 }
$out.step2 = Rect-Of $redBtn
$out.origin = Capture-Window $w.Current.NativeWindowHandle 'C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\step2-raw.png'
Write-Output ('step2 rect: ' + ($out.step2 | ConvertTo-Json -Compress))

# ---- Step 3: open confirm dialog, capture its hwnd, highlight "Yes, delete forever" ----
$redBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 1
$procId = [uint32](Get-Process FastDelete).Id
$script:dlg = [IntPtr]::Zero
$cb = [SH+EnumProc]{
  param([IntPtr]$h, [IntPtr]$l)
  $p = [uint32]0
  [void][SH]::GetWindowThreadProcessId($h, [ref]$p)
  if ($p -eq $script:procId -and [SH]::IsWindowVisible($h)) {
    $rc = New-Object SH+RECT
    [void][SH]::GetWindowRect($h, [ref]$rc)
    $ww = $rc.Right - $rc.Left; $hh = $rc.Bottom - $rc.Top
    if ($ww -gt 300 -and $ww -lt 1400 -and $hh -gt 200 -and $hh -lt 900 -and $h -ne $w.Current.NativeWindowHandle) { $script:dlg = $h }
  }
  return $true
}
$script:procId = $procId
[void][SH]::EnumWindows($cb, [IntPtr]::Zero)
if ($script:dlg -eq [IntPtr]::Zero) { Write-Output 'NO DIALOG'; exit 1 }

$yes = $null
foreach($b in All-Buttons){ if($b.Current.Name -eq 'Yes, delete forever'){ $yes=$b } }
if (-not $yes) { Write-Output 'NO YES BUTTON'; exit 1 }
$out.step3 = Rect-Of $yes
$out.dialogOrigin = Capture-Window $script:dlg 'C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\step3-raw.png'
Write-Output ('step3 rect: ' + ($out.step3 | ConvertTo-Json -Compress))

$out | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 'C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\shots.json'
Write-Output 'ALL CAPTURED'
