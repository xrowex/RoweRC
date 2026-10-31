-- RoweRC prototype for RC Tandems build 3c46a39f90 / UE 5.7.
-- All Unreal object access occurs in queued game-thread work. File transport is local only.
local UE = require("UEHelpers")
local UI = require("menu")
local Animation = require("animation")
local Smooth = require("smoothing")
local Appearance = require("appearance")
local base = (os.getenv("LOCALAPPDATA") or ".") .. "/RoweRC/bridge/"
local run = tostring(os.time()) .. "-" .. tostring(math.random(100000, 999999))
local sequence, pending, commandSeen = 0, false, nil
local transitioning = false
local ghosts = {}
local levelName, lastRemoteText, lastRemoteAt = "", "", -1
local lastError = ""
local bootstrapAttempted = false
local driveAfterLoad = nil
local adapter, lastExport, lastTick = nil, -1, nil
local appearanceIdentity, lastAppearance = nil, -1

local function log(s) print("[RoweRC] " .. s .. "\n") end
local function valid(o) return o ~= nil and o:IsValid() end
local function read(name,limit)
    limit = limit or 65536
    local f = io.open(base .. name, "rb")
    if not f then return nil end
    local text = f:read(limit+1); f:close()
    if text and #text <= limit then return text end
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
            Animation.destroyAudio(group)
            for _, part in ipairs(group) do if valid(part.actor) then part.actor:K2_DestroyActor() end end
        end
    end
    ghosts = {}; lastRemoteText = ""; lastRemoteAt = -1; adapter = nil
end

RegisterLoadMapPreHook(function()
    if UI.open then UI.Close() else UI.Invalidate() end
    for _,group in pairs(ghosts) do Animation.destroyAudio(group) end
    appearanceIdentity = nil; lastAppearance = -1; adapter = nil; lastExport = -1; lastTick = nil; transitioning = true; ghosts = {}; levelName = ""; lastRemoteText = ""
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
-- Local visuals are a compatibility fallback while a stock appearance snapshot arrives.
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
        local key = Animation.key(source)
        local anchor = key and adapter.wheels[key]
        local part = { actor = actor, key = key, relative = mathLib:MakeRelativeTransform(source:K2_GetComponentToWorld(), parent) }
        if valid(anchor) then
            part.bind = mathLib:MakeRelativeTransform(source:K2_GetComponentToWorld(), anchor:K2_GetComponentToWorld())
            local a,b=anchor:K2_GetComponentScale(),pawn:GetActorScale3D()
            part.wheelScale={X=a.X/b.X,Y=a.Y/b.Y,Z=a.Z/b.Z}
        end
        group[#group + 1] = part
        local target = actor.StaticMeshComponent
        target:SetMobility(2) -- Movable
        target:SetCollisionEnabled(0)
        target:SetSimulatePhysics(false)
        actor:SetActorEnableCollision(false)
        target:SetStaticMesh(source.StaticMesh)
        for i = 0, math.min(source:GetNumMaterials(), 32) - 1 do target:SetMaterial(i, source:GetMaterial(i)) end
    end)
    group.audio = Animation.spawnAudio(adapter, world, pawn:K2_GetActorLocation())
    group.buffer = {}
    log("Remote car visuals: " .. tostring(#group) .. " components")
    return group
end
local function render(pawn, text, now, dt)
    local present = {}
    local mathLib = StaticFindObject("/Script/Engine.Default__KismetMathLibrary")
    local listener = pawn:K2_GetActorLocation()
    local count = 0
    for line in text:gmatch("[^\r\n]+") do
        local p=split(line)
        if (#p==11 or #p==10 or #p==7) and p[1]:match("^%d+$") and #p[1]<=20 then
            local pose,good={},true
            for i=2,7 do
                pose[i-1]=tonumber(p[i])
                if not pose[i-1] or pose[i-1]~=pose[i-1] or math.abs(pose[i-1])>(i<=4 and 1e8 or 360) then good=false end
            end
            local motion
            if p[10] and p[10]~="0" then
                motion={}
                for v in p[10]:gmatch("[^,]+") do
                    local n=tonumber(v); if not n or n~=n or math.abs(n)>1e10 then good=false; break end
                    motion[#motion+1]=n
                end
                if #motion~=67 then good=false end
            end
            if good and count<7 then
                count=count+1; local id=p[1]; present[id]=true
                if not ghosts[id] then ghosts[id]=createGhost(pawn) end
                local group=ghosts[id]
                if p[11] and p[11]:match("^%x+$") and #p[11]==64 and group.revision~=p[11] then
                    local definitions=Appearance.parse(read("appearance."..id..".tsv",131072),p[8])
                    if definitions then
                        for _,part in ipairs(group) do if valid(part.actor) then part.actor:K2_DestroyActor() end end
                        for i=#group,1,-1 do group[i]=nil end
                        Appearance.create(pawn,definitions,group,mathLib)
                        group.revision=p[11]
                        log("Applied peer appearance "..id..": "..#group.." parts")
                    end
                end
                if group.buffer then
                    Smooth.push(group.buffer,{pose=pose,motion=motion,run=p[8] or "legacy",seq=tonumber(p[9]) or sequence},now)
                    local sample=Smooth.sample(group.buffer,now)
                    if sample then
                        local v=sample.pose
                        local location={X=v[1],Y=v[2],Z=v[3]}
                        local transform=mathLib:MakeTransform(location,{Pitch=v[4],Yaw=v[5],Roll=v[6]},pawn:GetActorScale3D())
                        for _,part in ipairs(group) do
                            if valid(part.actor) then
                                local relative=part.relative
                                if sample.motion and part.bind and part.key then
                                    local offset=2+(part.key-1)*6; local m=sample.motion
                                    local wheel=mathLib:MakeTransform({X=m[offset],Y=m[offset+1],Z=m[offset+2]},
                                        {Pitch=m[offset+3],Yaw=m[offset+4],Roll=m[offset+5]},part.wheelScale or {X=1,Y=1,Z=1})
                                    relative=mathLib:ComposeTransforms(part.bind,wheel)
                                end
                                part.actor:K2_SetActorTransform(mathLib:ComposeTransforms(relative,transform),false,{},true)
                            end
                        end
                        Animation.updateAudio(group,sample.motion,location,listener,dt,sample.age)
                    else Animation.updateAudio(group,nil,pawn:K2_GetActorLocation(),listener,dt,3) end
                end
            end
        end
    end
    for id,group in pairs(ghosts) do
        if not present[id] then
            Animation.destroyAudio(group)
            for _,part in ipairs(group) do if valid(part.actor) then part.actor:K2_DestroyActor() end end
            ghosts[id]=nil
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
            elseif p[2] == "quit" then
                StaticFindObject("/Script/Engine.Default__KismetSystemLibrary"):QuitGame(pawn,UE.GetPlayerController(),0,false)
            elseif p[2] == "menu" then UI.Open()
            elseif p[2] == "dump" then
                diagnose(pawn)
                local lines={"RoweRC peer visuals"}
                for id,group in pairs(ghosts) do
                    lines[#lines+1]="peer\t"..id.."\t"..tostring(group.revision or "fallback").."\t"..#group
                    for index,audio in pairs(group.audio or {}) do
                        if valid(audio) then lines[#lines+1]="audio\t"..index.."\t"..tostring(audio:IsPlaying()).."\t"..audio.VolumeMultiplier.."\t"..audio.PitchMultiplier end
                    end
                    for _,part in ipairs(group) do
                        if valid(part.actor) then
                            local mesh=part.actor.StaticMeshComponent
                            lines[#lines+1]=(part.name or "part").."\t"..mesh.StaticMesh:GetFullName()
                            if part.key and part.key>0 then
                                local r=part.actor:K2_GetActorRotation()
                                lines[#lines+1]="wheel\t"..(part.name or "part").."\t"..r.Pitch..","..r.Yaw..","..r.Roll
                            end
                            for slot=0,math.min(mesh:GetNumMaterials(),8)-1 do
                                local material=mesh:GetMaterial(slot)
                                if valid(material) and material:IsA("/Script/Engine.MaterialInstanceDynamic") then
                                    material.VectorParameterValues:ForEach(function(_,entry)
                                        local v=entry:get(); local c=v.ParameterValue
                                        lines[#lines+1]="paint\t"..v.ParameterInfo.Name:ToString().."\t"..c.R..","..c.G..","..c.B..","..c.A
                                    end)
                                end
                            end
                        end
                    end
                end
                write("peer-visuals.tsv",table.concat(lines,"\n"))
            end
        end
    end
    if driveAfterLoad == currentLevel then driveAfterLoad = nil; pawn:ResumeDrive() end
    local gameplay = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    local now = gameplay:GetRealTimeSeconds(pawn)
    local dt = lastTick and math.max(.001,math.min(.1,now-lastTick)) or .016; lastTick=now
    if not adapter then adapter=Animation.discover(pawn,eachComponent) end
    if now-lastExport >= .03 then
    lastExport=now; sequence = sequence + 1
    local motion=Animation.capture(adapter,StaticFindObject("/Script/Engine.Default__KismetMathLibrary"),now)
    local values={}; for _,v in ipairs(motion) do values[#values+1]=string.format("%.7g",v) end
    local position, rotation = pawn:K2_GetActorLocation(), pawn:K2_GetActorRotation()
    write("local." .. tostring(sequence % 2) .. ".tsv", string.format(
        "RRC1\t%s\t%d\t%s\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%s\t%d\n",
        run, sequence, currentLevel, position.X, position.Y, position.Z,
        rotation.Pitch, rotation.Yaw, rotation.Roll, table.concat(values,","), sequence))
    end
    if now-lastAppearance>1 then
        lastAppearance=now
        local ok,snapshot,identity=pcall(Appearance.capture,pawn,eachComponent,Animation,adapter,
            StaticFindObject("/Script/Engine.Default__KismetMathLibrary"),run,sequence,appearanceIdentity)
        if ok then
            if snapshot then write("appearance.tsv",snapshot) end
            appearanceIdentity=identity; write("appearance-error.txt","")
        else write("appearance-error.txt",tostring(snapshot)) end
    end
    local remote = read("remote.tsv")
    if remote and remote:sub(1,5)=="RRC1\n" and remote~=lastRemoteText then
        lastRemoteText=remote; lastRemoteAt=now
    end
    -- A short Windows sharing/rename collision must not destroy and respawn the car.
    if lastRemoteText~="" and now-lastRemoteAt<2 then render(pawn,lastRemoteText,now,dt)
    elseif next(ghosts) then clear() end
end

-- Avoid queuing unbounded work while the game is loading or paused.
LoopAsync(16, function()
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
