# Lumen Lock

Turn your PS4 controller's light bar off, or lock it to any colour you like.

Lumen Lock is a small Windows app for DualShock 4 controllers, connected by USB or Bluetooth. Click once to turn the light bar off, or pick any colour from the colour picker. It keeps re-applying your choice, so games, Steam or replugging the controller can't change it back.

## Download

Get `LumenLock-x.y.z-win-x64.zip` from the [latest release](../../releases/latest), unzip it and run `LumenLock.exe`. There's nothing to install, and .NET comes built in.

Windows may show **"Windows protected your PC"** because the app isn't code-signed. Click **More info**, then **Run anyway**.

## Features

- **Turn light off / on** with one click. "On" brings back your last colour.
- **Colour picker** with shade, hue, quick colours and a hex box. A darker colour gives a dimmer light.
- **Lock light** re-applies your colour every second, so nothing else can change it.
- **Runs in the tray.** Closing the window keeps it running. Right-click the tray icon to exit.
- **Start with Windows** (optional). It starts hidden in the tray.

## Requirements

- Windows 10 or 11, 64-bit
- A DualShock 4 (either version), connected by USB cable or Bluetooth

## Advanced settings

Right-click the tray icon and choose **Advanced settings file…** The file opens in Notepad, and changes apply as soon as you save.

| Key | Meaning |
| --- | --- |
| `interval` | How often the colour is re-applied, in milliseconds (100–60000, default 1000) |
| `color` | Light bar colour as `RRGGBB` hex. `000000` = off |
| `lastcolor` | Colour that "Turn light on" restores |
| `guard` | `1` = keep re-applying, `0` = leave the light alone |

The file is stored at `%AppData%\LumenLock\settings.txt`.

## How it works

Lumen Lock finds the controller through the Windows HID API and sends it a DualShock 4 output report that sets only the light bar fields: report `0x05` over USB, or report `0x11` with a CRC-32 checksum over Bluetooth. Rumble is left alone. The controller can't report its current light colour, so Lumen Lock re-sends the colour on a timer instead of watching for changes.

## Build from source

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
cd src
dotnet publish -c Release -o ..\release -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

## License

[MIT](LICENSE)

---

Lumen Lock is an independent project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment. PlayStation, PS4 and DualShock are trademarks of Sony Interactive Entertainment Inc.
