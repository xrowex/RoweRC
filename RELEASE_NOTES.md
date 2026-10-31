# RoweRC 0.1.3 — experimental

Automatic update checks now run at startup and every 10 minutes while the game is running. Once a signed update finishes downloading and its installer starts, a banner appears over gameplay: close and reopen RC Tandems to install. The banner does not pause the game, capture focus or intercept clicks. F8 retains the detailed update status. A ready update remains ready instead of being replaced by a later check's status.

Players on 0.1.1 or 0.1.2 can use F8 → Check for mod update, wait for the download, then close and reopen the game to install 0.1.3. The periodic checks and gameplay banner are available after that upgrade. This release also fixes missing remote body shells and chassis when the sender uses a stock car the receiver has not loaded locally. UE4SS now receives the full asset object path, and visual replacement waits until every mesh and material resolves. Failed loads keep the previous complete car and retry, rather than committing a partial chassis.

Live validation: the affected green Nissan 180SX now loads all 80/80 parts over Steam, and the guest confirmed the body is visible. The gameplay notification preview rendered without pausing the car updates. Validation also includes 70 backend checks, 11 smoothing checks, four asset-loading regression checks and five Lua syntax checks.

Custom maps still require compatible cooked content. Neither playing an already compatible cooked map nor importing its RoweRC ZIP requires Unreal Editor on the player's PC. Raw source assets still need a compatible cook before the current loader can use them. No engine is installed or maps downloaded by this update.
