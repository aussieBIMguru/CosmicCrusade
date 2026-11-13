# Third-party notices

Cosmic Crusade is a self-contained .NET app: the installer includes parts of .NET so players don't need to install it. These components are used under their own licences.

| Component | Used in | Licence |
|---|---|---|
| .NET runtime and libraries (incl. WPF) | The game (`CosmicCrusade.exe`) | MIT, Copyright (c) .NET Foundation and Contributors |
| ASP.NET Core | The LAN server (`CosmicCrusade-LanServer.exe`) | MIT, Copyright (c) .NET Foundation and Contributors |

Licence texts: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>, <https://github.com/dotnet/wpf/blob/main/LICENSE.TXT>, <https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt>. .NET's own third-party notices: <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>.

Not shipped (used only to build or test): xUnit and the .NET test SDK (tests), Inno Setup (installer), Cloudflare Wrangler (deploying the online relay).

The online relay runs on Cloudflare Workers under the operator's own Cloudflare account and terms.
