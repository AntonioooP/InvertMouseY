# InvertMouseY
Invert the mouse Y axis on keybind press
Very simple program created for a friend, so you cannot modify the keybinds. The default keybinds are:
Toggle Y axis invert: `CTRl + ALT + Y` 
Quit program: `CTRl + ALT + Q`

If you'd like to modify the keybinds, feel free to modify the Program.cs file and compile your own using:
`dotnet publish -c Release -r win-x64 --self-contained true   /p:PublishSingleFile=true   /p:PublishTrimmed=false` 

This is expected to run on Windows x64 platform. No need for external libraries or any dependencies, since they're already in the binary.
To download the .exe of this version you can do it from the Releases tab.
