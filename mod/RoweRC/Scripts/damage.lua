-- Cosmetic shell dents only. Native body/tire contact shapes remain unchanged.
local M={}
local localState={revision=0,dents={}}
local hidden={}
local resetPending=false
local function valid(o) return o and o:IsValid() end
local function asset(o) return o:GetFullName():match('^[^ ]+ (.+)$') end
local function restore(s)
    if valid(s.source) then s.source:SetVisibility(true,false);hidden[s.source:GetAddress()]=nil end
    if valid(s.proc) then s.proc:K2_DestroyComponent(s.proc) end
    s.proc=nil;s.source=nil;s.original=nil;s.applied=nil;s.stats=nil
end
function M.isSource(o) return hidden[o:GetAddress()]==true end
function M.clear() localState={revision=0,dents={}};hidden={} end
function M.reset() resetPending=true end
pcall(function() RegisterHook('/Script/RCDriftSim.RCDriftPawn:ResetCar',function() resetPending=true end) end)
function M.deform(vertices,dents)
    local result={}
    for i,v in ipairs(vertices) do
        local x,y,z=v.X,v.Y,v.Z
        for _,d in ipairs(dents) do
            local dx,dy,dz=v.X-d.x,v.Y-d.y,v.Z-d.z
            local t=1-(dx*dx+dy*dy+dz*dz)/(d.radius*d.radius)
            if t>0 then local amount=d.depth*t*t;x=x-d.nx*amount;y=y-d.ny*amount end
        end
        local dx,dy=x-v.X,y-v.Y;local length=math.sqrt(dx*dx+dy*dy)
        if length>1.2 then x=v.X+dx*1.2/length;y=v.Y+dy*1.2/length end
        result[i]={X=x,Y=y,Z=z}
    end
    return result
end
local function parse(text)
    local dents={}
    if not text or text=='' then return dents end
    assert(#text<=180,'Dent state too large')
    for token in text:gmatch('[^;]+') do
        local x,y,depth=token:match('^([%d.e+-]+):([%d.e+-]+):([%d.e+-]+)$');x,y,depth=tonumber(x),tonumber(y),tonumber(depth)
        assert(x and y and depth and x==x and y==y and depth==depth and math.abs(x)<=1.001 and math.abs(y)<=1.001 and depth>0 and depth<=.8,'Invalid dent')
        assert(x*x+y*y>.9 and x*x+y*y<1.1,'Invalid dent direction');dents[#dents+1]={x,y,depth};assert(#dents<=4,'Too many dents')
    end
    return dents
end
M.parse=parse
local function apply(s,source,text,mathLib)
    if not valid(source) or not valid(source.StaticMesh) then return end
    if s.source and (not valid(s.source) or s.source:GetAddress()~=source:GetAddress() or s.mesh~=asset(source.StaticMesh)) then restore(s) end
    if text=='' then if s.source then restore(s) end;return end
    if s.applied==text and valid(s.proc) then return end
    local dents=parse(text)
    if not valid(s.proc) then
        assert(source.StaticMesh.bAllowCPUAccess,'This body does not expose CPU geometry')
        local identity=mathLib:MakeTransform({X=0,Y=0,Z=0},{Pitch=0,Yaw=0,Roll=0},{X=1,Y=1,Z=1})
        local owner=source:GetOwner()
        local proc=owner:AddComponentByClass(StaticFindObject('/Script/ProceduralMeshComponent.ProceduralMeshComponent'),true,identity,false)
        assert(valid(proc),'Could not create deformable shell')
        s.proc=proc;s.source=source;s.mesh=asset(source.StaticMesh)
        proc:SetCollisionEnabled(0);proc:SetVisibility(false,false)
        proc:K2_AttachToComponent(source,FName('None'),0,0,0,false)
        proc:K2_SetRelativeTransform(identity,false,{},true)
        local library=StaticFindObject('/Script/ProceduralMeshComponent.Default__KismetProceduralMeshLibrary')
        library:CopyProceduralMeshFromStaticMeshComponent(source,0,proc,false)
        local vertices,indices,normals,uv,tangents={},{},{},{},{}
        library:GetSectionFromStaticMesh(source.StaticMesh,0,0,vertices,indices,normals,uv,tangents)
        assert(#vertices<=500000,'Body paint section exceeds deformation budget')
        s.original={};s.normals={};s.uv={};s.tangents={}
        for i,entry in ipairs(vertices) do local v=entry:get();s.original[i]={X=v.X,Y=v.Y,Z=v.Z} end
        for i,entry in ipairs(normals) do local n=entry:get();s.normals[i]={X=n.X,Y=n.Y,Z=n.Z} end
        for i,entry in ipairs(uv) do local v=entry:get();s.uv[i]={X=v.X,Y=v.Y} end
        for i,entry in ipairs(tangents) do local v=entry:get();s.tangents[i]={TangentX={X=v.TangentX.X,Y=v.TangentX.Y,Z=v.TangentX.Z},bFlipTangentY=v.bFlipTangentY} end
    end
    local marks={};local relative=mathLib:MakeRelativeTransform(source:K2_GetComponentToWorld(),source:GetOwner():GetTransform())
    local bounds=source.StaticMesh:GetBounds();local z=bounds.Origin.Z-bounds.BoxExtent.Z*.35
    for _,d in ipairs(dents) do
        local n=mathLib:InverseTransformDirection(relative,{X=d[1],Y=d[2],Z=0});local length=math.sqrt(n.X*n.X+n.Y*n.Y);local nx,ny=n.X/length,n.Y/length
        local support=-math.huge
        for _,v in ipairs(s.original) do support=math.max(support,v.X*nx+v.Y*ny) end
        local x,y,count=0,0,0
        for _,v in ipairs(s.original) do if support-v.X*nx-v.Y*ny<.5 then x=x+v.X;y=y+v.Y;count=count+1 end end
        assert(count>0,'No dent surface');marks[#marks+1]={x=x/count,y=y/count,z=z,nx=nx,ny=ny,depth=d[3],radius=5}
    end
    local vertices=M.deform(s.original,marks)
    local changed,maxDepth=0,0
    for i,v in ipairs(vertices) do local p=s.original[i];local d=math.sqrt((v.X-p.X)^2+(v.Y-p.Y)^2);if d>.00001 then changed=changed+1;maxDepth=math.max(maxDepth,d) end end
    s.stats=string.format('vertices %d / moved %d / maximum dent %.4f cm',#vertices,changed,maxDepth)
    s.proc:UpdateMeshSection_LinearColor(0,vertices,s.normals,s.uv,{},{},{},{},s.tangents,false)
    s.proc:SetVisibility(true,false);source:SetVisibility(false,false);hidden[source:GetAddress()]=true
    s.applied=text
end
local function encoded()
    local values={};for _,d in ipairs(localState.dents) do values[#values+1]=string.format('%.4f:%.4f:%.4f',d[1],d[2],d[3]) end
    return table.concat(values,';')
end
function M.impact(pawn,dx,dy)
    local speed=math.sqrt(dx*dx+dy*dy);if speed<.15 or #localState.dents>=4 then return end
    local yaw=math.rad(pawn:K2_GetActorRotation().Yaw);local c,s=math.cos(yaw),math.sin(yaw)
    localState.dents[#localState.dents+1]={(-c*dx-s*dy)/speed,(s*dx-c*dy)/speed,math.min(.8,speed*.6)}
    localState.revision=localState.revision+1
end
function M.update(pawn,mathLib,run,write)
    if resetPending then restore(localState);localState.dents={};localState.revision=localState.revision+1;localState.failedRevision=nil;resetPending=false end
    local source=pawn.BodyShell
    if not valid(source) or not valid(source.StaticMesh) then return end
    local mesh=asset(source.StaticMesh)
    if localState.mesh and localState.mesh~=mesh then restore(localState);localState.dents={};localState.revision=localState.revision+1 end
    localState.mesh=mesh
    if localState.failedRevision==localState.revision then return end
    local ok,error=pcall(apply,localState,source,encoded(),mathLib)
    if not ok then restore(localState);localState.failedRevision=localState.revision;write('damage-error.txt',tostring(error)) else write('damage-error.txt','') end
    write('damage-last.txt',localState.stats or '')
    write('damage.tsv',string.format('RRD1\t%s\t%d\t%s\t%s\n',run,localState.revision,mesh,encoded()))
end
function M.remote(group,mesh,text,mathLib)
    for _,part in ipairs(group) do
        if part.name=='OptionalBodyShell' and valid(part.actor) then
            local source=part.actor.StaticMeshComponent
            if valid(source.StaticMesh) and asset(source.StaticMesh)==mesh then group.damage=group.damage or {};apply(group.damage,source,text or '',mathLib) end
            return
        end
    end
end
function M.destroy(group) if group.damage then restore(group.damage);group.damage=nil end end
-- Explicit developer readback, never part of the driving loop.
function M.verify(pawn)
    if not valid(localState.proc) then
        return 'repaired\tvisible='..tostring(valid(pawn.BodyShell) and pawn.BodyShell.bVisible)
    end
    local library=StaticFindObject('/Script/ProceduralMeshComponent.Default__KismetProceduralMeshLibrary')
    local vertices,indices,normals,uv,tangents={},{},{},{},{}
    library:GetSectionFromProceduralMesh(localState.proc,0,vertices,indices,normals,uv,tangents)
    assert(#vertices==#localState.original,'Deformed mesh readback has the wrong vertex count')
    local moved,maxDepth=0,0
    for i,entry in ipairs(vertices) do
        local v=entry:get();local original=localState.original[i]
        local d=math.sqrt((v.X-original.X)^2+(v.Y-original.Y)^2+(v.Z-original.Z)^2)
        if d>.00001 then moved=moved+1;maxDepth=math.max(maxDepth,d) end
    end
    return string.format('vertices=%d\tmoved=%d\tdepth=%.5f\tvisible=%s',#vertices,moved,maxDepth,tostring(localState.proc.bVisible))
end
return M
