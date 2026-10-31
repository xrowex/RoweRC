-- Stock vehicle visuals and independent per-peer material instances.
local M={}
local function valid(o) return o and o:IsValid() end
local function name(o) return o:GetFullName():match("%.([^%.]+)$") or "" end
local function asset(o)
    if not valid(o) then return nil end
    local path=o:GetFullName():match("^[^ ]+ (.+)$")
    if path and (path:match("^/Game/Vehicle/[%w_/]+%.[%w_]+$") or path=="/Engine/EngineMaterials/DefaultMaterial.DefaultMaterial") then return path end
end
local function numbers(v)
    local result={}; for _,value in ipairs(v) do result[#result+1]=string.format("%.7g",value) end
    return table.concat(result,",")
end
local function ordered(t)
    local keys={}; for key in pairs(t) do keys[#keys+1]=key end; table.sort(keys); return keys
end
local function material(source)
    local result={scalars={},vectors={}}
    local current=source
    for _=1,8 do
        if not valid(current) then return nil end
        if not current:IsA("/Script/Engine.MaterialInstanceDynamic") then result.asset=asset(current); break end
        current.ScalarParameterValues:ForEach(function(_,entry)
            local v=entry:get(); local n=v.ParameterInfo.Name:ToString()
            if n:match("^[%w_ -]+$") and #n<=64 and result.scalars[n]==nil then result.scalars[n]=v.ParameterValue end
        end)
        current.VectorParameterValues:ForEach(function(_,entry)
            local v=entry:get(); local n=v.ParameterInfo.Name:ToString(); local c=v.ParameterValue
            if n:match("^[%w_ -]+$") and #n<=64 and result.vectors[n]==nil then result.vectors[n]={c.R,c.G,c.B,c.A} end
        end)
        current=current.Parent
    end
    if not result.asset then return nil end
    return result
end
local function transform(mathLib,source,parent)
    local relative=mathLib:MakeRelativeTransform(source:K2_GetComponentToWorld(),parent)
    local p=mathLib:TransformLocation(relative,{X=0,Y=0,Z=0})
    local r=mathLib:TransformRotation(relative,{Pitch=0,Yaw=0,Roll=0})
    return {p.X,p.Y,p.Z,r.Pitch,r.Yaw,r.Roll}
end
function M.capture(pawn,each,animation,adapter,mathLib,run,serial,previous,now)
    local sources={}
    for _,source in ipairs(adapter.visualSources or {}) do if not valid(source) then adapter.nextVisualScan=0; break end end
    if not adapter.visualSources or (now or 0)>=(adapter.nextVisualScan or 0) then
        adapter.visualSources={}
        each(pawn,"StaticMeshComponent",function(source) adapter.visualSources[#adapter.visualSources+1]=source end)
        adapter.nextVisualScan=(now or 0)+5
    end
    for _,source in ipairs(adapter.visualSources) do
    if valid(source) then
        if not source.bHiddenInGame and source.bVisible and asset(source.StaticMesh) then
        local ancestor,onCar=source,false
        for _=1,20 do
            if not valid(ancestor) then break end
            if ancestor:GetAddress()==pawn.ModelRoot:GetAddress() then onCar=true; break end
            ancestor=ancestor:GetAttachParent()
        end
        if onCar then sources[#sources+1]=source end
        end
    end
    end
    table.sort(sources,function(a,b) return name(a)<name(b) end)
    local signature,parts,materialCache={},{},{}
    for index,source in ipairs(sources) do
        if index>160 then break end
        local key=animation.key(source) or 0
        local part={source=source,key=key,materials={}}
        local componentName=name(source)
        local scaleSignature=""
        -- Suspension links and springs change scale during driving; that is motion,
        -- not a new outfit. Only user-customizable body/wheel scale affects identity.
        if componentName=="OptionalBodyShell" or componentName:match("^RimDisplay") or componentName:match("^TireDisplay") then
            local scale=source:K2_GetComponentScale(); scaleSignature=numbers({scale.X,scale.Y,scale.Z})
        end
        signature[#signature+1]=componentName.."\t"..asset(source.StaticMesh).."\t"..key.."\t"..scaleSignature
        for slot=0,math.min(source:GetNumMaterials(),8)-1 do
            local sourceMaterial=source:GetMaterial(slot)
            local address=valid(sourceMaterial) and tostring(sourceMaterial:GetAddress()) or "invalid"
            local m=materialCache[address]
            if m==nil then m=material(sourceMaterial) or false; materialCache[address]=m end
            if m then
                local lines={"m\t"..index.."\t"..slot.."\t"..m.asset}
                for j,n in ipairs(ordered(m.scalars)) do if j<=8 then lines[#lines+1]="s\t"..index.."\t"..slot.."\t"..n.."\t"..numbers({m.scalars[n]}) end end
                for j,n in ipairs(ordered(m.vectors)) do if j<=8 then lines[#lines+1]="v\t"..index.."\t"..slot.."\t"..n.."\t"..numbers(m.vectors[n]) end end
                for _,line in ipairs(lines) do signature[#signature+1]=line; part.materials[#part.materials+1]=line end
            end
        end
        parts[#parts+1]=part
    end
    local identity=table.concat(signature,"\n")
    if previous==identity or #parts==0 then return nil,identity end
    local output={"RRA1\t"..run.."\t"..serial}
    local pawnScale=pawn:GetActorScale3D()
    for index,part in ipairs(parts) do
        local source=part.source
        local anchor=part.key>0 and adapter.wheels[part.key] or nil
        local parent=valid(anchor) and anchor:K2_GetComponentToWorld() or pawn:GetTransform()
        local values=transform(mathLib,source,parent)
        local s=source:K2_GetComponentScale(); local a=valid(anchor) and anchor:K2_GetComponentScale() or pawnScale
        for _,v in ipairs({s.X/a.X,s.Y/a.Y,s.Z/a.Z,a.X/pawnScale.X,a.Y/pawnScale.Y,a.Z/pawnScale.Z}) do values[#values+1]=v end
        output[#output+1]="p\t"..index.."\t"..name(source).."\t"..asset(source.StaticMesh).."\t"..part.key.."\t"..numbers(values)
        for _,line in ipairs(part.materials) do output[#output+1]=line end
    end
    output[#output+1]="end\t"..#parts.."\t"..serial
    return table.concat(output,"\n").."\n",identity
end
local function split(line)
    local fields={}; for v in (line.."\t"):gmatch("(.-)\t") do fields[#fields+1]=v end; return fields
end
local function csv(text)
    local values={}; for v in text:gmatch("[^,]+") do values[#values+1]=tonumber(v) end; return values
end
local function load(path,class)
    if not path or not (path:match("^/Game/Vehicle/[%w_/]+%.[%w_]+$") or path=="/Engine/EngineMaterials/DefaultMaterial.DefaultMaterial") then return nil end
    local object=StaticFindObject(path)
    -- UE4SS queries GetAssetByObjectPath: preserve the .ObjectName suffix.
    if not valid(object) then object=LoadAsset(path) end
    return valid(object) and object:IsA(class) and object or nil
end
function M.resolve(parts)
    for _,definition in ipairs(parts) do
        definition.loadedMesh=load(definition.mesh,"/Script/Engine.StaticMesh")
        if not valid(definition.loadedMesh) then return nil,"Could not load vehicle mesh: "..tostring(definition.mesh) end
        for _,m in pairs(definition.materials) do
            m.loadedBase=load(m.asset,"/Script/Engine.MaterialInterface")
            if not valid(m.loadedBase) then return nil,"Could not load vehicle material: "..tostring(m.asset) end
        end
    end
    return parts
end
function M.parse(text,run)
    if not text or #text>131072 then return nil end
    local header=text:match("^([^\r\n]+)"); local h=split(header or "")
    if #h~=3 or h[1]~="RRA1" or h[2]~=run then return nil end
    local parts={}
    for line in text:gmatch("[^\r\n]+") do
        local p=split(line)
        if p[1]=="p" then
            local index=tonumber(p[2]); local v=csv(p[6] or "")
            if not index or index<1 or index>160 or #v~=12 then return nil end
            parts[index]={name=p[3],mesh=p[4],key=tonumber(p[5]),values=v,materials={}}
        elseif p[1]=="m" then
            local part=parts[tonumber(p[2])]; if not part then return nil end
            part.materials[tonumber(p[3])]={asset=p[4],scalars={},vectors={}}
        elseif p[1]=="s" or p[1]=="v" then
            local part=parts[tonumber(p[2])]; local m=part and part.materials[tonumber(p[3])]; if not m then return nil end
            if p[1]=="s" then m.scalars[p[4]]=tonumber(p[5]) else m.vectors[p[4]]=csv(p[5]) end
        elseif p[1]=="end" then if tonumber(p[2])~=#parts or p[3]~=h[3] then return nil end; return parts end
    end
end
function M.create(pawn,parts,group,mathLib)
    local actorClass=StaticFindObject("/Script/Engine.StaticMeshActor")
    for _,definition in ipairs(parts) do
        local mesh=definition.loadedMesh
        if valid(mesh) then
            local actor=pawn:GetWorld():SpawnActor(actorClass,pawn:K2_GetActorLocation(),pawn:K2_GetActorRotation())
            if valid(actor) then
                local v=definition.values
                local relative=mathLib:MakeTransform({X=v[1],Y=v[2],Z=v[3]},{Pitch=v[4],Yaw=v[5],Roll=v[6]},{X=v[7],Y=v[8],Z=v[9]})
                local part={actor=actor,name=definition.name,key=definition.key,relative=relative,wheelScale={X=v[10],Y=v[11],Z=v[12]}}
                if part.key>0 then part.bind=relative end
                group[#group+1]=part -- Own the actor before any material operation can fail.
                local target=actor.StaticMeshComponent
                target:SetMobility(2); target:SetCollisionEnabled(0); target:SetSimulatePhysics(false); actor:SetActorEnableCollision(false)
                target:SetStaticMesh(mesh)
                if valid(group.root) then
                    assert(actor:K2_AttachToActor(group.root,FName("None"),1,1,1,false),"Could not attach remote visual")
                    actor:K2_SetActorRelativeTransform(relative,false,{},true)
                end
                for slot,m in pairs(definition.materials) do
                    local base=m.loadedBase
                    if valid(base) then
                        target:SetMaterial(slot,base)
                        if next(m.scalars) or next(m.vectors) then
                            local instance=target:CreateDynamicMaterialInstance(slot,base,FName("None"))
                            if valid(instance) then
                                for n,value in pairs(m.scalars) do instance:SetScalarParameterValue(FName(n),value) end
                                for n,c in pairs(m.vectors) do instance:SetVectorParameterValue(FName(n),{R=c[1],G=c[2],B=c[3],A=c[4]}) end
                            end
                        end
                    end
                end
            end
        end
    end
end
return M
