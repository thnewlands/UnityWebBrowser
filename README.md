<img align="right" src="chromium-logo.png" width="120" width="120">

# Unity Web Browser

[![License](https://img.shields.io/github/license/Voltstro-Studios/UnityWebBrowser.svg)](/LICENSE)
[![Unity Package](https://img.shields.io/badge/Unity-Package-blue.svg)](https://gitlab.com/Voltstro-Studios/WebBrowser/Package)
[![Discord](https://img.shields.io/badge/Discord-Voltstro-7289da.svg?logo=discord)](https://discord.voltstro.dev) 
[![YouTube](https://img.shields.io/badge/Youtube-Voltstro-red.svg?logo=youtube)](https://www.youtube.com/Voltstro)

Adds a functional web browser to Unity without any issues or hassle.

This web browser uses [CEF](https://bitbucket.org/chromiumembedded/cef/src/master/) and [CEFGlue](https://gitlab.com/xiliumhq/chromiumembedded/cefglue) to render the page to a texture and display it in Unity. This project also doesn't crash when you play in the editor for a second time.

# Features

- Displays the web using a modern web browser engine (Chromium)
- Load web pages from URL or HTML
- Controls and inputs
- Execute JS code
- Install as an Unity package
- Doesn't crash the entire editor
- Free and open-source

# Getting Started

## How does this work?

If you ever tried to use CEF in Unity, you know it doesn't go well, it always crashes due to how CEF works, and how Unity handles native plugins.

This project avoids these issues by running CEF in a separate process and using IPC to communicate data between Unity and the process.

## Package Installation

### Prerequisites

```
Unity 2020.2.x
```

To install it via the package manager you will need to:

1. Open up the package manager via Windows -> Package Manager
2. Click on the little + sign -> Add package from git URL...
3. Type `https://gitlab.com/Voltstro-Studios/WebBrowser/Package.git` and add it
4. Unity will now download and install the package

## Contributing

Please READ ALL OF THIS if you want to contribute or work on the project.

### Prerequisites

```
Unity 2020.2.x
.NET 5 SDK
Powershell Core
```

1. Clone the repo with `git clone --recursive https://github.com/Voltstro-Studios/UnityWebBrowser.git` (Clone with sub-modules!)

2. Run `scripts/download-cef-binaries.ps1` with PowerShell.

3. Run `scripts/publish-browserprocess-and-copy-win64.ps1` with PowerShell.

4. Add `src/CefBrowser/` as a package in Unity.

## Operating System Support

Right now we only support Windows, however a Linux version is on the TO-DO. MacOS may be supported in the future, but right now, it is too much work to support due to the lack of real-life hardware and having to use a virtual machine to test stuff for MacOS.

Other OSes such as Android and IOS cannot be supported due to the lack of knowledge on how to develop for this OSes.

## Screenshots

<details>
  <summary>Click to expand!</summary>

![Screenshot 1](media/Screenshot-Editor1.png)
![Screenshot 2](media/Screenshot-Editor2.png)
![Screenshot 3](media/Screenshot-Editor3.png)
![Screenshot 4](media/Screenshot-InPlayer.png)

</details>

# Authors

* **Voltstro** - *Initial work* - [Voltstro](https://github.com/Voltstro)

# License

This project is licensed under the LGPL-3.0 License - see the [LICENSE](https://github.com/Voltstro-Studios/UnityWebBrowser/blob/master/LICENSE) file for details.
