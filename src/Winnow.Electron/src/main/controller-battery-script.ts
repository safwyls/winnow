// Static, read-only helper. No renderer data is inserted into PowerShell or C# source.
export const controllerBatteryScript = String.raw`
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinnowControllerProbe {
  [StructLayout(LayoutKind.Sequential)] public struct State {
    public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger;
    public short LeftX, LeftY, RightX, RightY;
  }
  [StructLayout(LayoutKind.Sequential)] public struct Battery { public byte Type, Level; }
  [DllImport("xinput1_4.dll", EntryPoint="XInputGetState")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  static extern uint GetState(uint index, out State state);
  [DllImport("xinput1_4.dll", EntryPoint="XInputGetBatteryInformation")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  static extern uint GetBattery(uint index, byte device, out Battery battery);
  public class Reading {
    public uint slot; public double[] buttons, axes; public int? type, level;
  }
  static readonly long[] nextBattery = new long[4];
  static readonly int?[] types = new int?[4], levels = new int?[4];
  static double Axis(short value) { return ((value + 32768.0) / 32767.5) - 1; }
  public static Reading[] Read() {
    var readings = new List<Reading>();
    try {
      for (uint index = 0; index < 4; index++) {
        State state;
        if (GetState(index, out state) != 0) {
          nextBattery[index] = 0; types[index] = levels[index] = null; continue;
        }
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now >= nextBattery[index]) {
          nextBattery[index] = now + System.Diagnostics.Stopwatch.Frequency * 30;
          Battery battery;
          if (GetBattery(index, 0, out battery) == 0) {
            types[index] = battery.Type; levels[index] = battery.Level;
          } else types[index] = levels[index] = null;
        }
        int[] masks = {4096,8192,16384,32768,256,512,0,0,32,16,64,128,1,2,4,8};
        var buttons = new double[16];
        for (int b = 0; b < 16; b++) buttons[b] = (state.Buttons & masks[b]) != 0 ? 1 : 0;
        buttons[6] = state.LeftTrigger / 255.0; buttons[7] = state.RightTrigger / 255.0;
        readings.Add(new Reading { slot = index, buttons = buttons,
          axes = new[] {Axis(state.LeftX), -Axis(state.LeftY), Axis(state.RightX), -Axis(state.RightY)},
          type = types[index], level = levels[index] });
      }
    } catch (DllNotFoundException) { return new Reading[0]; }
      catch (EntryPointNotFoundException) { return new Reading[0]; }
      catch (BadImageFormatException) { return new Reading[0]; }
    return readings.ToArray();
  }
}
'@
while ($null -ne ($request = [Console]::ReadLine())) {
  if ($request -ne 'read') { break }
  $readings = @([WinnowControllerProbe]::Read())
  [Console]::WriteLine((ConvertTo-Json -InputObject $readings -Compress -Depth 4))
}
`
