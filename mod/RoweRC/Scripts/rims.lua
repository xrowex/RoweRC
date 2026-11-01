-- The game's rim color MIDs use M_Workbench, whose shader exposes Metallic.
-- Change only that scalar: preserve per-axle color, roughness and glow.
local M={}
local path=(os.getenv('LOCALAPPDATA') or '.')..'/RoweRC/rim-metallic.txt'
local desired,sources,nextScan,originals=nil,{},-1,{}
local function valid(o) return o and o:IsValid() end
local function finite(v) return type(v)=='number' and v==v and math.abs(v)<math.huge end
local function readSetting()
    local f=io.open(path,'rb'); if not f then return end
    local value=tonumber(f:read(32)); f:close()
    if finite(value) and value>=0 and value<=1 then desired=value end
end
readSetting()
function M.set(value)
    assert(finite(value),'Invalid rim metallic value')
    local selected=math.floor(math.max(0,math.min(1,value))*100+.5)/100
    local f=assert(io.open(path,'wb'),'Could not save rim finish')
    assert(f:write(string.format('%.2f\n',selected)),'Could not save rim finish'); f:close()
    desired=selected
end
function M.clear() sources={}; nextScan=-1; originals={} end
local function compatible(material)
    if not valid(material) or not material:IsA('/Script/Engine.MaterialInstanceDynamic') then return false end
    local base=material
    for _=1,8 do
        if not valid(base) then return false end
        if not base:IsA('/Script/Engine.MaterialInstanceDynamic') then
            return base:GetFullName()=='Material /Game/Environment/Workbench/M_Workbench.M_Workbench'
        end
        base=base.Parent
    end
    return false
end
function M.stock()
    local f=assert(io.open(path,'wb'),'Could not save rim finish'); f:write('stock\n'); f:close()
    desired=nil
    M.current=nil
    for _,entry in pairs(originals) do
        if valid(entry.material) then entry.material:SetScalarParameterValue(FName('Metallic'),entry.value); M.current=M.current or entry.value end
    end
    originals={}
end
function M.value() return desired or M.current or 0 end
function M.update(pawn,each,now)
    if not valid(pawn) then return end
    if now>=nextScan then
        sources={}
        each(pawn,'StaticMeshComponent',function(source)
            if source:GetFullName():match('%.RimDisplay[%w_]+$') then sources[#sources+1]=source end
        end)
        nextScan=now+1
    end
    local count,current=0,nil
    for _,source in ipairs(sources) do
        if valid(source) then
            for slot=0,math.min(source:GetNumMaterials(),8)-1 do
                local material=source:GetMaterial(slot)
                if compatible(material) then
                    local value=material:K2_GetScalarParameterValue(FName('Metallic'))
                    local key=tostring(material:GetAddress())
                    if desired~=nil then
                        if not originals[key] then originals[key]={material=material,value=value} end
                        if math.abs(value-desired)>.0001 then material:SetScalarParameterValue(FName('Metallic'),desired) end
                        value=desired
                    end
                    current=current or value; count=count+1
                end
            end
        end
    end
    M.current=current; M.count=count
    return count
end
return M
