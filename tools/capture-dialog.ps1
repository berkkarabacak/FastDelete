Add-Type -AssemblyName UIAutomationClient, System.Drawing
$src = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class SH {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
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
$mainHwnd = $w.Current.NativeWindowHandle

$procId = [uint32](Get-Process FastDelete).Id
$script:candidates = @()
$cb = [SH+EnumProc]{
  param([IntPtr]$h, [IntPtr]$l)
  $p = [uint32]0
  [void][SH]::GetWindowThreadProcessId($h, [ref]$p)
  if ($p -eq $script:procId -and [SH]::IsWindowVisible($h) -and $h -ne $script:mainHwnd) {
    $rc = New-Object SH+RECT
    [void][SH]::GetWindowRect($h, [ref]$rc)
    $script:candidates += [pscustomobject]@{ hwnd=$h; w=$rc.Right-$rc.Left; h=$rc.Bottom-$rc.Top; left=$rc.Left; top=$rc.Top }
  }
  return $true
}
$script:procId = $procId
$script:mainHwnd = $mainHwnd
[void][SH]::EnumWindows($cb, [IntPtr]::Zero)
foreach($cd in $script:candidates){ Write-Output ("candidate: hwnd=" + $cd.hwnd + " " + $cd.w + "x" + $cd.h + " at " + $cd.left + "," + $cd.top) }
if($script:candidates.Count -eq 0){ Write-Output 'NO DIALOG WINDOW'; exit 1 }
$dlg = $script:candidates[0]

$rc = New-Object SH+RECT
[void][SH]::GetWindowRect($dlg.hwnd, [ref]$rc)
$scale = [SH]::GetDpiForWindow($dlg.hwnd) / 96.0
$bw = [int](($rc.Right - $rc.Left) * $scale); $bh = [int](($rc.Bottom - $rc.Top) * $scale)
$bmp = New-Object System.Drawing.Bitmap($bw, $bh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][SH]::PrintWindow($dlg.hwnd, $hdc, 3)
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save('C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\step3-raw.png')
Write-Output ("captured dialog " + $bw + "x" + $bh + " origin " + $rc.Left + "," + $rc.Top)

# refresh shots.json dialogOrigin
$meta = Get-Content 'C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\shots.json' -Raw | ConvertFrom-Json
$meta.dialogOrigin = @{ left=$rc.Left; top=$rc.Top }
$meta | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 'C:\Users\OdinLocal\Documents\Kimi\Workspaces\MediaOrganizer\FastDelete\docs\shots.json'
Write-Output 'shots.json updated'
