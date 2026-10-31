-- RoweRC prototype for RC Tandems build 3c46a39f90 / UE 5.7.
-- All Unreal object access occurs in queued game-thread work. File transport is local only.
local UE = require("UEHelpers")
local UI = require("menu")
local base = (os.getenv("LOCALAPPDATA") or ".") .. "/RoweRC/bridge/"
local run = tostring(os.time()) .. "-" .. tostring(math.random(100000, 999999))
local sequence, pending, commandSeen = 0, false, nil
local transitioning = false
local ghosts = {}
local levelName, lastRemoteText, quietTicks = "", "", 0
local lastError = ""
local bootstrapAttempted = false
local driveAfterLoad = nil

local function log(s) print("[RoweRC] " .. s .. "\n") end
local function valid(o) return o ~= nil and o:IsValid() end
local function read(name)
    local f = io.open(base .. name, "rb")
    if not f then return nil end
    local text = f:read(65537); f:close()
    if text and #text <= 65536 then return text end
end
local function write(name, text)
    local f = io.open(base .. name, "wb")
    if f then f:write(text); f:close() end
end
-- Ignore old map commands left by an earlier companion/game run.
local startupCommand = read("command.tsv")
if startupCommand then commandSeen = startupCommand:match("^([^\t]+)") end
local function split(line)
    local result = {}
    for value in (line .. "\t"):gmatch("(.-)\t") do result[#result + 1] = value end
    return result
end
local function validLevel(name)
    return type(name) == "string" and #name < 240 and name:match("^/Game/[%w_/]+$") ~= nil and not name:find("//", 1, true)
end
local function eachComponent(pawn, className, callback)
    -- Reflected TArray returns vary between UE4SS versions. Object lookup gives us
    -- stable UObject wrappers and is used only when a peer first appears/probing.
    for _, item in ipairs(FindAllOf(className) or {}) do
        if valid(item) then
            local owner = item:GetOuter()
            if valid(owner) and owner:GetAddress() == pawn:GetAddress() then callback(item) end
        end
    end
end
local function clear()
    -- Never dereference objects from the world being unloaded.
    if not transitioning then
        for _, group in pairs(ghosts) do
            for _, part in ipairs(group) do if valid(part.actor) then part.actor:K2_DestroyActor() end end
        end
    end
    ghosts = {}; lastRemoteText = ""; quietTicks = 0
end

RegisterLoadMapPreHook(function()
    if UI.open then UI.Close() else UI.Invalidate() end
    transitioning = true; ghosts = {}; levelName = ""; lastRemoteText = ""
end)
RegisterLoadMapPostHook(function() transitioning = false end)

local function player()
    local controller = UE.GetPlayerController()
    if not valid(controller) then return nil end
    local pawn = controller.Pawn
    if valid(pawn) and pawn:IsA("/Script/RCDriftSim.RCDriftPawn") then return pawn end
    return nil
end
local function level(pawn)
    local world = pawn:GetWorld()
    if not valid(world) then return nil end
    return world:GetFullName():match("World%s+(/Game/[%w_/]+)%.")
end
local function diagnose(pawn)
    DumpAllObjects()
    local text = "RoweRC runtime probe\n" .. pawn:GetFullName() .. "\n"
    eachComponent(pawn, "ActorComponent", function(object)
        if valid(object) then text = text .. object:GetFullName() .. "\n" end
    end)
    for _, action in ipairs(FindAllOf("RCUIAction") or {}) do
        if valid(action) then
            local caption = valid(action.Caption) and action.Caption:GetText():ToString() or ""
            text = text .. "ACTION " .. caption .. " | " .. action.Command:ToString() .. " | " .. tostring(action.Argument) .. " | " .. action.Context:ToString() .. "\n"
        end
    end
    write("diagnostics.txt", text)
end

-- Replicate static visual components as separate kinematic actors. Never spawn another
-- RCDriftPawn: its native input, physics, audio and camera initialization must stay local.
-- Cosmetics currently mirror the local car. Skeletal/dynamic meshes need a game-specific adapter.
local function createGhost(pawn)
    local meshClass = StaticFindObject("/Script/Engine.StaticMeshComponent")
    local actorClass = StaticFindObject("/Script/Engine.StaticMeshActor")
    local mathLib = StaticFindObject("/Script/Engine.Default__KismetMathLibrary")
    if not valid(meshClass) or not valid(actorClass) or not valid(mathLib) then return {} end
    local world, group = pawn:GetWorld(), {}
    local parent = pawn:GetTransform()
    eachComponent(pawn, "StaticMeshComponent", function(source)
        if #group >= 160 then return end
        if not valid(source) or source.bHiddenInGame or not source.bVisible or not valid(source.StaticMesh) then return end
        local ancestor, onCar = source, false
        for _ = 1, 20 do
            if not valid(ancestor) then break end
            if ancestor:GetAddress() == pawn.ModelRoot:GetAddress() then onCar = true; break end
            ancestor = ancestor:GetAttachParent()
        end
        if not onCar then return end
        local actor = world:SpawnActor(actorClass, pawn:K2_GetActorLocation(), pawn:K2_GetActorRotation())
        if not valid(actor) then return end
        -- Keep an owned reference immediately, so a later component failure can be cleaned up.
        local part = { actor = actor, relative = mathLib:MakeRelativeTransform(source:K2_GetComponentToWorld(), parent) }
        group[#group + 1] = part
        local target = actor.StaticMeshComponent
        target:SetMobility(2) -- Movable
        target:SetCollisionEnabled(0)
        target:SetSimulatePhysics(false)
        actor:SetActorEnableCollision(false)
        target:SetStaticMesh(source.StaticMesh)
        for i = 0, math.min(source:GetNumMaterials(), 32) - 1 do target:SetMaterial(i, source:GetMaterial(i)) end
    end)
    log("Remote car visuals: " .. tostring(#group) .. " components")
    return group
end
local function lerpAngle(a, b, alpha) return a + ((b - a + 180) % 360 - 180) * alpha end
local function render(pawn, frame)
    local values, present = {}, {}
    for line in frame:gmatch("[^\r\n]+") do
        local p = split(line)
        if #p == 7 and p[1]:match("^%d+$") and #p[1] <= 20 then
            local pose, good = {}, true
            for i = 2, 7 do
                pose[i-1] = tonumber(p[i])
                if not pose[i-1] or pose[i-1] ~= pose[i-1] or math.abs(pose[i-1]) > (i <= 4 and 1e8 or 360) then good = false end
            end
            if good then values[p[1]] = pose end
        end
    end
    local count = 0
    local mathLib = StaticFindObject("/Script/Engine.Default__KismetMathLibrary")
    for id, pose in pairs(values) do
        count = count + 1; if count > 7 then break end
        present[id] = true
        if not ghosts[id] then ghosts[id] = createGhost(pawn) end
        local group = ghosts[id]
        local previous = group.pose or pose
        local distance = (pose[1]-previous[1])^2 + (pose[2]-previous[2])^2 + (pose[3]-previous[3])^2
        local alpha = distance > 1000000 and 1 or 0.45
        local smooth = {}
        for i = 1, 3 do smooth[i] = previous[i] + (pose[i] - previous[i]) * alpha end
        for i = 4, 6 do smooth[i] = lerpAngle(previous[i], pose[i], alpha) end
        group.pose = smooth
        local transform = mathLib:MakeTransform({X=smooth[1],Y=smooth[2],Z=smooth[3]},
            {Pitch=smooth[4],Yaw=smooth[5],Roll=smooth[6]}, pawn:GetActorScale3D())
        for _, part in ipairs(group) do
            if valid(part.actor) then
                part.actor:K2_SetActorTransform(mathLib:ComposeTransforms(part.relative, transform), false, {}, true)
            end
        end
    end
    for id, group in pairs(ghosts) do
        if not present[id] then
            for _, part in ipairs(group) do if valid(part.actor) then part.actor:K2_DestroyActor() end end
            ghosts[id] = nil
        end
    end
end
local function update()
    if transitioning then return end
    if not bootstrapAttempted then
        bootstrapAttempted = true
        for _, directory in pairs(IterateGameDirectories()) do
            if type(directory) == "table" and directory.Binaries and directory.Binaries.Win64 then
                local helper = directory.Binaries.Win64.__absolute_path .. "/RoweRC/RoweRC.exe"
                local f = io.open(helper, "rb")
                if f then
                    f:close()
                    local bootstrap, failure = package.loadlib(directory.Binaries.Win64.__absolute_path .. "/RoweRC/bootstrap.dll", "RoweRCStart")
                    if bootstrap then bootstrap(); log("Requested background Steam helper")
                    else log("Steam helper bootstrap unavailable: " .. tostring(failure)) end
                end
            end
        end
    end
    UI.Update()
    local pawn = player()
    if not pawn then clear(); return end
    local currentLevel = level(pawn)
    if not validLevel(currentLevel) then return end
    if currentLevel ~= levelName then clear(); levelName = currentLevel end
    local command = read("command.tsv")
    if command then
        local p = split(command:gsub("[\r\n]+$", ""))
        if p[1] ~= commandSeen then
            commandSeen = p[1]
            if p[2] == "open" and validLevel(p[3]) and p[3] ~= currentLevel then
                clear()
                driveAfterLoad = p[3]
                local gameplay = StaticFindObject("/Script/Engine.Default__GameplayStatics")
                gameplay:OpenLevel(pawn, FName(p[3]), true, "game=/Script/RCDriftSim.RCDriftGameMode")
                return
            elseif p[2] == "open" and p[3] == currentLevel then
                pawn:ResumeDrive()
            elseif p[2] == "dump" then diagnose(pawn) end
        end
    end
    if driveAfterLoad == currentLevel then driveAfterLoad = nil; pawn:ResumeDrive() end
    sequence = sequence + 1
    local position, rotation = pawn:K2_GetActorLocation(), pawn:K2_GetActorRotation()
    write("local." .. tostring(sequence % 2) .. ".tsv", string.format(
        "RRC1\t%s\t%d\t%s\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t0\t%d\n",
        run, sequence, currentLevel, position.X, position.Y, position.Z,
        rotation.Pitch, rotation.Yaw, rotation.Roll, sequence))
    local remote = read("remote.tsv")
    if remote and remote:sub(1, 5) == "RRC1\n" then
        -- The companion refreshes a serial even when positions don't change, so a dead
        -- companion cannot leave permanent ghosts. (Line 2 is a heartbeat comment.)
        if remote == lastRemoteText then quietTicks = quietTicks + 1 else quietTicks = 0; lastRemoteText = remote end
        if quietTicks < 60 then render(pawn, remote)
        else clear(); lastRemoteText = remote; quietTicks = 60 end
    else clear() end
end

-- Avoid queuing unbounded work while the game is loading or paused.
LoopAsync(33, function()
    if pending then return end
    pending = true
    ExecuteInGameThread(function()
        local ok, err = pcall(update)
        if not ok and tostring(err) ~= lastError then lastError = tostring(err); log(lastError); write("error.txt", lastError) end
        pending = false
    end)
end)
write("error.txt", "")
log("loaded; F8 opens friends and maps")
