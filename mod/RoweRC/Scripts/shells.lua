-- Locally installed UE5.7 shells. Meshes/materials replicate by asset path;
-- each peer needs the matching cooked pack before starting the game.
local M={options={{id='optional_shell',name='Pickup shell'},{id='supra_rc',name='Supra RC'}}}
local Damage=require('damage')
local Assets=require('vehicle_assets')
local selected,state='stock',nil
local path=(os.getenv('LOCALAPPDATA') or '.')..'/RoweRC/shell.txt'
local function valid(o) return o and o:IsValid() end
local function asset(o) return valid(o) and o:GetFullName():match('^[^ ]+ (.+)$') end
local function definition(id)
    for _,option in ipairs(M.options) do if option.id==id then return option end end
end
local function save(id)
    local f=assert(io.open(path,'wb'),'Could not save shell choice')
    assert(f:write(id..'\n'),'Could not save shell choice');f:close()
end
do
    local f=io.open(path,'rb')
    if f then local id=(f:read(64) or ''):match('^([%w_]+)'); f:close(); if definition(id) then selected=id end end
end
local function resolve(id)
    -- Lua UObject wrappers do not retain Unreal assets through garbage collection.
    -- Reacquire from the engine each time; a shell restored to stock can be unloaded.
    assert(definition(id),'Unknown shell')
    local prefix='/Game/Vehicle/RoweRC/Shells/'..id..'/'
    local mesh=Assets.load(prefix..'SM_'..id..'.SM_'..id)
    assert(valid(mesh) and mesh:IsA('/Script/Engine.StaticMesh'),'Shell pack is not loaded. Install it and restart RC Tandems.')
    local groups=id=='optional_shell' and {'Paint'} or {'Paint','Glass','BlackTrim','Metal','RedLens','GrayPlastic'}
    local materials={}
    for _,group in ipairs(groups) do
        local name='M_'..id..'_'..group
        local material=Assets.load(prefix..name..'.'..name)
        assert(valid(material) and material:IsA('/Script/Engine.MaterialInterface'),'Shell material is missing: '..group)
        materials[#materials+1]=material
    end
    return {mesh=mesh,materials=materials}
end
local function remember(pawn,source)
    local materials={}
    for slot=0,math.min(source:GetNumMaterials(),16)-1 do materials[#materials+1]=source:GetMaterial(slot) end
    state={pawn=pawn:GetAddress(),source=source,mesh=source.StaticMesh,materials=materials,
        transform=source:GetRelativeTransform()}
end
local function apply(pawn,id)
    local source=pawn.BodyShell
    assert(valid(source) and valid(source.StaticMesh),'Waiting for the car body')
    local package=resolve(id) -- Resolve the whole shell before touching the car.
    if not state or state.pawn~=pawn:GetAddress() or not valid(state.source) or state.source:GetAddress()~=source:GetAddress() then remember(pawn,source) end
    if source.StaticMesh:GetAddress()~=package.mesh:GetAddress() then
        -- A native body change establishes the new stock fallback and body transform.
        if not asset(source.StaticMesh):find('/Game/Vehicle/RoweRC/Shells/',1,true) then remember(pawn,source) end
        Damage.reset()
        assert(source:SetStaticMesh(package.mesh),'Could not apply shell mesh')
        for slot=0,math.max(source:GetNumMaterials(),#state.materials,#package.materials)-1 do source:SetMaterial(slot,package.materials[slot+1] or package.materials[1]) end
        local paint=source:CreateDynamicMaterialInstance(0,package.materials[1],FName('None'))
        assert(valid(paint),'Could not create shell paint')
        state.paint=paint
    end
    -- Use the live native paint MID so the game's color picker still works.
    local current=source:GetMaterial(0)
    if valid(state.paint) then
        local reference=state.materials[1]
        if valid(reference) and reference:IsA('/Script/Engine.MaterialInstanceDynamic') then
            state.paint:SetVectorParameterValue(FName('BodyColor'),reference:K2_GetVectorParameterValue(FName('BodyColor')))
        end
        if not valid(current) or current:GetAddress()~=state.paint:GetAddress() then source:SetMaterial(0,state.paint) end
    end
    M.status=definition(id).name
end
function M.select(pawn,id)
    assert(id=='stock' or definition(id),'Unknown shell');M.error=nil
    if id=='stock' then
        if state and valid(state.source) and state.pawn==pawn:GetAddress() then
            Damage.reset()
            assert(state.source:SetStaticMesh(state.mesh),'Could not restore game shell')
            for slot=0,math.max(state.source:GetNumMaterials(),#state.materials)-1 do state.source:SetMaterial(slot,state.materials[slot+1]) end
            state.source:K2_SetRelativeTransform(state.transform,false,{},true)
        end
        save(id);selected=id;state=nil;M.status='Game shell'
    else apply(pawn,id);save(id);selected=id end
end
function M.update(pawn)
    if selected~='stock' then
        local ok,error=pcall(apply,pawn,selected)
        if not ok then M.status=tostring(error):gsub('^.-:%d+: ','') end
    else M.status=M.status or 'Game shell' end
end
function M.clear() state=nil end
function M.selected() return selected end
return M
