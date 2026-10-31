import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute('''
local calls={}
local mesh='/Game/Vehicle/Bodies/Nissan180SX/SM_Nissan180SX_Body.SM_Nissan180SX_Body'
local paint='/Game/Vehicle/Customization/Materials/M_Paint_Nissan180SX.M_Paint_Nissan180SX'
local function object(class) return {IsValid=function()return true end,IsA=function(_,c)return c==class end} end
function StaticFindObject() return {IsValid=function()return false end} end
function LoadAsset(path)
    calls[#calls+1]=path
    if path==mesh then return object('/Script/Engine.StaticMesh') end
    if path==paint then return object('/Script/Engine.MaterialInterface') end
end
local M=require('appearance')
local parts={{mesh=mesh,materials={[0]={asset=paint}}}}
assert(M.resolve(parts)==parts and calls[1]==mesh and calls[2]==paint,'use complete Unreal object paths to load cold body and paint')
print('PASS: cold body and paint load with full object paths')
assert(parts[1].loadedMesh and parts[1].materials[0].loadedBase,'resolved assets survive preflight')
print('PASS: preflight resolves mesh and material before visual replacement')
local missing={{mesh=mesh,materials={}},{mesh='/Game/Vehicle/Missing.Missing',materials={}}}
local result,reason=M.resolve(missing)
assert(result==nil and reason:find('Missing'),'incomplete appearance must fail preflight')
print('PASS: missing parts fail the entire appearance preflight')
local before=#calls
assert(M.resolve({{mesh='/Game/Maps/Map.Map',materials={}}})==nil and #calls==before,'unrelated assets must not be loaded')
print('PASS: appearance loading remains restricted to vehicle assets')
''')
