# Dependencies and references

- UE4SS `v3.0.1-1188-gf885e8d2`, experimental build from the [official UE4SS repository](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/experimental-latest). UE4SS.dll SHA-256: `743b56dab254e2282b43c3d4ed11b3e5e769f3699e92249aa28fe5833891703e`. dwmapi.dll SHA-256: `285dcd82e0277de063795132db1fcba1ab0a7f34ee7aaf52fcee9b04ad17e23d`. Its included license is distributed with the runtime.
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET/tree/2024.8.0) NuGet `2024.8.0`, MIT license. Native `steam_api64.dll` comes from the same tag's `com.rlabrecque.steamworks.net/Plugins` directory. SHA-256: `1add7f151fa644870a735ae86e68d1f019f296130d8e7c0a7ed3ecc7482dccbc`. Valve's Steamworks SDK/runtime has its own applicable terms; the wrapper's MIT license does not replace them.
- .NET 9 Windows runtime, included by the self-contained publish. Microsoft .NET runtime license/third-party notices are retained when packaging.
- Lupa 2.0 / Lua 5.4 is used locally for syntax checking only and is not part of the game mod.

Technical references: [Steam lobbies](https://partner.steamgames.com/doc/features/multiplayer/matchmaking), [Steam Networking Messages](https://partner.steamgames.com/doc/api/ISteamNetworkingMessages), [UE4SS Lua APIs](https://docs.ue4ss.com/dev/lua-api.html).

ReSkate was reviewed as a reference for the requested experience. No ReSkate code or game assets are copied into RoweRC.
