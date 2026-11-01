-- Adapter for RCDriftSim build 1801ffbdfe: four tire displays, four hubs, six motor loops.
local M={}
local corners={FL=1,FR=2,RL=3,RR=4}
local audioIndex={RC_Idle=1,RC_Motor20=2,RC_Motor40=3,RC_Motor60=4,RC_Motor80=5,RC_Motor100=6}
local function valid(o) return o and o:IsValid() end
local function name(o) return o:GetFullName():match("%.([^%.]+)$") or "" end
function M.key(o)
    local n=name(o)
    local corner=n:match("^TireDisplay%d+_(%u%u)$") or n:match("^RimDisplay%d+_(%u%u)$") or n:match("^Reflex_(%u%u)_wheel$")
    if corner then return corners[corner] end
    corner=n:match("^Reflex_(%u%u)_hub$")
    if corner then return corners[corner]+4 end
end
function M.discover(pawn,each)
    local adapter={pawn=pawn,wheels={},audio={},visualSources={}}
    each(pawn,"StaticMeshComponent",function(o)
        adapter.visualSources[#adapter.visualSources+1]=o
        local key=M.key(o)
        if key and (not adapter.wheels[key] or name(o):match("^TireDisplay")) then adapter.wheels[key]=o end
    end)
    each(pawn,"AudioComponent",function(o) local index=audioIndex[name(o)]; if index then adapter.audio[index]=o end end)
    return adapter
end
function M.capture(adapter,mathLib,now)
    local output={now}; local parent=adapter.pawn:GetTransform()
    for i=1,8 do
        local wheel=adapter.wheels[i]
        local location,rotation={X=0,Y=0,Z=0},{Pitch=0,Yaw=0,Roll=0}
        if valid(wheel) then
            local relative=mathLib:MakeRelativeTransform(wheel:K2_GetComponentToWorld(),parent)
            -- FTransform reflected access is deliberately avoided; Kismet getters work in UE4SS.
            location=mathLib:TransformLocation(relative,{X=0,Y=0,Z=0})
            rotation=mathLib:TransformRotation(relative,{Pitch=0,Yaw=0,Roll=0})
        end
        for _,v in ipairs({location.X,location.Y,location.Z,rotation.Pitch,rotation.Yaw,rotation.Roll}) do output[#output+1]=v end
    end
    for i=1,6 do
        local a=adapter.audio[i]
        local volume,pitch,playing=0,1,0
        if valid(a) then volume=math.max(0,math.min(4,a.VolumeMultiplier)); pitch=math.max(.01,math.min(4,a.PitchMultiplier)); playing=a:IsPlaying() and 1 or 0 end
        output[#output+1]=volume; output[#output+1]=pitch; output[#output+1]=playing
    end
    return output
end
function M.spawnAudio(adapter,world,location)
    local result={}
    local gameplay=StaticFindObject("/Script/Engine.Default__GameplayStatics")
    for i=1,6 do
        local source=adapter.audio[i]
        if valid(source) and valid(source.Sound) then
            local audio=gameplay:SpawnSoundAtLocation(world,source.Sound,location,{Pitch=0,Yaw=0,Roll=0},.001,1,0,nil,nil,false)
            if valid(audio) then
                result[i]=audio
                local attenuation=audio.AttenuationOverrides
                attenuation.bAttenuate=false; attenuation.bSpatialize=true
                attenuation.AttenuationShapeExtents={X=30,Y=0,Z=0}; attenuation.FalloffDistance=2500
                audio:AdjustAttenuation(attenuation)
            end
        end
    end
    return result
end
function M.updateAudio(group,motion,location,listener,dt,age)
    local distance=math.sqrt((location.X-listener.X)^2+(location.Y-listener.Y)^2+(location.Z-listener.Z)^2)
    -- Gain follows car-to-car distance; fixed overhead cameras must not mute nearby cars.
    local gain=.65/(1+(distance/180)^2)*math.max(0,1-(age or 0)/.5)
    if distance>2500 then gain=0 end
    group.audioValues=group.audioValues or {}
    local alpha=1-math.exp(-dt*18)
    for i=1,6 do
        local audio=group.audio and group.audio[i]
        if valid(audio) then
            local offset=50+(i-1)*3
            local volume=motion and motion[offset]*motion[offset+2]*gain or 0
            local pitch=motion and motion[offset+1] or 1
            local previous=group.audioValues[i] or {0,pitch}
            previous={previous[1]+(volume-previous[1])*alpha,previous[2]+(pitch-previous[2])*alpha}; group.audioValues[i]=previous
            audio:K2_SetWorldLocation(location,false,{},true)
            audio:SetVolumeMultiplier(previous[1]); audio:SetPitchMultiplier(previous[2])
        end
    end
end
function M.destroyAudio(group)
    for _,audio in pairs(group.audio or {}) do if valid(audio) then audio:Stop(); audio:K2_DestroyComponent(audio) end end
end
return M
