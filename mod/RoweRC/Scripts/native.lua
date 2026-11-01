local M={}
local solve,lastRequest
local function load()
    for _,directory in pairs(IterateGameDirectories()) do
        if type(directory)=="table" and directory.Binaries and directory.Binaries.Win64 then
            local root=directory.Binaries.Win64.__absolute_path
            local fn=package.loadlib(root.."/RoweRC/bootstrap.dll","RoweRCNativeSolve")
            if fn then return fn end
        end
    end
    error("Native contact bridge is unavailable; restart after updating")
end
function M.update(pawn,read,write)
    local request=read("native-request.tsv",131072)
    local id=request and request:match("^RRCN1\t(%d+)\t")
    if not id or id==lastRequest then return end
    lastRequest=id
    local ok,reason=pcall(function()
        solve=solve or load()
        local p,r=pawn:K2_GetActorLocation(),pawn:K2_GetActorRotation()
        write("native-context.tsv",string.format("RRCCTX1\t%x\t%.12g\t%.12g\t%.12g",pawn:GetAddress(),p.X,p.Y,r.Yaw))
        solve()
    end)
    if not ok then write("native-error.txt",tostring(reason)) end
end
return M
