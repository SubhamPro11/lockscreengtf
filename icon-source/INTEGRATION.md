# LockscreenGif icon: how to wire it in

Contents
- `app.ico`: multi-size icon (16, 20, 24, 32, 40, 48, 64, 128, 256), each size drawn separately. Sizes up to 40 px use a simplified, bolder variant so it stays readable on the taskbar.
- `icon.svg` / `icon-small.svg`: editable masters (full detail / small sizes).
- `png/`: standalone PNGs (16 to 1024).
- `Assets/`: Square44x44Logo (scale + targetsize + altform-unplated), Square150x150Logo, StoreLogo, for packaged (MSIX) builds.
- `preview-taskbar.png`: how it looks on dark and light taskbars.

## 1. Unpackaged WinUI 3 app (MSI installer)

1. Copy `app.ico` into the app project, e.g. `Assets/app.ico`.
2. In the app `.csproj` (sets the .exe icon: Explorer, Start menu, pinned shortcut):
   ```xml
   <PropertyGroup>
     <ApplicationIcon>Assets\app.ico</ApplicationIcon>
   </PropertyGroup>
   <ItemGroup>
     <Content Include="Assets\app.ico">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
     </Content>
   </ItemGroup>
   ```
3. For the icon on the running window / taskbar button (Windows App SDK 1.2+), in the main window constructor:
   ```csharp
   AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
   ```
   This is the only line of code; it is visual-only.
4. MSI (if WiX): declare the icon and use it for the shortcut and Apps & features:
   ```xml
   <Icon Id="AppIcon.ico" SourceFile="Assets\app.ico" />
   <Property Id="ARPPRODUCTICON" Value="AppIcon.ico" />
   <!-- on each <Shortcut ...>: Icon="AppIcon.ico" -->
   ```

## 2. Packaged (MSIX) app

Copy the files from `Assets/` into the project's `Assets` folder. In `Package.appxmanifest`:
```xml
<uap:VisualElements ... Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" ...>
```
Windows picks the matching `scale-*` and `targetsize-*` files automatically; the `_altform-unplated` files keep the taskbar icon free of the colored plate.

## 3. If the taskbar still shows the old icon

Windows caches icons. Unpin and re-pin the app, or restart Explorer. If it still persists, rebuild the icon cache (`ie4uinit.exe -show`, then sign out and back in).

## Notes
- This is original artwork made for this project; it uses no upstream assets. If the repo still contains the original app's icon files, replace them with these.
- Colors match the portal identity: graphite `#15171a`, brass `#c9a66b`, ivory `#d9d4c7`.
