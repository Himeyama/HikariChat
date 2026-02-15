# WinUIExample
[日本語](README.md) | English

Example of a WinUI3 app and creating an installer.

![image](https://github.com/user-attachments/assets/ae07aa17-48c2-46d9-a1e8-2c0ff4810034)

## Changing the Project Name
If the project name is MyApp, use:

```ps1
.\setup --name MyApp
```

## Development
Commands are summarized in `.\dev.ps1`.

### Running the App
```ps1
.\dev run
```

## Creating the Installer
Creates the installer install.exe.

```ps1
.\dev pack
```

Running Install.exe launches the installer. You can uninstall it from the Control Panel or Settings.

### Publishing the App
```ps1
.\dev publish
```

### Packaging the App into a ZIP File
```ps1
.\dev zip
```

### Installation

> [!NOTE]
> Since it is not registered in the registry, it cannot be uninstalled from the Control Panel or Settings.

```ps1
.\dev install
```

### Uninstallation
```ps1
.\dev uninstall
```