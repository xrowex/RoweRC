-- Local reference export. Stock meshes stay on this computer and out of releases.
local M={}
local task
local root=(os.getenv('LOCALAPPDATA') or '.')..'/RoweRC/Exports/'
local function valid(o) return o and o:IsValid() end
local function clean(s) return tostring(s):gsub('[\t\r\n]',' ') end
local function unwrap(v) return v:get() end
local function material(o)
    local rgb={.32,.34,.38,1}; local path=valid(o) and o:GetFullName() or 'None'
    for _=1,8 do
        if not valid(o) or not o:IsA('/Script/Engine.MaterialInstance') then break end
        o.VectorParameterValues:ForEach(function(_,entry)
            local v=entry:get(); local n=v.ParameterInfo.Name:ToString():lower()
            if n:find('color',1,true) or n:find('colour',1,true) or n:find('tint',1,true) then
                local c=v.ParameterValue; rgb={c.R,c.G,c.B,c.A}
            end
        end)
        o=o.Parent
    end
    return clean(path),rgb
end
local function run(pawn,kind)
    local folder=root..kind..'/'
    local scene=assert(io.open(folder..'scene.tsv','wb'),'Create the export output folder first')
    scene:write('RRSCENE1\t',kind,'\t',pawn:GetWorld():GetFullName(),'\n')
    local lib=StaticFindObject('/Script/ProceduralMeshComponent.Default__KismetProceduralMeshLibrary')
    local mathLib=StaticFindObject('/Script/Engine.Default__KismetMathLibrary')
    local meshes,serial,parts,skipped={},0,0,0
    local parent=pawn:GetTransform()
    local function mesh(source)
        local key=source:GetFullName(); if meshes[key] then return meshes[key] end
        serial=serial+1; local id=tostring(serial); meshes[key]=id
        scene:write('asset\t',id,'\t',clean(key),'\n')
        return id
    end
    local function instance(source,transform,id,suffix)
        if kind=='car' then transform=mathLib:MakeRelativeTransform(transform,parent) end
        local values={}; for _,p in ipairs({{X=0,Y=0,Z=0},{X=1,Y=0,Z=0},{X=0,Y=1,Z=0},{X=0,Y=0,Z=1}}) do
            local v=mathLib:TransformLocation(transform,p);values[#values+1]=string.format('%.12g,%.12g,%.12g',v.X,v.Y,v.Z)
        end
        parts=parts+1; scene:write('part\t',parts,'\t',id,'\t',clean(source:GetFullName()..suffix),'\t',table.concat(values,','),'\n')
        for slot=0,source:GetNumMaterials()-1 do local path,rgb=material(source:GetMaterial(slot))
            scene:write('mat\t',parts,'\t',slot,'\t',path,'\t',table.concat(rgb,','),'\n')
        end
        scene:flush()
    end
    for _,source in ipairs(FindAllOf('StaticMeshComponent') or {}) do
        if valid(source) and not source.bHiddenInGame and source.bVisible and valid(source.StaticMesh) then
            local include=false
            if kind=='car' then
                local ancestor=source
                for _=1,20 do if not valid(ancestor) then break end
                    if ancestor:GetAddress()==pawn.ModelRoot:GetAddress() then include=true;break end
                    ancestor=ancestor:GetAttachParent()
                end
            else
                include=source:GetFullName():find(pawn:GetWorld():GetFullName():match('World (.+)')..':',1,true)~=nil
                local outer=source:GetOuter(); if valid(outer) and outer:GetAddress()==pawn:GetAddress() then include=false end
            end
            if include then
                local id=mesh(source.StaticMesh)
                if id then
                    if source:IsA('/Script/Engine.InstancedStaticMeshComponent') then
                        for i=0,source:GetInstanceCount()-1 do local transform={}; source:GetInstanceTransform(i,transform,true);instance(source,transform,id,' instance '..i) end
                    else instance(source,source:K2_GetComponentToWorld(),id,'') end
                end
                -- Extraction stays in the registered game-thread Lua state.
            end
        end
    end
    scene:write('done\t',parts,'\t',serial,'\t',skipped,'\n');scene:close()
    return string.format('Exported %d parts, %d meshes, %d unavailable meshes to %s',parts,serial,skipped,folder)
end
function M.start(pawn,kind,write)
    assert(kind=='car' or kind=='map','Invalid export kind')
    write('export-status.txt','Exporting '..kind..'...')
    local ok,result=pcall(run,pawn,kind)
    write('export-status.txt',ok and result or 'Export failed: '..tostring(result))
end
function M.update(write) end
function M.clear() end
return M
