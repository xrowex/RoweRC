-- UE4SS LoadAsset uses registry entries; new mounted shell packs may have none.
-- The engine's soft-object loader resolves their cooked packages directly.
local M={}
local function valid(o) return o and o:IsValid() end
function M.load(path)
    local object=StaticFindObject(path)
    if not valid(object) then object=LoadAsset(path) end
    if not valid(object) and path:match('^/Game/Vehicle/RoweRC/Shells/[%w_/]+%.[%w_]+$') then
        local system=StaticFindObject('/Script/Engine.Default__KismetSystemLibrary')
        local reference=system:Conv_SoftObjPathToSoftObjRef(system:MakeSoftObjectPath(path))
        object=system:LoadAsset_Blocking(reference)
    end
    return object
end
return M
