-- The native adapter is installed only on the local pawn in this exact city map.
local M={}
local target='/Game/RoweRCMaps/apex_wangan/Tandyz_Overcast_With_APEX_Repaired'
local bind,clear,probe,identity
local function load()
    for _,directory in pairs(IterateGameDirectories()) do
        if type(directory)=='table' and directory.Binaries and directory.Binaries.Win64 then
            local dll=directory.Binaries.Win64.__absolute_path..'/RoweRC/bootstrap.dll'
            bind=package.loadlib(dll,'RoweRCTerrainBind')
            clear=package.loadlib(dll,'RoweRCTerrainClear')
            probe=package.loadlib(dll,'RoweRCTerrainProbe')
            if bind and clear then return end
        end
    end
    error('Terrain adapter is unavailable; restart after updating')
end
function M.update(pawn,level,write)
    if level~=target then if identity then M.clear() end;return end
    local address=pawn:GetAddress()
    if identity==address then return end
    load()
    local p=pawn:K2_GetActorLocation()
    write('terrain-context.tsv',string.format('RRCTERRAIN1\t%x\t%.12g\t%.12g\t%s',address,p.X,p.Y,level))
    bind();identity=address
end
function M.clear()
    if clear then clear() end
    identity=nil
end
function M.probe(pawn,level,spec,read,write)
    assert(level==target and identity==pawn:GetAddress(),'Terrain test requires the city adapter')
    assert((read('ui-state.tsv') or ''):find('\nrole\tidle\n',1,true),'Terrain test requires a disconnected game')
    assert(probe,'Terrain test bridge is unavailable')
    write('terrain-probe-request.tsv',spec);probe()
end
return M
