import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute('''
local custom='/Game/Vehicle/RoweRC/Shells/supra_rc/SM_supra_rc.SM_supra_rc'
local loaded={IsValid=function()return true end}
local calls={}
local system={MakeSoftObjectPath=function(_,path)calls[#calls+1]='path:'..path;return {path=path} end,
 Conv_SoftObjPathToSoftObjRef=function(_,path)calls[#calls+1]='reference';return {reference=path.path} end,
 LoadAsset_Blocking=function(_,reference)assert(reference.reference==custom);calls[#calls+1]='blocking';return loaded end}
function StaticFindObject(path)if path=='/Script/Engine.Default__KismetSystemLibrary' then return system end end
function LoadAsset()return nil end
local M=require('vehicle_assets')
assert(M.load(custom)==loaded and calls[3]=='blocking','cold custom shell loads via native soft object reference')
calls={};assert(M.load('/Game/Vehicle/Stock.Stock')==nil and #calls==0,'stock misses do not use custom-pack fallback')
function LoadAsset()return loaded end
assert(M.load(custom)==loaded and #calls==0,'registered or already-loaded assets avoid extra loading')
print('PASS: direct engine loading for cold shell packs; stock/registered paths preserve existing behavior')
''')
